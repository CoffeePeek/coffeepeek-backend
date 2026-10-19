#!/usr/bin/env python3
"""Explicit full photo rollout on the fixed VPS. Private lineage; bounded work; no uncertain write retries."""
import copy
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile
import time

import photo_backfill as scan
import photo_backfill_runs as controls
import photo_production_activation as activation
import photo_production_canary as canary
import photo_production_inventory as inventory

OPERATIONS = {"rollout-enable": "full-activate", "rollout-inventory": "archive-inventory", "rollout-adopt": "archive-adopt",
              "rollout-status": "archive-status", "rollout-advance": "archive-advance", "rollout-pause": "archive-pause",
              "rollout-verify": "archive-verify"}
MARKER = "coffeepeek-full-photo-rollout-v1"
PILOT_SHA = "4290e9419254d5526652ff054743e730d0487460"
FLAGS = {"CHECKIN_VARIANTS_ENABLED": "true", "CHECKIN_VARIANTS_READ_ENABLED": "true",
         "CHECKIN_VARIANTS_SERVE_ENABLED": "true", "CHECKIN_VARIANTS_ADVERTISE_ENABLED": "true",
         "CHECKIN_VARIANTS_CLEANUP_ENABLED": "false",
         "CHECKIN_VARIANTS_CLEANUP_DRY_RUN": "true"}
OBSOLETE = activation.OBSOLETE | {"CHECKIN_VARIANTS_CANARY_ONLY", "CHECKIN_VARIANTS_CANARY_USER_IDS"}
ACTIVE = ("Pending", "Processing", "RetryScheduled")
FROZEN = ("beforeUtc", "recentSinceUtc", "batchSize", "minBatchIntervalSeconds", "recipeVersion")
PROGRESS = ("phase", "cursorSortAtUtc", "cursorCheckInId", "cursorPhotoId", "scannedCount", "enqueuedCount", "existingCount", "blockedCount", "scanComplete")


def digest(path):
    canary.require(not path.is_symlink() and path.stat().st_size <= canary.MAX_JSON)
    return hashlib.sha256(path.read_bytes()).hexdigest()


def canonical_time(value):
    return scan.utc_text(scan.timestamp(value))


def env_replacement(original):
    lines, found = activation.assignment_lines(original, set(FLAGS) | OBSOLETE)
    newline = b"\r\n" if b"\r\n" in original else b"\n"
    for key in OBSOLETE:
        if key in found:
            lines[found[key]] = b""
    for key, value in FLAGS.items():
        replacement = key.encode() + b"=" + value.encode()
        if key in found:
            line = lines[found[key]]
            ending = b"\r\n" if line.endswith(b"\r\n") else b"\n" if line.endswith(b"\n") else b""
            lines[found[key]] = replacement + ending
        else:
            if lines and not lines[-1].endswith(b"\n"):
                lines.append(newline)
            lines.append(replacement + newline)
    return b"".join(lines)


class Capture:
    def __init__(self, client):
        self.client, self.candidates, self.inspections = client, {}, {}

    def call(self, origin, path, body=None, method="POST"):
        result = self.client.call(origin, path, body, method)
        if path.endswith("/candidates"):
            self.candidates.update((row["photoId"], row) for row in result["items"])
        elif "/inspect?" in path:
            self.inspections.update((row["photoId"], row) for row in result)
        return result


def commit_inventory(checkpoint, previous, updated, bindings, capture):
    db = checkpoint.db
    db.execute("BEGIN IMMEDIATE")
    try:
        canary.require(checkpoint.encode(checkpoint.read()) == checkpoint.encode(previous))
        db.executemany("INSERT INTO seen(photo_id,source_version) VALUES(?,?)", bindings)
        for photo_id, _ in bindings:
            payload = {"candidate": capture.candidates[photo_id], "inspection": capture.inspections[photo_id]}
            db.execute("INSERT INTO coverage(photo_id,payload) VALUES(?,?)", (photo_id, checkpoint.encode(payload)))
        db.execute("UPDATE checkpoint SET state=? WHERE id=1", (checkpoint.encode(updated),))
        db.execute("COMMIT")
    except BaseException:
        db.execute("ROLLBACK")
        raise


def normalized(variants):
    if isinstance(variants, str):
        variants = json.loads(variants)
    canary.require(isinstance(variants, list) and len(variants) == 6)
    result = {}
    for row in variants:
        values = {key: row.get(key, row.get(key[0].upper() + key[1:]))
                  for key in ("purpose", "format", "contentType", "width", "height", "sizeBytes", "digest")}
        pair = values["purpose"], values["format"]
        canary.require(pair not in result and pair[0] in canary.PURPOSES and pair[1] in ("webp", "jpeg", "png")
                       and values["contentType"] == "image/" + pair[1]
                       and all(type(values[key]) is int and values[key] > 0 for key in ("width", "height", "sizeBytes"))
                       and max(values["width"], values["height"]) <= canary.PURPOSES[pair[0]]
                       and re.fullmatch(r"[0-9a-f]{64}", values["digest"] or ""))
        result[pair] = values
    canary.require(all((purpose, "webp") in result and sum(p == purpose and f in ("jpeg", "png") for p, f in result) == 1
                       for purpose in canary.PURPOSES))
    return result


class Rollout:
    def __init__(self, environment, commands=None, private_client=None, public_client=None, sleep=time.sleep):
        self.environment = dict(environment)
        self.operator = activation.Activation(environment, commands)
        self.sha = self.operator.expected_sha()
        self.root = activation.PRIVATE / ("full-rollout-" + self.sha)
        inventory.private_directory(self.root)
        self.path = self.root / "archive.json"
        self.client = private_client
        self.public = public_client or canary.Client()
        self.sleep = sleep
        self.stage = "release"

    def release(self):
        self.operator.running_release()
        self.operator.image_tag()
        if self.client is None:
            peers = inventory.deployment(self.environment, lambda args: self.operator.commands.run(args))
            self.client = inventory.PrivateClient(self.environment.get("GATEWAY_AUTH_SECRET", ""), peers["origins"])

    def runtime(self):
        expected = {"media": {"PhotoVariantOptions__Enabled": "true", "PhotoVariantOptions__ReadEnabled": "true",
                              "PhotoVariantOptions__CleanupEnabled": "false", "PhotoVariantOptions__CleanupDryRun": "true"},
                    "shops": {"CheckInPhotoDeliveryOptions__ServeVariants": "true",
                              "CheckInPhotoDeliveryOptions__AdvertiseVariants": "true"}}
        for service, values in expected.items():
            keys = set(values) | {"CheckInPhotoDeliveryOptions__AdvertiseCanaryOnly", "CheckInPhotoDeliveryOptions__AdvertiseCanaryUserIds"}
            conditions = " ".join('(eq (index (split . "=") 0) "' + key + '")' for key in sorted(keys))
            template = '{{range .Config.Env}}{{if or ' + conditions + '}}{{println .}}{{end}}{{end}}'
            entries = self.operator.commands.run(["docker", "inspect", "--format", template, self.operator.container(service)]).splitlines()
            for key, value in values.items():
                canary.require([entry.split("=", 1)[1] for entry in entries if entry.startswith(key + "=")] == [value])
            canary.require(not any(entry.startswith("CheckInPhotoDeliveryOptions__AdvertiseCanary") for entry in entries))

    def activate(self):
        path = self.root / "activation.json"
        if path.exists():
            self.runtime()
            for service in ("shops", "media"):
                self.operator.healthy(service)
            canary.private_write(path, {"deploymentSha": self.sha, "verified": True})
            return {"enabledForAll": True, "reconciledWithoutRestart": True}
        env = activation.ROOT / "deploy" / ".env"
        canary.require(not env.is_symlink())
        metadata = env.stat()
        canary.require(stat.S_ISREG(metadata.st_mode))
        original = env.read_bytes()
        updated = env_replacement(original)
        canary.private_write(path, {"deploymentSha": self.sha, "attempted": True})
        descriptor, name = tempfile.mkstemp(prefix=".photo-full-env-", dir=env.parent)
        try:
            with os.fdopen(descriptor, "wb") as target:
                target.write(updated); target.flush(); os.fsync(target.fileno())
            os.chmod(name, 0o600)
            if hasattr(os, "chown"):
                os.chown(name, metadata.st_uid, metadata.st_gid)
            canary.require(not env.is_symlink() and env.read_bytes() == original)
            os.replace(name, env)
        finally:
            if os.path.exists(name):
                os.unlink(name)
        self.operator.commands.environment.update(FLAGS)
        for key in OBSOLETE:
            self.operator.commands.environment.pop(key, None)
        self.operator.commands.compose("up", "-d", "--no-deps", "shops", "media")
        for service in ("shops", "media"):
            self.operator.healthy(service)
        self.operator.running_release()
        self.runtime()
        canary.private_write(path, {"deploymentSha": self.sha, "verified": True})
        return {"enabledForAll": True, "restartedServices": 2}

    def latest_inventory(self):
        meta = canary.private_read(self.root / "inventory.json")
        canary.require(meta.get("deploymentSha") == self.sha and re.fullmatch(r"inventory-[0-9]{4}\.sqlite", meta.get("file", "")))
        checkpoint = scan.InventoryCheckpoint(self.root / meta["file"])
        try:
            state = checkpoint.read()
            canary.require(state.get("deploymentSha") == self.sha and state.get("complete") is True and state.get("verifyContent") is True)
            rows = [json.loads(row[0]) for row in checkpoint.db.execute("SELECT payload FROM coverage ORDER BY photo_id")]
            canary.require(len(rows) == state["totals"]["scanned"] and not state["totals"]["changedBindings"])
            return meta["file"], state, rows
        finally:
            checkpoint.close()

    def inventory(self):
        meta_path = self.root / "inventory.json"
        meta = canary.private_read(meta_path) if meta_path.exists() else {"index": 0}
        checkpoint = None
        if meta.get("file"):
            canary.require(re.fullmatch(r"inventory-[0-9]{4}\.sqlite", meta["file"]))
            checkpoint = scan.InventoryCheckpoint(self.root / meta["file"])
            if checkpoint.read()["complete"]:
                checkpoint.close(); checkpoint = None
        if checkpoint is None:
            index = meta["index"] + 1
            canary.require(1 <= index <= 9999)
            before = dt.datetime.now(dt.timezone.utc)
            if not self.path.exists():
                before = scan.timestamp(canary.private_read(activation.PRIVATE / "pilot.json")["beforeUtc"])
            initial = scan.new_inventory(inventory.SHOPS, inventory.MEDIA, scan.utc_text(before),
                                        scan.utc_text(before - dt.timedelta(days=30)), 5, True)
            initial["deploymentSha"] = self.sha
            meta = {"deploymentSha": self.sha, "index": index, "file": f"inventory-{index:04d}.sqlite"}
            checkpoint = scan.InventoryCheckpoint(self.root / meta["file"], initial)
            checkpoint.db.execute("CREATE TABLE coverage(photo_id TEXT PRIMARY KEY,payload TEXT NOT NULL)")
            os.chmod(self.root / meta["file"], 0o600)
            canary.private_write(meta_path, meta)
        try:
            state = checkpoint.read()
            canary.require(state.get("deploymentSha") == self.sha and state.get("pageSize") == 5 and state.get("verifyContent") is True)
            for _ in range(20):
                if state["complete"]:
                    break
                capture = Capture(self.client)
                updated, bindings = scan.inventory_step(capture, state, checkpoint.seen)
                commit_inventory(checkpoint, state, updated, bindings, capture)
                state = updated
                if not state["complete"]:
                    self.sleep(1)
            report = inventory.aggregate_inventory(state)
            canary.private_write(self.root / meta["file"].replace(".sqlite", "-report.json"), report)
            return report
        finally:
            checkpoint.close()

    def view(self, state, action="status"):
        return controls.run_control(type("Args", (), {"command": action, "media_url": inventory.MEDIA, "run_id": state["runId"]})(), self.client)

    def validate(self, state, view):
        canary.require(canary.identity(view["id"]) == state["runId"])
        for field in FROZEN:
            expected, actual = state["parameters"][field], view[field]
            canary.require(canonical_time(actual) == canonical_time(expected) if field.endswith("Utc") else actual == expected)

    def adopt(self):
        canary.require(not self.path.exists())
        old_path = activation.PRIVATE / "pilot.json"
        old_hash, old = digest(old_path), canary.private_read(old_path)
        canary.require(old.get("marker") == inventory.MARKER and old.get("deploymentSha") == PILOT_SHA
                       and old.get("pending") is None and not old.get("unresolved"))
        old["runId"] = canary.identity(old["runId"])
        current = self.view(old)
        canary.require(current.get("paused") is True and not any(current.get("jobs", {}).get(key, 0) for key in (*ACTIVE, "Failed", "Cancelled")))
        baseline, observed, rows = self.latest_inventory()
        eligible = [row for row in rows if row["inspection"].get("eligibilityFailure") is None]
        canary.require(eligible and all(row["inspection"].get("sourceStatus") == "verified" for row in eligible)
                       and current["recipeVersion"] == observed["recipeVersion"]
                       and canonical_time(current["beforeUtc"]) == canonical_time(observed["beforeUtc"])
                       and current["batchSize"] == 5 and current["minBatchIntervalSeconds"] == 5)
        for key in (*FROZEN, *PROGRESS):
            if key in old.get("lastObserved", {}):
                canary.require(current.get(key) == old["lastObserved"][key])
        canary.require(canonical_time(current["beforeUtc"]) == canonical_time(old["beforeUtc"])
                       and canonical_time(current["recentSinceUtc"]) == canonical_time(old["recentSinceUtc"])
                       and digest(old_path) == old_hash)
        state = {"marker": MARKER, "deploymentSha": self.sha, "runId": old["runId"], "sourceJournalSha256": old_hash,
                 "parameters": {key: current[key] for key in FROZEN}, "baselineFile": baseline,
                 "baselineEligible": len(eligible), "lastObserved": current, "pending": None,
                 "adoptedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat()}
        canary.private_write(self.root / "adopted-pilot.json", old)
        canary.private_write(self.path, state)
        return {"adoptedPausedRun": True, "eligibleArchivePhotos": len(eligible), "run": inventory.safe_run(current)}

    def state(self):
        state = canary.private_read(self.path)
        canary.require(state.get("marker") == MARKER and state.get("deploymentSha") == self.sha
                       and digest(activation.PRIVATE / "pilot.json") == state["sourceJournalSha256"])
        return state

    def status(self, state):
        view = self.view(state)
        self.validate(state, view)
        def reconciled(pending):
            action = pending["operation"]
            return (action == "resume" and view["paused"] is False or action == "pause" and view["paused"] is True
                    or action == "advance" and any(view.get(key) != pending["before"].get(key) for key in PROGRESS))
        if state.get("pending") and reconciled(state["pending"]):
            state["pending"] = None
        state["unresolved"] = [item for item in state.get("unresolved", []) if not reconciled(item)]
        state["lastObserved"] = view
        canary.private_write(self.path, state)
        return view

    def mutate(self, state, action):
        canary.require(state.get("pending") is None)
        state["pending"] = {"operation": action, "before": state["lastObserved"]}
        canary.private_write(self.path, state)
        result = self.view(state, action)
        self.validate(state, result)
        canary.require(result.get("outcome") in {"resumed", "paused", "advanced", "scan_complete", "rate_limited", "queue_full"})
        state["pending"] = None
        state["lastObserved"] = result
        canary.private_write(self.path, state)
        return result

    def advance(self, state):
        view = self.status(state)
        canary.require(state.get("pending") is None and not state.get("unresolved"))
        if view["paused"]:
            view = self.mutate(state, "resume")
        pages = 0
        while pages < 20 and not view["scanComplete"]:
            delay = max(0, (scan.timestamp(view["nextBatchAtUtc"]) - dt.datetime.now(dt.timezone.utc)).total_seconds())
            canary.require(delay <= 10)
            if delay:
                self.sleep(delay + 0.05)
            view = self.mutate(state, "advance")
            if view.get("outcome") in ("queue_full", "rate_limited"):
                break
            pages += 1
        view = self.status(state)
        return {"pagesAdvanced": pages, "run": inventory.safe_run(view), "requiresManualReconciliation": state.get("pending") is not None}

    def coverage(self):
        _, observed, rows = self.latest_inventory()
        state = self.state()
        canary.require(observed["recipeVersion"] == state["parameters"]["recipeVersion"])
        eligible = [row for row in rows if row["inspection"].get("eligibilityFailure") is None]
        canary.require(eligible and all(row["inspection"].get("sourceStatus") == "verified"
            and row["inspection"].get("jobStatus") == "Ready" and (row["inspection"].get("currentVariantBytes") or 0) > 0 for row in eligible))
        baseline = scan.InventoryCheckpoint(self.root / state["baselineFile"])
        try:
            wanted = {json.loads(payload)["candidate"]["photoId"]: json.loads(payload)["candidate"]["sourceVersion"]
                      for payload, in baseline.db.execute("SELECT payload FROM coverage")
                      if json.loads(payload)["inspection"].get("eligibilityFailure") is None}
        finally:
            baseline.close()
        current = {row["candidate"]["photoId"]: row["candidate"]["sourceVersion"] for row in eligible}
        canary.require(len(wanted) == state["baselineEligible"] and all(current.get(key) == value for key, value in wanted.items()))
        public = []
        for row in eligible:
            candidate, inspection = row["candidate"], row["inspection"]
            photo_id, check_in, owner = (canary.identity(candidate[key]) for key in ("photoId", "checkInId", "ownerId"))
            source, recipe = candidate["sourceVersion"], observed["recipeVersion"]
            canary.require(re.fullmatch(r"[0-9a-f]{64}", source) and type(recipe) is int and recipe > 0)
            community = self.operator.query("cpshopsdb", f'''SELECT COALESCE((SELECT json_build_object(
              'checkInId',c."Id",'ownerId',c."UserId",'storageKey',p."StorageKey",'sourceVersion',m."SourceVersion",
              'recipeVersion',m."RecipeVersion",'variants',m."VariantsJson",'deleted',c."DeletedAtUtc" IS NOT NULL,
              'public',c."DeletedAtUtc" IS NULL AND c."Visibility"=1 AND c."ModerationState"=2
                AND c."ApprovedRevision"=c."ContentRevision" AND (c."Kind"=1 OR EXISTS
                  (SELECT 1 FROM "Shops" s WHERE s."Id"=c."ShopId" AND NOT s."IsHidden")))
              FROM "CheckInPhotos" p LEFT JOIN "CanonicalCheckIns" c ON c."Id"=p."CanonicalCheckInId" AND p."OwnerId"=c."UserId"
              LEFT JOIN "CheckInPhotoVariantManifests" m ON m."PhotoId"=p."Id" AND m."CheckInId"=c."Id"
                AND m."SourceVersion"='{source}' AND m."RecipeVersion"={recipe}
                AND NOT EXISTS(SELECT 1 FROM "CheckInPhotoVariantManifests" newer WHERE newer."PhotoId"=m."PhotoId"
                    AND newer."SourceVersion"=m."SourceVersion" AND newer."RecipeVersion">m."RecipeVersion")
              WHERE p."Id"='{photo_id}'::uuid),'{{}}'::json);''')
            canary.require(community.get("checkInId") == check_in and community.get("ownerId") == owner
                           and community.get("storageKey") == candidate["storageKey"] and community.get("deleted") is False
                           and community.get("sourceVersion") == source and community.get("recipeVersion") == recipe)
            media = self.operator.query("cpmediadb", f'''SELECT COALESCE((SELECT json_build_object(
              'sourceDigest',j."SourceDigest",'storageKey',p."StorageKey",'variants',(SELECT json_agg(json_build_object('purpose',v."Purpose",'format',v."Format",
                'contentType',v."ContentType",'width',v."Width",'height',v."Height",'sizeBytes',v."SizeBytes",'digest',v."Digest"))
                FROM "PhotoVariants" v WHERE v."JobId"=j."Id")) FROM "PhotoVariantJobs" j
              JOIN "Photos" p ON p."Id"=j."PhotoId" AND p."StorageKey"=j."SourceKey" AND p."CheckInId"=j."CheckInId"
                AND p."OwnerId"=j."OwnerId" AND p."Status"='Confirmed' AND p."BucketType"='CheckIn' AND p."ShopAttachmentId" IS NULL
              WHERE j."PhotoId"='{photo_id}'::uuid AND j."CheckInId"='{check_in}'::uuid AND j."OwnerId"='{owner}'::uuid
                AND j."SourceVersion"='{source}' AND j."RecipeVersion"={recipe} AND j."Status"='Ready'
                AND NOT EXISTS(SELECT 1 FROM "CheckInPhotoTombstones" t WHERE t."CheckInId"=j."CheckInId")),'{{}}'::json);''')
            canary.require(normalized(community.get("variants")) == normalized(media.get("variants"))
                           and media.get("storageKey") == candidate["storageKey"]
                           and re.fullmatch(r"[0-9a-f]{64}", media.get("sourceDigest", "")))
            if community.get("public") is True:
                public.append({"candidate": candidate, "variants": list(normalized(media["variants"]).values()),
                               "sourceDigest": media["sourceDigest"]})
        return {"eligibleReadyPhotos": len(eligible), "archiveCohortReady": len(wanted),
                "newerReadyPhotos": len(current.keys() - wanted.keys()), "communityManifestsVerified": len(eligible)}, public

    def verify(self):
        state = self.state()
        view = self.status(state)
        canary.require(state.get("pending") is None and not state.get("unresolved") and view.get("scanComplete") is True
                       and view.get("generationDrained") is True and not any(view.get("jobs", {}).get(key, 0) for key in (*ACTIVE, "Failed", "Cancelled")))
        result, public = self.coverage()
        canary.require(public)
        checks, sampled = 0, 0
        for row in public[:5]:
            candidate = row["candidate"]
            base = canary.base_path(candidate["checkInId"], candidate["photoId"])
            detail = self.public.api("GET", "/api/v1/check-ins/" + candidate["checkInId"])
            photos = [*detail.get("photos", [])]
            if isinstance(detail.get("drinkPhoto"), dict):
                photos.append(detail["drinkPhoto"])
            photo = next((photo for photo in photos if photo.get("id") == candidate["photoId"]), None)
            descriptors = photo.get("variants") if photo else detail.get("drinkPhotoVariants") if detail.get("drinkPhotoUrl") == base else None
            canary.require(isinstance(descriptors, list) and len(descriptors) == 6)
            expected = {(variant["purpose"], variant["format"]): variant for variant in row["variants"]}
            for variant in descriptors:
                key = variant["purpose"], variant["format"]
                match = expected.pop(key)
                canary.require(variant["sourceVersion"] == candidate["sourceVersion"]
                    and variant["recipeVersion"] == state["parameters"]["recipeVersion"]
                    and all(variant[field] == match[field] for field in ("contentType", "width", "height", "sizeBytes"))
                    and variant["url"] == base + "/variants/" + candidate["sourceVersion"] + "/"
                        + str(variant["recipeVersion"]) + "/" + variant["purpose"] + "." + variant["format"])
            variants = [dict(item, sourceVersion=candidate["sourceVersion"], recipeVersion=state["parameters"]["recipeVersion"]) for item in row["variants"]]
            checks += canary.photo_checks(self.public, base, variants, public=True, original_digest=row["sourceDigest"])
            sampled += 1
        old = canary.private_read(activation.PRIVATE / "canary.json")
        canary.validate_state(old)
        canary.require(old.get("deleted") is True and old.get("cleanupVerification", {}).get("denialChecks") == 38)
        auth = self.root / "verification-auth.json"
        copied = copy.deepcopy(old)
        copied.pop("accessToken", None)
        token = canary.login(copied, auth, self.public)
        denials = 0
        for photo in old["cleanupObservation"]["photos"]:
            for credentials in (None, token):
                canary.cleanup_denial(self.public, "GET", photo["originalUrl"], credentials)
                denials += 1
                for variant in photo["variants"]:
                    for method, etag in (("GET", None), ("HEAD", None), ("GET", variant["etag"])):
                        canary.cleanup_denial(self.public, method, variant["url"], credentials, etag)
                        denials += 1
        result.update(publicPhotosSampled=sampled, publicHttpChecks=checks, anonymousDescriptorsVerified=True,
                      deletedPrivateDenialChecks=denials, positiveOwnedPrivateProof="not_available_deleted_fixture")
        canary.private_write(self.root / "verification-report.json", result)
        return result

    def run(self, operation):
        self.release()
        self.stage = operation
        if operation == "full-activate":
            return self.activate()
        self.runtime()
        if operation == "archive-inventory":
            return self.inventory()
        if operation == "archive-adopt":
            return self.adopt()
        if operation == "archive-verify":
            return self.verify()
        state = self.state()
        if operation == "archive-advance":
            return self.advance(state)
        if operation == "archive-pause":
            if state.get("pending"):
                state.setdefault("unresolved", []).append(state["pending"]); state["pending"] = None
            return {"run": inventory.safe_run(self.mutate(state, "pause")), "requiresManualReconciliation": bool(state.get("unresolved"))}
        return {"run": inventory.safe_run(self.status(state)),
                "requiresManualReconciliation": state.get("pending") is not None or bool(state.get("unresolved"))}


def main(argv=None, environment=None, commands=None, private_client=None, public_client=None):
    operator = None
    try:
        canary.require(not (sys.argv[1:] if argv is None else argv) and Path.cwd().resolve() == activation.ROOT)
        environment = dict(os.environ if environment is None else environment)
        operation = environment.get("PHOTO_OPERATION")
        canary.require(operation in OPERATIONS)
        with activation.private_lock():
            operator = Rollout(environment, commands, private_client, public_client)
            result = operator.run(OPERATIONS[operation])
        print(json.dumps({"success": True, "operation": operation, "result": result}, allow_nan=False))
        return 0
    except Exception:
        print(json.dumps({"success": False, "stage": operator.stage if operator else "validation",
                          "requiresPrivateStateReview": True, "automaticWriteRetry": False}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
