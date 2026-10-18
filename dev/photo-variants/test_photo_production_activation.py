import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import uuid

import photo_production_activation as activation
import photo_production_canary as canary
from test_photo_production_canary import FakeProduction


class PilotProduction(FakeProduction):
    def photo(self, index):
        result = super().photo(index)
        result["url"] = canary.base_path(self.home_id, result["id"])
        source = hashlib.sha256(("checkin:" + result["id"].replace("-", "") + ":" + result["storageKey"]).encode()).hexdigest()
        result["variants"] = [{"purpose": purpose, "format": fmt, "sourceVersion": source, "recipeVersion": 1,
                               "contentType": "image/" + fmt, "sizeBytes": 100, "width": 48, "height": 64,
                               "url": result["url"] + "/variants/" + source + "/1/" + purpose + "." + fmt}
                              for purpose in canary.PURPOSES for fmt in ("webp", "png" if index else "jpeg")]
        return result

    def api(self, method, path, token=None, data=None):
        if path.startswith("/api/v1/check-ins/") and "/home" not in path:
            self.calls.append((method, path, token, data))
            base = canary.base_path(self.old["checkInId"], self.old["photoId"])
            variants = []
            if token:
                for raw in self.old["variants"]:
                    row = {key[0].lower() + key[1:]: value for key, value in raw.items()}
                    variants.append({key: row[key] for key in ("purpose", "format", "contentType", "width", "height", "sizeBytes")})
                    variants[-1].update(sourceVersion=self.old["sourceVersion"], recipeVersion=self.old["recipeVersion"],
                                        url=base + "/variants/" + self.old["sourceVersion"] + "/" + str(self.old["recipeVersion"])
                                            + "/" + row["purpose"] + "." + row["format"])
            if getattr(self, "public_as_drink", False):
                return {"photos": [], "drinkPhotoUrl": base, "drinkPhotoVariants": variants or None}
            return {"photos": [{"id": self.old["photoId"], "url": base, "variants": variants or None}], "drinkPhotoVariants": None}
        return super().api(method, path, token, data)


class FakeCommands:
    def __init__(self, environment, client):
        self.environment = dict(environment)
        self.client = client
        self.calls = []
        self.bad_revision = False
        self.stopped = False
        self.migrated = True
        self.query_failure = False
        self.ready = True
        self.health = "healthy"
        self.duplicate_runtime = False
        self.ids = {service: str(index + 1) * 64 for index, service in enumerate((*activation.APPS, "postgres"))}
        photo, check_in, owner = (str(uuid.uuid4()) for _ in range(3))
        key = "check-ins/" + photo.replace("-", "") + ".png"
        source = hashlib.sha256(("checkin:" + photo.replace("-", "") + ":" + key).encode()).hexdigest()
        rows = []
        for purpose in canary.PURPOSES:
            for fmt in ("webp", "jpeg"):
                body = b"RIFF\x00\x00\x00\x00WEBPtest" if fmt == "webp" else b"\xff\xd8synthetic-jpeg"
                rows.append({"Purpose": purpose, "Format": fmt, "ContentType": "image/" + fmt,
                             "Width": 48, "Height": 64, "SizeBytes": len(body), "Digest": hashlib.sha256(body).hexdigest()})
        self.old = {"public": True, "checkInId": check_in, "photoId": photo, "ownerId": owner,
                    "storageKey": key, "sourceVersion": source, "recipeVersion": 1, "variants": rows}
        self.client.old = self.old
        self.old_digest = hashlib.sha256(canary.png(False)).hexdigest()

    def compose(self, *args):
        self.calls.append(("compose", args, None, None))
        if args[:3] == ("ps", "-a", "-q"):
            return self.ids[args[3]]
        if args == ("up", "-d", "--no-deps", "shops", "media"):
            return ""
        raise AssertionError("Unreviewed fake compose operation")

    def run(self, args, stdin=None, environment=None, timeout=25):
        self.calls.append(("run", args, stdin, environment))
        if args[:3] == ["docker", "image", "inspect"]:
            return "b" * 40 if self.bad_revision else self.environment["EXPECTED_DEPLOY_SHA"]
        if args[:2] == ["docker", "inspect"]:
            if "running" in args[3]:
                return json.dumps({"running": not self.stopped, "image": "sha256:" + "c" * 64})
            if ".Config.Env" in args[3]:
                user_id = self.client.owner
                values = {"PhotoVariantOptions__Enabled": "true", "PhotoVariantOptions__ReadEnabled": "true",
                          "PhotoVariantOptions__CleanupEnabled": "false", "PhotoVariantOptions__CleanupDryRun": "true",
                          "CheckInPhotoDeliveryOptions__ServeVariants": "true", "CheckInPhotoDeliveryOptions__AdvertiseVariants": "true",
                          "CheckInPhotoDeliveryOptions__AdvertiseCanaryOnly": "true", "CheckInPhotoDeliveryOptions__AdvertiseCanaryUserIds": user_id,
                          "CheckInPhotoDeliveryOptions__CachePublicVariants": "false"}
                rows = [key + "=" + value for key, value in values.items()]
                if self.duplicate_runtime:
                    rows.append("CheckInPhotoDeliveryOptions__CachePublicVariants=true")
                return "\n".join(rows)
            return self.health
        if args[:2] == ["docker", "exec"]:
            if self.query_failure:
                raise RuntimeError("SQL private@example.invalid password private-db-secret confirmation-secret")
            database = args[args.index("-d") + 1]
            if "__EFMigrationsHistory" in stdin:
                return json.dumps({"ready": self.migrated})
            if database == "cpaccountdb":
                return json.dumps({"email": self.client.account["email"], "userId": self.client.owner,
                                   "confirmationToken": "confirmation-secret"})
            if database == "cpshopsdb":
                return json.dumps(self.old)
            return json.dumps({"ready": self.ready, "originalSha256": self.old_digest, "variants": self.old["variants"]})
        raise AssertionError("Unreviewed fake Docker operation")


class ActivationTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.private = self.root / "private"
        self.private.mkdir(mode=0o700)
        (self.root / "deploy").mkdir()
        self.env_path = self.root / "deploy" / ".env"
        self.original = b'OTHER_PASSWORD="private-db-secret"\nIMAGE_TAG=sha-' + b'a' * 40 + b'\nCHECKIN_VARIANTS_CACHE_ENABLED=true\n'
        self.env_path.write_bytes(self.original)
        if os.name != "nt":
            self.env_path.chmod(0o600)
        self.addCleanup(patch.stopall)
        patch.object(activation, "ROOT", self.root).start()
        patch.object(activation, "PRIVATE", self.private).start()
        self.environment = {"EXPECTED_DEPLOY_SHA": "a" * 40, "POSTGRES_USER": "private-db-user", "POSTGRES_PASSWORD": "private-db-secret"}
        self.client = PilotProduction()
        self.commands = FakeCommands(self.environment, self.client)
        self.operator = activation.Activation(self.environment, self.commands, self.client)

    def prepare(self):
        self.operator.run("canary-prepare")

    def activate(self):
        self.prepare()
        return self.operator.run("canary-activate")

    def test_full_closed_pilot_protocol_uses_only_generated_account_and_private_home(self):
        self.activate()
        created = self.operator.run("canary-create")
        self.assertTrue(created["privateHomeCreated"])
        result = self.operator.run("canary-verify")
        self.assertTrue(result["descriptorGateVerified"])
        self.assertTrue(result["publicCanaryDescriptorGateVerified"])
        self.assertEqual((2, 1), (result["newPhotos"], result["oldPhotos"]))
        self.assertTrue(self.operator.state()["verification"]["descriptorGateVerified"])
        self.assertTrue(self.operator.state()["verification"]["publicCanaryDescriptorGateVerified"])
        old_reads = [(method, token) for method, route, token, _ in self.client.calls
                     if route == "/api/v1/check-ins/" + self.commands.old["checkInId"]]
        self.assertEqual([("GET", None), ("GET", self.operator.state()["accessToken"])], old_reads)
        self.assertTrue(self.operator.run("canary-cleanup")["privateHomeDeleted"])
        home_writes = [data for method, route, _, data in self.client.calls if method == "POST" and route == "/api/v1/check-ins/home"]
        self.assertEqual(1, len(home_writes))
        self.assertEqual("Private", home_writes[0]["visibility"])
        self.assertFalse(any("visibility" in route for _, route, _, _ in self.client.calls))
        self.assertTrue((self.private / "confirmation.json").exists())
        self.assertTrue((self.private / "old-photo.json").exists())

    def test_public_canary_selected_drink_photo_advertises_the_verified_manifest(self):
        self.activate()
        self.operator.run("canary-create")
        self.client.public_as_drink = True
        result = self.operator.run("canary-verify")
        self.assertTrue(result["publicCanaryDescriptorGateVerified"])

    def test_allowlisted_public_descriptor_omission_or_mismatch_cannot_pass(self):
        self.activate()
        self.operator.run("canary-create")
        original_api = self.client.api
        mutations = (
            lambda photo: photo.update(variants=None),
            lambda photo: photo["variants"].pop(),
            lambda photo: photo["variants"][0].update(sourceVersion="f" * 64),
            lambda photo: photo["variants"][0].update(recipeVersion=2),
            lambda photo: photo["variants"][0].update(url="https://foreign.example/private"),
            lambda photo: photo["variants"][0].update(sizeBytes=999),
            lambda photo: photo["variants"][0].update(width=47),
            lambda photo: photo["variants"][0].update(height=63),
            lambda photo: photo["variants"][0].update(contentType="image/png"),
            lambda photo: photo["variants"].__setitem__(1, dict(photo["variants"][0])),
        )
        for index, mutation in enumerate(mutations):
            with self.subTest(mutation=index):
                def changed_response(method, path, token=None, data=None):
                    result = original_api(method, path, token, data)
                    if path == "/api/v1/check-ins/" + self.commands.old["checkInId"] and token:
                        mutation(result["photos"][0])
                    return result
                with patch.object(self.client, "api", side_effect=changed_response):
                    with self.assertRaises(canary.CanaryError):
                        self.operator.run("canary-verify")
                self.assertNotIn("publicCanaryDescriptorGateVerified", self.operator.state()["verification"])

    def test_activation_restarts_only_two_services_and_persists_fixed_fail_closed_flags(self):
        result = self.activate()
        self.assertEqual(5, result["runningAppsVerified"])
        up = [args for kind, args, _, _ in self.commands.calls if kind == "compose" and args[0] == "up"]
        self.assertEqual([("up", "-d", "--no-deps", "shops", "media")], up)
        expected = activation.pilot_flags(self.client.owner)
        content = self.env_path.read_text()
        self.assertIn('OTHER_PASSWORD="private-db-secret"\n', content)
        for key, value in expected.items():
            self.assertEqual(1, content.count(key + "="))
            self.assertIn(key + "=" + value + "\n", content)
            self.assertEqual(value, self.commands.environment[key])
        self.assertNotIn("ADVERTISE_CANARY", content)
        if os.name != "nt":
            self.assertEqual(0o600, self.env_path.stat().st_mode & 0o777)

    def test_db_queries_are_read_only_bounded_exact_email_and_password_is_never_an_argument(self):
        self.activate()
        queries = [(args, sql, environment) for kind, args, sql, environment in self.commands.calls if kind == "run" and sql]
        account = [sql for args, sql, _ in queries if args[args.index("-d") + 1] == "cpaccountdb"]
        self.assertEqual(1, len(account))
        self.assertIn('WHERE "Email"=\'' + self.client.account["email"] + "'", account[0])
        self.assertIn('AND "Username"=', account[0])
        self.assertIn('"EmailConfirmationExpiresAt">now()', account[0])
        for args, sql, environment in queries:
            self.assertNotIn("private-db-secret", " ".join(args))
            self.assertNotIn("private-db-user", " ".join(args))
            self.assertEqual("private-db-secret", environment["PGPASSWORD"])
            self.assertEqual("private-db-user", environment["PGUSER"])
            self.assertTrue(sql.startswith("BEGIN READ ONLY;"))
            self.assertIn("statement_timeout = '5s'", sql)
            self.assertIn("lock_timeout = '2s'", sql)
            self.assertTrue(sql.endswith("ROLLBACK;\n"))

    def test_wrong_release_stopped_service_or_missing_migration_stops_before_flags_and_confirmation(self):
        for field in ("bad_revision", "stopped", "migrated"):
            with self.subTest(field=field):
                self.commands.calls.clear()
                self.commands.bad_revision, self.commands.stopped, self.commands.migrated = False, False, True
                setattr(self.commands, field, field != "migrated")
                if not self.client.account:
                    self.prepare()
                with self.assertRaises(canary.CanaryError):
                    self.operator.run("canary-activate")
                self.assertEqual(self.original, self.env_path.read_bytes())
                self.assertFalse((self.private / "confirmation.json").exists())
                self.assertFalse(any(kind == "compose" and args[0] == "up" for kind, args, _, _ in self.commands.calls))

    def test_arbitrary_account_never_reaches_account_query_or_flags(self):
        self.prepare()
        state = canary.private_read(self.operator.state_path)
        state["email"] = "unrelated@example.invalid"
        canary.private_write(self.operator.state_path, state)
        self.commands.calls.clear()
        with self.assertRaises(canary.CanaryError):
            self.operator.activate()
        self.assertEqual([], self.commands.calls)
        self.assertEqual(self.original, self.env_path.read_bytes())

    def test_failed_sql_never_emits_query_secrets_or_restarts_apps(self):
        self.prepare()
        self.commands.query_failure = True
        output = io.StringIO()
        with patch.object(activation, "private_lock", contextlib.nullcontext), contextlib.redirect_stdout(output):
            code = activation.main([], dict(self.environment, PHOTO_OPERATION="canary-activate"), self.commands, self.client)
        self.assertEqual(1, code)
        for private in ("private-db-secret", "private@example.invalid", "confirmation-secret", self.client.account["email"], self.client.owner):
            self.assertNotIn(private, output.getvalue())
        self.assertEqual(self.original, self.env_path.read_bytes())
        self.assertFalse(any(kind == "compose" and args[0] == "up" for kind, args, _, _ in self.commands.calls))

    def test_activation_health_failure_keeps_checkpoint_incomplete_and_blocks_create(self):
        self.prepare()
        self.commands.health = "stopped"
        with self.assertRaises(canary.CanaryError):
            self.operator.activate()
        state = self.operator.state()
        self.assertFalse(state["activation"]["healthy"])
        before = len(self.client.calls)
        with self.assertRaises(canary.CanaryError):
            self.operator.run("canary-create")
        self.assertEqual(before, len(self.client.calls))

    def test_actual_container_config_duplicate_or_changed_flag_blocks_create(self):
        self.activate()
        self.commands.duplicate_runtime = True
        before = len(self.client.calls)
        with self.assertRaises(canary.CanaryError):
            self.operator.run("canary-create")
        self.assertEqual(before, len(self.client.calls))

    def test_wrong_or_duplicate_configured_tag_or_stale_shell_override_stops_before_flag_write(self):
        self.prepare()
        for invalid in (self.original.replace(b"IMAGE_TAG=sha-" + b'a' * 40, b"IMAGE_TAG=dev"),
                        self.original + b"export IMAGE_TAG=sha-" + b'a' * 40 + b"\n"):
            self.env_path.write_bytes(invalid)
            with self.assertRaises(canary.CanaryError):
                self.operator.activate()
            self.assertEqual(invalid, self.env_path.read_bytes())
            self.assertFalse(self.operator.confirmation_path.exists())
        self.env_path.write_bytes(self.original)
        self.operator.environment["IMAGE_TAG"] = "dev"
        with self.assertRaises(canary.CanaryError):
            self.operator.activate()
        self.assertFalse(any(kind == "compose" and args[0] == "up" for kind, args, _, _ in self.commands.calls))

    def test_old_observation_requires_canonical_binding_and_matching_ready_media(self):
        self.activate()
        state = self.operator.state()
        for changed in ("binding", "ready", "source"):
            with self.subTest(changed=changed):
                original = dict(self.commands.old)
                self.commands.ready = changed != "ready"
                if changed == "binding":
                    self.commands.old["storageKey"] = "check-ins/untrusted-presign.png"
                if changed == "source":
                    self.commands.old["sourceVersion"] = "f" * 64
                with self.assertRaises(canary.CanaryError):
                    self.operator.old_photo(state)
                self.assertFalse(self.operator.old_path.exists())
                self.commands.old = original

    def test_old_community_query_covers_current_public_revision_hidden_shop_and_owner_binding(self):
        self.activate()
        self.operator.old_photo(self.operator.state())
        sql = next(sql for _, _, sql, _ in self.commands.calls if sql and 'JOIN "CheckInPhotos"' in sql)
        for predicate in ('p."OwnerId"=c."UserId"', 'c."Visibility"=1', 'c."ModerationState"=2',
                          'c."ApprovedRevision"=c."ContentRevision"', 'NOT s."IsHidden"', 'c."DeletedAtUtc" IS NULL'):
            self.assertIn(predicate, sql)
        self.assertIn("LIMIT 1", sql)

    def test_main_has_no_path_account_sql_or_flag_arguments_and_only_emits_counts(self):
        for arguments, operation in ((["--state", "/tmp/arbitrary"], "canary-prepare"), ([], "arbitrary")):
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                self.assertEqual(1, activation.main(arguments, dict(self.environment, PHOTO_OPERATION=operation), self.commands, self.client))
            self.assertEqual([], self.client.calls)
        output = io.StringIO()
        with patch.object(activation, "private_lock", contextlib.nullcontext), contextlib.redirect_stdout(output):
            self.assertEqual(0, activation.main([], dict(self.environment, PHOTO_OPERATION="canary-prepare"), self.commands, self.client))
        state = self.operator.state()
        self.assertNotIn(state["email"], output.getvalue())
        self.assertNotIn(state["password"], output.getvalue())
        self.assertEqual(True, json.loads(output.getvalue())["success"])


class EnvironmentTests(unittest.TestCase):
    def setUp(self):
        self.user = str(uuid.uuid4())

    def test_only_fixed_assignments_change_preserving_multiline_values_and_crlf(self):
        original = b'OTHER="first\r\nCHECKIN_VARIANTS_ENABLED=false\r\nlast"\r\nexport CHECKIN_VARIANTS_ENABLED = false # old\r\nUNRELATED=yes\r\n'
        updated = activation.environment_replacement(original, self.user)
        self.assertIn(b'OTHER="first\r\nCHECKIN_VARIANTS_ENABLED=false\r\nlast"\r\n', updated)
        self.assertIn(b"export CHECKIN_VARIANTS_ENABLED =true\r\n", updated)
        self.assertIn(b"UNRELATED=yes\r\n", updated)
        self.assertNotIn(b"# old", updated)
        self.assertEqual(updated, activation.environment_replacement(updated, self.user))

    def test_duplicate_multiline_continued_incomplete_or_executable_environment_fails_closed(self):
        for value in (b"CHECKIN_VARIANTS_ENABLED=false\nexport CHECKIN_VARIANTS_ENABLED=true\n",
                      b'CHECKIN_VARIANTS_ENABLED="first\nsecond"\n', b"CHECKIN_VARIANTS_ENABLED=false\\\nother\n",
                      b'OTHER="unfinished\n', b"OTHER=$(private-command)\n", b"OTHER=`private-command`\n", b"OTHER=bad\0value"):
            with self.subTest(value=value), self.assertRaises(canary.CanaryError):
                activation.environment_replacement(value, self.user)

    def test_invalid_or_empty_uuid_cannot_enter_environment(self):
        for identity in ("", "other,user", "\nKEY=true", str(uuid.UUID(int=0))):
            with self.subTest(identity=identity), self.assertRaises((canary.CanaryError, ValueError)):
                activation.environment_replacement(b"", identity)


class CommandTests(unittest.TestCase):
    def test_subprocess_failure_captures_stderr_and_does_not_echo_credentials(self):
        commands = activation.Commands({"POSTGRES_PASSWORD": "private-db-secret"})
        for failure in (subprocess.TimeoutExpired("private-command", 1), OSError("private-db-secret")):
            with patch.object(activation.subprocess, "run", side_effect=failure) as run:
                with self.assertRaises(canary.CanaryError) as raised:
                    commands.run(["docker", "inspect", "safe-id"])
                self.assertNotIn("private-db-secret", str(raised.exception))
                self.assertNotIn("private-command", str(raised.exception))
                self.assertEqual(subprocess.DEVNULL, run.call_args.kwargs["stderr"])

    def test_cache_disabled_wrapper_rejects_positive_freshness_and_shared_cache(self):
        for policy in ("private, max-age=0, must-revalidate", "private, no-store", "private, max-age=20, must-revalidate", "public, max-age=0, must-revalidate"):
            class Client:
                def request(self, *args, **kwargs):
                    return 200, {"cache-control": policy}, b"image"
            wrapped = activation.CacheDisabledClient(Client())
            if policy in ("private, max-age=0, must-revalidate", "private, no-store"):
                self.assertEqual(200, wrapped.request("GET", canary.API + "/variants/source")[0])
            else:
                with self.assertRaises(canary.CanaryError):
                    wrapped.request("GET", canary.API + "/variants/source")


if __name__ == "__main__":
    unittest.main()
