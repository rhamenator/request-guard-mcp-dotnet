#!/usr/bin/env python3
"""Qualify PostgreSQL TLS trust/rejection and live outage recovery."""

from __future__ import annotations

import argparse
import json
import pathlib
import secrets
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

from run import IntegrationBackends, clean_environment


def command(*arguments: str, capture: bool = False, timeout: int = 45) -> str:
    result = subprocess.run(arguments, check=True, text=True, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, timeout=timeout)
    return result.stdout.strip() if capture else ""


def docker(*arguments: str, capture: bool = False, timeout: int = 45) -> str:
    return command("docker", *arguments, capture=capture, timeout=timeout)


def unused_port() -> int:
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet-dll", type=pathlib.Path, required=True)
    args = parser.parse_args()
    dll = args.dotnet_dll.resolve(strict=True)
    suffix = secrets.token_hex(6)
    container = f"rg-dotnet-pg-tls-{suffix}"
    volume = f"rg-dotnet-pg-tls-{suffix}"
    password = secrets.token_hex(24)
    token = secrets.token_hex(24)
    cache_key = secrets.token_hex(32)
    host_port = unused_port()
    postgres_port = unused_port()
    while postgres_port == host_port:
        postgres_port = unused_port()
    process: subprocess.Popen[bytes] | None = None
    paused = False
    created = False
    volume_created = False
    request_number = 0

    with tempfile.TemporaryDirectory(prefix="rg-dotnet-pg-tls-") as temporary:
        work = pathlib.Path(temporary)
        certs = work / "certs"
        certs.mkdir(mode=0o700)
        trusted_ca = certs / "trusted-ca.crt"
        trusted_key = certs / "trusted-ca.key"
        untrusted_ca = certs / "untrusted-ca.crt"
        server_key = certs / "server.key"
        server_csr = certs / "server.csr"
        server_cert = certs / "server.crt"
        extensions = certs / "server.ext"

        command("openssl", "req", "-x509", "-newkey", "rsa:2048", "-noenc", "-days", "1",
                "-subj", "/CN=Request Guard PostgreSQL Test CA",
                "-addext", "basicConstraints=critical,CA:TRUE",
                "-addext", "keyUsage=critical,keyCertSign,cRLSign",
                "-keyout", str(trusted_key), "-out", str(trusted_ca))
        command("openssl", "req", "-x509", "-newkey", "rsa:2048", "-noenc", "-days", "1",
                "-subj", "/CN=Untrusted Request Guard Test CA",
                "-keyout", str(certs / "untrusted-ca.key"), "-out", str(untrusted_ca))
        command("openssl", "req", "-newkey", "rsa:2048", "-noenc", "-subj", "/CN=localhost",
                "-keyout", str(server_key), "-out", str(server_csr))
        extensions.write_text("subjectAltName=DNS:localhost\nextendedKeyUsage=serverAuth\n", encoding="utf-8")
        command("openssl", "x509", "-req", "-in", str(server_csr), "-CA", str(trusted_ca),
                "-CAkey", str(trusted_key), "-CAcreateserial", "-days", "1",
                "-extfile", str(extensions), "-out", str(server_cert))

        def wait_postgres() -> None:
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                probe = subprocess.run(
                    ["docker", "exec", "--env", f"PGPASSWORD={password}", container,
                     "pg_isready", "-U", "postgres", "-d", "postgres"],
                    stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=5)
                if probe.returncode == 0:
                    return
                time.sleep(0.1)
            raise TimeoutError("TLS PostgreSQL container did not become ready")

        def connection_string(host: str, root: pathlib.Path) -> str:
            return (f"Host={host};Port={postgres_port};Database=postgres;Username=postgres;"
                    f"Password={password};SSL Mode=VerifyFull;Root Certificate={root};Timeout=2")

        def stop_host() -> None:
            nonlocal process
            if process is None:
                return
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)
            process = None

        def start_host(name: str, postgres_url: str, should_start: bool) -> pathlib.Path:
            nonlocal process
            log_path = work / f"{name}.log"
            environment = {
                **clean_environment(),
                "MCP__HOST": "127.0.0.1",
                "MCP__PORT": str(host_port),
                "AUTH_TOKENS": token,
                "CACHE_SCOPE_HMAC_KEY": cache_key,
                "MCP__POSTGRES__URL": postgres_url,
                "MCP__POSTGRES__MAX_CONNECTIONS": "3",
                "MCP__POSTGRES__CONNECT_TIMEOUT_SECS": "2",
                "MCP__LIMITS__PER_TOOL_TIMEOUT_SECS": "2",
                "MCP__LIMITS__CLASSIFY_TIMEOUT_SECS": "2",
            }
            log = open(log_path, "wb")
            process = subprocess.Popen(["dotnet", str(dll)], cwd=work, env=environment,
                                       stdout=log, stderr=subprocess.STDOUT)
            if should_start:
                deadline = time.monotonic() + 30
                try:
                    while time.monotonic() < deadline:
                        if process.poll() is not None:
                            raise RuntimeError(f"{name}: host exited before readiness")
                        try:
                            with urllib.request.urlopen(f"http://127.0.0.1:{host_port}/health", timeout=2) as response:
                                if response.status == 200:
                                    return log_path
                        except (OSError, urllib.error.URLError):
                            pass
                        time.sleep(0.1)
                    raise TimeoutError(f"{name}: host did not become ready")
                except BaseException:
                    log.flush()
                    lines = log_path.read_text(encoding="utf-8", errors="replace").splitlines()
                    diagnostic = "\n".join(lines[-20:])
                    for secret in (password, token, cache_key):
                        diagnostic = diagnostic.replace(secret, "<redacted>")
                    print(f"--- {name} startup log (last 20 lines) ---\n{diagnostic}", flush=True)
                    raise
                finally:
                    log.close()
            try:
                exit_code = process.wait(timeout=12)
                if exit_code == 0:
                    raise AssertionError(f"{name}: rejected TLS configuration exited successfully")
            except subprocess.TimeoutExpired:
                raise AssertionError(f"{name}: rejected TLS configuration started or hung") from None
            finally:
                log.close()
                stop_host()
            captured = log_path.read_bytes()
            if not captured or len(captured) > 128 * 1024:
                raise AssertionError(f"{name}: startup log was empty or unexpectedly large")
            return log_path

        def rpc(method: str, parameters: dict, *, timeout: float = 8) -> tuple[dict, float]:
            nonlocal request_number
            request_number += 1
            payload = {"jsonrpc": "2.0", "id": request_number, "method": method, "params": parameters}
            request = urllib.request.Request(
                f"http://127.0.0.1:{host_port}/mcp", json.dumps(payload).encode(),
                headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json",
                         "Accept": "application/json, text/event-stream"})
            started = time.monotonic()
            with urllib.request.urlopen(request, timeout=timeout) as response:
                result = json.load(response)
            return result, time.monotonic() - started

        def require_success(method: str, parameters: dict) -> dict:
            response, _ = rpc(method, parameters)
            if response.get("error") is not None or not isinstance(response.get("result"), dict):
                raise AssertionError(f"{method}: expected success, received {response}")
            return response["result"]

        def require_bounded_failure(method: str, parameters: dict, stage: str) -> None:
            response, duration = rpc(method, parameters)
            if response.get("result") is not None or not isinstance(response.get("error"), dict):
                raise AssertionError(f"{stage}: backend-dependent tool did not fail: {response}")
            if duration > 6:
                raise AssertionError(f"{stage}: backend failure took {duration:.3f}s")
            print(json.dumps({"stage": stage, "duration_seconds": duration,
                              "error_code": response["error"].get("code")}), flush=True)

        def recover(request_id: str, stage: str) -> None:
            deadline = time.monotonic() + 20
            last_response: dict | None = None
            last_error: BaseException | None = None
            while time.monotonic() < deadline:
                try:
                    last_response, _ = rpc("replay_decision", {"request_id": request_id, "deterministic": True})
                    if last_response.get("error") is not None or not isinstance(last_response.get("result"), dict):
                        raise AssertionError(f"replay_decision: expected success, received {last_response}")
                    result = last_response["result"]
                    if result.get("matches_original") is True:
                        print(f"PASS PostgreSQL recovery: {stage}", flush=True)
                        return
                except (OSError, urllib.error.URLError, AssertionError) as error:
                    last_error = error
                time.sleep(0.2)
            log_path = work / "trusted-ca.log"
            if log_path.exists():
                lines = log_path.read_text(encoding="utf-8", errors="replace").splitlines()
                diagnostic = "\n".join(lines[-12:])
                for secret in (password, token, cache_key):
                    diagnostic = diagnostic.replace(secret, "<redacted>")
                print(f"--- trusted-ca server log (last 12 lines) ---\n{diagnostic}", flush=True)
            raise AssertionError(
                f"{stage}: PostgreSQL-backed replay did not recover; "
                f"last_response={last_response!r}; last_error={last_error!r}")

        try:
            docker("volume", "create", volume)
            volume_created = True
            docker("run", "--rm", "--user", "root", "--cpus", "0.5", "--memory", "128m",
                   "--pids-limit", "64", "--mount", f"type=bind,source={certs},target=/source,readonly",
                   "--mount", f"source={volume},target=/target", IntegrationBackends.POSTGRES_IMAGE,
                   "sh", "-c", "cp /source/server.crt /source/server.key /target/ && "
                   "chown postgres:postgres /target/server.crt /target/server.key && "
                   "chmod 0644 /target/server.crt && chmod 0600 /target/server.key")
            docker("run", "--detach", "--name", container,
                   "--cpus", "1.0", "--memory", "512m", "--pids-limit", "256",
                   "--publish", f"127.0.0.1:{postgres_port}:5432", "--env", "POSTGRES_USER=postgres",
                   "--env", f"POSTGRES_PASSWORD={password}",
                   "--mount", f"source={volume},target=/tls", IntegrationBackends.POSTGRES_IMAGE,
                   "-c", "ssl=on", "-c", "ssl_cert_file=/tls/server.crt",
                   "-c", "ssl_key_file=/tls/server.key")
            created = True
            wait_postgres()

            trusted = connection_string("localhost", trusted_ca)
            wrong_ca_log = start_host("untrusted-ca", connection_string("localhost", untrusted_ca), False)
            mismatch_log = start_host("hostname-mismatch", connection_string("127.0.0.1", trusted_ca), False)
            for log_path in (wrong_ca_log, mismatch_log):
                captured = log_path.read_bytes()
                for secret in (password, token, cache_key):
                    if secret.encode() in captured:
                        raise AssertionError(f"synthetic secret leaked to {log_path.name}")
            print("PASS PostgreSQL VerifyFull rejection: untrusted CA and hostname mismatch", flush=True)

            positive_log = start_host("trusted-ca", trusted, True)
            request_id = f"postgres-tls-{suffix}"
            classified = require_success("classify", {
                "request_id": request_id, "user_agent": "GPTBot/1.0", "path": "/postgres-tls"})
            if classified.get("request_id") != request_id:
                raise AssertionError("trusted TLS classification request ID mismatch")
            replayed = require_success("replay_decision", {"request_id": request_id, "deterministic": True})
            if replayed.get("matches_original") is not True:
                raise AssertionError("trusted TLS deterministic replay failed")
            encrypted = docker(
                "exec", "--env", f"PGPASSWORD={password}", container, "psql", "-At", "-U", "postgres",
                "-d", "postgres", "-c",
                "SELECT COUNT(*) FROM pg_stat_activity a JOIN pg_stat_ssl s USING(pid) "
                "WHERE a.datname='postgres' AND a.usename='postgres' AND s.ssl",
                capture=True)
            if int(encrypted) < 1:
                raise AssertionError("PostgreSQL did not observe an encrypted application connection")
            print("PASS PostgreSQL VerifyFull trusted CA: classify, persistence, replay, encrypted session", flush=True)

            docker("pause", container)
            paused = True
            try:
                if docker("inspect", "--format", "{{.State.Paused}}", container, capture=True) != "true":
                    raise AssertionError("Docker did not report PostgreSQL as paused")
                require_bounded_failure("replay_decision", {"request_id": request_id, "deterministic": True},
                                        "paused replay")
            finally:
                docker("unpause", container)
                paused = False
            wait_postgres()
            recover(request_id, "unpaused")

            docker("stop", "--time", "1", container)
            if docker("inspect", "--format", "{{.State.Running}}", container, capture=True) != "false":
                raise AssertionError("Docker did not report PostgreSQL as stopped")
            require_bounded_failure("replay_decision", {"request_id": request_id, "deterministic": True},
                                    "stopped replay")
            require_bounded_failure("classify", {
                "request_id": f"{request_id}-outage", "user_agent": "GPTBot/1.0", "path": "/postgres-down"},
                "stopped classify persistence")
            docker("start", container)
            wait_postgres()
            recover(request_id, "restarted")
            print("PASS PostgreSQL TLS and healthy/pause/stop/restart recovery gate", flush=True)

            stop_host()
            captured = positive_log.read_bytes()
            if not captured or len(captured) > 256 * 1024:
                raise AssertionError("positive server log was empty or unexpectedly large")
            for secret in (password, token, cache_key):
                if secret.encode() in captured:
                    raise AssertionError("synthetic secret leaked to positive server log")
        finally:
            if paused:
                subprocess.run(["docker", "unpause", container], stdout=subprocess.DEVNULL,
                               stderr=subprocess.DEVNULL, timeout=15)
            stop_host()
            if created:
                cleanup = subprocess.run(["docker", "rm", "--force", container],
                                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
                if cleanup.returncode:
                    raise RuntimeError(f"could not remove scoped test container {container}")
            if volume_created:
                cleanup = subprocess.run(["docker", "volume", "rm", "--force", volume],
                                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
                if cleanup.returncode:
                    raise RuntimeError(f"could not remove scoped test volume {volume}")


if __name__ == "__main__":
    main()
