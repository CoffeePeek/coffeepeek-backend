import base64
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import struct
import tempfile
import time
import unittest
from unittest.mock import patch
import urllib.error
import urllib.parse
import uuid
import zlib

import photo_production_canary as canary


class FakeProduction:
    def __init__(self):
        self.owner = str(uuid.uuid4())
        self.home_id = str(uuid.uuid4())
        self.photo_ids = [str(uuid.uuid4()), str(uuid.uuid4())]
        self.calls = []
        self.photo_requests = []
        self.account = None
        self.deleted = False
        self.home = None
        self.fail_creation = False
        self.wrong_identity = False
        self.public_no_store = False
        self.public_max_age = 0

    def api(self, method, path, token=None, data=None):
        self.calls.append((method, path, token, data))
        if path == "/api/Users":
            self.account = data
            return None
        if path.startswith("/api/Users/me/email-confirmation?"):
            return None
        if path == "/api/Tokens":
            claim = {"sub": str(uuid.uuid4()) if self.wrong_identity else self.owner, "exp": int(time.time()) + 3600}
            encoded = base64.urlsafe_b64encode(json.dumps(claim).encode()).decode().rstrip("=")
            return {"accessToken": "header." + encoded + ".signature"}
        if path == "/api/Users/me":
            return {"email": self.account["email"]}
        if path == "/api/Photos/check-in":
            return [{"photoId": photo, "storageKey": "check-ins/" + str(uuid.uuid4()) + ".png",
                     "uploadUrl": canary.UPLOAD + "/private/upload?X-Amz-Signature=secret-synthetic"} for photo in self.photo_ids]
        if path == "/api/v1/check-ins/home" and method == "POST":
            if self.fail_creation:
                raise canary.CanaryError("uncertain private request https://private/secret-token")
            self.home = {"id": self.home_id, "visibility": "Private", "photos": [self.photo(0)], "drinkPhoto": self.photo(1)}
            return self.home
        if path == "/api/v1/check-ins/home/" + self.home_id:
            if method == "GET":
                return self.home
            if method == "DELETE":
                self.deleted = True
                return None
        raise AssertionError("Unexpected fake API route")

    def photo(self, index):
        photo_id = self.photo_ids[index]
        key = "check-ins/" + uuid.UUID(photo_id).hex + ".png"
        base = canary.base_path(self.home_id, photo_id)
        source = hashlib.sha256(("checkin:" + uuid.UUID(photo_id).hex + ":" + key).encode()).hexdigest()
        return {"id": photo_id, "storageKey": key, "url": base,
                "variants": [{"purpose": purpose, "format": fmt, "sourceVersion": source, "recipeVersion": 1,
                              "contentType": "image/" + fmt,
                              "url": base + "/variants/" + source + "/1/" + purpose + "." + fmt}
                             for purpose in canary.PURPOSES for fmt in ("webp", "png" if index else "jpeg")]}

    def request(self, method, url, token=None, data=None, headers=None, expected=(200,), cap=canary.MAX_JSON, upload=False):
        self.calls.append((method, url, token, data))
        if upload:
            assert token is None and headers["x-amz-tagging"] == "is_permanent=False"
            assert data in (canary.png(False), canary.png(True))
            return 200, {}, b""
        self.photo_requests.append({"method": method, "url": url, "token": token, "headers": dict(headers or {}),
                                    "afterDelete": self.deleted})
        path = urllib.parse.urlsplit(url).path
        new = self.home_id in path
        if token == "invalid-canary-token":
            status = 401
        elif new and (not token or self.deleted):
            status = 404
        elif headers and headers.get("If-None-Match") and not new and not self.public_no_store:
            status = 304
        else:
            status = 200
        assert status in expected, (status, expected)
        if status in (401, 404):
            body = json.dumps({"status": status}).encode()
            return status, {"content-type": "application/problem+json", "content-length": str(len(body))}, b"" if method == "HEAD" else body
        cutout = self.photo_ids[1] in path
        fmt = path.rsplit(".", 1)[-1] if "/variants/" in path else "png"
        body = canary.png(cutout) if fmt == "png" else (b"RIFF\x00\x00\x00\x00WEBPtest" if fmt == "webp" else b"\xff\xd8synthetic-jpeg")
        parts = path.split("/")
        etag = '"original"' if "/variants/" not in path else '"' + parts[-3] + "-v" + parts[-2] + "-" + hashlib.sha256(body).hexdigest() + '"'
        response_headers = {"content-type": "image/" + fmt, "content-length": str(len(body)), "etag": etag,
                            "cache-control": "private, no-store" if new or "/variants/" not in path or self.public_no_store else f"private, max-age={self.public_max_age}, must-revalidate",
                            "x-content-type-options": "nosniff"}
        return status, response_headers, b"" if method == "HEAD" or status != 200 else body


class CanaryTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.state = Path(self.folder.name) / "state.json"
        self.confirmation = Path(self.folder.name) / "confirmation.json"
        self.old = Path(self.folder.name) / "old.json"
        self.client = FakeProduction()

    def prepare(self):
        canary.prepare(self.state, self.client)
        state = canary.private_read(self.state)
        canary.private_write(self.confirmation, {"email": state["email"], "userId": self.client.owner, "confirmationToken": "private-confirmation-token"})
        return state

    def create(self):
        self.prepare()
        canary.confirm_create(self.state, self.confirmation, self.client)
        canary.private_write(self.old, {"public": True, "checkInId": str(uuid.uuid4()), "photoId": str(uuid.uuid4()),
            "sourceVersion": "a" * 64, "recipeVersion": 1,
            "variants": [{"purpose": purpose, "format": fmt} for purpose in canary.PURPOSES for fmt in ("webp", "jpeg")]})

    def test_full_protocol_new_private_home_old_public_conditional_and_cleanup(self):
        self.create()
        result = canary.verify(self.state, self.old, self.client)
        self.assertEqual(2, result["newPhotos"])
        self.assertEqual(1, result["oldPhotos"])
        self.assertGreater(result["checks"], 60)
        confirmed = canary.private_read(self.state)
        self.assertEqual(2, len(confirmed["uploads"]))
        self.assertTrue(confirmed["verifiedAtUtc"] if "verifiedAtUtc" in confirmed else confirmed["verification"]["verifiedAtUtc"])
        cleanup = canary.cleanup(self.state, self.client)
        self.assertTrue(cleanup["accountRetained"])
        self.assertEqual((2, 12, 38), (cleanup["originalsDenied"], cleanup["variantsDenied"], cleanup["denialChecks"]))
        self.assertIsInstance(cleanup["revocationCheckDurationMs"], int)
        self.assertGreaterEqual(cleanup["revocationCheckDurationMs"], 0)
        self.assertTrue(cleanup["verifiedAtUtc"])
        journal = canary.private_read(self.state)
        self.assertEqual(cleanup, journal["cleanupVerification"])
        self.assertTrue(journal["cleanupObservation"]["observedAtUtc"])
        descriptors = [variant for photo in [self.client.home["photos"][0], self.client.home["drinkPhoto"]]
                       for variant in photo["variants"]]
        issued = [variant for photo in journal["cleanupObservation"]["photos"] for variant in photo["variants"]]
        self.assertEqual({item["url"] for item in descriptors}, {item["url"] for item in issued})
        after = [request for request in self.client.photo_requests if request["afterDelete"]]
        self.assertEqual(38, len(after))
        for item in issued:
            requests = [request for request in after if request["url"] == canary.API + item["url"]]
            self.assertEqual([("GET", {}), ("HEAD", {}), ("GET", {"If-None-Match": item["etag"]})],
                             [(request["method"], request["headers"]) for request in requests])
            self.assertTrue(all(request["token"] for request in requests))
        self.assertNotIn(self.client.home_id, json.dumps(cleanup))
        self.assertNotIn("etag", json.dumps(cleanup))
        self.assertTrue(canary.private_read(self.state)["deleted"])
        self.assertNotIn("accessToken", canary.private_read(self.state))
        self.assertFalse(any(method == "DELETE" and route.startswith("/api/Users") for method, route, _, _ in self.client.calls))
        before = len(self.client.calls)
        with self.assertRaises(canary.CanaryError):
            canary.cleanup(self.state, self.client)
        self.assertEqual(before, len(self.client.calls))

    def test_cleanup_captures_real_etags_and_journal_before_single_uncertain_delete(self):
        self.create()
        original_api = self.client.api

        def uncertain_delete(method, path, token=None, data=None):
            if method == "DELETE":
                journal = canary.private_read(self.state)
                self.assertTrue(journal["cleanupAttempted"])
                self.assertEqual(12, sum(len(photo["variants"]) for photo in journal["cleanupObservation"]["photos"]))
                original_api(method, path, token, data)
                raise canary.CanaryError("uncertain private delete secret-token")
            return original_api(method, path, token, data)

        with patch.object(self.client, "api", side_effect=uncertain_delete):
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                self.assertEqual(1, canary.main(["cleanup", "--state", str(self.state)], self.client))
        state = canary.private_read(self.state)
        self.assertTrue(state["cleanupAttempted"])
        self.assertNotIn("deleted", state)
        self.assertNotIn("cleanupVerification", state)
        self.assertNotIn("secret-token", output.getvalue())
        before = self.state.read_bytes()
        calls = len(self.client.calls)
        with self.assertRaises(canary.CanaryError):
            canary.cleanup(self.state, self.client)
        self.assertEqual(before, self.state.read_bytes())
        self.assertEqual(calls, len(self.client.calls))
        self.assertEqual(1, sum(method == "DELETE" for method, _, _, _ in self.client.calls))

    def test_cleanup_uses_etag_returned_by_predelete_head_in_conditional_denials(self):
        self.create()
        original_request = self.client.request
        observed = {}

        def real_etag(method, url, *args, **kwargs):
            status, headers, body = original_request(method, url, *args, **kwargs)
            if method == "HEAD" and not self.client.deleted:
                # Distinct from the fixture image digest: cleanup must use the observed header verbatim.
                headers["etag"] = headers["etag"].rsplit("-", 1)[0] + "-" + "f" * 64 + '"'
                observed[url] = headers["etag"]
            return status, headers, body

        with patch.object(self.client, "request", side_effect=real_etag):
            canary.cleanup(self.state, self.client)
        conditional = [request for request in self.client.photo_requests
                       if request["afterDelete"] and "If-None-Match" in request["headers"]]
        self.assertEqual(12, len(observed))
        self.assertEqual(observed, {request["url"]: request["headers"]["If-None-Match"] for request in conditional})

    def test_cleanup_reports_measured_duration_even_when_it_exceeds_sixty_seconds(self):
        self.create()
        with patch.object(canary.time, "monotonic", side_effect=[20.0, 81.0]):
            result = canary.cleanup(self.state, self.client)
        self.assertEqual(61000, result["revocationCheckDurationMs"])
        self.assertEqual(61000, canary.private_read(self.state)["cleanupVerification"]["revocationCheckDurationMs"])

    def test_cleanup_rejects_missing_foreign_or_incomplete_descriptors_before_delete(self):
        self.create()
        original = json.dumps(self.client.home)
        mutations = (
            lambda photo: photo.pop("variants"),
            lambda photo: photo["variants"][0].update(sourceVersion="a" * 64),
            lambda photo: photo["variants"][0].update(url="https://foreign.example/private"),
            lambda photo: photo["variants"].pop(),
            lambda photo: photo["variants"].__setitem__(1, dict(photo["variants"][0])),
        )
        for mutate in mutations:
            with self.subTest(mutation=mutations.index(mutate)):
                self.client.home = json.loads(original)
                mutate(self.client.home["photos"][0])
                with self.assertRaises(canary.CanaryError):
                    canary.cleanup(self.state, self.client)
                self.assertFalse(canary.private_read(self.state).get("cleanupAttempted"))
                self.assertFalse(any(method == "DELETE" for method, _, _, _ in self.client.calls))
                self.assertFalse(any("foreign.example" in route for _, route, _, _ in self.client.calls))

    def test_cleanup_failed_predelete_head_does_not_mark_attempt_or_delete(self):
        self.create()
        original_request = self.client.request

        def unavailable_head(method, url, *args, **kwargs):
            if method == "HEAD":
                raise canary.CanaryError("unavailable")
            return original_request(method, url, *args, **kwargs)

        with patch.object(self.client, "request", side_effect=unavailable_head):
            with self.assertRaises(canary.CanaryError):
                canary.cleanup(self.state, self.client)
        self.assertFalse(canary.private_read(self.state).get("cleanupAttempted"))
        self.assertFalse(self.client.deleted)
        self.assertTrue(canary.cleanup(self.state, self.client)["privateHomeDeleted"])

    def test_cleanup_never_reports_success_for_cached_or_image_denials_after_delete(self):
        for failure in ("200", "304", "image-type", "image-body", "head-body"):
            with self.subTest(failure=failure):
                self.state.unlink(missing_ok=True)
                self.client = FakeProduction()
                self.create()
                original_request = self.client.request

                def bad_denial(method, url, token=None, data=None, headers=None, **kwargs):
                    if self.client.deleted and "/variants/" in url:
                        if failure in ("200", "304") and headers and "If-None-Match" in headers:
                            return int(failure), {"content-type": "image/webp"}, b""
                        if failure == "image-type":
                            return 404, {"content-type": "image/png"}, b""
                        if failure == "image-body":
                            return 404, {"content-type": "application/problem+json"}, canary.png()
                        if failure == "head-body" and method == "HEAD":
                            return 404, {"content-type": "application/problem+json"}, b'{"status":404}'
                    return original_request(method, url, token, data, headers, **kwargs)

                with patch.object(self.client, "request", side_effect=bad_denial):
                    with self.assertRaises((canary.CanaryError, json.JSONDecodeError, UnicodeDecodeError)):
                        canary.cleanup(self.state, self.client)
                state = canary.private_read(self.state)
                self.assertTrue(state["cleanupAttempted"] and state["deleted"])
                self.assertNotIn("accessToken", state)
                self.assertNotIn("cleanupVerification", state)
                before = len(self.client.calls)
                with self.assertRaises(canary.CanaryError):
                    canary.cleanup(self.state, self.client)
                self.assertEqual(before, len(self.client.calls))

    def test_uncertain_create_stops_retries_and_keeps_uploads_checkpointed(self):
        self.prepare()
        self.client.fail_creation = True
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            self.assertEqual(1, canary.main(["confirm-create", "--state", str(self.state), "--confirmation-file", str(self.confirmation)], self.client))
        state = canary.private_read(self.state)
        self.assertTrue(state["creationAttempted"])
        self.assertEqual([0, 1], state["uploaded"])
        self.assertEqual(2, len(state["uploads"]))
        self.assertNotIn("homeId", state)
        self.assertNotIn("secret-token", output.getvalue())
        before = len(self.client.calls)
        with self.assertRaises(canary.CanaryError):
            canary.confirm_create(self.state, self.confirmation, self.client)
        self.assertEqual(before, len(self.client.calls))

    def test_public_no_store_200_mode_is_supported_without_cache_flag(self):
        self.create()
        self.client.public_no_store = True
        self.assertFalse(canary.verify(self.state, self.old, self.client)["publicCacheRequested"])

    def test_public_positive_freshness_requires_explicit_cache_mode_and_304(self):
        self.create()
        self.client.public_max_age = 20
        with self.assertRaises(canary.CanaryError):
            canary.verify(self.state, self.old, self.client)
        self.assertTrue(canary.verify(self.state, self.old, self.client, public_cache_enabled=True)["publicCacheRequested"])
        self.client.public_no_store = True
        with self.assertRaises(canary.CanaryError):
            canary.verify(self.state, self.old, self.client, public_cache_enabled=True)

    def test_wrong_confirmation_email_and_wrong_jwt_owner_stop_before_upload(self):
        state = self.prepare()
        canary.private_write(self.confirmation, {"email": "unrelated@example.invalid", "userId": self.client.owner, "confirmationToken": "secret"})
        with self.assertRaises(canary.CanaryError):
            canary.confirm_create(self.state, self.confirmation, self.client)
        self.assertFalse(canary.private_read(self.state).get("creationAttempted"))
        canary.private_write(self.confirmation, {"email": state["email"], "userId": self.client.owner, "confirmationToken": "secret"})
        self.client.wrong_identity = True
        with self.assertRaises(canary.CanaryError):
            canary.confirm_create(self.state, self.confirmation, self.client)
        self.assertFalse(any(route == "/api/Photos/check-in" for _, route, _, _ in self.client.calls))

    def test_existing_user_state_cannot_be_used_for_any_mutation(self):
        state = self.prepare()
        state["email"] = "existing@example.com"
        canary.private_write(self.state, state)
        before = len(self.client.calls)
        with self.assertRaises(canary.CanaryError):
            canary.confirm_create(self.state, self.confirmation, self.client)
        self.assertEqual(before, len(self.client.calls))

    def test_prepare_does_not_overwrite_existing_state_or_register_twice(self):
        state = self.prepare()
        self.assertRegex(state["email"], r"^photo_canary_[0-9a-f]{16}@photo-canary\.invalid$")
        self.assertGreaterEqual(len(state["password"]), 24)
        before = len(self.client.calls)
        with self.assertRaises(canary.CanaryError):
            canary.prepare(self.state, self.client)
        self.assertEqual(before, len(self.client.calls))
        if os.name != "nt":
            self.assertEqual(0o600, self.state.stat().st_mode & 0o777)

    def test_png_crc_dimensions_and_real_transparent_visible_pixels(self):
        for alpha in (False, True):
            image = canary.png(alpha)
            self.assertEqual((48, 64), struct.unpack(">II", image[16:24]))
            offset = 8
            while offset < len(image):
                size = struct.unpack(">I", image[offset:offset + 4])[0]
                body = image[offset + 4:offset + 8 + size]
                expected = struct.unpack(">I", image[offset + 8 + size:offset + 12 + size])[0]
                self.assertEqual(expected, zlib.crc32(body))
                offset += 12 + size
        self.assertTrue(canary.png_has_alpha(canary.png(True)))
        with self.assertRaises(canary.CanaryError):
            canary.png_has_alpha(canary.png(False))

    def test_old_nonpublic_or_arbitrary_url_observation_cannot_be_read(self):
        self.create()
        canary.private_write(self.old, {"public": False, "url": "https://foreign.example/private"})
        with self.assertRaises(canary.CanaryError):
            canary.verify(self.state, self.old, self.client)
        self.assertFalse(any("foreign.example" in route for _, route, _, _ in self.client.calls))


class TransportTests(unittest.TestCase):
    def test_expected_http_error_preserves_actual_body_with_a_strict_limit_and_closes_stream(self):
        for payload, cap in ((b'{"status":404}', 128), (canary.png(), canary.MAX_JSON), (b"oversized-private-response", 5)):
            with self.subTest(cap=cap):
                stream = io.BytesIO(payload)
                error = urllib.error.HTTPError(canary.API + "/api/private", 404, "private", {"Content-Type": "application/problem+json"}, stream)
                client = canary.Client()
                client.opener = unittest.mock.Mock()
                client.opener.open.side_effect = error
                if len(payload) > cap:
                    with self.assertRaises(canary.CanaryError):
                        client.request("GET", canary.API + "/api/private", expected=(404,), cap=cap)
                else:
                    status, headers, body = client.request("GET", canary.API + "/api/private", expected=(404,), cap=cap)
                    self.assertEqual((404, payload), (status, body))
                    self.assertEqual("application/problem+json", headers["content-type"])
                self.assertTrue(stream.closed)
                self.assertEqual(1, client.opener.open.call_count)

    def test_transport_rejects_foreign_origins_and_never_sends_bearer_to_upload(self):
        client = canary.Client()
        for url, upload in (("http://api.coffeepeek.by/api/Users", False), ("https://foreign.example/api/Users", False),
                            ("https://api.coffeepeek.by@foreign.example/api/Users", False), (canary.API + "/private", True)):
            with self.assertRaises(canary.CanaryError):
                client.request("GET", url, upload=upload)
        with self.assertRaises(canary.CanaryError):
            client.request("PUT", canary.UPLOAD + "/source?signature=secret", token="bearer", upload=True)

    def test_redirect_is_never_followed_and_errors_do_not_expose_private_addresses(self):
        self.assertIsNone(canary.NoRedirect().redirect_request(None, None, 307, "redirect", {}, "https://foreign/secret"))
        client = canary.Client()
        client.opener = unittest.mock.Mock()
        client.opener.open.side_effect = urllib.error.URLError("https://private/X-Amz-Signature=secret")
        with self.assertRaises(canary.CanaryError) as raised:
            client.request("GET", canary.API + "/api/Users/me", token="secret")
        self.assertNotIn("Signature", str(raised.exception))
        self.assertEqual(1, client.opener.open.call_count)

    def test_failed_state_and_service_errors_print_only_fixed_message(self):
        with tempfile.TemporaryDirectory() as directory:
            state = Path(directory) / "state.json"
            canary.private_write(state, {"password": "should-never-print", "email": "private@example.invalid"})
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                result = canary.main(["cleanup", "--state", str(state)], FakeProduction())
            self.assertEqual(1, result)
            self.assertNotIn("should-never-print", output.getvalue())
            self.assertNotIn("private@example.invalid", output.getvalue())


if __name__ == "__main__":
    unittest.main()
