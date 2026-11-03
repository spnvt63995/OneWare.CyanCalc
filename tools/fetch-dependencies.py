#!/usr/bin/env python3
"""Fetch the exact public source revisions used by this plugin. Never reset a checkout."""
import argparse
import json
import pathlib
import shutil
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[1]


def git(folder, *arguments):
    return subprocess.check_output(["git", "-C", str(folder), *arguments], text=True).strip()


def verify(folder, source):
    if git(folder, "remote", "get-url", "origin") != source["repository"]:
        raise RuntimeError(f"Unexpected origin in {folder}; keep or relocate this checkout manually.")
    if git(folder, "rev-parse", "HEAD") != source["commit"]:
        raise RuntimeError(f"Unexpected revision in {folder}; no files were reset.")
    if git(folder, "status", "--porcelain", "--untracked-files=all"):
        raise RuntimeError(f"Local changes in {folder}; no files were reset.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true", help="Check existing source revisions without downloading")
    args = parser.parse_args()
    sources = json.loads((ROOT / "dependencies.json").read_text())["sources"]
    destination = ROOT / ".deps"
    destination.mkdir(exist_ok=True)
    for name, source in sources.items():
        folder = destination / name
        if not folder.exists():
            if args.verify:
                raise RuntimeError(f"Missing {folder}; run tools/fetch-dependencies.py first.")
            with tempfile.TemporaryDirectory(prefix=f".{name}-", dir=destination) as temporary:
                checkout = pathlib.Path(temporary) / "source"
                subprocess.run(["git", "init", "--quiet", str(checkout)], check=True)
                git(checkout, "remote", "add", "origin", source["repository"])
                git(checkout, "fetch", "--depth", "1", "origin", source["commit"])
                git(checkout, "checkout", "--quiet", "--detach", "FETCH_HEAD")
                verify(checkout, source)
                shutil.move(str(checkout), folder)
        verify(folder, source)
        print(f"{name}: {source['commit']}", flush=True)


if __name__ == "__main__":
    main()
