import contextlib
import copy
import datetime as dt
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch
import uuid

import photo_production_inventory as tool

SHA = "a" * 40
PRIVATE = "private-source-key-and-service-secret-that-must-not-be-printed"
RUN = "11111111-1111-4111-8111-111111111111"


class FakeClient:
    def __init__(self):
        self.calls = []
        self.run = None
        self.fail = None
        self.mutate_before_failure = False

    def call(self, origin, path, body=None, method="POST"):
        self.calls.append((origin, path, copy.deepcopy(body), method))
        if path.endswith("/candidates"):
            start = uuid.UUID(body["cursor"]["photoId"]).int if body["cursor"] else 0
            before = dt.datetime(2026, 1, 1, tzinfo=dt.timezone.utc)
            items = [{"photoId": str(uuid.UUID(int=index)), "checkInId": RUN, "ownerId": RUN,
                "storageKey": PRIVATE, "sourceVersion": f"{index:064x}",
                "sortAtUtc": (before - dt.timedelta(seconds=index)).isoformat()} for index in range(start + 1, start + 6)]
            last = items[-1]
            return {"items": items, "nextCursor": {key: last[key] for key in ("sortAtUtc", "photoId", "checkInId")}}
        if "/inspect?" in path:
            return [{"photoId": item["photoId"], "sourceStatus": "verified", "recipeVersion": 1,
                "jobStatus": "Ready" if uuid.UUID(item["photoId"]).int == 1 else None, "eligibilityFailure": None,
                "declaredBytes": 100, "actualBytes": 101, "currentVariantBytes": 20 if uuid.UUID(item["photoId"]).int == 1 else None,
                "metadataMismatch": True} for item in body]
        if path.endswith("/runs"):
            if self.fail == "create":
                raise tool.inventory.OperationError(PRIVATE)
            self.run = {"id": RUN, "beforeUtc": body["beforeUtc"], "recentSinceUtc": body["recentSinceUtc"],
                        "recipeVersion": 1, "phase": 0, "batchSize": 5, "minBatchIntervalSeconds": 5, "paused": True,
                        "scanComplete": False, "nextBatchAtUtc": body["beforeUtc"], "scannedCount": 0,
                        "enqueuedCount": 0, "existingCount": 0, "blockedCount": 0, "storageKey": PRIVATE}
            return copy.deepcopy(self.run)
        action = path.rsplit("/", 1)[-1]
        if action in ("advance", "resume", "pause"):
            if self.fail != action or self.mutate_before_failure:
                if action == "resume": self.run["paused"] = False
                if action == "pause": self.run["paused"] = True
                if action == "advance":
                    self.run["scannedCount"] += 5; self.run["enqueuedCount"] += 5
            if self.fail == action:
                raise tool.inventory.OperationError(PRIVATE)
            return {"outcome": {"resume": "resumed", "pause": "paused", "advance": "advanced"}[action], "run": copy.deepcopy(self.run)}
        return {"run": copy.deepcopy(self.run), "jobs": {"Pending": self.run["enqueuedCount"], PRIVATE: 100}, "generationDrained": False}


class ProductionInventoryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        tool.private_directory(self.root)
        self.release = {"sha": SHA, "generationEnabled": True, "origins": {tool.SHOPS: "http://172.20.0.2:8080", tool.MEDIA: "http://172.20.0.3:8080"}}
        self.client = FakeClient()

    def test_inventory_is_capped_at_twenty_five_photo_pages_with_content_verification_and_aggregates_only(self):
        result = tool.bounded_inventory(self.root, self.release, self.client, 20, sleep=Mock())
        self.assertEqual(100, result["totals"]["scanned"])
        self.assertEqual(10100, result["totals"]["verifiedSourceBytes"])
        self.assertEqual(1, result["totals"]["readySampleCount"])
        self.assertEqual(20, result["totals"]["readySampleVariantBytes"])
        self.assertTrue(result["verifyContent"])
        self.assertEqual(20, len([call for call in self.client.calls if call[1].endswith("/candidates")]))
        self.assertTrue(all(call[2]["limit"] == 5 for call in self.client.calls if call[1].endswith("/candidates")))
        self.assertTrue(all(len(call[2]) == 5 for call in self.client.calls if "/inspect?verifyContent=true" in call[1]))
        self.assertFalse(any("/runs" in call[1] for call in self.client.calls))
        self.assertNotIn(PRIVATE, json.dumps(result)); self.assertNotIn(RUN, json.dumps(result))
        self.assertEqual(result, tool.read_json(self.root / "inventory-report.json"))
        with self.assertRaises(tool.inventory.OperationError):
            tool.bounded_inventory(self.root, self.release, self.client, 21)

    def test_inventory_resume_keeps_frozen_cutoff_and_distinct_ledger_when_container_addresses_change(self):
        first = tool.bounded_inventory(self.root, self.release, self.client, 1)
        before = self.client.calls[0][2]["beforeUtc"]
        changed_ips = self.release | {"origins": {tool.SHOPS: "http://172.20.0.5:8080", tool.MEDIA: "http://172.20.0.6:8080"}}
        second = tool.bounded_inventory(self.root, changed_ips, self.client, 1)
        self.assertEqual(5, first["totals"]["scanned"]); self.assertEqual(10, second["totals"]["scanned"])
        self.assertEqual(before, self.client.calls[2][2]["beforeUtc"])
        with self.assertRaises(tool.inventory.OperationError):
            tool.bounded_inventory(self.root, self.release | {"sha": "b" * 40}, self.client, 1)

    def test_inventory_inspection_failure_does_not_advance_checkpoint(self):
        original = self.client.call
        def fail(origin, path, body=None, method="POST"):
            if "/inspect?" in path: raise tool.inventory.OperationError(PRIVATE)
            return original(origin, path, body, method)
        self.client.call = fail
        with self.assertRaises(tool.inventory.OperationError):
            tool.bounded_inventory(self.root, self.release, self.client, 1)
        checkpoint = tool.inventory.InventoryCheckpoint(self.root / "inventory.sqlite")
        try: self.assertEqual(0, checkpoint.read()["pages"])
        finally: checkpoint.close()

    def test_pilot_create_is_paused_and_advance_resumes_then_sends_exactly_one_page(self):
        created = tool.pilot(self.root, self.release, self.client, "pilot-create")
        self.assertTrue(created["run"]["paused"])
        self.assertNotIn(RUN, json.dumps(created))
        with self.assertRaises(tool.inventory.OperationError):
            tool.pilot(self.root, self.release, self.client, "pilot-create")
        advanced = tool.pilot(self.root, self.release, self.client, "pilot-advance")
        actions = [call[1].rsplit("/", 1)[-1] for call in self.client.calls]
        self.assertEqual(["runs", "resume", "advance"], actions)
        self.assertEqual(5, advanced["run"]["scannedCount"])
        self.assertNotIn(PRIVATE, json.dumps(advanced)); self.assertNotIn(RUN, json.dumps(advanced))
        paused = tool.pilot(self.root, self.release, self.client, "pilot-pause")
        self.assertTrue(paused["run"]["paused"])

    def test_actual_generation_disabled_prevents_any_resume_or_advance(self):
        tool.pilot(self.root, self.release, self.client, "pilot-create")
        self.client.calls.clear()
        with self.assertRaises(tool.inventory.OperationError):
            tool.pilot(self.root, self.release | {"generationEnabled": False}, self.client, "pilot-advance")
        self.assertEqual([], self.client.calls)

    def test_uncertain_create_is_never_repeated_and_status_requires_manual_reconciliation(self):
        self.client.fail = "create"
        with self.assertRaises(tool.inventory.OperationError):
            tool.pilot(self.root, self.release, self.client, "pilot-create")
        with self.assertRaises(tool.inventory.OperationError):
            tool.pilot(self.root, self.release, self.client, "pilot-create")
        status = tool.pilot(self.root, self.release, self.client, "pilot-status")
        self.assertTrue(status["requiresManualReconciliation"])
        self.assertEqual(1, len(self.client.calls))
        self.assertEqual("create", tool.read_json(self.root / "pilot.json")["pending"]["operation"])

    def test_uncertain_advance_reconciles_only_after_observed_server_progress_without_automatic_write_retry(self):
        tool.pilot(self.root, self.release, self.client, "pilot-create")
        self.client.fail = "advance"; self.client.mutate_before_failure = True
        with self.assertRaises(tool.inventory.OperationError):
            tool.pilot(self.root, self.release, self.client, "pilot-advance")
        with self.assertRaises(tool.inventory.OperationError):
            tool.pilot(self.root, self.release, self.client, "pilot-advance")
        self.assertEqual(1, len([call for call in self.client.calls if call[1].endswith("/advance")]))
        status = tool.pilot(self.root, self.release, self.client, "pilot-status")
        self.assertFalse(status["requiresManualReconciliation"])
        self.assertEqual(5, status["run"]["scannedCount"])

    def test_uncertain_unchanged_advance_is_retained_but_explicit_pause_is_available(self):
        tool.pilot(self.root, self.release, self.client, "pilot-create")
        self.client.fail = "advance"
        with self.assertRaises(tool.inventory.OperationError):
            tool.pilot(self.root, self.release, self.client, "pilot-advance")
        self.assertTrue(tool.pilot(self.root, self.release, self.client, "pilot-status")["requiresManualReconciliation"])
        self.client.fail = None
        paused = tool.pilot(self.root, self.release, self.client, "pilot-pause")
        self.assertTrue(paused["run"]["paused"]); self.assertTrue(paused["requiresManualReconciliation"])
        with self.assertRaises(tool.inventory.OperationError):
            tool.pilot(self.root, self.release, self.client, "pilot-advance")

    def test_untrusted_eligibility_classification_cannot_enter_stdout_report(self):
        state = tool.inventory.new_inventory(tool.SHOPS, tool.MEDIA, "2026-10-18T00:00:00Z", "2026-09-18T00:00:00Z", 5, True)
        state["totals"]["eligibility"][PRIVATE] = 1
        result = tool.aggregate_inventory(state)
        self.assertEqual({"other": 1}, result["totals"]["eligibility"])
        self.assertNotIn(PRIVATE, json.dumps(result))

    def test_deployment_discovers_only_label_scoped_private_peers_and_checks_commit_revision(self):
        calls = []
        def docker(args):
            calls.append(args)
            if args[1] == "ps": return "a" * 64 if "label=com.docker.compose.service=shops" in args else "b" * 64
            return json.dumps({"revision": SHA, "generationEnabled": "true", "networks": {
                "coffeepeek_default": {"IPAddress": "172.20.0.2" if args[-1].startswith("a") else "172.20.0.3"}}})
        release = tool.deployment({"IMAGE_TAG": "sha-" + SHA}, docker)
        self.assertEqual("http://172.20.0.3:8080", release["origins"][tool.MEDIA])
        self.assertTrue(release["generationEnabled"])
        self.assertTrue(all("label=com.docker.compose.project=coffeepeek" in call for call in calls if call[1] == "ps"))
        self.assertNotIn("{{json .Config.Env}}", tool.INSPECT)
        with self.assertRaises(tool.inventory.OperationError):
            tool.deployment({"IMAGE_TAG": "sha-" + SHA, "EXPECTED_DEPLOY_SHA": "b" * 40}, docker)
        with self.assertRaises(tool.inventory.OperationError):
            tool.deployment({"IMAGE_TAG": "dev"}, docker)

    def test_changed_or_missing_revision_is_rejected_before_service_requests(self):
        docker = Mock(side_effect=["a" * 64, json.dumps({"revision": "b" * 40, "networks": {}})])
        with self.assertRaises(tool.inventory.OperationError): tool.deployment({"IMAGE_TAG": "sha-" + SHA}, docker)

    def test_shared_proxy_network_does_not_override_exact_default_network(self):
        def docker(args):
            if args[1] == "ps": return "a" * 64 if "label=com.docker.compose.service=shops" in args else "b" * 64
            return json.dumps({"revision": SHA, "networks": {"coffeepeek_proxy": {"IPAddress": "172.30.42.8"},
                "coffeepeek_default": {"IPAddress": "172.20.0.2" if args[-1].startswith("a") else "172.20.0.3"}}})
        observed = tool.deployment({"IMAGE_TAG": "sha-" + SHA}, docker)
        self.assertEqual("http://172.20.0.3:8080", observed["origins"][tool.MEDIA])

    def test_workflow_environment_without_positional_args_runs_one_bounded_twenty_page_inventory(self):
        output = io.StringIO()
        with patch.object(tool.sys, "argv", ["photo_production_inventory.py"]), patch.dict(os.environ, {"PHOTO_OPERATION": "inventory"}), \
                patch.object(tool, "REPO", Path.cwd()), patch.object(tool, "STATE", self.root), \
                patch.object(tool, "deployment", return_value=self.release), patch.object(tool, "PrivateClient", return_value=self.client), \
                patch.object(tool, "bounded_inventory", return_value={"complete": True}) as bounded, contextlib.redirect_stdout(output):
            self.assertEqual(0, tool.main())
        self.assertEqual(20, bounded.call_args.args[3])
        self.assertTrue(json.loads(output.getvalue())["complete"])

    def test_main_failure_is_sanitized_json_and_does_not_print_container_or_service_errors(self):
        output = io.StringIO()
        with patch.object(tool, "REPO", Path.cwd()), patch.object(tool, "STATE", self.root), \
                patch.object(tool, "deployment", side_effect=RuntimeError(PRIVATE)), contextlib.redirect_stdout(output):
            self.assertEqual(1, tool.main(["inventory"]))
        self.assertEqual("operation_stopped", json.loads(output.getvalue())["state"])
        self.assertNotIn(PRIVATE, output.getvalue())


if __name__ == "__main__":
    unittest.main()
