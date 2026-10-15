#!/usr/bin/env python3
"""Install pinned Gitleaks and run identical checks locally and in CI."""

from __future__ import annotations

import argparse
import hashlib
import io
import os
from pathlib import Path
import platform
import subprocess
import sys
import tarfile
import urllib.request
import zipfile


ROOT = Path(__file__).resolve().parents[2]
VERSION = "8.30.1"
CHECKSUMS = {
    "windows_x64.zip": "d29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e",
    "linux_x64.tar.gz": "551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb",
    "linux_arm64.tar.gz": "e4a487ee7ccd7d3a7f7ec08657610aa3606637dab924210b3aee62570fb4b080",
    "darwin_x64.tar.gz": "dfe101a4db2255fc85120ac7f3d25e4342c3c20cf749f2c20a18081af1952709",
    "darwin_arm64.tar.gz": "b40ab0ae55c505963e365f271a8d3846efbc170aa17f2607f13df610a9aeb6a5",
}
BINARY = ROOT / ".tools" / "gitleaks" / ("gitleaks.exe" if os.name == "nt" else "gitleaks")


def git(*args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def install() -> None:
    if BINARY.is_file():
        installed = subprocess.check_output([str(BINARY), "version"], text=True).strip()
        if installed == VERSION:
            return
    machine = platform.machine().lower()
    architecture = {"amd64": "x64", "x86_64": "x64", "aarch64": "arm64", "arm64": "arm64"}.get(machine)
    system = platform.system().lower()
    suffix = "zip" if system == "windows" else "tar.gz"
    asset = f"{system}_{architecture}.{suffix}"
    if asset not in CHECKSUMS:
        raise RuntimeError(f"Unsupported platform: {system}/{machine}")
    url = f"https://github.com/gitleaks/gitleaks/releases/download/v{VERSION}/gitleaks_{VERSION}_{asset}"
    with urllib.request.urlopen(url, timeout=60) as response:
        archive = response.read()
    if hashlib.sha256(archive).hexdigest() != CHECKSUMS[asset]:
        raise RuntimeError("Gitleaks archive checksum mismatch; installation stopped.")
    if suffix == "zip":
        with zipfile.ZipFile(io.BytesIO(archive)) as package:
            executable = package.read(BINARY.name)
    else:
        with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as package:
            member = package.extractfile(BINARY.name)
            if member is None:
                raise RuntimeError("Gitleaks executable missing from archive.")
            executable = member.read()
    BINARY.parent.mkdir(parents=True, exist_ok=True)
    temporary = BINARY.with_suffix(".download")
    temporary.write_bytes(executable)
    temporary.chmod(0o755)
    temporary.replace(BINARY)
    print(f"Installed Gitleaks {VERSION} (SHA-256 verified).")


def install_hooks() -> None:
    current = subprocess.run(
        ["git", "config", "--get", "core.hooksPath"], cwd=ROOT, capture_output=True, text=True, check=False
    ).stdout.strip()
    if current and current != ".githooks":
        raise RuntimeError(f"Existing core.hooksPath={current}; integrate the security hooks there before replacing it.")
    if not current:
        for name in ("pre-commit", "pre-push"):
            if (ROOT / git("rev-parse", "--git-path", f"hooks/{name}")).is_file():
                raise RuntimeError(f"Existing {name} hook; integrate it before enabling .githooks.")
    for name in ("pre-commit", "pre-push"):
        (ROOT / ".githooks" / name).chmod(0o755)
    subprocess.run(["git", "config", "--local", "core.hooksPath", ".githooks"], cwd=ROOT, check=True)
    print("Enabled pre-commit and pre-push secret checks for this checkout.")


def scan(staged: bool = False) -> int:
    if not BINARY.is_file():
        raise RuntimeError("Run: python dev/security/scan_secrets.py install --hooks")
    command = [
        str(BINARY), "git", str(ROOT), "--config", str(ROOT / ".gitleaks.toml"),
        "--gitleaks-ignore-path", str(ROOT / ".gitleaksignore"),
        "--redact=100", "--no-banner", "--ignore-gitleaks-allow",
    ]
    command += ["--staged"] if staged else ["--log-opts=--all"]
    return subprocess.run(command, cwd=ROOT, check=False).returncode


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    setup = commands.add_parser("install", help="Install the pinned and checksum-verified scanner.")
    setup.add_argument("--hooks", action="store_true", help="Enable hooks in this checkout.")
    check = commands.add_parser("scan", help="Check all reachable history, or staged additions.")
    check.add_argument("--staged", action="store_true")
    args = parser.parse_args()
    try:
        if args.command == "install":
            install()
            if args.hooks:
                install_hooks()
            return 0
        return scan(args.staged)
    except (OSError, RuntimeError, subprocess.SubprocessError) as error:
        print(f"Secret scan stopped: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
