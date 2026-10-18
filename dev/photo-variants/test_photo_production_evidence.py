import contextlib
import copy
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import uuid

import photo_production_activation as activation
import photo_production_canary as canary
import photo_production_evidence as evidence


class FakeCommands:
    def __init__(self, environment):
        self.environment = dict(environment)
        self.calls = []
        self.identities = [str(uuid.uuid4()) for _ in range(3)]
        self.rows = {table: [{"id": identity, "fingerprint": "b" * 32} for identity in self.identities[:2]]
                     for table in evidence.TABLES}
        self.bad_release = False
        self.bad_rss = False
        self.fail = False
        self.media = {"queues": [{"priority": 0, "status": "Pending", "count": 2},
                                 {"priority": 1, "status": "Ready", "count": 1}],
                      "pilot": {"readyJobs": 1, "declaredReadySourceBytes": 500,
                                "readyVariantBytes": 300,
                                "variants": [{"purpose": "feed", "format": "webp", "count": 1, "bytes": 100}],
                                "queueToReadyLatency": {"samples": 1, "below1Second": 0, "from1To10Seconds": 1,
                                    "from10To60Seconds": 0, "atLeast60Seconds": 0, "negativeClockSamples": 0,
                                    "p50Ms": 1250.25, "p95Ms": 1250.25, "maxMs": 1250.25}}}

    def compose(self, *args):
        self.calls.append(("compose", args, None, None))
        if args[:3] == ("ps", "-a", "-q"):
            return "1" * 64
        raise AssertionError("Unexpected compose mutation")

    def run(self, args, stdin=None, environment=None, timeout=25):
        self.calls.append(("run", args, stdin, environment))
        if self.fail:
            raise RuntimeError("private-password secret-storage/key private@example.invalid")
        if args[:3] == ["docker", "image", "inspect"]:
            return "c" * 40 if self.bad_release else self.environment["EXPECTED_DEPLOY_SHA"]
        if args[:2] == ["docker", "inspect"]:
            return json.dumps({"running": True, "image": "sha256:" + "d" * 64})
        if args[:2] == ["docker", "stats"]:
            return json.dumps({"cpu": "12.50%", "memory": "15.25MiB / 1.5GiB", "pids": "7",
                               "ignored": "secret-storage/key"})
        if args[:2] == ["docker", "top"]:
            return "RSS\n1000\n250" if not self.bad_rss else "RSS\nprivate-password"
        if args[:2] == ["docker", "exec"]:
            self.assert_read_only(args, stdin, environment)
            return json.dumps(self.rows if args[args.index("-d") + 1] == "cpshopsdb" else self.media)
        raise AssertionError("Unreviewed Docker command")

    def assert_read_only(self, args, sql, environment):
        if not sql.startswith("BEGIN READ ONLY;") or not sql.endswith("ROLLBACK;\n"):
            raise AssertionError("Query escaped read-only transaction")
        if "private-password" in " ".join(args) or environment["PGPASSWORD"] != "private-password":
            raise AssertionError("Database credential exposure")
        for text in ("SET LOCAL statement_timeout = '5s'", "SET LOCAL lock_timeout = '2s'", "-X", "ON_ERROR_STOP=1"):
            if text not in sql and text not in args:
                raise AssertionError("Missing psql/read timeout constraint")


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.private = self.root / "private"
        self.private.mkdir(mode=0o700)
        (self.root / "deploy").mkdir()
        (self.root / "deploy" / ".env").write_text("IMAGE_TAG=sha-" + "a" * 40 + "\n")
        self.addCleanup(patch.stopall)
        patch.object(activation, "ROOT", self.root).start()
        patch.object(activation, "PRIVATE", self.private).start()
        self.environment = {"EXPECTED_DEPLOY_SHA": "a" * 40, "IMAGE_TAG": "sha-" + "a" * 40,
                            "POSTGRES_USER": "private-user", "POSTGRES_PASSWORD": "private-password"}
        self.commands = FakeCommands(self.environment)
        self.operator = evidence.Evidence(self.environment, self.commands)

    def pilot(self):
        identity = str(uuid.uuid4())
        canary.private_write(self.private / "pilot.json", {"marker": "coffeepeek-photo-production-inventory-v1",
            "deploymentSha": "a" * 40, "runId": identity})
        return identity

    def canary(self):
        identity = str(uuid.uuid4())
        canary.private_write(self.private / "canary.json", {"marker": canary.MARKER,
            "email": "photo_canary_" + "c" * 16 + "@photo-canary.invalid",
            "userName": "photo_canary_" + "c" * 16, "password": "s" * 32, "homeId": identity})
        return identity

    def test_before_after_keeps_per_id_fingerprints_private_and_reports_unchanged(self):
        self.pilot()
        before = self.operator.run("evidence-before")
        after = self.operator.run("evidence-after")
        self.assertTrue(before["success"])
        self.assertFalse(after["businessDifferencesObserved"])
        for table in evidence.TABLES:
            self.assertEqual({"before": 2, "after": 2, "added": 0, "removed": 0, "changed": 0, "unchanged": 2},
                             after["businessComparison"][table])
        output = json.dumps(after)
        self.assertFalse(any(identity in output for identity in self.commands.identities))
        self.assertNotIn('"fingerprint":', output)
        self.assertNotIn("b" * 32, output)
        self.assertNotIn("private-password", output)
        self.assertNotIn("secret-storage/key", output)
        private = canary.private_read(self.private / "evidence-before.json")
        self.assertEqual("b" * 32, private["rows"]["CanonicalCheckIns"][self.commands.identities[0]])
        if os.name != "nt":
            self.assertEqual(0o600, (self.private / "evidence-before.json").stat().st_mode & 0o777)

    def test_real_traffic_differences_are_aggregate_observations_not_automatic_failure(self):
        self.operator.run("evidence-before")
        self.commands.rows["CanonicalCheckIns"] = [{"id": self.commands.identities[0], "fingerprint": "c" * 32},
                                                    {"id": self.commands.identities[2], "fingerprint": "d" * 32}]
        after = self.operator.run("evidence-after")
        self.assertTrue(after["success"])
        self.assertTrue(after["businessDifferencesObserved"])
        group = after["businessComparison"]["CanonicalCheckIns"]
        self.assertEqual((1, 1, 1), (group["added"], group["changed"], group["removed"]))
        private = canary.private_read(self.private / "evidence-after.json")
        self.assertEqual([self.commands.identities[0]], private["differences"]["CanonicalCheckIns"]["changed"])

    def test_baseline_is_exclusive_and_never_replaced_or_recaptured(self):
        self.operator.run("evidence-before")
        original = (self.private / "evidence-before.json").read_bytes()
        self.commands.calls.clear()
        with self.assertRaises(canary.CanaryError):
            self.operator.run("evidence-before")
        self.assertEqual(original, (self.private / "evidence-before.json").read_bytes())
        self.assertEqual([], self.commands.calls)

    def test_new_known_canary_is_explicitly_excluded_after_baseline(self):
        self.operator.run("evidence-before")
        identity = self.canary()
        after = self.operator.run("evidence-after")
        self.assertTrue(after["excludedCanaryRows"])
        sql = [sql for kind, _, sql, _ in self.commands.calls if kind == "run" and sql and "CanonicalCheckIns" in sql][-1]
        self.assertIn(identity + "'::uuid", sql)
        self.assertIn('"CanonicalCheckInId"<>', sql)

    def test_business_query_fingerprints_entire_rows_in_one_snapshot_and_bounds_rows(self):
        self.operator.business(None)
        sql = [sql for kind, _, sql, _ in self.commands.calls if kind == "run" and sql][0]
        self.assertEqual(2, sql.count("md5(to_jsonb(bounded)::text)"))
        self.assertEqual(2, sql.count("LIMIT " + str(evidence.MAX_ROWS + 1)))
        self.assertIn('SELECT * FROM "CanonicalCheckIns"', sql)
        self.assertIn('SELECT * FROM "CheckInPhotos"', sql)
        self.commands.rows["CheckInPhotos"] *= evidence.MAX_ROWS
        with self.assertRaises(canary.CanaryError):
            self.operator.business(None)

    def test_pilot_bytes_and_latency_scope_to_durable_run_not_other_uploads(self):
        identity = self.pilot()
        result = self.operator.media()
        sql = [sql for kind, _, sql, _ in self.commands.calls if kind == "run" and sql][0]
        self.assertIn('WHERE "BackfillRunId"=\'' + identity + "'::uuid", sql)
        self.assertIn('JOIN ready j ON j."Id"=v."JobId"', sql)
        self.assertTrue(result["pilot"]["identified"])
        self.assertEqual(500, result["pilot"]["declaredReadySourceBytes"])
        self.assertEqual(300, result["pilot"]["readyVariantBytes"])
        self.assertEqual(1250.25, result["pilot"]["queueToReadyLatency"]["p95Ms"])
        self.assertNotIn(identity, json.dumps(result))

    def test_no_pilot_uses_null_filter_and_reports_unidentified(self):
        result = self.operator.media()
        sql = [sql for kind, _, sql, _ in self.commands.calls if kind == "run" and sql][0]
        self.assertIn('WHERE "BackfillRunId"=NULL::uuid', sql)
        self.assertFalse(result["pilot"]["identified"])

    def test_resource_observations_are_numeric_and_do_not_request_environment(self):
        result = self.operator.resources()
        for service in evidence.STAT_SERVICES:
            self.assertEqual(12.5, result[service]["cpuPercent"])
            self.assertEqual(15990784, result[service]["memoryWorkingSetBytes"])
            self.assertEqual(1280000, result[service]["processRssSumBytes"])
        commands = [args for kind, args, _, _ in self.commands.calls if kind == "run"]
        self.assertTrue(all(".Config.Env" not in " ".join(args) and ("--format" in args or args[1] == "top") for args in commands))
        self.assertEqual(3, sum(args[1] == "top" and args[-2:] == ["-eo", "rss"] for args in commands))

    def test_invalid_or_nonfinite_metrics_fail_closed_without_forwarding_raw_fields(self):
        original = copy.deepcopy(self.commands.media)
        for value in ("private-password", -1, True, float("nan")):
            self.commands.media = copy.deepcopy(original)
            self.commands.media["pilot"]["readyVariantBytes"] = value
            with self.assertRaises(canary.CanaryError):
                self.operator.media()
        self.commands.bad_rss = True
        with self.assertRaises(canary.CanaryError):
            self.operator.resources()

    def test_wrong_release_fails_before_observing_or_saving_business_data(self):
        self.commands.bad_release = True
        with self.assertRaises(canary.CanaryError):
            self.operator.run("evidence-before")
        self.assertFalse((self.private / "evidence-before.json").exists())
        self.assertFalse(any(sql for _, _, sql, _ in self.commands.calls))

    def test_main_uses_env_operation_and_sanitizes_all_exception_output(self):
        self.commands.fail = True
        environment = {**self.environment, "PHOTO_OPERATION": "evidence-before"}
        output = io.StringIO()
        with patch.object(Path, "cwd", return_value=self.root), patch.object(activation, "private_lock", contextlib.nullcontext), \
                contextlib.redirect_stdout(output):
            result = evidence.main([], environment, self.commands)
        self.assertEqual(1, result)
        self.assertEqual({"success": False, "stage": "release", "requiresPrivateStateReview": True}, json.loads(output.getvalue()))
        self.assertNotIn("private", output.getvalue().lower().replace("privatestatereview", ""))


if __name__ == "__main__":
    unittest.main()
