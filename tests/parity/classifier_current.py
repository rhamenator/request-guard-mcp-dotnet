#!/usr/bin/env python3
"""Scoped classifier comparison; does not claim full current-Rust parity."""

import argparse
import json
import pathlib
import subprocess
import tempfile

from contract import Case, normalize, rpc
from run import DOTNET_PORT, ROOT, RUST_PORT, Pair, build, clean_environment


SHARED_CASES = [
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
] + [({"path": "/a%2Fb%2Fc%2Fd%2Fe%2Ff%2Fg%2Fh%2Fi"}, "path_deep", True)] + [
    ({"user_agent": user_agent}, "ua_ai_bot", True)
    for user_agent in [
        "GPTBot", "ChatGPT-User", "Claude-Web", "anthropic-ai", "Bytespider", "CCBot",
        "cohere-ai", "DuckAssistBot", "FacebookBot", "Google-Extended", "ImagesiftBot",
        "PerplexityBot", "Scrapy", "python-httpx", "python-requests", "aiohttp", "curl/",
        "wget/", "libwww-perl", "Go-http-client", "Java/", "okhttp",
    ]
] + [
    ({"user_agent": user_agent}, "ua_scraper", True)
    for user_agent in [
        "beautifulsoup", "mechanize", "selenium", "phantom", "puppeteer", "playwright",
        "headless", "crawler", "spider", "bot", "scraper", "masscan", "nikto", "sqlmap",
        "httpclient",
    ]
] + [
    ({"headers": {}}, "header_missing_accept", True),
    ({"headers": {}}, "header_missing_accept_language", True),
    ({"headers": {"AcCePt": "application/json", "ACCEPT-LANGUAGE": "en"}},
     "header_missing_accept", False),
    ({"headers": {"AcCePt": "application/json", "ACCEPT-LANGUAGE": "en"}},
     "header_missing_accept_language", False),
] + [
    ({"method": method}, "method_unusual", expected)
    for method, expected in [
        ("GET", False), ("post", False), ("Put", False), ("PATCH", False),
        ("DELETE", False), ("HEAD", False), ("OPTIONS", False),
        ("TRACE", True), ("CONNECT", True), ("", True),
    ]
]

CURRENT_MAIN_CASES = [
    ({"path": path}, "path_sensitive", True)
    for path in [
        "/wp-config.php", "/wp-config.php.bak", "/wp-config.php_bak", "/wp-config.php-old",
        "/.aws/credentials", "/.ssh/id_rsa", "/.svn/entries", "/.htaccess",
        "/server-status", "/phpinfo.php", "/backup.sql", "/backup.sql_old",
        "/.git-credentials", "/config.json", "/.env.production", "/.env-backup",
        "/.env_prod", "/.env_local", "/.env-sample", "/.envrc", "/administrator",
        "/admin-panel", "/admin_old", "/admin123", "/adminer.php", "/admin-login",
        "/admin_console", "/admin-dashboard", "/admin_area", "/admin-ajax.php",
        "/admin-api", "/admin-users", "/adminpanel", "/admin-settings", "/admin-portal",
        "/admin_backup", "/api/internal-api", "/.envs/", "/administration",
        "/admin;jsessionid=1", "/admin%00.png", "/admin%3fx", "/%3f/admin",
        "/x%23/admin", "/etc/passwd%00.jpg", "/phpMyAdmin-4.9.1", "/phpmyadmin2",
        "/prod.env", "/repo.git/config", "/configuration.php", "/config-prod",
        "/config_local", "/config-staging", "/config-bak", "/configs", "/debug_toolbar",
        "/debug-info", "/graphql-playground", "/graphqlv1", "/swagger-ui-dist/",
        "/swagger-docs", "/debug-toolbar", "/api/internal-v2", "/.env~1",
        "/swagger-ui.html", "/swagger-ui/index.html", "/swagger-resources",
    ]
] + [
    ({"path": path}, "path_sensitive", False)
    for path in [
        "/products/configurable-shirt", "/docs/administrator-guide", "/docs/admin-guide",
        "/blog/node.environment-setup", "/api/internalized", "/xmlrpc.phpish",
        "/internal.example.com", "/proc.html", "/wp-config.phpish", "/backup.sqlish",
        "/docs/server-status-guide", "/wp-login.php", "/cgi-bin/status",
        "/search?q=/admin", "/search?q=/%61dmin", "/search#fragment=/admin",
    ]
]

EXPECTED_DEFAULT_SNAPSHOT = {
    "log_level": "info",
    "limits": {
        "max_request_bytes": 1024 * 1024,
        "max_batch_size": 50,
        "global_concurrency": 256,
        "per_tool_timeout_secs": 30,
        "classify_timeout_secs": 5,
    },
    "features": {
        "enable_batch": True,
        "enable_enrichment": True,
        "enable_feedback": True,
    },
    "auth": {"enabled": True, "token_count": 1},
    "telemetry": {
        "service_name": "request-guard-mcp",
        "metrics_path": "/metrics",
        "otlp_endpoint": None,
    },
    "redis": {
        "configured": False,
        "pool_size": 16,
        "key_prefix": "request_guard",
        "cache_ttl_secs": 300,
    },
    "postgres": {
        "configured": False,
        "max_connections": 10,
        "connect_timeout_secs": 5,
    },
    "geoip": {
        "configured": False,
        "city_configured": False,
        "asn_configured": False,
        "anonymous_ip_configured": False,
    },
    "tls_fingerprints": {
        "attestation_key_configured": False,
        "previous_attestation_key_configured": False,
        "max_age_seconds": 60,
        "known_bad_ja3_count": 0,
        "known_bad_ja4_count": 0,
    },
}


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
        ({"MCP_ALLOWED_ORIGINS": "https://client.example/path"}, "browser origin with a path"),
        ({"CACHE_SCOPE_HMAC_KEY": "short-cache-key"}, "short cache-scope key"),
        ({"MCP__POSTGRES__URL": "postgres://127.0.0.1/unreachable"},
         "authenticated PostgreSQL without a stable caller-scope key"),
        ({"TLS_FINGERPRINT_ATTESTATION_KEY": "short-attestation-key"},
         "short TLS attestation key"),
        ({"TLS_FINGERPRINT_ATTESTATION_PREVIOUS_KEY": "short-previous-key"},
         "short previous TLS attestation key"),
        ({"TLS_FINGERPRINT_ATTESTATION_MAX_AGE_SECONDS": "not-an-integer"},
         "non-integer TLS attestation age"),
        ({"TLS_FINGERPRINT_ATTESTATION_MAX_AGE_SECONDS": "0"},
         "zero TLS attestation age"),
        ({"TLS_KNOWN_BAD_JA3": "malformed"}, "malformed known-bad JA3"),
        ({"TLS_KNOWN_BAD_JA4": "malformed"}, "malformed known-bad JA4"),
        ({"MCP__TELEMETRY__METRICS_PATH": "metrics"}, "relative metrics path"),
        ({"MCP__TELEMETRY__METRICS_PATH": "/mcp"}, "reserved metrics path"),
        ({"MCP__TELEMETRY__OTLP_ENDPOINT": "ftp://collector.example"},
         "non-HTTP OTLP endpoint"),
        ({"MCP__REDIS__POOL_SIZE": "0"}, "zero Redis pool size"),
        ({"MCP__REDIS__CACHE_TTL_SECS": "0"}, "zero Redis cache TTL"),
        ({"MCP__REDIS__KEY_PREFIX": "invalid prefix"}, "invalid Redis key prefix"),
        ({"MCP__REDIS__URL": " "}, "blank Redis URL"),
        ({"MCP__POSTGRES__MAX_CONNECTIONS": "0"}, "zero PostgreSQL connection count"),
        ({"MCP__POSTGRES__CONNECT_TIMEOUT_SECS": "0"}, "zero PostgreSQL connection timeout"),
        ({"MCP__POSTGRES__URL": " "}, "blank PostgreSQL URL"),
        ({"MCP__GEOIP__MMDB_PATH": " "}, "blank legacy GeoIP path"),
        ({"MCP__GEOIP__CITY_MMDB_PATH": " "}, "blank City GeoIP path"),
        ({"MCP__GEOIP__ASN_MMDB_PATH": " "}, "blank ASN GeoIP path"),
        ({"MCP__GEOIP__ANONYMOUS_IP_MMDB_PATH": " "}, "blank anonymous-IP GeoIP path"),
    ]
    for setting in [
        "MAX_REQUEST_BYTES", "MAX_BATCH_SIZE", "GLOBAL_CONCURRENCY", "MAX_CONNECTIONS",
        "HTTP_HEADER_TIMEOUT_SECS", "HTTP_BODY_TIMEOUT_SECS", "WEBSOCKET_IDLE_TIMEOUT_SECS",
        "PER_TOOL_TIMEOUT_SECS", "CLASSIFY_TIMEOUT_SECS",
    ]:
        shared.append(({f"MCP__LIMITS__{setting}": "0"}, f"zero {setting.lower()} limit"))
    current_only = [
        ({"AUTH_TOKENS": "prefix-replace_me-and-enough-token-bytes-123456"},
         "embedded bearer-token placeholder"),
        ({"CACHE_SCOPE_HMAC_KEY": "prefix_replace_me_0123456789abcdef0123456789abcdef"},
         "embedded cache-scope placeholder"),
        ({"TLS_FINGERPRINT_ATTESTATION_KEY": "prefix_replace_me_0123456789abcdef0123456789abcdef"},
         "embedded TLS-attestation placeholder"),
        ({"MCP__LIMITS__HTTP2_IDLE_TIMEOUT_SECS": "0"}, "zero HTTP/2 idle timeout"),
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
    cases = SHARED_CASES + (CURRENT_MAIN_CASES if actual == "2660c1fbfd71b38108dfc7b9decd309e4a8e480c" else [])
    token = "synthetic-classifier-parity-token"
    with tempfile.TemporaryDirectory(prefix="classifier-parity-") as directory:
        with Pair(
            rust_repo / "target/debug/request-guard-mcp",
            ROOT / "src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll",
            pathlib.Path(directory),
            {"MCP__HOST": "127.0.0.1", "AUTH_TOKENS": token,
             "CACHE_SCOPE_HMAC_KEY": "synthetic-classifier-cache-key-32-bytes"},
        ):
            for index, (fields, signal, expected) in enumerate(cases):
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
                snapshot = redacted["result"]["snapshot"]
                stable_snapshot = {key: value for key, value in snapshot.items()
                                   if key not in {"host", "port"}}
                if stable_snapshot != EXPECTED_DEFAULT_SNAPSHOT:
                    raise AssertionError(json.dumps({"port": port, "expected": EXPECTED_DEFAULT_SNAPSHOT,
                                                     "actual": stable_snapshot}, sort_keys=True))
                snapshots.append(normalize(redacted, legacy_envelope=port == RUST_PORT))
            if snapshots[0] != snapshots[1]:
                raise AssertionError(json.dumps({"config_snapshots": snapshots}, sort_keys=True))
    print(f"PASS: {len(cases)} classifier cases plus config snapshot and startup security at {actual}")


if __name__ == "__main__":
    main()
