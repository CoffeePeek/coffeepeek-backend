#!/usr/bin/env python3
"""Bounded HTTP probes or explicit staging load; curl/Python standard library only."""
import argparse
import concurrent.futures
import hashlib
import json
import math
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time
from urllib.parse import urljoin, urlsplit

PHOTO_PATH = re.compile(r"^/api/v1/check-ins/[0-9a-fA-F-]{36}/photos/[0-9a-fA-F-]{36}$")


def photo_paths(value):
    """Ignore unrelated avatar/public URLs and refuse off-origin or token-bearing URLs."""
    result = []
    if isinstance(value, dict):
        for photo in value.get("photos", []):
            if isinstance(photo, dict) and PHOTO_PATH.fullmatch(photo.get("url", "")):
                result.append(photo["url"])
        for key, child in value.items():
            if key != "photos":
                result.extend(photo_paths(child))
    elif isinstance(value, list):
        for child in value:
            result.extend(photo_paths(child))
    return list(dict.fromkeys(result))


def request(url, token, timeout, limit_rate=None, keep_body=False):
    # Token is supplied on stdin, never in process arguments, report, URLs or stderr.
    if "\r" in token or "\n" in token:
        raise ValueError("Token contains invalid characters")
    config = 'header = ' + json.dumps("Authorization: Bearer " + token) + "\n" if token else ""
    with tempfile.TemporaryDirectory(prefix="cp-photo-probe-") as tmp:
        headers = Path(tmp) / "headers"
        body = Path(tmp) / "body"
        cmd = ["curl", "--silent", "--config", "-", "--proto", "=http,https",
               "--max-time", str(timeout), "--connect-timeout", "10", "--dump-header", str(headers),
               "--output", str(body) if keep_body else os.devnull, "--write-out", "%{json}"]
        if limit_rate:
            cmd.extend(["--limit-rate", limit_rate])
        # No redirects: credentials must never be forwarded to another origin.
        completed = subprocess.run(cmd + [url], input=config, text=True, capture_output=True,
                                   timeout=timeout + 5, check=False)
        try:
            raw = json.loads(completed.stdout)
        except json.JSONDecodeError:
            raw = {}
        allowed = {"cache-control", "content-type", "content-length", "etag", "age", "vary"}
        selected_headers = {}
        if headers.exists():
            for line in headers.read_text(errors="replace").splitlines():
                key, separator, value = line.partition(":")
                if separator and key.lower() in allowed:
                    selected_headers[key.lower()] = value.strip()
        measurement = {"status": raw.get("http_code", 0), "curl_exit": completed.returncode,
                       "ttfb_ms": 1000 * raw.get("time_starttransfer", 0),
                       "total_ms": 1000 * raw.get("time_total", 0),
                       "bytes": raw.get("size_download", 0), "headers": selected_headers}
        return measurement, body.read_bytes() if keep_body and body.exists() else None


def percentile(values, fraction):
    return sorted(values)[max(0, math.ceil(len(values) * fraction) - 1)] if values else None


def validate_run(base_url, environment, concurrency, request_count):
    origin = urlsplit(base_url)
    if origin.scheme not in ("http", "https") or not origin.hostname or origin.username or origin.password \
            or origin.query or origin.fragment or origin.path not in ("", "/"):
        raise ValueError("base-url must be an HTTP(S) origin without credentials, path or query")
    local = origin.hostname in ("localhost", "127.0.0.1", "::1")
    if environment == "local" and not local:
        raise ValueError("local mode requires localhost")
    if environment == "staging" and origin.hostname in ("api.coffeepeek.by", "coffeepeek.by"):
        raise ValueError("Use bounded probe mode on production")
    if environment == "probe" and (concurrency != 1 or request_count > 10):
        raise ValueError("Probe mode permits at most 10 image requests and concurrency 1")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--environment", choices=["probe", "local", "staging"], default="probe")
    parser.add_argument("--page-size", type=int, choices=[20, 100], default=20)
    parser.add_argument("--photo-limit", type=int, default=3)
    parser.add_argument("--repeats", type=int, default=2)
    parser.add_argument("--concurrency", type=int, choices=[1, 10, 50], default=1)
    parser.add_argument("--timeout", type=int, default=30)
    parser.add_argument("--limit-rate", help="curl rate per transfer (e.g. 625K); not aggregate bandwidth or RTT")
    parser.add_argument("--token-env", default="COFFEEPEEK_PHOTO_TOKEN")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if not 1 <= args.photo_limit <= 500 or not 1 <= args.repeats <= 1000 or not 1 <= args.timeout <= 300:
        parser.error("photo-limit 1..500, repeats 1..1000 and timeout 1..300 are required")
    try:
        validate_run(args.base_url, args.environment, args.concurrency, args.photo_limit * args.repeats)
    except ValueError as error:
        parser.error(str(error))
    token = os.environ.get(args.token_env, "")
    feed, body = request(urljoin(args.base_url, f"/api/v1/feed?pageSize={args.page_size}"), token, args.timeout, keep_body=True)
    if feed["status"] != 200 or feed["curl_exit"]:
        parser.exit(1, f"Feed failed: status={feed['status']}, curl_exit={feed['curl_exit']}\n")
    paths = photo_paths(json.loads(body))[:args.photo_limit]
    if not paths:
        parser.exit(1, "Feed contains no canonical check-in photo URLs\n")

    def probe(item):
        iteration, path = item
        result, _ = request(urljoin(args.base_url, path), token, args.timeout, args.limit_rate)
        return {"photo": hashlib.sha256(path.encode()).hexdigest()[:16], "iteration": iteration, **result}

    started = time.monotonic()
    rows = []
    # Complete each pass before repeating, so first/repeat passes are comparable.
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        for iteration in range(args.repeats):
            rows.extend(pool.map(probe, [(iteration, path) for path in paths]))
    successful = [row for row in rows if row["status"] == 200 and row["curl_exit"] == 0]
    report = {"schema": 1, "recorded_at_unix": time.time(), "environment": args.environment,
              "reader": "authenticated" if token else "anonymous", "page_size": args.page_size,
              "concurrency": args.concurrency, "photo_count": len(paths), "repeats": args.repeats,
              "per_transfer_limit": args.limit_rate, "elapsed_seconds": time.monotonic() - started,
              "cache_mode": "HTTP network requests on every pass; no browser or bitmap cache",
              "sample_warning": "Sample percentiles are not an SLO or client render measurement",
              "feed": feed, "requests": rows,
              "summary": {"successful": len(successful), "failed": len(rows) - len(successful),
                          **{key + "_p95": percentile([row[key] for row in successful], .95)
                             for key in ("ttfb_ms", "total_ms", "bytes")}}}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report["summary"]))
    return 0 if len(successful) == len(rows) else 1


if __name__ == "__main__":
    raise SystemExit(main())
