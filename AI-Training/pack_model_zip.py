#!/usr/bin/env python3
"""Zip a Transformers model folder and print size + SHA256.

Useful for hosting on Hugging Face (or any static file host) and configuring the app to:
- download a single .zip artifact
- verify ExpectedSha256
- extract into the install folder
"""

from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model_dir", default="./lora-output/merged", help="Folder with config.json/tokenizer/etc")
    ap.add_argument("--out_zip", default="./lora-model.zip")
    args = ap.parse_args()

    model_dir = Path(args.model_dir)
    out_zip = Path(args.out_zip)

    if not model_dir.exists() or not model_dir.is_dir():
        raise SystemExit(f"model_dir not found: {model_dir}")

    out_zip.parent.mkdir(parents=True, exist_ok=True)
    if out_zip.exists():
        out_zip.unlink()

    base = model_dir.resolve()

    with ZipFile(out_zip, "w", compression=ZIP_DEFLATED, compresslevel=6) as z:
        for root, _, files in os.walk(base):
            root_path = Path(root)
            for name in files:
                p = root_path / name
                rel = p.relative_to(base)
                z.write(p, arcname=str(rel).replace("\\", "/"))

    size = out_zip.stat().st_size
    sha = sha256_file(out_zip)

    print(f"Wrote: {out_zip}")
    print(f"Bytes: {size}")
    print(f"SHA256: {sha}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
