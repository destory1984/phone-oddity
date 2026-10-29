"""Rig a Meshy model with the Meshy rigging API.

Usage:
    python meshy_rig.py <input task id> [height in meters, default 1.6]

Reads the API key from the MESHY_HA_KEY (or MESHY_API_KEY) environment variable.
Saves every GLB/FBX the task returns to ./output/<input task id>/rig/.
"""
import json
import os
import sys
import time
from pathlib import Path
from urllib.parse import urlparse

import requests

API = "https://api.meshy.ai/openapi/v1/rigging"
POLL_SECONDS = 10
TIMEOUT_SECONDS = 30 * 60


def request(method: str, url: str, key: str, **kwargs) -> dict:
    # Keep the key out of error messages: raise our own error instead of requests' default.
    resp = requests.request(method, url, headers={"Authorization": f"Bearer {key}"}, timeout=60, **kwargs)
    if not resp.ok:
        raise RuntimeError(f"Meshy API {method} failed: HTTP {resp.status_code} {resp.text[:500]}")
    return resp.json()


def model_urls(node, prefix=""):
    """Yield (name, url) for every GLB/FBX URL nested in the task result."""
    if isinstance(node, dict):
        for k, v in node.items():
            yield from model_urls(v, f"{prefix}{k}_" if prefix or k != "result" else "")
    elif isinstance(node, str) and node.startswith("http"):
        ext = Path(urlparse(node).path).suffix.lower()
        if ext in (".glb", ".fbx"):
            yield prefix.rstrip("_").removesuffix("_url") + ext, node


def main() -> None:
    key = os.environ.get("MESHY_HA_KEY") or os.environ.get("MESHY_API_KEY")
    if not key:
        sys.exit("MESHY_HA_KEY environment variable is not set.")
    if len(sys.argv) < 2:
        sys.exit("Pass the input task id.")
    input_id = sys.argv[1]
    height = float(sys.argv[2]) if len(sys.argv) > 2 else 1.6

    task_id = request("POST", API, key, json={"input_task_id": input_id, "height_meters": height})["result"]
    print(f"rigging task {task_id} created")

    start = time.time()
    while True:
        task = request("GET", f"{API}/{task_id}", key)
        status = task["status"]
        print(f"  {status} {task.get('progress', 0)}%")
        if status == "SUCCEEDED":
            break
        if status in ("FAILED", "CANCELED"):
            sys.exit(f"task {status}: {task.get('task_error')}")
        if time.time() - start > TIMEOUT_SECONDS:
            sys.exit("timed out waiting for the task")
        time.sleep(POLL_SECONDS)

    out = Path("output") / input_id / "rig"
    out.mkdir(parents=True, exist_ok=True)
    (out / "task.json").write_text(json.dumps({k: v for k, v in task.items() if k != "result"}, indent=2))
    for name, url in model_urls(task.get("result", {})):
        resp = requests.get(url, timeout=300)
        resp.raise_for_status()
        (out / name).write_bytes(resp.content)
        print(f"  saved {out / name} ({len(resp.content) // 1024} KB)")
    print(f"credits used: {task.get('consumed_credits')}")


if __name__ == "__main__":
    main()
