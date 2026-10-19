import json
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import patch

import photo_production_activation as activation
import photo_production_canary as canary
import photo_production_rollout_smoke as smoke
from test_photo_production_activation import FakeCommands, PilotProduction


class FullReleaseSmokeTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.client = PilotProduction()
        self.client.public_max_age = 20
        self.environment = {"EXPECTED_DEPLOY_SHA": "a" * 40, "POSTGRES_USER": "private-db-user", "POSTGRES_PASSWORD": "private-db-secret"}
        self.commands = FakeCommands(self.environment, self.client)
        probe = activation.Activation(self.environment, self.commands, self.client)
        self.operator = SimpleNamespace(operator=probe, root=self.root, public=self.client, sha="a" * 40,
                                        release=lambda: None, runtime=lambda: None)

    def test_fresh_private_upload_verify_and_cleanup_preserve_historical_journal(self):
        historical = self.root / "canary.json"
        historical.write_text('{"historical":true}', encoding="utf-8")
        self.assertTrue(smoke.execute(self.operator, "rollout-smoke-create")["privateHomeCreated"])
        result = smoke.execute(self.operator, "rollout-smoke-verify")
        self.assertTrue(result["publicVariantCacheVerified"])
        self.assertEqual((2, 1, 12), (result["newPhotos"], result["oldPhotos"], result["ownerDescriptorsVerified"]))
        deleted = smoke.execute(self.operator, "rollout-smoke-cleanup")
        self.assertTrue(deleted["privateHomeDeleted"])
        self.assertEqual(38, deleted["denialChecks"])
        self.assertEqual({"historical": True}, json.loads(historical.read_text(encoding="utf-8")))
        with self.assertRaises(canary.CanaryError):
            smoke.execute(self.operator, "rollout-smoke-verify")

    def test_uncertain_creation_is_never_repeated_and_no_cleanup_without_verified_fixture(self):
        original = self.client.api

        def fail_after_private_write(method, path, token=None, data=None):
            result = original(method, path, token, data)
            if method == "POST" and path == "/api/v1/check-ins/home":
                raise TimeoutError("uncertain private write")
            return result

        with patch.object(self.client, "api", side_effect=fail_after_private_write), self.assertRaises(TimeoutError):
            smoke.execute(self.operator, "rollout-smoke-create")
        before = len(self.client.calls)
        with self.assertRaises(canary.CanaryError):
            smoke.execute(self.operator, "rollout-smoke-create")
        self.assertEqual(before, len(self.client.calls))
        with self.assertRaises(canary.CanaryError):
            smoke.execute(self.operator, "rollout-smoke-cleanup")
        self.assertFalse(any(method == "DELETE" for method, _, _, _ in self.client.calls))

    def test_missing_owner_descriptors_fails_before_cleanup(self):
        smoke.execute(self.operator, "rollout-smoke-create")
        self.client.home["photos"][0].pop("variants")
        with self.assertRaises(canary.CanaryError):
            smoke.execute(self.operator, "rollout-smoke-verify")
        self.assertFalse(any(method == "DELETE" for method, _, _, _ in self.client.calls))


if __name__ == "__main__":
    unittest.main()
