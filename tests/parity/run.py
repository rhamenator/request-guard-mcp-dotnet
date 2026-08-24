#!/usr/bin/env python3
"""Build, launch, and compare the pinned Rust baseline and the .NET port."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request


ROOT = pathlib.Path(__file__).resolve().parents[2]
CONTRACT = pathlib.Path(__file__).with_name("contract.py")
BASELINE = pathlib.Path(__file__).with_name("rust-baseline.txt")
RUST_PORT = 18287
DOTNET_PORT = 18288
MMDB_COMMIT = "e1120013c4b5cbc830b958b2b7e73fba444d316d"
MMDB_FIXTURES = {
    "GeoIP2-City-Test.mmdb": "ed972738e4e03a3e56e12041a6af4d91592249d110f7e4a647e5f2fa0e639c09",
    "GeoLite2-ASN-Test.mmdb": "75901b98ed6e58d3bd41af9985044b747a7ec0be1369f930c24f5e044427181a",
    "GeoIP2-Anonymous-IP-Test.mmdb": "d080856c8dfb306780a2c20cf1b32fd873bd6ac262fbe5cb5ea37e497348244d",
}

CONFIG_FILES = {
    "config.json": '{"limits":{"max_request_bytes":123456,"max_batch_size":7},"redis":{"key_prefix":"parity_config"},"telemetry":{"service_name":"parity-config-service"}}',
    "config.ini": "[limits]\nmax_request_bytes=123456\nmax_batch_size=7\n[redis]\nkey_prefix=parity_config\n[telemetry]\nservice_name=parity-config-service\n",
    "config.toml": "[limits]\nmax_request_bytes=123456\nmax_batch_size=7\n[redis]\nkey_prefix='parity_config'\n[telemetry]\nservice_name='parity-config-service'\n",
    "config.yaml": "limits:\n  max_request_bytes: 123456\n  max_batch_size: 7\nredis:\n  key_prefix: parity_config\ntelemetry:\n  service_name: parity-config-service\n",
    "config.yml": "limits:\n  max_request_bytes: 123456\n  max_batch_size: 7\nredis: {key_prefix: parity_config}\ntelemetry: {service_name: parity-config-service}\n",
    "config.ron": "(limits:(max_request_bytes:123456,max_batch_size:7),redis:(key_prefix:\"parity_config\"),telemetry:(service_name:\"parity-config-service\"))",
    "config.json5": "{limits:{max_request_bytes:123456,max_batch_size:7},redis:{key_prefix:'parity_config'},telemetry:{service_name:'parity-config-service'}}",
}


def clean_environment() -> dict[str, str]:
    names = {
        "AUTH_TOKENS",
        "CACHE_SCOPE_HMAC_KEY",
        "CONFIG_FILE",
        "LOG_LEVEL",
        "TLS_FINGERPRINT_ATTESTATION_KEY",
        "TLS_FINGERPRINT_ATTESTATION_PREVIOUS_KEY",
        "TLS_FINGERPRINT_ATTESTATION_MAX_AGE_SECONDS",
        "TLS_KNOWN_BAD_JA3",
        "TLS_KNOWN_BAD_JA4",
    }
    return {key: value for key, value in os.environ.items() if not key.startswith("MCP__") and key not in names}


class Pair:
    def __init__(self, rust_binary: pathlib.Path, dotnet_dll: pathlib.Path, workdir: pathlib.Path, common: dict[str, str], rust_only: dict[str, str] | None = None, dotnet_only: dict[str, str] | None = None):
        base = clean_environment()
        rust_environment = {**base, **common, **(rust_only or {}), "MCP__PORT": str(RUST_PORT), "RUST_LOG": "error"}
        dotnet_environment = {**base, **common, **(dotnet_only or {}), "MCP__PORT": str(DOTNET_PORT)}
        self.logs: list[object] = []
        self.processes: list[subprocess.Popen[bytes]] = []
        try:
            for name, command, environment in (
                ("rust", [str(rust_binary)], rust_environment),
                ("dotnet", ["dotnet", str(dotnet_dll)], dotnet_environment),
            ):
                log = open(workdir / f"{name}.log", "ab", buffering=0)
                self.logs.append(log)
                self.processes.append(subprocess.Popen(command, cwd=workdir, env=environment, stdout=log, stderr=subprocess.STDOUT))
            self._wait_ready()
        except BaseException:
            self.stop()
            raise

    def _wait_ready(self) -> None:
        deadline = time.monotonic() + 20
        urls = [f"http://127.0.0.1:{RUST_PORT}/health", f"http://127.0.0.1:{DOTNET_PORT}/health"]
        while time.monotonic() < deadline:
            if any(process.poll() is not None for process in self.processes):
                raise RuntimeError("a parity server exited during startup")
            try:
                if all(urllib.request.urlopen(url, timeout=1).status == 200 for url in urls):
                    return
            except (OSError, urllib.error.URLError):
                pass
            time.sleep(0.1)
        raise TimeoutError("parity servers did not become healthy within 20 seconds")

    def stop(self) -> None:
        for process in getattr(self, "processes", []):
            if process.poll() is None:
                process.terminate()
        for process in getattr(self, "processes", []):
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        for log in getattr(self, "logs", []):
            log.close()

    def __enter__(self) -> "Pair":
        return self

    def __exit__(self, *_: object) -> None:
        self.stop()


class IntegrationBackends:
    REDIS_IMAGE = "redis:8.2-alpine@sha256:30abb90e62f14b737010746def3ba99cc79fe19dcdb3d37b41f21fc62e7da19d"
    POSTGRES_IMAGE = "postgres:18-alpine@sha256:d3e1620b530c944afa6e887d22eb899824da68e19c52024bf98f5220c88a65b2"

    def __init__(self) -> None:
        suffix = f"{os.getpid()}-{int(time.time())}"
        self.redis_name = f"request-guard-parity-redis-{suffix}"
        self.postgres_name = f"request-guard-parity-postgres-{suffix}"
        self.password = "parity-backend-password"
        self.redis_port = 0
        self.postgres_port = 0

    def start(self) -> None:
        try:
            subprocess.run(["docker", "run", "-d", "--rm", "--name", self.redis_name, "-p", "127.0.0.1::6379", self.REDIS_IMAGE, "redis-server", "--requirepass", self.password], check=True, stdout=subprocess.DEVNULL)
            subprocess.run(["docker", "run", "-d", "--rm", "--name", self.postgres_name, "-p", "127.0.0.1::5432", "-e", "POSTGRES_USER=postgres", "-e", f"POSTGRES_PASSWORD={self.password}", self.POSTGRES_IMAGE], check=True, stdout=subprocess.DEVNULL)
            self.redis_port = self._published_port(self.redis_name, "6379/tcp")
            self.postgres_port = self._published_port(self.postgres_name, "5432/tcp")
            self._wait()
            for database in ("parity_rust", "parity_dotnet"):
                self.postgres("createdb", "-U", "postgres", database)
        except BaseException:
            self.stop()
            raise

    @staticmethod
    def _published_port(name: str, container_port: str) -> int:
        output = subprocess.run(["docker", "port", name, container_port], check=True, text=True, stdout=subprocess.PIPE).stdout.strip()
        return int(output.rsplit(":", 1)[1])

    def _wait(self) -> None:
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            redis = subprocess.run(["docker", "exec", self.redis_name, "redis-cli", "-a", self.password, "ping"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            postgres = subprocess.run(["docker", "exec", self.postgres_name, "pg_isready", "-U", "postgres"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            if redis.returncode == 0 and postgres.returncode == 0:
                return
            time.sleep(0.25)
        raise TimeoutError("Redis/PostgreSQL test containers did not become ready")

    def redis(self, *arguments: str, capture: bool = False) -> str:
        result = subprocess.run(["docker", "exec", self.redis_name, "redis-cli", "--no-auth-warning", "-a", self.password, *arguments], check=True, text=True, stdout=subprocess.PIPE if capture else subprocess.DEVNULL)
        return result.stdout.strip() if capture else ""

    def postgres(self, *arguments: str, database: str | None = None, background: bool = False) -> subprocess.Popen[bytes] | None:
        command = ["docker", "exec", "-e", f"PGPASSWORD={self.password}", self.postgres_name, *arguments]
        if background:
            return subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        subprocess.run(command, check=True, stdout=subprocess.DEVNULL)
        return None

    def postgres_query(self, database: str, sql: str) -> str:
        result = subprocess.run(
            ["docker", "exec", "-e", f"PGPASSWORD={self.password}", self.postgres_name, "psql", "-At", "-U", "postgres", "-d", database, "-c", sql],
            check=True,
            text=True,
            stdout=subprocess.PIPE,
        )
        return result.stdout.strip()

    def seed(self) -> None:
        threat = json.dumps({"threat_type": "scanner", "severity": "high", "source": "parity-suite", "last_seen": "2026-08-11T00:00:00Z", "metadata": None}, separators=(",", ":"))
        canary_hash = hashlib.sha256(b"parity-canary-token").hexdigest()
        canary = json.dumps({"canary_id": "parity-canary", "metadata": {"owner": "parity-suite"}}, separators=(",", ":"))
        now = str(int(time.time()))
        for prefix in ("parity_rust", "parity_dotnet"):
            self.redis("HSET", f"{prefix}:threats:ip", "198.51.100.9", threat)
            self.redis("HSET", f"{prefix}:canaries", canary_hash, canary)
            self.redis("ZADD", f"{prefix}:queue:parity:active", now, "parity-operation")

    def assert_caller_scopes(self) -> None:
        for prefix in ("parity_rust", "parity_dotnet"):
            count = int(self.redis("--scan", "--pattern", f"{prefix}:cache:*", capture=True).count("\n") + 1)
            if count < 2:
                raise AssertionError(f"expected at least two caller-scoped cache keys for {prefix}, found {count}")

    def lock_feedback_tables(self) -> list[subprocess.Popen[bytes]]:
        locks = []
        for database in ("parity_rust", "parity_dotnet"):
            process = self.postgres("psql", "-U", "postgres", "-d", database, "-v", "ON_ERROR_STOP=1", "-c", "BEGIN; LOCK TABLE mcp_feedback IN ACCESS EXCLUSIVE MODE; SELECT pg_sleep(4);", background=True)
            assert process is not None
            locks.append(process)
        time.sleep(0.5)
        return locks

    def stop(self) -> None:
        for name in (self.redis_name, self.postgres_name):
            subprocess.run(["docker", "rm", "-f", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def __enter__(self) -> "IntegrationBackends":
        self.start()
        return self

    def __exit__(self, *_: object) -> None:
        self.stop()


def download_mmdb_fixtures(directory: pathlib.Path) -> dict[str, pathlib.Path]:
    paths = {}
    for filename, expected_hash in MMDB_FIXTURES.items():
        path = directory / filename
        url = f"https://raw.githubusercontent.com/maxmind/MaxMind-DB/{MMDB_COMMIT}/test-data/{filename}"
        with urllib.request.urlopen(url, timeout=30) as response:
            contents = response.read()
        actual_hash = hashlib.sha256(contents).hexdigest()
        if actual_hash != expected_hash:
            raise RuntimeError(f"MaxMind fixture hash mismatch for {filename}: {actual_hash}")
        path.write_bytes(contents)
        paths[filename] = path
    return paths


def run_contract(suite: str, *extra: str) -> None:
    subprocess.run(
        [sys.executable, str(CONTRACT), "--rust-url", f"http://127.0.0.1:{RUST_PORT}", "--dotnet-url", f"http://127.0.0.1:{DOTNET_PORT}", "--suite", suite, *extra],
        cwd=ROOT,
        check=True,
    )


def verify_baseline(rust_repo: pathlib.Path) -> None:
    expected = BASELINE.read_text(encoding="utf-8").strip()
    actual = subprocess.run(["git", "rev-parse", "HEAD"], cwd=rust_repo, check=True, text=True, stdout=subprocess.PIPE).stdout.strip()
    if actual != expected:
        raise RuntimeError(f"Rust baseline mismatch: expected {expected}, found {actual}. Review parity and update rust-baseline.txt intentionally.")


def build(rust_repo: pathlib.Path) -> None:
    subprocess.run(["dotnet", "build", "RequestGuardMcp.slnx", "--configuration", "Release", "--no-restore", "--warnaserror"], cwd=ROOT, check=True)
    subprocess.run(["cargo", "build", "--locked"], cwd=rust_repo, check=True)


def woothee_tests_directory(rust_repo: pathlib.Path) -> pathlib.Path:
    metadata = subprocess.run(
        ["cargo", "metadata", "--locked", "--format-version", "1"],
        cwd=rust_repo,
        check=True,
        text=True,
        stdout=subprocess.PIPE,
    )
    packages = json.loads(metadata.stdout)["packages"]
    manifest = next(pathlib.Path(package["manifest_path"]) for package in packages if package["name"] == "woothee" and package["version"] == "0.13.0")
    tests = manifest.parent / "tests"
    if not tests.is_dir():
        raise FileNotFoundError(f"Woothee upstream tests were not found at {tests}")
    return tests


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rust-repo", type=pathlib.Path, default=ROOT.parent / "request-guard-mcp")
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--core-only", action="store_true")
    parser.add_argument("--with-integrations", action="store_true", help="run Redis, PostgreSQL, MaxMind, cache-scope, and timeout phases")
    args = parser.parse_args()
    rust_repo = args.rust_repo.resolve()
    verify_baseline(rust_repo)
    if not args.skip_build:
        build(rust_repo)

    rust_binary = rust_repo / "target" / "debug" / "request-guard-mcp"
    dotnet_dll = ROOT / "src" / "RequestGuardMcp.Host" / "bin" / "Release" / "net10.0" / "request-guard-mcp-dotnet.dll"
    if not rust_binary.exists() or not dotnet_dll.exists():
        raise FileNotFoundError("build outputs are missing; rerun without --skip-build")

    with tempfile.TemporaryDirectory(prefix="request-guard-parity-") as temporary:
        workdir = pathlib.Path(temporary)
        with Pair(rust_binary, dotnet_dll, workdir, {"MCP__AUTH__ENABLED": "false"}):
            run_contract("core")
            run_contract("woothee", "--woothee-tests", str(woothee_tests_directory(rust_repo)))
        if args.core_only:
            return 0

        with Pair(rust_binary, dotnet_dll, workdir, {"MCP__AUTH__ENABLED": "false", "MCP__FEATURES__ENABLE_BATCH": "false", "MCP__FEATURES__ENABLE_ENRICHMENT": "false", "MCP__FEATURES__ENABLE_FEEDBACK": "false"}):
            run_contract("disabled")

        token = "parity-token-one"
        with Pair(rust_binary, dotnet_dll, workdir, {"MCP__AUTH__ENABLED": "true", "AUTH_TOKENS": token, "CACHE_SCOPE_HMAC_KEY": "parity-cache-scope-key-32-bytes-long"}):
            run_contract("auth", "--token", token)

        attestation_key = "parity-attestation-key-32-bytes-minimum"
        with Pair(rust_binary, dotnet_dll, workdir, {"MCP__AUTH__ENABLED": "false", "TLS_FINGERPRINT_ATTESTATION_KEY": attestation_key, "TLS_KNOWN_BAD_JA3": "72a589da586844d7f0818ce684948eea", "TLS_KNOWN_BAD_JA4": "t13d1516h2_8daaf6152771_e5627efa2ab1"}):
            run_contract("tls", "--attestation-key", attestation_key)

        for filename, contents in CONFIG_FILES.items():
            path = workdir / filename
            path.write_text(contents, encoding="utf-8")
            with Pair(rust_binary, dotnet_dll, workdir, {"MCP__AUTH__ENABLED": "false", "CONFIG_FILE": str(path)}):
                run_contract("config")
            print(f"PASS CONFIG_FILE format: {path.suffix}")

        dotenv = workdir / ".env"
        dotenv.write_text("MCP__LIMITS__MAX_REQUEST_BYTES=123456\nMCP__LIMITS__MAX_BATCH_SIZE=7\nMCP__REDIS__KEY_PREFIX=parity_config\nMCP__TELEMETRY__SERVICE_NAME=parity-config-service\n", encoding="utf-8")
        with Pair(rust_binary, dotnet_dll, workdir, {"MCP__AUTH__ENABLED": "false"}):
            run_contract("config")
        print("PASS dotenv compatibility")

        if args.with_integrations:
            fixtures = download_mmdb_fixtures(workdir)
            with IntegrationBackends() as backends:
                backends.seed()
                token = "parity-token-one"
                common = {
                    "MCP__AUTH__ENABLED": "true",
                    "AUTH_TOKENS": f"{token},parity-token-two",
                    "CACHE_SCOPE_HMAC_KEY": "parity-cache-scope-key-32-bytes-long",
                    "MCP__LIMITS__PER_TOOL_TIMEOUT_SECS": "1",
                    "MCP__REDIS__URL": f"redis://:{backends.password}@127.0.0.1:{backends.redis_port}",
                    "MCP__GEOIP__CITY_MMDB_PATH": str(fixtures["GeoIP2-City-Test.mmdb"]),
                    "MCP__GEOIP__ASN_MMDB_PATH": str(fixtures["GeoLite2-ASN-Test.mmdb"]),
                    "MCP__GEOIP__ANONYMOUS_IP_MMDB_PATH": str(fixtures["GeoIP2-Anonymous-IP-Test.mmdb"]),
                }
                rust_only = {
                    "MCP__REDIS__KEY_PREFIX": "parity_rust",
                    "MCP__POSTGRES__URL": f"postgres://postgres:{backends.password}@127.0.0.1:{backends.postgres_port}/parity_rust",
                }
                dotnet_only = {
                    "MCP__REDIS__KEY_PREFIX": "parity_dotnet",
                    "MCP__POSTGRES__URL": f"Host=127.0.0.1;Port={backends.postgres_port};Database=parity_dotnet;Username=postgres;Password={backends.password}",
                }
                with Pair(rust_binary, dotnet_dll, workdir, common, rust_only, dotnet_only):
                    try:
                        run_contract("integration", "--token", token)
                    except subprocess.CalledProcessError:
                        score_query = "SELECT response_payload->>'score' FROM mcp_decisions WHERE request_id='parity-persist'"
                        for database in ("parity_rust", "parity_dotnet"):
                            print(f"DEBUG {database} persisted score: {backends.postgres_query(database, score_query)}", file=sys.stderr)
                        raise
                    backends.assert_caller_scopes()
                    locks = backends.lock_feedback_tables()
                    try:
                        run_contract("timeout", "--token", token)
                    finally:
                        for lock in locks:
                            lock.wait(timeout=10)

    print("PASS differential parity suite" + (" (including integrations)" if args.with_integrations else ""))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        raise SystemExit(1)
