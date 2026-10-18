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
        return {"id": photo_id, "storageKey": "check-ins/" + uuid.UUID(photo_id).hex + ".png"}

    def request(self, method, url, token=None, data=None, headers=None, expected=(200,), cap=canary.MAX_JSON, upload=False):
        self.calls.append((method, url, token, data))
        if upload:
            assert token is None and headers["x-amz-tagging"] == "is_permanent=False"
            assert data in (canary.png(False), canary.png(True))
            return 200, {}, b""
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
        self.assertTrue(canary.private_read(self.state)["deleted"])
        self.assertNotIn("accessToken", canary.private_read(self.state))
        self.assertFalse(any(method == "DELETE" and route.startswith("/api/Users") for method, route, _, _ in self.client.calls))
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
