#!/usr/bin/env python3
"""Run a local image read-only and verify auth, health and real classification."""

import json
import subprocess
import time
import urllib.error
import urllib.request


IMAGE = "request-guard-mcp-dotnet:security"
TOKEN = "synthetic-container-smoke-token"


def main():
    identifier = subprocess.check_output([
        "docker", "run", "--detach", "--rm", "--read-only", "--cap-drop=ALL",
        "--security-opt=no-new-privileges", "--tmpfs", "/tmp:rw,noexec,nosuid,size=16m",
        "--publish", "127.0.0.1::8085", "--env", f"AUTH_TOKENS={TOKEN}",
        "--env", "CACHE_SCOPE_HMAC_KEY=synthetic-container-scope-key-32-bytes", IMAGE,
    ], text=True).strip()
    try:
        user = subprocess.check_output(["docker", "inspect", "--format", "{{.Config.User}}", identifier], text=True).strip()
        if user in {"", "0", "root", "0:0", "root:root"}:
            raise AssertionError(f"container must use a non-root user, got {user!r}")
        address = subprocess.check_output(["docker", "port", identifier, "8085/tcp"], text=True).strip()
        base = f"http://{address}"
        deadline = time.monotonic() + 30
        while True:
            try:
                with urllib.request.urlopen(f"{base}/health", timeout=2) as response:
                    if response.status == 200:
                        break
            except (OSError, urllib.error.URLError):
                if time.monotonic() >= deadline:
                    raise TimeoutError("container health startup timed out")
                time.sleep(0.2)
        payload = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "classify",
                              "params": {"user_agent": "GPTBot/1.0", "path": "/"}}).encode()
        try:
            urllib.request.urlopen(urllib.request.Request(f"{base}/mcp", payload), timeout=5)
            raise AssertionError("unauthenticated classification was accepted")
        except urllib.error.HTTPError as error:
            if error.code != 401:
                raise
        request = urllib.request.Request(f"{base}/mcp", payload, headers={
            "Content-Type": "application/json", "Authorization": f"Bearer {TOKEN}"})
        with urllib.request.urlopen(request, timeout=5) as response:
            envelope = json.load(response)
        if envelope.get("error") is not None:
            raise AssertionError(envelope)
        if not any(signal["name"] == "ua_ai_bot" for signal in envelope["result"]["signals"]):
            raise AssertionError(envelope)
        print("PASS: non-root read-only container health, authentication and classifier smoke")
    finally:
        subprocess.run(["docker", "logs", identifier], check=False)
        subprocess.run(["docker", "stop", "--time", "5", identifier], check=True)


if __name__ == "__main__":
    main()
