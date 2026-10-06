import copy
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from validate_deployment import read_env, selected_sources, validate_job, validate_mapping, validate_token
from verify_deployment_smoke import applied_request, fingerprint, verify_rows


IDENTIFIER = "11111111-1111-4111-8111-111111111111"
TOKEN = "private-for-test-configuration-value-1234567890"


class DeploymentConfigurationTests(unittest.TestCase):
    def write_mapping(self, directory, value):
        path = Path(directory) / "mapping.json"
        path.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")
        return path

    def test_only_complete_explicit_brand_maps_with_real_ids_are_accepted(self):
        with tempfile.TemporaryDirectory() as directory:
            value = {"CoffeeImportOptions": {"Roasters": {"roast.by": {"Roast.by": IDENTIFIER}}}}
            path = self.write_mapping(directory, value)
            self.assertEqual(value["CoffeeImportOptions"]["Roasters"], validate_mapping(path))
            for brands in ({"Roast.by": "00000000-0000-0000-0000-000000000000"}, {"roast.by": IDENTIFIER}):
                value["CoffeeImportOptions"]["Roasters"]["roast.by"] = brands
                with self.assertRaises(ValueError):
                    validate_mapping(self.write_mapping(directory, value))
            value["CoffeeImportOptions"]["Roasters"] = {"xn--80ad9al.xn--90ais": {"KOF": IDENTIFIER}}
            with self.assertRaises(ValueError):
                validate_mapping(self.write_mapping(directory, value))
            value["CoffeeImportOptions"]["Roasters"]["xn--80ad9al.xn--90ais"]["Kanga"] = IDENTIFIER
            validate_mapping(self.write_mapping(directory, value))

    def test_disabled_empty_map_is_safe_and_duplicate_keys_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = self.write_mapping(directory, {"CoffeeImportOptions": {"Roasters": {}}})
            self.assertEqual({}, validate_mapping(path, allow_empty=True))
            with self.assertRaises(ValueError):
                validate_mapping(path)
            path.write_text('{"CoffeeImportOptions":{"Roasters":{},"roasters":{}}}')
            with self.assertRaisesRegex(ValueError, "duplicate"):
                validate_mapping(path)

    def test_job_has_private_loopback_urls_and_explicit_sources(self):
        values = {"COFFEE_IMPORT_TOKEN": TOKEN, "COFFEE_IMPORT_SHOPS_URL": "http://127.0.0.1:5243",
                  "COFFEE_IMPORT_MEDIA_URL": "http://127.0.0.1:5025", "COFFEE_IMPORT_SOURCES": "roast.by,сова.бел"}
        self.assertEqual(["roast.by", "xn--80ad9al.xn--90ais"], validate_job(values))
        for url in ("http://shops:8080", "http://example.com:8080", "http://127.0.0.1:5243/?x=1", "http://a:b@localhost:5243", "http://localhost"):
            with self.assertRaises(ValueError):
                validate_job(values | {"COFFEE_IMPORT_SHOPS_URL": url})
        for sources in ("", "roast.by,roast.by", "unknown", "roast.by, spiritus.by"):
            with self.assertRaises(ValueError):
                selected_sources(sources)
        with self.assertRaises(ValueError):
            validate_token("change-me-at-least-32-characters-long-secret")

    def test_cli_compares_job_token_port_and_mapping_without_printing_credentials(self):
        with tempfile.TemporaryDirectory() as directory:
            mapping = self.write_mapping(directory, {"CoffeeImportOptions": {"Roasters": {"roast.by": {"Roast.by": IDENTIFIER}}}})
            env = Path(directory) / "job.env"
            values = {"COFFEE_IMPORT_SHOPS_URL": "http://127.0.0.1:5243", "COFFEE_IMPORT_MEDIA_URL": "http://127.0.0.1:5025",
                      "COFFEE_IMPORT_TOKEN": TOKEN, "COFFEE_IMPORT_SOURCES": "roast.by"}
            env.write_text("\n".join(key + "=" + json.dumps(value) for key, value in values.items()))
            self.assertEqual(values, read_env(env))
            command = [sys.executable, str(Path(__file__).parent / "validate_deployment.py"), "--mapping", str(mapping), "--job-env", str(env)]
            for token, port, expected in ((TOKEN, "5243", 0), ("different-valid-private-test-token-123456789", "5243", 1), (TOKEN, "5244", 1)):
                result = subprocess.run(command, env=os.environ | {"COFFEE_IMPORT_TOKEN": token, "COFFEE_IMPORT_SHOPS_PORT": port}, capture_output=True, text=True)
                self.assertEqual(expected, result.returncode, result.stderr)
                self.assertNotIn(TOKEN, result.stdout + result.stderr)
                self.assertNotIn(token, result.stdout + result.stderr)


class DeploymentSmokeTests(unittest.TestCase):
    def setUp(self):
        self.key = "coffees/" + "a" * 64 + ".png"
        self.photo_id = "22222222-2222-4222-8222-222222222222"
        self.snapshot = {"products": [{"sourceProductId": "source-product", "variants": [
            {"externalVariantId": "variant", "price": "30.00", "currency": "BYN", "weightGrams": None, "availableForPurchase": True}],
            "storedPhotos": [{"id": self.photo_id, "storageKey": self.key, "sizeBytes": 12}]}]}
        self.after = [["source-product", IDENTIFIER, "Draft", "[]"]]
        self.variants = [["variant", "30.00", "BYN", "", "InStock"]]
        self.photos = [["source-product", self.key, self.photo_id]]
        self.media = [[self.key, self.photo_id]]

    def verify(self, **overrides):
        values = dict(snapshot=self.snapshot, before=[], after=self.after, variants=self.variants, photos=self.photos, media_photos=self.media)
        return verify_rows(**(values | overrides))

    def test_smoke_requires_drafts_and_preserves_existing_moderation_state(self):
        self.assertEqual(self.key, self.verify()["storageKey"])
        published = [["source-product", IDENTIFIER, "Published", "[]"]]
        with self.assertRaises(ValueError):
            self.verify(after=published)
        self.verify(before=published, after=published)
        with self.assertRaises(ValueError):
            self.verify(before=published)

    def test_smoke_checks_relational_prices_and_media_references(self):
        with self.assertRaises(ValueError):
            self.verify(variants=[["variant", "31.00", "BYN", "", "InStock"]])
        with self.assertRaises(ValueError):
            self.verify(photos=[])
        with self.assertRaises(ValueError):
            self.verify(media_photos=[])
        self.verify(after=[["source-product", IDENTIFIER, "Draft", '["Photos"]']], photos=[])
        empty = copy.deepcopy(self.snapshot)
        empty["products"][0]["storedPhotos"] = []
        with self.assertRaisesRegex(ValueError, "photographs"):
            self.verify(snapshot=empty)

    def test_replay_document_identity_is_bound_to_the_successful_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            identifier = "a" * 64
            report = {"sources": [{"source": "roast.by", "status": "ok", "import": {"status": "Applied", "snapshotId": identifier}}]}
            (root / "last-run.json").write_text(json.dumps(report))
            history = root / "history" / "roast.by"
            history.mkdir(parents=True)
            request = {"sourceKey": "roast.by", "snapshotId": identifier, "snapshot": self.snapshot}
            (history / (identifier + ".json")).write_text(json.dumps(request))
            self.assertEqual(request, applied_request(root, "roast.by")[1])
            report["sources"][0]["import"]["snapshotId"] = "../../outside"
            (root / "last-run.json").write_text(json.dumps(report))
            with self.assertRaises(ValueError):
                applied_request(root, "roast.by")

    def test_timer_guard_expires_when_the_private_config_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            files = [Path(directory) / name for name in ("mapping", "job", "requirements")]
            for file in files:
                file.write_text(file.name)
            before = fingerprint(*files)
            files[1].write_text("new-token-and-sources")
            self.assertNotEqual(before, fingerprint(*files))


if __name__ == "__main__":
    unittest.main()
