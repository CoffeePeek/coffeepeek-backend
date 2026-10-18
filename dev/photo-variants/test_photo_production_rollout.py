import contextlib
import copy
import datetime as dt
import hashlib
import io
import json
import os
from pathlib import Path
import re
import tempfile
import unittest
from unittest.mock import Mock, patch
import uuid

import photo_production_rollout as tool

SHA = "a" * 40
RUN = "11111111-1111-4111-8111-111111111111"
BEFORE = "2026-10-18T00:00:00Z"
RECENT = "2026-09-18T00:00:00Z"
SECRET = "private-password-and-storage-key-that-must-not-appear"


def variants():
    return [{"purpose": purpose, "format": fmt, "contentType": "image/" + fmt,
             "width": 160, "height": 100, "sizeBytes": 20, "digest": "d" * 64}
            for purpose in tool.canary.PURPOSES for fmt in ("webp", "jpeg")]


class PrivateClient:
    def __init__(self):
        self.total, self.ready, self.fail, self.lost, self.calls = 10, False, None, False, []
        self.run = {"id": RUN, "beforeUtc": BEFORE, "recentSinceUtc": RECENT, "recipeVersion": 1, "phase": 0,
                    "batchSize": 5, "minBatchIntervalSeconds": 5, "paused": True, "scanComplete": False,
                    "nextBatchAtUtc": BEFORE, "scannedCount": 5, "enqueuedCount": 5, "existingCount": 0, "blockedCount": 0}

    def candidate(self, index):
        photo = str(uuid.UUID(int=index))
        key = "check-ins/" + uuid.UUID(photo).hex + ".jpg"
        return {"photoId": photo, "checkInId": RUN, "ownerId": RUN, "storageKey": key,
                "sourceVersion": hashlib.sha256(("checkin:" + uuid.UUID(photo).hex + ":" + key).encode()).hexdigest(),
                "kind": 0, "isDrink": False, "sortAtUtc": (tool.scan.timestamp(BEFORE) - dt.timedelta(seconds=index)).isoformat(),
                "declaredSizeBytes": 100, "contentType": "image/jpeg"}

    def call(self, origin, path, body=None, method="POST"):
        self.calls.append((path, copy.deepcopy(body), method))
        if path.endswith("/candidates"):
            start = uuid.UUID(body["cursor"]["photoId"]).int if body["cursor"] else 0
            items = [self.candidate(index) for index in range(start + 1, min(start + 5, self.total) + 1)] if body["phase"] == 0 else []
            cursor = {key: items[-1][key] for key in ("sortAtUtc", "photoId", "checkInId")} if items and start + 5 < self.total else None
            return {"items": items, "nextCursor": cursor}
        if "/inspect?" in path:
            return [{"photoId": row["photoId"], "eligibilityFailure": None, "sourceStatus": "verified",
                     "recipeVersion": 1, "declaredBytes": 100, "actualBytes": 100,
                     "jobStatus": "Ready" if self.ready or uuid.UUID(row["photoId"]).int <= 5 else None,
                     "currentVariantBytes": 120 if self.ready or uuid.UUID(row["photoId"]).int <= 5 else None} for row in body]
        action = path.rsplit("/", 1)[-1]
        if method == "GET":
            return {"run": copy.deepcopy(self.run), "jobs": {"Ready": self.run["enqueuedCount"]},
                    "generationDrained": self.run["scanComplete"]}
        if self.fail != action or self.lost:
            if action == "resume": self.run["paused"] = False
            elif action == "pause": self.run["paused"] = True
            elif action == "advance":
                self.run["scannedCount"] += 5; self.run["enqueuedCount"] += 5
                self.run["scanComplete"] = self.run["scannedCount"] >= self.total
        if self.fail == action:
            raise RuntimeError(SECRET)
        return {"run": copy.deepcopy(self.run), "outcome": "scan_complete" if self.run["scanComplete"] and action == "advance"
                else {"resume": "resumed", "pause": "paused", "advance": "advanced"}[action]}


class Commands:
    def __init__(self, environment):
        self.environment, self.calls, self.bad_flags = dict(environment), [], False

    def compose(self, *args):
        self.calls.append(("compose", args))
        return "1" * 64 if args[:3] == ("ps", "-a", "-q") else ""

    def run(self, args, **kwargs):
        self.calls.append(("run", args))
        if args[:3] == ["docker", "image", "inspect"]:
            return SHA
        if args[:2] == ["docker", "inspect"]:
            if '"running"' in args[3]:
                return json.dumps({"running": True, "image": "sha256:" + "b" * 64})
            if ".Config.Env" in args[3]:
                return "\n".join(["PhotoVariantOptions__Enabled=" + ("false" if self.bad_flags else "true"),
                    "PhotoVariantOptions__ReadEnabled=true", "PhotoVariantOptions__CleanupEnabled=false",
                    "PhotoVariantOptions__CleanupDryRun=true", "CheckInPhotoDeliveryOptions__ServeVariants=true",
                    "CheckInPhotoDeliveryOptions__AdvertiseVariants=true", "CheckInPhotoDeliveryOptions__CachePublicVariants=false"])
            return "healthy"
        raise AssertionError("Unexpected external command")


class RolloutTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name); self.private = self.repo / "private"
        self.private.mkdir(mode=0o700); (self.repo / "deploy").mkdir()
        self.env_path = self.repo / "deploy" / ".env"
        self.original = ("IMAGE_TAG=sha-" + SHA + '\nOTHER_SECRET="' + SECRET + '"\nCHECKIN_VARIANTS_CANARY_ONLY=true\nCHECKIN_VARIANTS_CANARY_USER_IDS=' + RUN + '\n').encode()
        self.env_path.write_bytes(self.original)
        self.addCleanup(patch.stopall)
        patch.object(tool.activation, "ROOT", self.repo).start()
        patch.object(tool.activation, "PRIVATE", self.private).start()
        self.environment = {"EXPECTED_DEPLOY_SHA": SHA, "IMAGE_TAG": "sha-" + SHA, "PHOTO_OPERATION": "rollout-status",
                            "CHECKIN_VARIANTS_CANARY_ONLY": "true", "CHECKIN_VARIANTS_CANARY_USER_IDS": RUN}
        self.client, self.commands = PrivateClient(), Commands(self.environment)
        self.operator = tool.Rollout(self.environment, self.commands, self.client, Mock(), sleep=Mock())
        self.old = {"marker": tool.inventory.MARKER, "version": 1, "deploymentSha": tool.PILOT_SHA, "runId": RUN,
                    "beforeUtc": BEFORE, "recentSinceUtc": RECENT, "pending": None, "lastObserved": copy.deepcopy(self.client.run)}
        tool.canary.private_write(self.private / "pilot.json", self.old)

    def baseline(self):
        result = self.operator.inventory()
        self.assertTrue(result["complete"])
        return result

    def adopt(self):
        self.baseline(); self.operator.adopt()
        return self.operator.state()

    def test_enable_preserves_unrelated_env_removes_old_gate_and_validates_actual_runtime(self):
        result = self.operator.run("full-activate")
        content = self.env_path.read_bytes()
        self.assertTrue(result["enabledForAll"])
        self.assertIn(('OTHER_SECRET="' + SECRET + '"\n').encode(), content)
        for key in tool.OBSOLETE:
            self.assertNotIn(key.encode(), content)
            self.assertNotIn(key, self.commands.environment)
        for key, value in tool.FLAGS.items():
            self.assertEqual(1, content.count((key + "=" + value).encode()))
        self.assertIn(("compose", ("up", "-d", "--no-deps", "shops", "media")), self.commands.calls)
        self.assertNotIn("{{json .Config.Env}}", json.dumps(self.commands.calls))

    def test_env_parser_preserves_quoted_multiline_and_refuses_duplicate_gate(self):
        original = b'SECRET="first\nCHECKIN_VARIANTS_CANARY_ONLY=fake\nlast"\nCHECKIN_VARIANTS_CANARY_ONLY=true\n'
        output = tool.env_replacement(original)
        self.assertTrue(output.startswith(b'SECRET="first\nCHECKIN_VARIANTS_CANARY_ONLY=fake\nlast"\n'))
        with self.assertRaises(tool.canary.CanaryError):
            tool.env_replacement(b'CHECKIN_VARIANTS_CANARY_ONLY=true\nCHECKIN_VARIANTS_CANARY_ONLY=false\n')

    def test_uncertain_activation_does_not_repeat_restart_if_actual_runtime_does_not_prove_completion(self):
        tool.canary.private_write(self.operator.root / "activation.json", {"attempted": True})
        self.commands.bad_flags = True
        with self.assertRaises(tool.canary.CanaryError): self.operator.activate()
        self.assertFalse(any(kind == "compose" and args[0] == "up" for kind, args in self.commands.calls))

    def test_inventory_is_fresh_bounded_full_source_and_atomic_coverage_ledger(self):
        first = self.baseline()
        self.assertEqual(10, first["totals"]["scanned"])
        filename, state, rows = self.operator.latest_inventory()
        self.assertEqual(10, len(rows)); self.assertEqual(1000, first["totals"]["verifiedSourceBytes"])
        self.assertEqual(SHA, state["deploymentSha"])
        original = (self.operator.root / filename).read_bytes()
        self.operator.inventory()
        latest, _, _ = self.operator.latest_inventory()
        self.assertNotEqual(filename, latest)
        self.assertEqual(original, (self.operator.root / filename).read_bytes())
        self.assertTrue(all(body["limit"] == 5 for path, body, _ in self.client.calls if path.endswith("/candidates")))
        self.assertFalse(any("/runs" in path for path, _, _ in self.client.calls))

    def test_failed_inspection_does_not_commit_cursor_or_denominator(self):
        call = self.client.call
        def failing(origin, path, body=None, method="POST"):
            if "/inspect?" in path: raise RuntimeError(SECRET)
            return call(origin, path, body, method)
        self.client.call = failing
        with self.assertRaises(RuntimeError): self.operator.inventory()
        checkpoint = tool.scan.InventoryCheckpoint(self.operator.root / "inventory-0001.sqlite")
        try:
            self.assertEqual(0, checkpoint.read()["pages"])
            self.assertEqual(0, checkpoint.db.execute("SELECT count(*) FROM coverage").fetchone()[0])
        finally: checkpoint.close()

    def test_adoption_preserves_old_journal_records_lineage_and_requires_paused_exact_params(self):
        original = (self.private / "pilot.json").read_bytes()
        state = self.adopt()
        self.assertEqual(original, (self.private / "pilot.json").read_bytes())
        self.assertEqual(hashlib.sha256(original).hexdigest(), state["sourceJournalSha256"])
        self.assertEqual(10, state["baselineEligible"])
        self.assertNotIn(RUN, json.dumps(self.operator.adopt.__name__))
        with self.assertRaises(tool.canary.CanaryError): self.operator.adopt()

    def test_adoption_refuses_old_uncertain_write_wrong_runtime_recipe_or_unpaused_run(self):
        self.baseline()
        for mutate in (lambda: self.old.update(pending={"operation": "advance"}),
                       lambda: self.old.update(unresolved=[{"operation": "advance"}]),
                       lambda: self.client.run.update(paused=False), lambda: self.client.run.update(recipeVersion=2)):
            old, current = copy.deepcopy(self.old), copy.deepcopy(self.client.run)
            mutate(); tool.canary.private_write(self.private / "pilot.json", self.old)
            with self.assertRaises(tool.canary.CanaryError): self.operator.adopt()
            self.old, self.client.run = old, current
        tool.canary.private_write(self.private / "pilot.json", self.old)

    def test_advance_is_bounded_twenty_pages_and_keeps_batch_five(self):
        state = self.adopt(); self.client.total = 10000
        result = self.operator.advance(state)
        self.assertEqual(20, result["pagesAdvanced"])
        self.assertEqual(20, sum(path.endswith("/advance") for path, _, _ in self.client.calls))
        self.assertEqual(105, self.client.run["scannedCount"])

    def test_lost_advance_is_not_repeated_and_readonly_status_reconciles_only_proven_progress(self):
        state = self.adopt(); self.client.fail = "advance"; self.client.lost = True
        with self.assertRaises(RuntimeError): self.operator.advance(state)
        state = self.operator.state()
        self.assertEqual("advance", state["pending"]["operation"])
        self.operator.status(state)
        self.assertIsNone(state["pending"])
        self.assertEqual(1, sum(path.endswith("/advance") for path, _, _ in self.client.calls))

    def test_unchanged_uncertain_write_stays_blocked_and_pause_retains_until_status_proves_progress(self):
        state = self.adopt(); self.client.fail = "advance"
        with self.assertRaises(RuntimeError): self.operator.advance(state)
        state = self.operator.state(); self.operator.status(state)
        self.assertIsNotNone(state["pending"])
        with self.assertRaises(tool.canary.CanaryError): self.operator.advance(state)
        self.client.fail = None
        state["unresolved"] = [state["pending"]]; state["pending"] = None
        self.operator.mutate(state, "pause")
        self.assertEqual(1, len(state["unresolved"]))
        self.client.run["scannedCount"] += 5
        self.operator.status(state)
        self.assertEqual([], state["unresolved"])

    def test_any_change_to_original_pilot_file_stops_further_control(self):
        self.adopt()
        self.old["lastObserved"]["scannedCount"] += 1
        tool.canary.private_write(self.private / "pilot.json", self.old)
        with self.assertRaises(tool.canary.CanaryError): self.operator.state()

    def test_manifest_normalization_requires_all_six_exact_digests_sizes_and_recipe_purposes(self):
        rows = variants()
        pascal = [{key[0].upper() + key[1:]: value for key, value in row.items()} for row in rows]
        self.assertEqual(tool.normalized(rows), tool.normalized(json.dumps(pascal)))
        for wrong in (rows[:-1], rows[:-1] + [rows[0]], rows[:-1] + [{**rows[-1], "digest": "bad"}],
                      rows[:-1] + [{**rows[-1], "width": 99999}]):
            with self.assertRaises(tool.canary.CanaryError): tool.normalized(wrong)

    def test_coverage_requires_ready_full_source_and_exact_current_community_and_media(self):
        self.adopt(); self.client.ready = True; self.operator.inventory()
        calls = []
        def query(database, sql):
            calls.append((database, sql))
            photo = re.search(r'(?:p|j)\."(?:Id|PhotoId)"=\'([0-9a-f-]{36})\'::uuid', sql)[1]
            candidate = self.client.candidate(uuid.UUID(photo).int)
            if database == "cpshopsdb":
                return {"checkInId": RUN, "ownerId": RUN, "storageKey": candidate["storageKey"], "deleted": False,
                        "sourceVersion": candidate["sourceVersion"], "recipeVersion": 1, "variants": variants(), "public": True}
            return {"sourceDigest": "d" * 64, "storageKey": candidate["storageKey"], "variants": variants()}
        self.operator.operator.query = query
        result, public = self.operator.coverage()
        self.assertEqual(10, result["archiveCohortReady"])
        self.assertEqual(10, result["communityManifestsVerified"])
        self.assertEqual(10, len(public))
        media = [sql for database, sql in calls if database == "cpmediadb"]
        self.assertTrue(all('JOIN "Photos"' in sql and '"CheckInPhotoTombstones"' in sql and '"StorageKey"=j."SourceKey"' in sql for sql in media))
        self.operator.operator.query = lambda *_: {}
        with self.assertRaises(tool.canary.CanaryError): self.operator.coverage()

    def test_main_accepts_exact_workflow_operation_and_never_emits_exception_or_private_ids(self):
        output = io.StringIO()
        with patch.object(Path, "cwd", return_value=self.repo), patch.object(tool.activation, "private_lock", contextlib.nullcontext), \
                patch.object(tool.Rollout, "run", side_effect=RuntimeError(SECRET)), contextlib.redirect_stdout(output):
            result = tool.main([], self.environment, self.commands, self.client, Mock())
        self.assertEqual(1, result)
        self.assertNotIn(SECRET, output.getvalue()); self.assertNotIn(RUN, output.getvalue())
        self.assertFalse(json.loads(output.getvalue())["automaticWriteRetry"])
        self.assertEqual({"rollout-enable", "rollout-adopt", "rollout-advance", "rollout-status", "rollout-inventory", "rollout-verify", "rollout-pause"}, set(tool.OPERATIONS))


if __name__ == "__main__":
    unittest.main()
