#!/usr/bin/env python3
"""Real .NET/Redis pause, outage and recovery gate; standard-library-only runner."""

import argparse
import json
import math
import pathlib
import secrets
import socket
import statistics
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

from run import IntegrationBackends, clean_environment


def port():
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


def docker(*args, capture=False):
    result = subprocess.run(["docker", *args], check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=45)
    return result.stdout.strip() if capture else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet-dll", type=pathlib.Path, required=True)
    parser.add_argument("--expect-paused-failure", action="store_true", help="negative control: healthy preflight must pass, paused phase must fail")
    args = parser.parse_args()
    dll = args.dotnet_dll.resolve()
    if not dll.is_file():
        raise FileNotFoundError(dll)
    name = f"rg-dotnet-outage-{secrets.token_hex(6)}"
    password, token = secrets.token_hex(24), secrets.token_hex(24)
    redis_port, host_port = port(), port()
    while host_port == redis_port:
        host_port = port()
    base = f"http://127.0.0.1:{host_port}"
    process = None
    paused = False
    created = False
    counter = 0
    prefix = f"outage_{secrets.token_hex(6)}"

    def redis(*command):
        return docker("exec", "--env", f"REDISCLI_AUTH={password}", name, "redis-cli", "--raw", "--no-auth-warning", *command, capture=True)

    def ready():
        deadline = time.monotonic() + 20
        while True:
            try:
                if redis("PING") == "PONG":
                    return
            except subprocess.CalledProcessError:
                pass
            if time.monotonic() > deadline:
                raise TimeoutError("Redis did not become healthy")
            time.sleep(0.1)

    def classify(stage, *, path_override=None, expected_model=None):
        nonlocal counter
        counter += 1
        request_id = f"outage-{stage}-{counter}"
        # Paths vary: these are cold keys, not request_id-only local-cache hits.
        payload = {"jsonrpc": "2.0", "id": counter, "method": "classify", "params": {
            "user_agent": "GPTBot/1.0", "path": path_override or f"/outage/{stage}/{counter}", "request_id": request_id}}
        request = urllib.request.Request(f"{base}/mcp", json.dumps(payload).encode(), headers={
            "Authorization": f"Bearer {token}", "Content-Type": "application/json", "Accept": "application/json, text/event-stream"})
        started = time.monotonic()
        with urllib.request.urlopen(request, timeout=8) as response:
            value = json.load(response)
        duration = time.monotonic() - started
        result = value.get("result")
        valid = (value.get("jsonrpc") == "2.0" and value.get("id") == counter and value.get("error") is None
                 and isinstance(result, dict) and result.get("request_id") == request_id
                 and type(result.get("score")) in {int, float}
                 and math.isclose(result["score"], 1.01 / 1.1, abs_tol=1e-9)
                 and result.get("verdict") == "block"
                 and result.get("confidence") == "medium"
                 and result.get("threat_category") == "ai_scraping"
                 and isinstance(result.get("model_version"), str)
                 and (expected_model is None or result["model_version"] == expected_model)
                 and any(signal.get("name") == "ua_ai_bot" for signal in result.get("signals", [])))
        return duration, valid

    def samples(stage):
        values = [classify(stage) for _ in range(3)]
        durations = [item[0] for item in values]
        report = {"stage": stage, "samples_seconds": durations, "median": statistics.median(durations),
                  "worst": max(durations), "valid_responses": sum(item[1] for item in values)}
        print(json.dumps(report), flush=True)
        return report["valid_responses"] == 3 and report["median"] < 1.8 and report["worst"] < 2.25

    def prove_redis_read(stage, request_number):
        wanted = f"outage-{stage}-{request_number}"
        keys = redis("--scan", "--pattern", f"{prefix}:cache:*").splitlines()
        for key in keys:
            original = redis("GET", key)
            cached = json.loads(original)
            if cached.get("request_id") != wanted:
                continue
            cached["model_version"] = "redis-cache-read-proof"
            try:
                if redis("SET", key, json.dumps(cached), "KEEPTTL") != "OK":
                    raise AssertionError("could not seed Redis read proof")
                _, valid = classify("redis-hit", path_override=f"/outage/{stage}/{request_number}",
                                    expected_model="redis-cache-read-proof")
                if not valid:
                    raise AssertionError("classification did not read the actual Redis cache")
            finally:
                redis("SET", key, original, "KEEPTTL")
            print(f"PASS real Redis cache read: {stage}", flush=True)
            return
        raise AssertionError(f"Redis did not contain the expected fresh decision: {wanted}")

    def recovery(stage):
        ready()
        deadline = time.monotonic() + 20
        while True:
            duration, valid = classify(stage)
            if valid and duration < 2.25:
                break
            if time.monotonic() > deadline:
                raise AssertionError(f"{stage}: host did not recover")
            time.sleep(0.2)
        before = set(redis("--scan", "--pattern", f"{prefix}:cache:*").splitlines())
        if not samples(stage):
            raise AssertionError(f"{stage}: recovered sample gate failed")
        after = set(redis("--scan", "--pattern", f"{prefix}:cache:*").splitlines())
        fresh = [json.loads(redis("GET", key)) for key in after - before]
        if sum(value.get("request_id", "").startswith(f"outage-{stage}-") for value in fresh) < 3:
            raise AssertionError(f"{stage}: three fresh Redis cache writes did not recover")
        prove_redis_read(stage, counter)

    try:
        docker("run", "--detach", "--name", name, "--publish", f"127.0.0.1:{redis_port}:6379",
               IntegrationBackends.REDIS_IMAGE, "redis-server", "--save", "", "--appendonly", "no", "--requirepass", password)
        created = True
        ready()
        with tempfile.TemporaryDirectory(prefix="rg-dotnet-outage-") as work:
            environment = {**clean_environment(), "MCP__HOST": "127.0.0.1", "MCP__PORT": str(host_port),
                           "AUTH_TOKENS": token, "CACHE_SCOPE_HMAC_KEY": secrets.token_hex(32),
                           "MCP__LIMITS__CLASSIFY_TIMEOUT_SECS": "2", "MCP__REDIS__KEY_PREFIX": prefix,
                           "MCP__REDIS__URL": f"redis://:{password}@127.0.0.1:{redis_port}"}
            with open(pathlib.Path(work) / "server.log", "wb") as log:
                process = subprocess.Popen(["dotnet", str(dll)], cwd=work, env=environment, stdout=log, stderr=subprocess.STDOUT)
                deadline = time.monotonic() + 30
                while True:
                    if process.poll() is not None:
                        raise RuntimeError(".NET host exited before healthy preflight")
                    try:
                        with urllib.request.urlopen(f"{base}/health", timeout=2) as response:
                            if response.status == 200:
                                break
                    except (OSError, urllib.error.URLError):
                        pass
                    if time.monotonic() > deadline:
                        raise TimeoutError(".NET host startup timed out")
                    time.sleep(0.1)
                if not samples("healthy") or not redis("--scan", "--pattern", f"{prefix}:cache:*"):
                    raise AssertionError("healthy host and real Redis cache preflight failed")
                prove_redis_read("healthy", counter)
                docker("pause", name)
                paused = True
                if docker("inspect", "--format", "{{.State.Paused}}", name, capture=True) != "true":
                    raise AssertionError("Redis was not actually paused")
                passed = samples("paused")
                docker("unpause", name)
                paused = False
                if args.expect_paused_failure:
                    if passed:
                        raise AssertionError("negative control unexpectedly passed paused Redis")
                    recovery("negative-control-recovered")
                    print("PASS negative control: healthy preflight/recovery, expected paused failure", flush=True)
                    return
                if not passed:
                    raise AssertionError("paused Redis gate failed")
                recovery("unpaused")
                docker("stop", "--time", "1", name)
                if docker("inspect", "--format", "{{.State.Running}}", name, capture=True) != "false":
                    raise AssertionError("Redis did not stop")
                if not samples("stopped"):
                    raise AssertionError("stopped Redis gate failed")
                docker("start", name)
                recovery("restarted")
                print("PASS Redis healthy/pause/outage/recovery classification gate", flush=True)
    finally:
        try:
            if paused:
                docker("unpause", name)
        finally:
            try:
                if process is not None:
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=5)
            finally:
                # Also clean up a stopped container left by a failed docker run.
                cleanup = subprocess.run(["docker", "rm", "--force", name], stdout=subprocess.DEVNULL,
                                         stderr=subprocess.DEVNULL, timeout=30)
                if created and cleanup.returncode:
                    raise RuntimeError(f"could not remove scoped test container {name}")


if __name__ == "__main__":
    main()
