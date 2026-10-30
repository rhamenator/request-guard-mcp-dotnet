#!/usr/bin/env python3
"""Scoped classifier comparison; does not claim full current-Rust parity."""

import argparse
import json
import pathlib
import subprocess
import tempfile

from contract import Case, normalize, rpc
from run import DOTNET_PORT, ROOT, RUST_PORT, Pair, build, clean_environment


CASES = [
    ({"user_agent": ua}, "ua_scraper", expected)
    for ua, expected in [
        ("masscan/1.3", True), ("Nikto", True), ("sqlmap/1.0", True),
        ("Apache-HttpClient/4.5", True), ("HeadlessChrome/120", True),
        ("masscanary browser", False), ("niktopolis", False),
        ("sqlmapper", False), ("myhttpclient", False),
    ]
] + [
    ({"path": path}, "path_sensitive", expected)
    for path, expected in [
        ("/%2eenv", True), ("/%252eenv", True), ("/%25252eenv", True),
        ("/%2525252eenv", True), ("/%252525252eenv", False),
        ("/%61dmin", True), ("/%2Fadmin", True), ("\\admin", True),
        ("/%255cadmin", True), ("/%FF/admin", True), ("/%ZZ/admin", True),
        ("/internal", True), ("/internal?x=1", True), ("/internal/metrics", True),
        ("/xmlrpc.php", True), ("/xmlrpc.php?x=1", True),
        ("/welcome", False), ("/internalized", False), ("/xmlrpc.phpish", False),
        ("/%", False), ("/%2", False),
    ]
] + [({"path": "/a%2Fb%2Fc%2Fd%2Fe%2Ff%2Fg%2Fh%2Fi"}, "path_deep", True)]


def assert_startup_rejected(command, workdir, overrides, label):
    environment = {
        **clean_environment(),
        "MCP__HOST": "127.0.0.1",
        "MCP__PORT": "18317",
        "AUTH_TOKENS": "synthetic-config-parity-token-at-least-32-bytes",
        **overrides,
    }
    process = subprocess.Popen(command, cwd=workdir, env=environment,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    try:
        try:
            output, _ = process.communicate(timeout=5)
        except subprocess.TimeoutExpired:
            process.terminate()
            process.wait(timeout=5)
            raise AssertionError(f"{label} was accepted")
        if process.returncode == 0:
            raise AssertionError(f"{label} exited successfully instead of rejecting configuration: {output}")
    finally:
        if process.poll() is None:
            process.kill()
            process.wait(timeout=5)


def check_startup_security(rust_repo, revision):
    commands = [
        ("rust", [str(rust_repo / "target/debug/request-guard-mcp")], rust_repo),
        ("dotnet", ["dotnet", str(ROOT / "src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll")], ROOT),
    ]
    shared = [
        ({"AUTH_TOKENS": "short-token"}, "short bearer token"),
        ({"MCP__AUTH__ENABLED": "false", "MCP__HOST": "0.0.0.0"},
         "authentication-disabled non-loopback listener"),
        ({"MCP_ALLOWED_ORIGINS": ",".join(f"https://client-{index}.example" for index in range(33))},
         "33-entry browser origin allowlist"),
    ]
    current_only = [
        ({"AUTH_TOKENS": "prefix-replace_me-and-enough-token-bytes-123456"},
         "embedded bearer-token placeholder"),
        ({"CACHE_SCOPE_HMAC_KEY": "prefix_replace_me_0123456789abcdef0123456789abcdef"},
         "embedded cache-scope placeholder"),
        ({"TLS_FINGERPRINT_ATTESTATION_KEY": "prefix_replace_me_0123456789abcdef0123456789abcdef"},
         "embedded TLS-attestation placeholder"),
    ] if revision == "2660c1fbfd71b38108dfc7b9decd309e4a8e480c" else []
    for implementation, command, workdir in commands:
        for overrides, case in shared + current_only:
            assert_startup_rejected(command, workdir, overrides, f"{implementation}: {case}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rust-repo", type=pathlib.Path, required=True)
    parser.add_argument("--expected-rust-revision", required=True)
    args = parser.parse_args()
    rust_repo = args.rust_repo.resolve()
    actual = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=rust_repo, text=True).strip()
    if actual != args.expected_rust_revision:
        raise RuntimeError(f"Rust revision mismatch: {actual}")
    build(rust_repo)
    check_startup_security(rust_repo, actual)
    token = "synthetic-classifier-parity-token"
    with tempfile.TemporaryDirectory(prefix="classifier-parity-") as directory:
        with Pair(
            rust_repo / "target/debug/request-guard-mcp",
            ROOT / "src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll",
            pathlib.Path(directory),
            {"MCP__HOST": "127.0.0.1", "AUTH_TOKENS": token,
             "CACHE_SCOPE_HMAC_KEY": "synthetic-classifier-cache-key-32-bytes"},
        ):
            for index, (fields, signal, expected) in enumerate(CASES):
                case = Case("classify", {**fields, "request_id": f"classifier-{index}"}, str(fields))
                outcomes = []
                for port in (RUST_PORT, DOTNET_PORT):
                    status, envelope = rpc(f"http://127.0.0.1:{port}", case, index, token)
                    if status != 200 or envelope.get("error") is not None:
                        raise AssertionError((port, status, envelope))
                    result = envelope["result"]
                    found = any(item["name"] == signal for item in result["signals"])
                    if found != expected:
                        raise AssertionError((port, fields, signal, expected, result))
                    # Compare all classification behavior; only measured latency and runtime
                    # model build metadata are outside this scoped contract.
                    outcomes.append({key: value for key, value in result.items()
                                     if key not in {"latency_ms", "model_version"}})
                if outcomes[0] != outcomes[1]:
                    raise AssertionError(json.dumps({"case": fields, "outcomes": outcomes}, sort_keys=True))

            snapshots = []
            for port in (RUST_PORT, DOTNET_PORT):
                url = f"http://127.0.0.1:{port}"
                status, rejected = rpc(url, Case("config_snapshot", {"redact_secrets": False},
                                                  "config-snapshot/unredacted"), 1000, token)
                if status != 200 or rejected.get("error", {}).get("code") != -400:
                    raise AssertionError((port, status, rejected))
                status, redacted = rpc(url, Case("config_snapshot", {"redact_secrets": True},
                                                  "config-snapshot/redacted"), 1001, token)
                if status != 200 or redacted.get("error") is not None:
                    raise AssertionError((port, status, redacted))
                if redacted["result"]["snapshot"]["telemetry"]["otlp_endpoint"] is not None:
                    raise AssertionError((port, redacted))
                snapshots.append(normalize(redacted, legacy_envelope=port == RUST_PORT))
            if snapshots[0] != snapshots[1]:
                raise AssertionError(json.dumps({"config_snapshots": snapshots}, sort_keys=True))
    print(f"PASS: {len(CASES)} classifier cases plus config snapshot and startup security at {actual}")


if __name__ == "__main__":
    main()
