import contextlib
import copy
import http.client
import io
import json
from pathlib import Path
import sqlite3
import tempfile
import threading
import unittest
from unittest.mock import patch
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import photo_backfill as tool

BEFORE = "2026-10-18T12:00:00Z"
RECENT = "2026-09-18T12:00:00Z"
PHOTO = "00000000-0000-0000-0000-000000000003"
PARENT = "00000000-0000-0000-0000-000000000002"


def candidate(photo=PHOTO, source="a" * 64):
    return {"photoId": photo, "checkInId": PARENT, "ownerId": PARENT, "storageKey": "private-never-export-this-key",
            "sourceVersion": source, "sortAtUtc": RECENT, "kind": "Shop", "isDrink": False,
            "declaredSizeBytes": 100, "contentType": "image/png"}


def inspection(photo=PHOTO, **overrides):
    value = {"photoId": photo, "eligibilityFailure": None, "sourceStatus": "verified", "declaredBytes": 100,
             "mediaDeclaredBytes": 100, "actualBytes": 100, "metadataMismatch": False,
             "jobStatus": None, "currentVariantBytes": None, "recipeVersion": 1}
    value.update(overrides)
    return value


def state(verify=True):
    return tool.new_inventory("http://shops", "http://media", BEFORE, RECENT, 5, verify)


class FakeClient:
    def __init__(self, items=None, inspected=None, next_cursor=None):
        self.items = [candidate()] if items is None else items
        self.inspected = [inspection()] if inspected is None else inspected
        self.next_cursor = next_cursor
        self.calls = []

    def call(self, origin, path, body=None, method="POST"):
        self.calls.append((origin, path, copy.deepcopy(body), method))
        if origin == "http://shops":
            return {"items": self.items, "nextCursor": self.next_cursor}
        return self.inspected


class InventoryTests(unittest.TestCase):
    def test_no_measured_samples_means_no_fabricated_storage_or_time(self):
        updated, _ = tool.inventory_step(FakeClient(), state())
        report = tool.inventory_report(updated)
        self.assertEqual(1, report["totals"]["scanned"])
        self.assertIsNone(report["forecast"]["estimatedAdditionalVariantBytes"])
        self.assertIsNone(report["forecast"]["estimatedGenerationSeconds"])
        self.assertNotIn("private-never-export", json.dumps(report))

    def test_only_content_verified_complete_ready_samples_forecast_unique_remaining_population(self):
        values = [inspection(jobStatus="Ready", currentVariantBytes=600),
                  inspection("00000000-0000-0000-0000-000000000004"),
                  inspection("00000000-0000-0000-0000-000000000005", jobStatus="Failed"),
                  inspection("00000000-0000-0000-0000-000000000006", jobStatus="Ready", currentVariantBytes=900, sourceStatus="missing")]
        current = state()
        tool.add_inspection(current, [candidate(x["photoId"]) for x in values], values)
        report = tool.inventory_report(current, 2)
        self.assertEqual(1, report["forecast"]["sampleCount"])
        self.assertEqual(600, report["forecast"]["estimatedAdditionalVariantBytes"])
        self.assertEqual(1, report["forecast"]["estimatedGenerationSeconds"])
        self.assertEqual(1, report["forecast"]["remainingVerifiedPhotos"])

    def test_metadata_only_never_claims_zero_duration_or_verified_bytes(self):
        current = state(False)
        tool.add_inspection(current, [candidate()], [inspection(sourceStatus="unverified", actualBytes=None)])
        report = tool.inventory_report(current, 20)
        self.assertEqual(0, report["totals"]["verifiedSourceBytes"])
        self.assertIsNone(report["forecast"]["estimatedGenerationSeconds"])

    def test_unavailable_mismatch_and_recipe_change_never_mutate_prior_checkpoint(self):
        for rows in ([inspection(sourceStatus="unavailable")], [], [inspection(recipeVersion=2)]):
            initial = state()
            initial["recipeVersion"] = 1
            original = copy.deepcopy(initial)
            with self.assertRaises(tool.OperationError):
                tool.inventory_step(FakeClient(inspected=rows), initial)
            self.assertEqual(original, initial)

    def test_sqlite_commit_is_atomic_and_restart_deduplicates_phase_movement(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "checkpoint.sqlite"
            initial = state()
            db = tool.InventoryCheckpoint(path, initial)
            updated, bindings = tool.inventory_step(FakeClient(), initial, db.seen)
            db.commit(initial, updated, bindings)
            db.close()
            db = tool.InventoryCheckpoint(path)
            restarted = db.read()
            client = FakeClient()
            moved, new_bindings = tool.inventory_step(client, restarted, db.seen)
            db.commit(restarted, moved, new_bindings)
            self.assertEqual(1, moved["totals"]["scanned"])
            self.assertEqual(2, moved["totals"]["observedRows"])
            self.assertEqual(1, moved["totals"]["duplicateObservations"])
            self.assertEqual(1, len(client.calls))  # No second storage read/decode.
            self.assertNotIn("private-never-export", path.read_bytes().decode("latin1"))
            db.close()

    def test_changed_source_is_reported_without_double_counting_or_replacing_first_observation(self):
        current = state()
        updated, bindings = tool.inventory_step(FakeClient(items=[candidate(source="b" * 64)]), current,
                                               lambda ids: {PHOTO: "a" * 64})
        self.assertEqual(1, updated["totals"]["changedBindings"])
        self.assertEqual(0, updated["totals"]["scanned"])
        self.assertEqual([], bindings)

    def test_transaction_failure_rolls_back_seen_ids_counters_and_cursor(self):
        with tempfile.TemporaryDirectory() as directory:
            db = tool.InventoryCheckpoint(Path(directory) / "checkpoint.sqlite", state())
            initial = db.read()
            updated, bindings = tool.inventory_step(FakeClient(), initial)
            with self.assertRaises(sqlite3.IntegrityError):
                db.commit(initial, updated, bindings + bindings)
            self.assertEqual(initial, db.read())
            self.assertEqual({}, db.seen([PHOTO]))
            db.close()

    def test_two_operators_cannot_commit_the_same_page_and_export_reads_latest_revision(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "checkpoint.sqlite"
            first = tool.InventoryCheckpoint(path, state())
            second = tool.InventoryCheckpoint(path)
            initial = first.read()
            updated, bindings = tool.inventory_step(FakeClient(), initial)
            first.commit(initial, updated, bindings)
            with self.assertRaises(tool.OperationError):
                second.commit(initial, updated, bindings)
            report = second.export(Path(directory) / "report.json")
            self.assertEqual(1, report["checkpointRevision"])
            self.assertEqual(1, report["totals"]["scanned"])
            first.close()
            second.close()

    def test_nonprogressing_or_unbounded_page_is_rejected(self):
        for client in (FakeClient(items=[candidate(), candidate()]), FakeClient(next_cursor={**candidate(), "photoId": PARENT})):
            with self.assertRaises(tool.OperationError):
                tool.inventory_step(client, state())
        current = state()
        current["cursor"] = {key: candidate()[key] for key in ("sortAtUtc", "checkInId", "photoId")}
        with self.assertRaises(tool.OperationError):
            tool.inventory_step(FakeClient(), current)

    def test_resume_rejects_changed_origin_or_cutoff_before_network(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "checkpoint.sqlite"
            db = tool.InventoryCheckpoint(path, state())
            db.close()
            args = tool.parser().parse_args(["inventory", "--shops-url", "http://other", "--media-url", "http://media",
                "--checkpoint", str(path), "--report", str(Path(directory) / "report.json"), "--resume"])
            client = FakeClient()
            with self.assertRaises(tool.OperationError):
                tool.run_inventory(args, client)
            self.assertFalse(client.calls)

    def test_partial_page_does_not_leak_error_body_key_or_credentials(self):
        client = tool.Client("test-only-" + "x" * 32)
        with patch.object(client.opener, "open", side_effect=http.client.IncompleteRead(b"private-secret-key", 5)):
            with self.assertRaises(tool.OperationError) as error:
                client.call("http://media", "/internal/check")
            self.assertNotIn("private-secret", str(error.exception))
        with self.assertRaises(tool.OperationError):
            tool.timestamp(None)

class TransportTests(unittest.TestCase):
    def test_redirect_never_forwards_service_secret_to_other_endpoint(self):
        calls = []
        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                calls.append((self.path, self.headers.get("X-Gateway-Auth")))
                self.send_response(307)
                self.send_header("Location", "/unexpected")
                self.end_headers()

            def log_message(self, *args):
                pass
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            client = tool.Client("test-only-" + "x" * 32)
            with self.assertRaises(tool.OperationError):
                client.call(f"http://127.0.0.1:{server.server_port}", "/query", {})
            self.assertEqual(1, len(calls))
            self.assertEqual("/query", calls[0][0])
        finally:
            server.shutdown()
            server.server_close()
            thread.join()


if __name__ == "__main__":
    unittest.main()
