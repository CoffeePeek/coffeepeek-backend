"""Synthetic loopback contract tests of the operator CLI as a separate process."""
import contextlib
import copy
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import threading
import unittest


SECRET = "synthetic-photo-backfill-" + "s" * 32
BEFORE = "2026-10-18T00:00:00Z"
SCRIPT = Path(__file__).with_name("photo_backfill.py")


def candidate(number, home=False):
    photo_id = f"00000000-0000-0000-0000-{number:012d}"
    return {
        "photoId": photo_id,
        "checkInId": "00000000-0000-0000-0000-000000000010",
        "ownerId": "00000000-0000-0000-0000-000000000011",
        "storageKey": "check-ins/" + photo_id.replace("-", "") + ".png",
        "sourceVersion": f"{number:064x}",
        "sortAtUtc": "2026-09-25T00:00:00Z",
        "kind": 1 if home else 0,
        "isDrink": home,
        "declaredSizeBytes": 100,
        "contentType": "image/png",
    }


class SyntheticServices:
    """No .NET, database or object storage is used behind these two fake services."""

    def __init__(self, fail_first_inspection=False):
        self.requests = []
        self.inspected_photos = []
        self.fail_first_inspection = fail_first_inspection
        self.phases = ([candidate(2), candidate(1)], [candidate(2)], [candidate(3, home=True)], [])
        services = self

        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                if self.headers.get("X-Gateway-Auth") != SECRET:
                    self.respond(401, {"error": "missing synthetic authentication"})
                    return
                length = int(self.headers.get("Content-Length", "0"))
                body = json.loads(self.rfile.read(length)) if length else None
                services.requests.append((self.path, copy.deepcopy(body)))
                if self.path == "/internal/check-in-photo-backfill/candidates":
                    rows = services.phases[body["phase"]]
                    offset = 0
                    if body["cursor"] is not None:
                        ids = [row["photoId"] for row in rows]
                        offset = ids.index(body["cursor"]["photoId"]) + 1
                    selected = rows[offset:offset + body["limit"]]
                    last = selected[-1] if selected else None
                    next_cursor = {key: last[key] for key in ("sortAtUtc", "checkInId", "photoId")} \
                        if last and offset + len(selected) < len(rows) else None
                    self.respond(200, {"items": selected, "nextCursor": next_cursor})
                elif self.path == "/internal/check-in-photo-backfill/inspect?verifyContent=true":
                    if services.fail_first_inspection:
                        services.fail_first_inspection = False
                        self.respond(503, {"error": "private-object-key and " + SECRET})
                        return
                    result = []
                    for item in body:
                        photo = item["photoId"]
                        services.inspected_photos.append(photo)
                        total = {candidate(1)["photoId"]: 100, candidate(2)["photoId"]: 300}.get(photo)
                        result.append({
                            "photoId": photo, "eligibilityFailure": None, "sourceStatus": "verified",
                            "declaredBytes": 100, "mediaDeclaredBytes": 100, "actualBytes": 100,
                            "metadataMismatch": False, "jobStatus": "Ready" if total else "Pending",
                            "currentVariantBytes": total, "recipeVersion": 1,
                        })
                    self.respond(200, result)
                else:
                    self.respond(500, {"error": "unexpected route: this inventory must not enqueue"})

            def respond(self, status, value):
                data = json.dumps(value).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(data)))
                self.send_header("Cache-Control", "private, no-store")
                self.end_headers()
                self.wfile.write(data)

            def log_message(self, *args):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=lambda: self.server.serve_forever(poll_interval=0.05), daemon=True)

    def __enter__(self):
        self.thread.start()
        self.origin = f"http://127.0.0.1:{self.server.server_port}"
        return self

    def __exit__(self, *args):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)


class CliHttpTests(unittest.TestCase):
    def run_cli(self, directory, services, *extra):
        env = os.environ.copy()
        env["PHOTO_BACKFILL_SERVICE_SECRET"] = SECRET
        for key in ("HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy", "ALL_PROXY", "all_proxy"):
            env.pop(key, None)
        env["NO_PROXY"] = "*"
        return subprocess.run([
            sys.executable, str(SCRIPT), "--timeout", "5", "inventory",
            "--shops-url", services.origin, "--media-url", services.origin,
            "--checkpoint", str(Path(directory) / "inventory.sqlite"),
            "--report", str(Path(directory) / "report.json"),
            "--page-size", "1", "--before", BEFORE, *extra,
        ], capture_output=True, text=True, env=env, timeout=25)

    def state(self, directory):
        with contextlib.closing(sqlite3.connect(Path(directory) / "inventory.sqlite")) as db:
            state = json.loads(db.execute("SELECT state FROM checkpoint WHERE id=1").fetchone()[0])
            seen = dict(db.execute("SELECT photo_id,source_version FROM seen"))
            return state, seen

    def test_real_cli_restart_deduplicates_phase_moves_and_forecasts_only_verified_ready_samples(self):
        with tempfile.TemporaryDirectory() as directory, SyntheticServices() as services:
            first = self.run_cli(directory, services, "--max-pages", "1")
            self.assertEqual(0, first.returncode, first.stdout + first.stderr)
            self.assertFalse(json.loads(first.stdout)["complete"])
            state, seen = self.state(directory)
            self.assertEqual(1, state["pages"])
            self.assertEqual(1, len(seen))
            self.assertIsNotNone(state["cursor"])

            resumed = self.run_cli(directory, services, "--resume", "--max-pages", "10")
            self.assertEqual(0, resumed.returncode, resumed.stdout + resumed.stderr)
            report = json.loads((Path(directory) / "report.json").read_text(encoding="utf-8"))
            self.assertTrue(report["complete"])
            self.assertEqual(3, report["totals"]["scanned"])
            self.assertEqual(4, report["totals"]["observedRows"])
            self.assertEqual(1, report["totals"]["duplicateObservations"])
            self.assertEqual(0, report["totals"]["changedBindings"])
            self.assertEqual(300, report["totals"]["verifiedSourceBytes"])
            self.assertEqual(2, report["forecast"]["sampleCount"])
            self.assertEqual(400, report["forecast"]["sampleVariantBytes"])
            self.assertEqual(200, report["forecast"]["estimatedAdditionalVariantBytes"])
            self.assertIsNone(report["forecast"]["estimatedGenerationSeconds"])
            self.assertEqual(3, len(services.inspected_photos))
            self.assertEqual(3, len(set(services.inspected_photos)))
            self.assertFalse(any("/runs" in path for path, _ in services.requests))
            state, seen = self.state(directory)
            self.assertEqual(3, len(seen))
            self.assertTrue(state["complete"])
            export = json.dumps(report) + first.stdout + first.stderr + resumed.stdout + resumed.stderr
            self.assertNotIn(SECRET, export)
            self.assertNotIn("check-ins/", export)
            self.assertNotIn(b"check-ins/", (Path(directory) / "inventory.sqlite").read_bytes())

    def test_http_failure_keeps_checkpoint_and_seen_ledger_unchanged_then_resume_recovers(self):
        with tempfile.TemporaryDirectory() as directory, SyntheticServices(fail_first_inspection=True) as services:
            failed = self.run_cli(directory, services)
            self.assertEqual(1, failed.returncode)
            state, seen = self.state(directory)
            self.assertEqual(0, state["pages"])
            self.assertEqual(0, state["totals"]["scanned"])
            self.assertFalse(seen)
            self.assertIsNone(state["cursor"])
            self.assertFalse((Path(directory) / "report.json").exists())
            self.assertNotIn(SECRET, failed.stdout + failed.stderr)
            self.assertNotIn("private-object-key", failed.stdout + failed.stderr)
            self.assertNotIn("Traceback", failed.stdout + failed.stderr)

            recovered = self.run_cli(directory, services, "--resume")
            self.assertEqual(0, recovered.returncode, recovered.stdout + recovered.stderr)
            state, seen = self.state(directory)
            self.assertEqual(1, state["pages"])
            self.assertEqual(1, state["totals"]["scanned"])
            self.assertEqual(1, len(seen))
            self.assertFalse(any("/runs" in path for path, _ in services.requests))


if __name__ == "__main__":
    unittest.main()
