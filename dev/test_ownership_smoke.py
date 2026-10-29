"""Offline checks for the stdlib local ownership smoke runner."""

import importlib.util
from pathlib import Path
import unittest


spec = importlib.util.spec_from_file_location("ownership_smoke", Path(__file__).with_name("ownership-smoke.py"))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)


class OwnershipSmokeTests(unittest.TestCase):
    def test_base_url_only_allows_explicit_loopback_gateway(self):
        for value in ("http://localhost:8080", "http://127.0.0.1:8080/", "https://[::1]:8080"):
            self.assertEqual(value.rstrip("/"), smoke.validate_base_url(value))
        for value in ("", "http://example.com", "http://192.168.1.2:8080", "http://localhost.example.com",
                      "http://token@localhost:8080", "http://localhost:8080/api", "http://localhost:8080?token=x",
                      "ftp://127.0.0.1", "http://127.0.0.1:0", "http://127.0.0.1:invalid"):
            with self.subTest(value=value), self.assertRaises(smoke.SmokeFailure):
                smoke.validate_base_url(value)

    def test_update_preserves_profile_values_and_existing_gallery(self):
        profile = {"name": "Fixture", "cityId": "city-id", "about": "old", "profileRevision": 7,
                   "location": {"address": "Street 1", "latitude": 53.1234, "longitude": 27.4567},
                   "contact": {"instagramLink": "https://instagram.com/example", "siteLink": "https://example.com"},
                   "photos": [{"id": "photo-id", "storageKey": "existing-key"}]}
        update = smoke.roaster_update(profile, "new")
        self.assertEqual({"name": "Fixture", "about": "new", "cityId": "city-id", "address": "Street 1",
                          "latitude": 53.1234, "longitude": 27.4567,
                          "instagramLink": "https://instagram.com/example", "siteLink": "https://example.com",
                          "photos": None}, update)
        self.assertEqual("old", profile["about"])
        self.assertIsNone(smoke.roaster_update({"name": "Fixture", "location": None, "contact": None}, "new")["photos"])

    def test_redirect_handler_never_forwards_bearer_request(self):
        self.assertIsNone(smoke.NoRedirect().redirect_request(None, None, 302, "Found", {}, "https://example.com"))


if __name__ == "__main__":
    unittest.main()
