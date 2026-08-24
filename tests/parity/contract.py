#!/usr/bin/env python3
"""Black-box differential contract checks for the Rust and .NET servers.

The runner uses only Python's standard library so it can execute in CI without adding a
test-framework dependency. It compares full JSON values after a deliberately narrow
normalization of runtime-generated metadata.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import hmac
import json
import os
import pathlib
import re
import socket
import struct
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import Any


VOLATILE_KEYS = {
    "build_date",
    "duration_ms",
    "feedback_id",
    "generated_at",
    "git_commit",
    "latency_ms",
    "processing_ms",
    "rust_version",
    "timestamp",
    "triggered_at",
    "uptime_seconds",
}


@dataclass(frozen=True)
class Case:
    name: str
    params: Any
    label: str


CORE_CASES = [
    Case("classify", {"ip": "203.0.113.10", "user_agent": "GPTBot/1.0", "path": "/wp-login.php", "method": "POST", "headers": {"accept": "*/*"}, "body_snippet": "UNION SELECT password", "request_id": "parity-bot"}, "classify/bot"),
    Case("classify", {"ip": "198.51.100.20", "user_agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36", "path": "/", "method": "GET", "accept": "text/html", "request_id": "parity-browser"}, "classify/browser"),
    Case("classify", {"tls_ja3": "72a589da586844d7f0818ce684948eea", "tls_ja4": "t13d1516h2_8daaf6152771_e5627efa2ab1", "tls_fingerprint_source": "edge", "request_id": "parity-untrusted-tls"}, "classify/untrusted-tls"),
    Case("explain", {"classification": {"request_id": "parity-explain", "verdict": "block", "score": 0.91, "confidence": "high", "category": "malicious_bot", "signals": [{"name": "known_bot_ua", "score": 0.9, "reason": "known bot"}]}, "format": "detailed"}, "explain"),
    Case("batch_classify", {"items": [{"user_agent": "Mozilla/5.0", "request_id": "parity-batch-1"}, {"user_agent": "GPTBot/1.0", "request_id": "parity-batch-2"}], "options": {"fail_fast": False, "include_details": True}}, "batch_classify"),
    Case("health", {}, "health"),
    Case("model_info", {}, "model_info"),
    Case("feedback", {"request_id": "absent", "correct_verdict": "allow", "notes": "parity"}, "feedback/disabled-backend"),
    Case("score_breakdown", {"request_id": "parity-score", "signals": {"known_bot_ua": 0.8, "rate_anomaly": 0.3}}, "score_breakdown"),
    Case("validate_payload", {"tool": "classify", "payload": {"user_agent": "GPTBot/1.0"}}, "validate_payload/valid"),
    Case("validate_payload", {"tool": "classify", "payload": "wrong"}, "validate_payload/invalid"),
    Case("feature_flags", {}, "feature_flags/all"),
    Case("feature_flags", {"flag": "enable_batch"}, "feature_flags/one"),
    Case("warmup", {}, "warmup"),
    Case("replay_decision", {"request_id": "absent", "deterministic": True}, "replay_decision/disabled-backend"),
    Case("redact_preview", {"payload": {"authorization": "secret", "nested": {"password": "pw", "safe": 1}}, "fields": ["password"]}, "redact_preview"),
    Case("enrich_ip", {"ip": "81.2.69.160"}, "enrich_ip/disabled-backend"),
    Case("enrich_asn", {"asn": 15169}, "enrich_asn/disabled-backend"),
    Case("enrich_ua", {"user_agent": "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Version/17.0 Mobile/15E148 Safari/604.1"}, "enrich_ua/mobile"),
    Case("enrich_ua", {"user_agent": "unknown parity agent"}, "enrich_ua/unknown"),
    Case("threat_lookup", {"indicator": "203.0.113.1", "type": "ip"}, "threat_lookup/disabled-backend"),
    Case("canary_eval", {"token": "absent", "context": {"path": "/"}}, "canary_eval/disabled-backend"),
    Case("abuse_pattern_match", {"text": "UNION SELECT password FROM users; <script>alert(1)</script>", "categories": ["sql_injection", "xss"]}, "abuse_pattern_match"),
    Case("drift_report", {"window_hours": 24}, "drift_report/disabled-backend"),
    Case("calibration_report", {"window_hours": 24}, "calibration_report/disabled-backend"),
    Case("queue_status", {}, "queue_status/disabled-backend"),
    Case("config_snapshot", {"redact_secrets": True}, "config_snapshot/redacted"),
    Case("config_snapshot", {"redact_secrets": False}, "config_snapshot/plain"),
    Case("self_test", {}, "self_test"),
]

ERROR_CASES = [
    Case("classify", {}, "classify/empty"),
    Case("classify", {"tls_ja3": "not-a-ja3", "request_id": "parity-bad-ja3"}, "classify/malformed-ja3"),
    Case("batch_classify", {"items": "wrong"}, "batch_classify/malformed"),
    Case("explain", {}, "explain/missing"),
    Case("feedback", {}, "feedback/missing"),
    Case("validate_payload", {}, "validate_payload/missing"),
    Case("replay_decision", {}, "replay_decision/missing"),
    Case("redact_preview", {}, "redact_preview/missing"),
    Case("enrich_ip", {"ip": "not-an-ip"}, "enrich_ip/malformed"),
    Case("enrich_asn", {"asn": 0}, "enrich_asn/malformed"),
    Case("enrich_ua", {}, "enrich_ua/missing"),
    Case("threat_lookup", {"indicator": "x", "type": "invalid"}, "threat_lookup/malformed"),
    Case("canary_eval", {}, "canary_eval/missing"),
    Case("abuse_pattern_match", {}, "abuse_pattern_match/missing"),
    Case("drift_report", {"window_hours": 0}, "drift_report/malformed"),
    Case("calibration_report", {"window_hours": 0}, "calibration_report/malformed"),
    Case("no_such_tool", {}, "unknown-tool"),
]


def normalize(value: Any) -> Any:
    if isinstance(value, dict):
        normalized: dict[str, Any] = {}
        for key, child in value.items():
            if key in VOLATILE_KEYS:
                normalized[key] = "<runtime>"
            elif key == "port":
                normalized[key] = "<server-port>"
            elif key in {"version", "model_version"} and isinstance(child, str):
                normalized[key] = child.split("+", 1)[0]
            else:
                normalized[key] = normalize(child)
        return normalized
    if isinstance(value, list):
        return [normalize(item) for item in value]
    return value


def request(url: str, payload: bytes, token: str | None = None) -> tuple[int, bytes, dict[str, str]]:
    headers = {"content-type": "application/json"}
    if token is not None:
        headers["authorization"] = f"Bearer {token}"
    req = urllib.request.Request(f"{url}/mcp", payload, headers, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            return response.status, response.read(), dict(response.headers.items())
    except urllib.error.HTTPError as error:
        return error.code, error.read(), dict(error.headers.items())


def rpc(url: str, case: Case, identifier: int, token: str | None = None) -> tuple[int, Any]:
    payload = json.dumps({"jsonrpc": "2.0", "id": identifier, "method": case.name, "params": case.params}, separators=(",", ":")).encode()
    status, body, _ = request(url, payload, token)
    return status, json.loads(body) if body else None


def compare_case(rust_url: str, dotnet_url: str, case: Case, identifier: int, token: str | None = None) -> None:
    rust = rpc(rust_url, case, identifier, token)
    dotnet = rpc(dotnet_url, case, identifier, token)
    rust_value = normalize(rust[1])
    dotnet_value = normalize(dotnet[1])
    if case.name == "classify" and isinstance(case.params, dict) and not case.params.get("request_id"):
        normalize_generated_request_ids(rust_value)
        normalize_generated_request_ids(dotnet_value)
    if case.name == "replay_decision":
        for value in (rust_value, dotnet_value):
            result = value.get("result") if isinstance(value, dict) else None
            replayed = result.get("replayed") if isinstance(result, dict) else None
            if isinstance(replayed, dict) and isinstance(replayed.get("request_id"), str) and replayed["request_id"].startswith("replay-"):
                replayed["request_id"] = "<runtime>"
    if (rust[0], rust_value) != (dotnet[0], dotnet_value):
        raise AssertionError(
            f"{case.label} differs\nRust ({rust[0]}): {json.dumps(rust[1], sort_keys=True)}\n"
            f".NET ({dotnet[0]}): {json.dumps(dotnet[1], sort_keys=True)}"
        )


def normalize_generated_request_ids(value: Any) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            if key == "request_id":
                value[key] = "<runtime>"
            else:
                normalize_generated_request_ids(child)
    elif isinstance(value, list):
        for child in value:
            normalize_generated_request_ids(child)


class WebSocket:
    def __init__(self, url: str, token: str | None = None):
        parsed = urllib.parse.urlparse(url)
        self.sock = socket.create_connection((parsed.hostname or "127.0.0.1", parsed.port or 80), timeout=10)
        key = base64.b64encode(os.urandom(16)).decode()
        headers = [
            f"GET /mcp HTTP/1.1",
            f"Host: {parsed.hostname}:{parsed.port}",
            "Upgrade: websocket",
            "Connection: Upgrade",
            f"Sec-WebSocket-Key: {key}",
            "Sec-WebSocket-Version: 13",
        ]
        if token is not None:
            headers.append(f"Authorization: Bearer {token}")
        self.sock.sendall(("\r\n".join(headers) + "\r\n\r\n").encode())
        response = self._read_headers()
        status = int(response.split(b" ", 2)[1])
        if status != 101:
            self.sock.close()
            raise ConnectionError(f"WebSocket upgrade returned {status}: {response.decode(errors='replace')}")
        accept = hashlib.sha1((key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").encode()).digest()
        if f"sec-websocket-accept: {base64.b64encode(accept).decode()}".lower().encode() not in response.lower():
            raise ConnectionError("invalid Sec-WebSocket-Accept")

    @staticmethod
    def upgrade_status(url: str, token: str | None = None) -> int:
        parsed = urllib.parse.urlparse(url)
        with socket.create_connection((parsed.hostname or "127.0.0.1", parsed.port or 80), timeout=10) as sock:
            key = base64.b64encode(os.urandom(16)).decode()
            headers = ["GET /mcp HTTP/1.1", f"Host: {parsed.hostname}:{parsed.port}", "Upgrade: websocket", "Connection: Upgrade", f"Sec-WebSocket-Key: {key}", "Sec-WebSocket-Version: 13"]
            if token is not None:
                headers.append(f"Authorization: Bearer {token}")
            sock.sendall(("\r\n".join(headers) + "\r\n\r\n").encode())
            data = b""
            while b"\r\n\r\n" not in data:
                data += sock.recv(4096)
            return int(data.split(b" ", 2)[1])

    def _read_headers(self) -> bytes:
        data = b""
        while b"\r\n\r\n" not in data:
            chunk = self.sock.recv(4096)
            if not chunk:
                raise ConnectionError("connection closed during WebSocket upgrade")
            data += chunk
        return data

    def send(self, payload: bytes, opcode: int = 1) -> None:
        mask = os.urandom(4)
        length = len(payload)
        header = bytearray([0x80 | opcode])
        if length < 126:
            header.append(0x80 | length)
        elif length <= 0xFFFF:
            header.append(0x80 | 126)
            header.extend(struct.pack("!H", length))
        else:
            header.append(0x80 | 127)
            header.extend(struct.pack("!Q", length))
        header.extend(mask)
        header.extend(byte ^ mask[index % 4] for index, byte in enumerate(payload))
        self.sock.sendall(header)

    def receive(self) -> tuple[int, bytes]:
        first, second = self._recv_exact(2)
        opcode = first & 0x0F
        length = second & 0x7F
        if length == 126:
            length = struct.unpack("!H", self._recv_exact(2))[0]
        elif length == 127:
            length = struct.unpack("!Q", self._recv_exact(8))[0]
        mask = self._recv_exact(4) if second & 0x80 else None
        payload = self._recv_exact(length)
        if mask:
            payload = bytes(byte ^ mask[index % 4] for index, byte in enumerate(payload))
        return opcode, payload

    def _recv_exact(self, length: int) -> bytes:
        chunks = bytearray()
        while len(chunks) < length:
            chunk = self.sock.recv(length - len(chunks))
            if not chunk:
                raise ConnectionError("WebSocket closed unexpectedly")
            chunks.extend(chunk)
        return bytes(chunks)

    def close(self) -> None:
        try:
            self.send(b"", opcode=8)
        finally:
            self.sock.close()


def websocket_rpc(url: str, case: Case, identifier: int, token: str | None = None, binary: bool = False) -> Any:
    client = WebSocket(url, token)
    try:
        payload = json.dumps({"jsonrpc": "2.0", "id": identifier, "method": case.name, "params": case.params}, separators=(",", ":")).encode()
        client.send(payload, opcode=2 if binary else 1)
        opcode, response = client.receive()
        if opcode != 1:
            raise AssertionError(f"expected a text response, received opcode {opcode}")
        return json.loads(response)
    finally:
        client.close()


def run_core(rust_url: str, dotnet_url: str) -> int:
    cases = CORE_CASES + ERROR_CASES
    for identifier, case in enumerate(cases, 1):
        compare_case(rust_url, dotnet_url, case, identifier)

    for binary in (False, True):
        case = Case("classify", {"user_agent": "GPTBot/1.0", "request_id": f"parity-ws-{binary}"}, f"websocket/{'binary' if binary else 'text'}")
        rust = websocket_rpc(rust_url, case, 700 + int(binary), binary=binary)
        dotnet = websocket_rpc(dotnet_url, case, 700 + int(binary), binary=binary)
        if normalize(rust) != normalize(dotnet):
            raise AssertionError(f"{case.label} differs\nRust: {rust}\n.NET: {dotnet}")

    invalid_results = []
    for url in (rust_url, dotnet_url):
        client = WebSocket(url)
        try:
            client.send(b"\xff", opcode=2)
            invalid_results.append(json.loads(client.receive()[1]))
            client.send(json.dumps({"jsonrpc": "2.0", "id": 799, "method": "health", "params": {}}).encode())
            invalid_results.append(json.loads(client.receive()[1]))
        finally:
            client.close()
    if normalize(invalid_results[0]) != normalize(invalid_results[2]) or normalize(invalid_results[1]) != normalize(invalid_results[3]):
        raise AssertionError(f"WebSocket invalid UTF-8/recovery differs: {invalid_results}")

    http_invalid = [request(url, b"\xff")[:2] for url in (rust_url, dotnet_url)]
    parsed_invalid = [(status, json.loads(body)) for status, body in http_invalid]
    if parsed_invalid[0] != parsed_invalid[1]:
        raise AssertionError(f"HTTP invalid UTF-8 differs: {parsed_invalid}")

    malformed = [request(url, b"{")[:2] for url in (rust_url, dotnet_url)]
    parsed_malformed = [(status, json.loads(body)) for status, body in malformed]
    if parsed_malformed[0] != parsed_malformed[1]:
        raise AssertionError(f"malformed JSON differs: {parsed_malformed}")

    notification = json.dumps({"jsonrpc": "2.0", "method": "warmup", "params": {}}).encode()
    notification_results = [request(url, notification)[:2] for url in (rust_url, dotnet_url)]
    if notification_results != [(204, b""), (204, b"")]:
        raise AssertionError(f"notification behavior differs: {notification_results}")

    print(f"PASS core: {len(cases)} HTTP RPC cases, text/binary WebSocket, malformed UTF-8/JSON, and notifications")
    return len(cases) + 7


def run_disabled(rust_url: str, dotnet_url: str) -> int:
    cases = [
        Case("batch_classify", {"items": []}, "disabled/batch"),
        Case("feedback", {"request_id": "x", "correct_verdict": "allow"}, "disabled/feedback"),
        Case("enrich_ua", {"user_agent": "GPTBot/1.0"}, "disabled/enrichment"),
        Case("model_info", {}, "disabled/model-info"),
        Case("feature_flags", {}, "disabled/feature-flags"),
    ]
    for identifier, case in enumerate(cases, 900):
        compare_case(rust_url, dotnet_url, case, identifier)
    print(f"PASS disabled features: {len(cases)} cases")
    return len(cases)


def run_auth(rust_url: str, dotnet_url: str, token: str) -> int:
    payload = json.dumps({"jsonrpc": "2.0", "id": 1000, "method": "health", "params": {}}).encode()
    for supplied, expected in ((None, 401), ("wrong-token", 403)):
        results = [request(url, payload, supplied)[:2] for url in (rust_url, dotnet_url)]
        if [result[0] for result in results] != [expected, expected] or results[0][1] != results[1][1]:
            raise AssertionError(f"auth failure differs for {supplied}: {results}")
    compare_case(rust_url, dotnet_url, Case("health", {}, "auth/authorized"), 1001, token)
    statuses = [WebSocket.upgrade_status(url) for url in (rust_url, dotnet_url)]
    if statuses != [401, 401]:
        raise AssertionError(f"unauthenticated WebSocket differs: {statuses}")
    statuses = [WebSocket.upgrade_status(url, "wrong-token") for url in (rust_url, dotnet_url)]
    if statuses != [403, 403]:
        raise AssertionError(f"forbidden WebSocket differs: {statuses}")
    case = Case("health", {}, "auth/websocket-authorized")
    rust = websocket_rpc(rust_url, case, 1002, token)
    dotnet = websocket_rpc(dotnet_url, case, 1002, token)
    if normalize(rust) != normalize(dotnet):
        raise AssertionError(f"authorized WebSocket differs: {rust} != {dotnet}")
    print("PASS auth: HTTP and WebSocket missing, invalid, and valid credentials")
    return 6


def tls_attestation(key: str, source: str, timestamp: int, ja3: str, ja4: str) -> str:
    payload = f"{source}\n{timestamp}\n{ja3}\n{ja4}".encode()
    return f"v1:{timestamp}:{hmac.new(key.encode(), payload, hashlib.sha256).hexdigest()}"


def run_tls(rust_url: str, dotnet_url: str, key: str) -> int:
    ja3 = "72a589da586844d7f0818ce684948eea"
    ja4 = "t13d1516h2_8daaf6152771_e5627efa2ab1"
    source = "trusted-edge"
    timestamp = int(time.time())
    attestation = tls_attestation(key, source, timestamp, ja3, ja4)
    case = Case("classify", {"request_id": "parity-attested-tls", "tls_ja3": ja3, "tls_ja4": ja4, "tls_fingerprint_source": source, "tls_fingerprint_attestation": attestation}, "tls/valid-attestation")
    compare_case(rust_url, dotnet_url, case, 1100)
    bad = Case("classify", {**case.params, "request_id": "parity-bad-attestation", "tls_fingerprint_attestation": attestation[:-1] + ("0" if attestation[-1] != "0" else "1")}, "tls/invalid-attestation")
    compare_case(rust_url, dotnet_url, bad, 1101)
    print("PASS TLS fingerprint attestation: valid and tampered signatures")
    return 2


def run_config(rust_url: str, dotnet_url: str) -> int:
    case = Case("config_snapshot", {"redact_secrets": False}, "config-file/snapshot")
    compare_case(rust_url, dotnet_url, case, 1200)
    for url in (rust_url, dotnet_url):
        _, response = rpc(url, case, 1201)
        snapshot = response["result"]["snapshot"]
        expected = {
            "max_request_bytes": 123456,
            "max_batch_size": 7,
            "key_prefix": "parity_config",
            "service_name": "parity-config-service",
        }
        actual = {
            "max_request_bytes": snapshot["limits"]["max_request_bytes"],
            "max_batch_size": snapshot["limits"]["max_batch_size"],
            "key_prefix": snapshot["redis"]["key_prefix"],
            "service_name": snapshot["telemetry"]["service_name"],
        }
        if actual != expected:
            raise AssertionError(f"configuration values were not applied by {url}: {actual}")
    print("PASS CONFIG_FILE mapping and snake_case binding")
    return 3


def run_woothee(rust_url: str, dotnet_url: str, tests_directory: pathlib.Path) -> int:
    user_agents: list[str] = []
    pattern = re.compile(r'parser\.parse\(r#"(.*?)"#\)', re.DOTALL)
    for path in sorted(tests_directory.glob("*.rs")):
        user_agents.extend(pattern.findall(path.read_text(encoding="utf-8")))
    user_agents = list(dict.fromkeys(user_agents))
    if len(user_agents) < 200:
        raise AssertionError(f"expected the upstream Woothee corpus, found only {len(user_agents)} cases in {tests_directory}")
    for identifier, user_agent in enumerate(user_agents, 2000):
        compare_case(rust_url, dotnet_url, Case("enrich_ua", {"user_agent": user_agent}, "woothee/upstream-corpus"), identifier)
    print(f"PASS Woothee upstream corpus: {len(user_agents)} user agents")
    return len(user_agents)


def run_integration(rust_url: str, dotnet_url: str, token: str) -> int:
    cases = [
        Case("health", {}, "integration/health"),
        Case("model_info", {}, "integration/model-info"),
        Case("classify", {"request_id": "parity-persist", "ip": "203.0.113.25", "user_agent": "GPTBot/1.0", "path": "/integration", "method": "GET"}, "integration/classify-persist"),
        Case("feedback", {"request_id": "parity-persist", "correct_verdict": "block", "notes": "parity", "reporter": "differential-suite"}, "integration/feedback"),
        Case("replay_decision", {"request_id": "parity-persist", "deterministic": True}, "integration/replay"),
        Case("calibration_report", {"window_hours": 1}, "integration/calibration"),
        Case("drift_report", {"window_hours": 1}, "integration/drift"),
        Case("threat_lookup", {"indicator": "198.51.100.9", "type": "ip"}, "integration/threat"),
        Case("enrich_ip", {"ip": "198.51.100.9"}, "integration/ip-reputation"),
        Case("enrich_ip", {"ip": "81.2.69.160"}, "integration/maxmind-city-anonymous"),
        Case("enrich_ip", {"ip": "203.0.113.25"}, "integration/maxmind-unknown"),
        Case("enrich_asn", {"asn": 15169}, "integration/maxmind-asn"),
        Case("canary_eval", {"token": "parity-canary-token", "context": {"path": "/protected"}}, "integration/canary"),
        Case("queue_status", {"queue": "parity"}, "integration/queue"),
    ]
    for identifier, case in enumerate(cases, 1300):
        compare_case(rust_url, dotnet_url, case, identifier, token)

    cache_params = {"ip": "192.0.2.44", "user_agent": "Mozilla/5.0", "path": "/cache-scope", "method": "GET"}
    for url in (rust_url, dotnet_url):
        first = Case("classify", {**cache_params, "request_id": "scope-one"}, "cache/scope-one")
        second = Case("classify", {**cache_params, "request_id": "scope-two"}, "cache/scope-two")
        first_result = rpc(url, first, 1400, token)[1]
        second_result = rpc(url, second, 1401, "parity-token-two")[1]
        if first_result["result"]["request_id"] != "scope-one" or second_result["result"]["request_id"] != "scope-two":
            raise AssertionError(f"caller-scoped cache isolation failed for {url}")

    print(f"PASS integrations: {len(cases)} Redis/PostgreSQL/MaxMind cases and caller-scoped cache behavior")
    return len(cases) + 4


def run_timeout(rust_url: str, dotnet_url: str, token: str) -> int:
    case = Case("feedback", {"request_id": "parity-persist", "correct_verdict": "block"}, "timeout/feedback-lock")
    rust = rpc(rust_url, case, 1500, token)
    dotnet = rpc(dotnet_url, case, 1500, token)
    if (rust[0], normalize(rust[1])) != (dotnet[0], normalize(dotnet[1])):
        raise AssertionError(f"timeout behavior differs: Rust={rust}, .NET={dotnet}")
    for url, (_, response) in ((rust_url, rust), (dotnet_url, dotnet)):
        if response["error"]["code"] != -504:
            raise AssertionError(f"expected tool timeout from {url}, received {response}")
    print("PASS timeout parity: blocked PostgreSQL operation is bounded by the tool deadline")
    return 2


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rust-url", required=True)
    parser.add_argument("--dotnet-url", required=True)
    parser.add_argument("--suite", choices=("core", "disabled", "auth", "tls", "config", "woothee", "integration", "timeout"), default="core")
    parser.add_argument("--token", default="parity-token-one")
    parser.add_argument("--attestation-key", default="parity-attestation-key-32-bytes-minimum")
    parser.add_argument("--woothee-tests", type=pathlib.Path)
    args = parser.parse_args()
    if args.suite == "woothee" and args.woothee_tests is None:
        parser.error("--woothee-tests is required for the woothee suite")
    runners = {"core": lambda: run_core(args.rust_url, args.dotnet_url), "disabled": lambda: run_disabled(args.rust_url, args.dotnet_url), "auth": lambda: run_auth(args.rust_url, args.dotnet_url, args.token), "tls": lambda: run_tls(args.rust_url, args.dotnet_url, args.attestation_key), "config": lambda: run_config(args.rust_url, args.dotnet_url), "woothee": lambda: run_woothee(args.rust_url, args.dotnet_url, args.woothee_tests), "integration": lambda: run_integration(args.rust_url, args.dotnet_url, args.token), "timeout": lambda: run_timeout(args.rust_url, args.dotnet_url, args.token)}
    try:
        count = runners[args.suite]()
        print(f"Parity assertions passed: {count}")
        return 0
    except (AssertionError, ConnectionError, OSError, ValueError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
