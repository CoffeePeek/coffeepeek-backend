#!/usr/bin/env python3
"""Validate a commit image tag and atomically replace only IMAGE_TAG in deploy/.env."""
import argparse
import os
from pathlib import Path
import re
import stat
import tempfile

TAG = re.compile(r"sha-[0-9a-f]{40}\Z")
ASSIGNMENT = re.compile(rb"^[ \t]*(?:export[ \t]+)?IMAGE_TAG[ \t]*=")


def assignment_indexes(lines):
    """Recognize assignments outside quoted multiline values; reject executable/ambiguous syntax."""
    quote = None
    continued = False
    matches = []
    for index, line in enumerate(lines):
        candidate = quote is None and not continued and ASSIGNMENT.match(line)
        if candidate:
            matches.append(index)
        continued = False
        position = 0
        while position < len(line):
            char = line[position:position + 1]
            if quote == b"'":
                if char == b"'":
                    quote = None
            elif char == b"\\":
                continued = quote is None and line[position + 1:] in (b"\n", b"\r\n")
                position += 1
            elif quote == b"ansi":
                if char == b"'":
                    quote = None
            elif char == b"`" or line[position:position + 2] == b"$(":
                raise ValueError("Complex shell environment requires operator review.")
            elif quote == b'"':
                if char == b'"':
                    quote = None
            elif char == b"#" and (position == 0 or line[position - 1:position].isspace()):
                break
            elif line[position:position + 2] == b"$'":
                quote = b"ansi"
                position += 1
            elif char in (b"'", b'"'):
                quote = char
            position += 1
        if candidate and (quote is not None or continued):
            raise ValueError("Multiline IMAGE_TAG requires operator review.")
    if quote is not None or continued:
        raise ValueError("Incomplete shell environment requires operator review.")
    return matches


def replacement(original, tag):
    if not isinstance(tag, str) or not TAG.fullmatch(tag):
        raise ValueError("Invalid commit image tag.")
    lines = original.splitlines(keepends=True)
    matches = assignment_indexes(lines)
    if len(matches) > 1:
        raise ValueError("Duplicate IMAGE_TAG assignments require operator review.")
    if matches:
        index = matches[0]
        line = lines[index]
        newline = b"\r\n" if line.endswith(b"\r\n") else b"\n" if line.endswith(b"\n") else b""
        lines[index] = ASSIGNMENT.match(line).group() + tag.encode("ascii") + newline
    else:
        newline = b"\r\n" if b"\r\n" in original else b"\n"
        if original and not original.endswith(b"\n"):
            lines.append(newline)
        lines.append(b"IMAGE_TAG=" + tag.encode("ascii") + newline)
    return b"".join(lines)


def persist(path, tag, check_only=False):
    path = Path(path)
    if path.is_symlink():
        raise ValueError("Symlink environment files require operator review.")
    original = path.read_bytes()
    updated = replacement(original, tag)
    if check_only or original == updated:
        return
    metadata = path.stat()
    descriptor, temporary = tempfile.mkstemp(prefix=".image-tag-", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as output:
            output.write(updated)
            output.flush()
            os.fsync(output.fileno())
        os.chmod(temporary, stat.S_IMODE(metadata.st_mode))
        if hasattr(os, "chown"):
            os.chown(temporary, metadata.st_uid, metadata.st_gid)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("path")
    parser.add_argument("tag")
    args = parser.parse_args(argv)
    try:
        persist(args.path, args.tag, args.check)
        return 0
    except (OSError, ValueError):
        print("Image tag configuration is invalid or could not be persisted.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
