import unittest
import http.server
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
from photo_delivery import photo_paths, validate_run, percentile


class PhotoDeliveryHarnessTests(unittest.TestCase):
    def test_report_measures_real_http_requests_without_storing_token_or_urls(self):
        path = "/api/v1/check-ins/11111111-1111-1111-1111-111111111111/photos/22222222-2222-2222-2222-222222222222"
        authorizations = []

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                authorizations.append(self.headers.get("Authorization"))
                body = json.dumps({"items": [{"photos": [{"url": path}]}]}).encode() if self.path.startswith("/api/v1/feed?") else b"test-photo"
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.send_header("Cache-Control", "private, no-store")
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, *_):
                pass

        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            with tempfile.TemporaryDirectory() as tmp:
                report = Path(tmp) / "report.json"
                result = subprocess.run([sys.executable, str(Path(__file__).with_name("photo_delivery.py")),
                    "--base-url", f"http://127.0.0.1:{server.server_port}", "--environment", "local",
                    "--photo-limit", "1", "--repeats", "2", "--output", str(report)],
                    env={**os.environ, "COFFEEPEEK_PHOTO_TOKEN": "test-private-token"}, capture_output=True, text=True, check=True)
                text = report.read_text()
                data = json.loads(text)
                self.assertEqual(2, data["summary"]["successful"])
                self.assertEqual([10, 10], [row["bytes"] for row in data["requests"]])
                self.assertNotIn("test-private-token", text + result.stdout + result.stderr)
                self.assertNotIn(path, text)
                self.assertEqual(["Bearer test-private-token"] * 3, authorizations)
        finally:
            server.shutdown()
            server.server_close()
            thread.join()

    def test_photo_urls_are_deduplicated_and_cross_origin_or_token_urls_are_ignored(self):
        path = "/api/v1/check-ins/11111111-1111-1111-1111-111111111111/photos/22222222-2222-2222-2222-222222222222"
        feed = {"items": [{"checkIn": {"photos": [{"url": path}, {"url": path},
                {"url": "https://untrusted.example" + path}, {"url": path + "?token=secret"}]}}]}
        self.assertEqual([path], photo_paths(feed))

    def test_production_probe_limits_and_credential_safety(self):
        for base, mode, concurrency, count in [
            ("https://api.coffeepeek.by", "staging", 50, 1000),
            ("https://api.coffeepeek.by", "probe", 10, 3),
            ("https://api.coffeepeek.by", "probe", 1, 11),
            ("https://api.coffeepeek.by", "local", 1, 3),
            ("https://user:secret@example.com", "probe", 1, 3),
            ("https://example.com/?token=secret", "probe", 1, 3),
        ]:
            with self.subTest(base=base, mode=mode), self.assertRaises(ValueError):
                validate_run(base, mode, concurrency, count)
        validate_run("http://localhost:8080", "local", 50, 1000)
        validate_run("https://api.coffeepeek.by", "probe", 1, 6)

    def test_percentiles_do_not_include_missing_samples(self):
        self.assertIsNone(percentile([], .95))
        self.assertEqual(95, percentile(list(range(1, 101)), .95))


if __name__ == "__main__":
    unittest.main()
