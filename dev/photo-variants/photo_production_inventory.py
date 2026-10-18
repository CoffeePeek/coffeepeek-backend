#!/usr/bin/env python3
"""Explicit bounded VPS inventory/pilot controls. Private durable state; aggregate JSON stdout only."""
import argparse
import contextlib
import datetime as dt
import ipaddress
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
from types import SimpleNamespace
import urllib.request
import uuid

import photo_backfill as inventory
import photo_backfill_runs as controls

REPO = Path("/opt/coffeepeek")
STATE = Path("/var/lib/coffeepeek/photo-backfill")
MARKER = "coffeepeek-photo-production-inventory-v1"
SHOPS = "http://shops.photo-backfill.internal:8080"
MEDIA = "http://media.photo-backfill.internal:8080"
MAX_PAGES = 20
SHA = re.compile(r"[0-9a-f]{40}\Z")
CONTAINER = re.compile(r"[0-9a-f]{64}\Z")
FAILURES = {"eligible_binding", "invalid_candidate", "deleted", "media_missing", "binding_mismatch",
            "media_not_confirmed", "needs_transfer", "provenance_unverified"}
OUTCOMES = {"paused", "resumed", "advanced", "scan_complete", "rate_limited", "queue_full",
            "another_run_active", "previous_archive_backlog", "recipe_changed"}
RUN_FIELDS = ("beforeUtc", "recentSinceUtc", "recipeVersion", "phase", "batchSize", "minBatchIntervalSeconds",
              "paused", "scanComplete", "nextBatchAtUtc", "scannedCount", "enqueuedCount", "existingCount", "blockedCount")
INSPECT = ('{"revision":{{json (index .Config.Labels "org.opencontainers.image.revision")}},'
           '"networks":{{json .NetworkSettings.Networks}},'
           '"generationEnabled":"{{range .Config.Env}}{{if or (eq . "PhotoVariantOptions__Enabled=true") '
           '(eq . "PhotoVariantOptions__Enabled=True") (eq . "PhotoVariantOptions__Enabled=TRUE")}}true{{end}}{{end}}"}')


def require(value):
    if not value:
        raise inventory.OperationError("Production archive validation failed; inspect private state and service readiness.")


def command(args):
    try:
        result = subprocess.run(args, cwd=REPO, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                text=True, timeout=20, check=False)
        require(result.returncode == 0 and len(result.stdout) <= 1024 * 1024)
        return result.stdout.strip()
    except (OSError, subprocess.SubprocessError):
        raise inventory.OperationError("Container observation is unavailable.") from None


def deployment(environment, run=command):
    tag = environment.get("IMAGE_TAG", "")
    require(tag.startswith("sha-") and SHA.fullmatch(tag[4:]))
    expected = environment.get("EXPECTED_DEPLOY_SHA", tag[4:])
    require(SHA.fullmatch(expected) and expected == tag[4:])
    observed = {}
    for service in ("shops", "media"):
        ids = run(["docker", "ps", "--quiet", "--no-trunc", "--filter", "status=running",
                   "--filter", "label=com.docker.compose.project=coffeepeek", "--filter",
                   "label=com.docker.compose.service=" + service]).splitlines()
        require(len(ids) == 1 and CONTAINER.fullmatch(ids[0]))
        value = json.loads(run(["docker", "inspect", "--format", INSPECT, ids[0]]))
        require(isinstance(value, dict) and value.get("revision") == expected and isinstance(value.get("networks"), dict))
        observed[service] = value
    network = "coffeepeek_default"
    addresses = []
    for service in ("shops", "media"):
        require(network in observed[service]["networks"])
        address = ipaddress.IPv4Address(observed[service]["networks"][network].get("IPAddress", ""))
        require(address.is_private and not address.is_loopback and not address.is_link_local and not address.is_unspecified)
        addresses.append(str(address))
    return {"sha": expected, "origins": {SHOPS: "http://" + addresses[0] + ":8080", MEDIA: "http://" + addresses[1] + ":8080"},
            "generationEnabled": observed["media"].get("generationEnabled") == "true"}


class PrivateClient(inventory.Client):
    def __init__(self, secret, origins):
        super().__init__(secret, timeout=180)
        self.origins = origins
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), inventory.NoRedirect())

    def call(self, origin, path, body=None, method="POST"):
        require(origin in self.origins)
        require(path in ("/internal/check-in-photo-backfill/candidates", "/internal/check-in-photo-backfill/runs",
                         "/internal/check-in-photo-backfill/inspect?verifyContent=true")
                or re.fullmatch(r"/internal/check-in-photo-backfill/runs/[0-9a-f-]{36}(?:/(?:resume|advance|pause))?", path))
        return super().call(self.origins[origin], path, body, method)


def private_directory(root):
    require(not root.is_symlink())
    root.mkdir(parents=True, exist_ok=True, mode=0o700)
    os.chmod(root, 0o700)


def private_json(path, value):
    require(not path.is_symlink())
    inventory.atomic_json(path, value)
    os.chmod(path, 0o600)


def read_json(path):
    require(not path.is_symlink())
    with path.open("rb") as source:
        info = os.fstat(source.fileno())
        require(stat.S_ISREG(info.st_mode) and (os.name == "nt" or not info.st_mode & 0o077))
        data = source.read(1024 * 1024 + 1)
    require(len(data) <= 1024 * 1024)
    value = json.loads(data)
    require(isinstance(value, dict))
    return value


@contextlib.contextmanager
def lock(root):
    path = root / "operator.lock"
    require(not path.is_symlink())
    descriptor = os.open(path, os.O_CREAT | os.O_RDWR | getattr(os, "O_NOFOLLOW", 0), 0o600)
    try:
        os.write(descriptor, b"0"); os.lseek(descriptor, 0, os.SEEK_SET)
        if os.name == "nt":
            import msvcrt
            msvcrt.locking(descriptor, msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield
    finally:
        os.close(descriptor)


def safe_run(value):
    require(isinstance(value, dict))
    result = {}
    for key in RUN_FIELDS:
        if key not in value:
            continue
        item = value[key]
        if key in ("beforeUtc", "recentSinceUtc", "nextBatchAtUtc"):
            result[key] = inventory.utc_text(inventory.timestamp(item))
        elif key in ("paused", "scanComplete"):
            require(isinstance(item, bool)); result[key] = item
        elif key == "phase":
            require(item in inventory.PHASES or isinstance(item, int) and not isinstance(item, bool) and 0 <= item < 4)
            result[key] = item
        else:
            require(inventory.nonnegative(item)); result[key] = item
    if value.get("outcome") in OUTCOMES:
        result["outcome"] = value["outcome"]
    if isinstance(value.get("jobs"), dict):
        result["jobs"] = {key: item for key, item in value["jobs"].items() if key in inventory.JOB_STATES and inventory.nonnegative(item)}
        result["generationDrained"] = value.get("generationDrained") is True
    return result


def aggregate_inventory(state):
    full = inventory.inventory_report(state)
    totals = full["totals"]
    result = {"mode": "inventory", "complete": full["complete"] is True, "pages": full["pages"],
              "verifyContent": True, "recipeVersion": full["recipeVersion"], "totals": {}}
    for key in ("scanned", "observedRows", "duplicateObservations", "changedBindings", "declaredSourceBytes", "verifiedSourceBytes",
                "metadataMismatches", "readySampleCount", "readySampleVariantBytes", "remainingForGeneration"):
        require(inventory.nonnegative(totals[key])); result["totals"][key] = totals[key]
    for key, allowed in (("eligibility", FAILURES), ("sourceStatus", inventory.SOURCE_STATES),
                         ("jobStatus", inventory.JOB_STATES | {"none"}), ("phases", set(inventory.PHASES))):
        result["totals"][key] = {}
        for name, value in totals[key].items():
            require(inventory.nonnegative(value))
            safe = name if name in allowed else "other"
            result["totals"][key][safe] = result["totals"][key].get(safe, 0) + value
    result["forecast"] = {key: value for key, value in full["forecast"].items()
        if key not in ("sampleBasis",) and (value is None or isinstance(value, (int, float)) and not isinstance(value, bool))}
    result["limitations"] = full["limitations"]
    return result


def bounded_inventory(root, release, client, pages, sleep=time.sleep):
    require(1 <= pages <= MAX_PAGES)
    path = root / "inventory.sqlite"
    require(not path.is_symlink())
    if path.exists():
        checkpoint = inventory.InventoryCheckpoint(path)
    else:
        before = dt.datetime.now(dt.timezone.utc)
        initial = inventory.new_inventory(SHOPS, MEDIA, inventory.utc_text(before),
            inventory.utc_text(before - dt.timedelta(days=30)), 5, True)
        initial["deploymentSha"] = release["sha"]
        checkpoint = inventory.InventoryCheckpoint(path, initial)
        os.chmod(path, 0o600)
    try:
        state = checkpoint.read()
        require(state.get("deploymentSha") == release["sha"] and state.get("shops") == SHOPS
                and state.get("media") == MEDIA and state.get("pageSize") == 5 and state.get("verifyContent") is True)
        for index in range(pages):
            if state["complete"]:
                break
            updated, bindings = inventory.inventory_step(client, state, checkpoint.seen)
            checkpoint.commit(state, updated, bindings)
            state = updated
            private_json(root / "inventory-report.json", aggregate_inventory(state))
            if not state["complete"] and index + 1 < pages:
                sleep(1)
        result = aggregate_inventory(state)
        private_json(root / "inventory-report.json", result)
        return result
    finally:
        checkpoint.close()


def pilot_call(client, command_name, state):
    args = SimpleNamespace(command=command_name, media_url=MEDIA, run_id=state.get("runId"))
    if command_name == "create":
        args.before = state["beforeUtc"]; args.recent_days = 30; args.page_size = 5; args.interval_seconds = 5
    return controls.run_control(args, client)


def validate_pilot(state, view):
    require(view.get("beforeUtc") == state["beforeUtc"] and view.get("recentSinceUtc") == state["recentSinceUtc"]
            and view.get("batchSize") == 5 and view.get("minBatchIntervalSeconds") == 5
            and isinstance(view.get("recipeVersion"), int) and view["recipeVersion"] > 0)
    if state.get("recipeVersion") is not None:
        require(view["recipeVersion"] == state["recipeVersion"])


def attempt(root, state, operation, client):
    state["pending"] = {"operation": operation, "attempt": str(uuid.uuid4()), "before": state.get("lastObserved", {}),
                        "startedAtUtc": inventory.utc_text(dt.datetime.now(dt.timezone.utc))}
    private_json(root / "pilot.json", state)
    # Any exception leaves the attempt marker durable. No automatic network retry is performed.
    result = pilot_call(client, operation, state)
    if operation == "create":
        run = uuid.UUID(result["id"]); require(run.int != 0)
        require(result.get("paused") is True)
        state["runId"] = str(run)
    view = safe_run(result)
    validate_pilot(state, view)
    state["recipeVersion"] = view["recipeVersion"]
    state["lastObserved"] = view
    state["pending"] = None
    private_json(root / "pilot.json", state)
    return view


def pilot(root, release, client, operation):
    path = root / "pilot.json"
    if operation == "pilot-create":
        require(not path.exists() and not path.is_symlink())
        before = dt.datetime.now(dt.timezone.utc)
        state = {"marker": MARKER, "version": 1, "deploymentSha": release["sha"], "beforeUtc": inventory.utc_text(before),
                 "recentSinceUtc": inventory.utc_text(before - dt.timedelta(days=30)), "runId": None, "pending": None}
        # Existing marker always blocks another create, including when the response was lost.
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w", encoding="utf-8") as output:
            json.dump(state, output); output.flush(); os.fsync(output.fileno())
        view = attempt(root, state, "create", client)
        return {"mode": operation, "run": view}
    state = read_json(path)
    require(state.get("marker") == MARKER and state.get("version") == 1 and state.get("deploymentSha") == release["sha"])
    if not state.get("runId"):
        require(operation == "pilot-status")
        return {"mode": operation, "state": "create_uncertain", "requiresManualReconciliation": True}
    require(uuid.UUID(state["runId"]).int != 0)
    if operation == "pilot-status":
        view = safe_run(pilot_call(client, "status", state)); validate_pilot(state, view)
        def reconciled(pending):
            action = pending["operation"]
            changed = any(view.get(key) != pending.get("before", {}).get(key)
                          for key in ("phase", "nextBatchAtUtc", "scannedCount", "enqueuedCount", "existingCount", "blockedCount", "scanComplete"))
            return action == "resume" and view.get("paused") is False or action == "pause" and view.get("paused") is True \
                    or action == "advance" and changed
        if state.get("pending") and reconciled(state["pending"]):
            state["pending"] = None
        state["unresolved"] = [entry for entry in state.get("unresolved", []) if not reconciled(entry)]
        state["lastObserved"] = view
        private_json(path, state)
        return {"mode": operation, "run": view, "requiresManualReconciliation": state.get("pending") is not None or bool(state["unresolved"])}
    if operation == "pilot-pause":
        # An explicit halt remains possible after an uncertain advance; retain its marker.
        if state.get("pending"):
            state.setdefault("unresolved", []).append(state["pending"])
            state["pending"] = None
        require(len(state.get("unresolved", [])) <= 20)
        return {"mode": operation, "run": attempt(root, state, "pause", client),
                "requiresManualReconciliation": bool(state.get("unresolved"))}
    require(state.get("pending") is None and not state.get("unresolved"))
    require(operation == "pilot-advance" and release["generationEnabled"] is True)
    attempt(root, state, "resume", client)
    return {"mode": operation, "run": attempt(root, state, "advance", client)}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("inventory", "pilot-create", "pilot-advance", "pilot-status", "pilot-pause"))
    parser.add_argument("--max-pages", type=int, default=1)
    if argv is None and len(sys.argv) == 1:
        operation = os.environ.get("PHOTO_OPERATION")
        argv = [operation] + (["--max-pages", "20"] if operation == "inventory" else []) if operation else []
    args = parser.parse_args(argv)
    old_umask = os.umask(0o077)
    try:
        require(Path.cwd().resolve() == REPO and 1 <= args.max_pages <= MAX_PAGES)
        private_directory(STATE)
        with lock(STATE):
            release = deployment(os.environ)
            client = PrivateClient(os.environ.get("GATEWAY_AUTH_SECRET", ""), release["origins"])
            result = bounded_inventory(STATE, release, client, args.max_pages) if args.command == "inventory" \
                else pilot(STATE, release, client, args.command)
        print(json.dumps(result, allow_nan=False))
        return 0
    except Exception:
        print(json.dumps({"state": "operation_stopped", "requiresPrivateStateReview": True,
                          "message": "No automatic mutation retry was attempted; inspect journal, deployment and service readiness."}))
        return 1
    finally:
        os.umask(old_umask)


if __name__ == "__main__":
    raise SystemExit(main())
