"""Generate a 3D model from multi-view images with the Meshy API.

Usage:
    python meshy_generate.py front.png side.png back.png [--no-enhance] [--texture-res 4k]

Reads the API key from the MESHY_HA_KEY (or MESHY_API_KEY) environment variable.
Saves the GLB, FBX and preview images to ./output/<task id>/.
"""
import argparse
import base64
import mimetypes
import os
import sys
import time
from pathlib import Path

import requests

API = "https://api.meshy.ai/openapi/v1/multi-image-to-3d"
POLL_SECONDS = 10
TIMEOUT_SECONDS = 30 * 60


def data_uri(path: Path) -> str:
    mime = mimetypes.guess_type(path.name)[0] or "image/png"
    return f"data:{mime};base64,{base64.b64encode(path.read_bytes()).decode()}"


def request(method: str, url: str, key: str, **kwargs) -> dict:
    # Keep the key out of error messages: raise our own error instead of requests' default.
    resp = requests.request(method, url, headers={"Authorization": f"Bearer {key}"}, timeout=60, **kwargs)
    if not resp.ok:
        raise RuntimeError(f"Meshy API {method} failed: HTTP {resp.status_code} {resp.text[:500]}")
    return resp.json()


def download(url: str, dest: Path) -> None:
    resp = requests.get(url, timeout=300)
    resp.raise_for_status()
    dest.write_bytes(resp.content)
    print(f"  saved {dest} ({len(resp.content) // 1024} KB)")


def main() -> None:
    key = os.environ.get("MESHY_HA_KEY") or os.environ.get("MESHY_API_KEY")
    if not key:
        sys.exit("MESHY_HA_KEY environment variable is not set.")
    parser = argparse.ArgumentParser()
    parser.add_argument("images", nargs="+", type=Path, help="1 to 4 view images")
    parser.add_argument("--no-enhance", action="store_true", help="send images to Meshy unchanged")
    parser.add_argument("--texture-res", default="2k", choices=["2k", "4k", "8k"])
    args = parser.parse_args()
    if not 1 <= len(args.images) <= 4:
        sys.exit("Pass 1 to 4 image paths.")

    body = {
        "image_urls": [data_uri(p) for p in args.images],
        "ai_model": "latest",
        "geometry_resolution": "2k",
        "image_enhancement": not args.no_enhance,
        "should_texture": True,
        "enable_pbr": False,
        "texture_resolution": args.texture_res,
        "pose_mode": "a-pose",
        "should_remesh": True,
        "topology": "quad",
        "target_polycount": 30000,
        "target_formats": ["glb", "fbx"],
        "multi_view_thumbnails": True,
    }
    task_id = request("POST", API, key, json=body)["result"]
    print(f"task {task_id} created")

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

    out = Path("output") / task_id
    out.mkdir(parents=True, exist_ok=True)
    for fmt, url in task.get("model_urls", {}).items():
        if fmt in ("glb", "fbx"):
            download(url, out / f"harin.{fmt}")
    for view, url in (task.get("thumbnail_urls") or {}).items():
        download(url, out / f"preview_{view}.png")
    print(f"credits used: {task.get('consumed_credits')}")


if __name__ == "__main__":
    main()
