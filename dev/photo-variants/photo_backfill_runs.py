#!/usr/bin/env python3
"""Explicit one-request control of a durable photo backfill. No scheduler or auto-resume."""
import argparse
import datetime as dt
import json
import os
import uuid

from photo_backfill import Client, JOB_STATES, OperationError, nonnegative, service_url, timestamp, utc_text


def safe_run_view(value):
    run = value.get("run", value)
    keys = ("id", "beforeUtc", "recentSinceUtc", "recipeVersion", "phase", "cursorSortAtUtc", "cursorCheckInId",
            "cursorPhotoId", "batchSize", "minBatchIntervalSeconds", "paused", "scanComplete", "nextBatchAtUtc",
            "scannedCount", "enqueuedCount", "existingCount", "blockedCount")
    result = {key: run[key] for key in keys if key in run}
    if "outcome" in value:
        result["outcome"] = value["outcome"]
    if "jobs" in value:
        result["jobs"] = {key: number for key, number in value["jobs"].items() if key in JOB_STATES and nonnegative(number)}
        result["generationDrained"] = value.get("generationDrained") is True
    return result


def run_control(args, client):
    media = service_url(args.media_url)
    path = "/internal/check-in-photo-backfill/runs"
    if args.command == "create":
        now = dt.datetime.now(dt.timezone.utc)
        before = timestamp(args.before) if args.before else now
        if before > now or not 1 <= args.recent_days <= 36500 or not 1 <= args.page_size <= 100 \
                or not 5 <= args.interval_seconds <= 3600:
            raise OperationError("Invalid date, batch or interval bounds.")
        result = client.call(media, path, {"beforeUtc": utc_text(before),
            "recentSinceUtc": utc_text(before - dt.timedelta(days=args.recent_days)),
            "batchSize": args.page_size, "minBatchIntervalSeconds": args.interval_seconds})
    else:
        run_id = str(uuid.UUID(args.run_id))
        result = client.call(media, path + "/" + run_id + ("" if args.command == "status" else "/" + args.command),
                             method="GET" if args.command == "status" else "POST")
    return safe_run_view(result)


def parser():
    root = argparse.ArgumentParser(description=__doc__)
    root.add_argument("--auth-env", default="PHOTO_BACKFILL_SERVICE_SECRET")
    root.add_argument("--timeout", type=int, default=30)
    sub = root.add_subparsers(dest="command", required=True)
    create = sub.add_parser("create", help="Create a paused scan; resume is a separate explicit operation.")
    create.add_argument("--media-url", required=True)
    create.add_argument("--before")
    create.add_argument("--recent-days", type=int, default=30)
    create.add_argument("--page-size", type=int, default=10)
    create.add_argument("--interval-seconds", type=int, default=5)
    for command in ("status", "advance", "pause", "resume"):
        control = sub.add_parser(command)
        control.add_argument("--media-url", required=True)
        control.add_argument("--run-id", required=True)
    return root


def main(argv=None):
    args = parser().parse_args(argv)
    try:
        if not 5 <= args.timeout <= 300:
            raise OperationError("HTTP timeout must be 5..300 seconds.")
        client = Client(os.environ.get(args.auth_env, ""), args.timeout)
        print(json.dumps(run_control(args, client), indent=2))
        return 0
    except (OperationError, OSError, ValueError, KeyError, TypeError):
        print("Photo backfill control failed; check private service, run status and arguments. No automatic retry was attempted.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
