#!/usr/bin/env python3
"""Bounded, read-only photo archive inventory (stdlib only)."""
import argparse
import copy
import datetime as dt
import http.client
import json
import math
import os
import sqlite3
from pathlib import Path
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

PHASES = ("RecentPublicShop", "OtherShop", "Home", "PublicShopArchive")
MAX_RESPONSE = 1024 * 1024
SOURCE_STATES = {"verified", "unverified", "missing", "corrupt", "unsupported", "oversized",
                 "length_mismatch", "source_changed", "unavailable", "not_checked"}
JOB_STATES = {"Pending", "Processing", "RetryScheduled", "Ready", "Failed", "Cancelled"}


class OperationError(Exception):
    """An intentionally sanitized operator error."""


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def timestamp(value):
    if not isinstance(value, str):
        raise OperationError("UTC timestamps are required.")
    parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None or parsed.utcoffset() != dt.timedelta(0):
        raise OperationError("UTC timestamps are required.")
    return parsed


def utc_text(value):
    return value.astimezone(dt.timezone.utc).isoformat().replace("+00:00", "Z")


def service_url(value):
    parsed = urllib.parse.urlsplit(value)
    if parsed.scheme not in ("http", "https") or not parsed.hostname or parsed.username or parsed.password \
            or parsed.query or parsed.fragment or parsed.path not in ("", "/"):
        raise OperationError("Supply a service origin without credentials, query or path.")
    return value.rstrip("/")


class Client:
    def __init__(self, secret, timeout=180):
        if len(secret) < 32:
            raise OperationError("Set the service authentication environment variable (at least 32 characters).")
        self.secret = secret
        self.timeout = timeout
        self.opener = urllib.request.build_opener(NoRedirect())

    def call(self, origin, path, body=None, method="POST"):
        # No cookie jar, bearer login, redirects or retry that could repeat a write.
        request = urllib.request.Request(origin + path,
            data=None if body is None else json.dumps(body, allow_nan=False).encode(), method=method,
            headers={"X-Gateway-Auth": self.secret, "Content-Type": "application/json", "Cache-Control": "no-store"})
        try:
            with self.opener.open(request, timeout=self.timeout) as response:
                payload = response.read(MAX_RESPONSE + 1)
                if len(payload) > MAX_RESPONSE:
                    raise OperationError("Service response exceeded its bound.")
                return json.loads(payload)
        except urllib.error.HTTPError as error:
            status = error.code
            error.close()
            raise OperationError(f"Private service returned HTTP {status}; progress was not advanced.") from None
        except (urllib.error.URLError, TimeoutError, OSError, ValueError, http.client.HTTPException):
            raise OperationError("Private service request failed; progress was not advanced.") from None


def atomic_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent, delete=False) as output:
            temporary = Path(output.name)
            json.dump(value, output, indent=2, ensure_ascii=False, allow_nan=False)
            output.write("\n")
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, path)
    finally:
        if temporary and temporary.exists():
            temporary.unlink()


def new_inventory(shops, media, before, recent, page_size, verify):
    timestamp(before)
    if timestamp(recent) >= timestamp(before):
        raise OperationError("Recent cutoff must precede the fixed upper cutoff.")
    return {"version": 1, "mode": "inventory", "shops": shops, "media": media,
            "beforeUtc": before, "recentSinceUtc": recent, "pageSize": page_size, "verifyContent": verify,
            "phaseIndex": 0, "cursor": None, "complete": False, "pages": 0, "recipeVersion": None,
            "totals": {"scanned": 0, "observedRows": 0, "duplicateObservations": 0, "changedBindings": 0,
                       "eligibility": {}, "sourceStatus": {}, "jobStatus": {}, "phases": {},
                       "declaredSourceBytes": 0, "verifiedSourceBytes": 0, "metadataMismatches": 0,
                       "readySampleCount": 0, "readySampleVariantBytes": 0, "remainingForGeneration": 0}}


class InventoryCheckpoint:
    """Disk-backed distinct IDs and aggregates/cursor commit atomically with bounded RAM."""
    def __init__(self, path, initial=None):
        path = Path(path)
        if (initial is None and not path.is_file()) or (initial is not None and path.exists()):
            raise OperationError("Resume an existing checkpoint or choose a new checkpoint path.")
        path.parent.mkdir(parents=True, exist_ok=True)
        self.db = sqlite3.connect(path, timeout=5, isolation_level=None)
        self.db.execute("PRAGMA synchronous=FULL")
        if initial is not None:
            self.db.executescript("BEGIN IMMEDIATE; CREATE TABLE checkpoint(id INTEGER PRIMARY KEY CHECK(id=1), state TEXT NOT NULL);"
                "CREATE TABLE seen(photo_id TEXT PRIMARY KEY, source_version TEXT NOT NULL);")
            self.db.execute("INSERT INTO checkpoint(id,state) VALUES(1,?)", (self.encode(initial),))
            self.db.execute("COMMIT")

    @staticmethod
    def encode(value):
        return json.dumps(value, sort_keys=True, allow_nan=False)

    def read(self):
        row = self.db.execute("SELECT state FROM checkpoint WHERE id=1").fetchone()
        if row is None:
            raise OperationError("Checkpoint has no committed state.")
        return json.loads(row[0])

    def seen(self, ids):
        if not ids:
            return {}
        return dict(self.db.execute("SELECT photo_id,source_version FROM seen WHERE photo_id IN ("
            + ",".join("?" for _ in ids) + ")", ids))

    def commit(self, previous, updated, bindings):
        self.db.execute("BEGIN IMMEDIATE")
        try:
            if self.encode(self.read()) != self.encode(previous):
                raise OperationError("Another process advanced this checkpoint; reload before continuing.")
            self.db.executemany("INSERT INTO seen(photo_id,source_version) VALUES(?,?)", bindings)
            self.db.execute("UPDATE checkpoint SET state=? WHERE id=1", (self.encode(updated),))
            self.db.execute("COMMIT")
        except BaseException:
            self.db.execute("ROLLBACK")
            raise

    def export(self, path, pilot_rate=None):
        # Serialize exports as well as commits. A delayed writer cannot overwrite a
        # newer report with a stale in-memory checkpoint. No network occurs under this lock.
        self.db.execute("BEGIN IMMEDIATE")
        try:
            report = inventory_report(self.read(), pilot_rate)
            atomic_json(path, report)
            self.db.execute("COMMIT")
            return report
        except BaseException:
            self.db.execute("ROLLBACK")
            raise

    def close(self):
        self.db.close()


def cursor_for(item):
    try:
        return (timestamp(item["sortAtUtc"]), uuid.UUID(item["checkInId"]).int, uuid.UUID(item["photoId"]).int)
    except (KeyError, ValueError, TypeError):
        raise OperationError("Invalid Community cursor; progress was not advanced.") from None


def validate_page(page, state):
    if not isinstance(page, dict) or not isinstance(page.get("items"), list):
        raise OperationError("Invalid Community page.")
    items = page["items"]
    if len(items) > state["pageSize"] or len({x.get("photoId") for x in items if isinstance(x, dict)}) != len(items):
        raise OperationError("Unbounded or duplicated Community page.")
    previous = cursor_for(state["cursor"]) if state["cursor"] else None
    for item in items:
        current = cursor_for(item)
        if current[0] > timestamp(state["beforeUtc"]) or previous and current >= previous:
            raise OperationError("Non-progressing Community page.")
        if not isinstance(item.get("storageKey"), str) or not isinstance(item.get("sourceVersion"), str):
            raise OperationError("Invalid Community binding.")
        previous = current
    next_cursor = page.get("nextCursor")
    if next_cursor is not None and (not items or cursor_for(next_cursor) != cursor_for(items[-1])):
        raise OperationError("Cursor did not identify the last candidate.")
    return items


def count(values, key):
    values[key] = values.get(key, 0) + 1


def nonnegative(value):
    return isinstance(value, int) and not isinstance(value, bool) and value >= 0


def add_inspection(state, items, inspected):
    if not isinstance(inspected, list) or len(inspected) != len(items) \
            or {x.get("photoId") for x in inspected if isinstance(x, dict)} != {x["photoId"] for x in items}:
        raise OperationError("Incomplete Media inspection; progress was not advanced.")
    for result in inspected:
        source = result.get("sourceStatus")
        recipe = result.get("recipeVersion")
        job = result.get("jobStatus")
        if source not in SOURCE_STATES or source == "unavailable" or not nonnegative(recipe) or recipe == 0 \
                or job is not None and job not in JOB_STATES:
            raise OperationError("Inspection unavailable or invalid; repeat this page after recovery.")
        if state["recipeVersion"] is not None and state["recipeVersion"] != recipe:
            raise OperationError("Recipe changed; start a new inventory instead of mixing measurements.")
        state["recipeVersion"] = recipe
        declared = result.get("declaredBytes")
        actual = result.get("actualBytes")
        measured = result.get("currentVariantBytes")
        if not nonnegative(declared) or actual is not None and not nonnegative(actual) \
                or measured is not None and (not nonnegative(measured) or measured == 0):
            raise OperationError("Invalid size measurement.")
        if source == "verified" and actual is None:
            raise OperationError("Verified source has no byte measurement.")
        failure = result.get("eligibilityFailure")
        if failure is not None and not isinstance(failure, str):
            raise OperationError("Invalid eligibility classification.")
        totals = state["totals"]
        totals["scanned"] += 1
        totals["declaredSourceBytes"] += declared
        totals["metadataMismatches"] += int(result.get("metadataMismatch") is True)
        count(totals["eligibility"], failure or "eligible_binding")
        count(totals["sourceStatus"], source)
        count(totals["jobStatus"], job or "none")
        count(totals["phases"], PHASES[state["phaseIndex"]])
        if failure is None and source == "verified":
            totals["verifiedSourceBytes"] += actual
            if job == "Ready" and measured:
                totals["readySampleCount"] += 1
                totals["readySampleVariantBytes"] += measured
            elif job in (None, "Pending", "Processing", "RetryScheduled"):
                totals["remainingForGeneration"] += 1


def inventory_report(state, pilot_rate=None):
    totals = state["totals"]
    samples = totals["readySampleCount"]
    total_bytes = totals["readySampleVariantBytes"]
    remaining = totals["remainingForGeneration"]
    estimate = (remaining * total_bytes + samples - 1) // samples if samples else None
    return {"version": 1, "checkpointRevision": state["pages"], "complete": state["complete"], "beforeUtc": state["beforeUtc"],
            "recentSinceUtc": state["recentSinceUtc"], "recipeVersion": state["recipeVersion"],
            "verifyContent": state["verifyContent"], "pages": state["pages"], "totals": copy.deepcopy(totals),
            "forecast": {"sampleBasis": "current_recipe_ready_manifests_of_content_verified_sources",
                "sampleCount": samples, "sampleVariantBytes": total_bytes,
                "meanVariantBytes": total_bytes / samples if samples else None,
                "remainingVerifiedPhotos": remaining, "estimatedAdditionalVariantBytes": estimate,
                "pilotPhotosPerSecond": pilot_rate,
                "estimatedGenerationSeconds": math.ceil(remaining / pilot_rate) if pilot_rate and state["verifyContent"] else None},
            "limitations": ["A bounded live pass is not a database snapshot or a delta stream; restart from the beginning for reconciliation.",
                "Photo IDs are counted once using the local disk ledger; later changed bindings require a new pass.",
                "Forecast describes the verified observed population and recorded Ready sizes, not an exact storage bill.",
                "Private transfer, Failed/Cancelled recovery and invalid Ready manifests require separate operator action.",
                "No storage objects, database rows, policy or jobs were written by this inventory."]}


def inventory_step(client, state, seen_lookup=lambda ids: {}):
    if state["complete"]:
        return state, []
    page = client.call(state["shops"], "/internal/check-in-photo-backfill/candidates", {
        "phase": state["phaseIndex"], "beforeUtc": state["beforeUtc"], "recentSinceUtc": state["recentSinceUtc"],
        "cursor": state["cursor"], "limit": state["pageSize"]})
    items = validate_page(page, state)
    observed = len(items)
    known = seen_lookup([item["photoId"] for item in items])
    changed = sum(known[item["photoId"]] != item["sourceVersion"] for item in items if item["photoId"] in known)
    new_items = [item for item in items if item["photoId"] not in known]
    # Limit every storage request to five sequential inspections, even if the SQL page is larger.
    inspected = []
    for offset in range(0, len(new_items), 5):
        inspected.extend(client.call(state["media"], "/internal/check-in-photo-backfill/inspect?verifyContent="
            + str(state["verifyContent"]).lower(), new_items[offset:offset + 5]))
    updated = copy.deepcopy(state)
    add_inspection(updated, new_items, inspected)
    updated["totals"]["observedRows"] += observed
    updated["totals"]["duplicateObservations"] += observed - len(new_items)
    updated["totals"]["changedBindings"] += changed
    updated["pages"] += 1
    updated["cursor"] = page.get("nextCursor")
    if updated["cursor"] is None:
        updated["phaseIndex"] += 1
        updated["complete"] = updated["phaseIndex"] == len(PHASES)
    return updated, [(item["photoId"], item["sourceVersion"]) for item in new_items]


def run_inventory(args, client):
    shops, media = service_url(args.shops_url), service_url(args.media_url)
    checkpoint = None
    if args.resume:
        checkpoint = InventoryCheckpoint(args.checkpoint)
        try:
            state = checkpoint.read()
            if state.get("version") != 1 or state.get("mode") != "inventory" or state.get("shops") != shops \
                    or state.get("media") != media or state.get("verifyContent") != (not args.metadata_only) \
                    or state.get("pageSize") != args.page_size:
                raise OperationError("Checkpoint belongs to different services, format or inspection settings.")
            if args.before and state["beforeUtc"] != args.before:
                raise OperationError("Resume cannot change the fixed cutoff.")
        except BaseException:
            checkpoint.close()
            raise
    else:
        before = timestamp(args.before) if args.before else dt.datetime.now(dt.timezone.utc)
        state = new_inventory(shops, media, utc_text(before), utc_text(before - dt.timedelta(days=args.recent_days)),
                              args.page_size, not args.metadata_only)
        checkpoint = InventoryCheckpoint(args.checkpoint, state)
    try:
        for page_number in range(args.max_pages):
            if state["complete"]:
                break
            updated, bindings = inventory_step(client, state, checkpoint.seen)
            checkpoint.commit(state, updated, bindings)
            state = updated
            checkpoint.export(args.report, args.pilot_photos_per_second)
            if not state["complete"] and page_number + 1 < args.max_pages:
                time.sleep(args.interval_seconds)
        report = checkpoint.export(args.report, args.pilot_photos_per_second)
        print(json.dumps({"complete": report["complete"], "scanned": report["totals"]["scanned"],
                          "pages": report["pages"], "forecast": report["forecast"]}))
    finally:
        checkpoint.close()


def positive_rate(value):
    parsed = float(value)
    if not math.isfinite(parsed) or parsed <= 0:
        raise argparse.ArgumentTypeError("Pilot throughput must be a finite positive measurement.")
    return parsed


def parser():
    root = argparse.ArgumentParser(description=__doc__)
    root.add_argument("--auth-env", default="PHOTO_BACKFILL_SERVICE_SECRET")
    root.add_argument("--timeout", type=int, default=180, help="Private HTTP timeout in seconds; inspections are grouped by five.")
    sub = root.add_subparsers(dest="command", required=True)
    inv = sub.add_parser("inventory", help="Read-only inventory; never enqueues or changes storage policy.")
    inv.add_argument("--shops-url", required=True)
    inv.add_argument("--media-url", required=True)
    inv.add_argument("--checkpoint", required=True)
    inv.add_argument("--report", required=True)
    inv.add_argument("--before")
    inv.add_argument("--recent-days", type=int, default=30)
    inv.add_argument("--page-size", type=int, default=5)
    inv.add_argument("--max-pages", type=int, default=1)
    inv.add_argument("--interval-seconds", type=float, default=1)
    inv.add_argument("--metadata-only", action="store_true")
    inv.add_argument("--resume", action="store_true")
    inv.add_argument("--pilot-photos-per-second", type=positive_rate)
    return root


def main(argv=None):
    args = parser().parse_args(argv)
    try:
        if not 5 <= args.timeout <= 3600:
            raise OperationError("HTTP timeout must be 5..3600 seconds.")
        client = Client(os.environ.get(args.auth_env, ""), args.timeout)
        if args.command == "inventory":
            if not 1 <= args.page_size <= 100 or not 1 <= args.max_pages <= 1000 \
                    or not 1 <= args.recent_days <= 36500 or not math.isfinite(args.interval_seconds) or args.interval_seconds < 1:
                raise OperationError("Invalid page, date or rate bounds.")
            if Path(args.checkpoint).resolve() == Path(args.report).resolve():
                raise OperationError("Checkpoint and report must be different files.")
            run_inventory(args, client)
        return 0
    except (OperationError, OSError, ValueError, KeyError, TypeError, sqlite3.Error):
        # No response body, request address, object key or credential is copied to the console.
        print("Photo archive operation failed; check private services, arguments and checkpoint. Progress was not advanced by a failed page.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
