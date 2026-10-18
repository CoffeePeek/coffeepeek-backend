import contextlib
import datetime as dt
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import Mock, patch
import urllib.error

import photo_production_guard as guard

PRIVATE = "private-object-key-and-credential-never-print"
ENV = {"POSTGRES_USER": "test_role", "POSTGRES_PASSWORD": PRIVATE,
       "MINIO_ROOT_USER": "test_storage_role", "MINIO_ROOT_PASSWORD": PRIVATE}
NETWORK = "f" * 64


def digest(value):
    return hashlib.sha256(value.encode()).hexdigest()


class FakeCommands:
    def __init__(self):
        self.calls = []
        self.dumped = []
        self.tags = {}
        self.mutate = False
        self.account_reads = 0

    def run(self, args, **kwargs):
        self.calls.append(args)
        if args[:2] == ["docker", "ps"]:
            return digest(args[-1].split("=")[-1])
        if args[:2] == ["docker", "inspect"]:
            service = next(name for name in (*guard.APPLICATIONS, "postgres", "minio") if digest(name) == args[-1])
            if service == "account":
                self.account_reads += 1
            return json.dumps({"id": digest(service), "name": "/coffeepeek-" + service + "-1", "running": True,
                "project": "coffeepeek", "service": service,
                "imageId": "sha256:" + digest(service + ("changed" if self.mutate and self.account_reads > 1 else "")),
                "networks": {"coffeepeek_default": {"NetworkID": NETWORK, "IPAddress": "172.18.0.2"}}})
        if args[:3] == ["docker", "network", "inspect"]:
            return json.dumps({"id": NETWORK, "driver": "bridge", "scope": "local", "config": [{"Subnet": "172.18.0.0/16"}]})
        if args[:3] == ["docker", "image", "ls"]:
            return self.tags.get(args[-1], "")
        if args[:3] == ["docker", "image", "tag"]:
            self.tags[args[-1]] = args[-2]
            return ""
        if args[:3] == ["docker", "image", "inspect"]:
            return self.tags[args[-1]]
        raise AssertionError("Unexpected guard command")

    def dump(self, container, database, destination):
        self.dumped.append(database)
        guard.write_bytes(destination, b"PGDMPsynthetic-" + database.encode())


class FakePolicies:
    def __init__(self, anonymous_private=False, unknown=False):
        self.calls = []
        self.anonymous_private, self.unknown = anonymous_private, unknown

    def read(self, bucket):
        self.calls.append(bucket)
        if self.unknown:
            raise guard.GuardError(PRIVATE)
        public = bucket == "coffeepeek.shops" or self.anonymous_private
        policy = {"Statement": [{"Effect": "Allow", "Principal": "*", "Action": "s3:GetObject", "Resource": PRIVATE}] if public else []}
        body = json.dumps(policy).encode()
        return body, guard.policy_view(policy, body)


class GuardTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.root = self.base / "production"
        self.output = self.base / "private-backups"
        (self.root / "deploy").mkdir(parents=True)
        (self.root / "deploy" / ".env").write_text(PRIVATE)
        (self.root / "deploy" / "docker-compose.yml").write_text("services: {}")
        self.addCleanup(patch.stopall)
        patch.object(guard, "ROOT", self.root).start()
        patch.object(guard, "OUTPUT", self.output).start()
        self.commands = FakeCommands()
        self.now = dt.datetime(2026, 10, 18, tzinfo=dt.timezone.utc)

    def test_storage_reads_only_three_fixed_policies_and_saves_raw_private_evidence(self):
        policies = FakePolicies()
        report = guard.execute("storage", "12345", ENV, self.commands, policies, self.now)
        self.assertTrue(report["complete"])
        self.assertTrue(report["privateBucketsReady"])
        self.assertEqual(list(guard.BUCKETS), policies.calls)
        self.assertNotIn(PRIVATE, json.dumps(report))
        self.assertNotIn("172.18", json.dumps(report))
        destination = self.output / report["backupId"]
        stored = (destination / "coffeepeek.shops.policy.json").read_bytes()
        self.assertIn(PRIVATE.encode(), stored)
        self.assertEqual(hashlib.sha256(stored).hexdigest(), report["policies"]["coffeepeek.shops"]["sha256"])
        if os.name != "nt":
            self.assertEqual(0o600, (destination / "coffeepeek.shops.policy.json").stat().st_mode & 0o777)
            self.assertEqual(0o700, destination.stat().st_mode & 0o777)
        self.assertFalse(any("tag" in args or "exec" in args for args in self.commands.calls))

    def test_backup_validates_four_databases_copies_private_config_and_tags_five_immutable_images(self):
        report = guard.execute("backup", "12346", ENV, self.commands, FakePolicies(), self.now)
        self.assertEqual(list(guard.DATABASES), self.commands.dumped)
        self.assertEqual(5, len(self.commands.tags))
        self.assertTrue(all(item["validatedToc"] for item in report["databases"].values()))
        self.assertNotIn(PRIVATE, json.dumps(report))
        destination = self.output / report["backupId"]
        self.assertEqual(PRIVATE, (destination / "deploy_.env.backup").read_text())
        manifest = json.loads((destination / "rollback-manifest.json").read_text())
        self.assertFalse(manifest["automaticRestore"])
        self.assertFalse(manifest["automaticSchemaDown"])
        self.assertEqual("individual-database-snapshots", manifest["databaseConsistency"])
        self.assertEqual(set(guard.APPLICATIONS), set(manifest["applications"]))
        self.assertFalse(any(set(args).intersection({"stop", "rm", "down", "up", "restart", "push", "pull"}) for args in self.commands.calls))

    def test_unknown_storage_stops_before_any_dump_or_tag(self):
        with self.assertRaises(guard.GuardError):
            guard.execute("backup", "unknown", ENV, self.commands, FakePolicies(unknown=True), self.now)
        self.assertFalse(self.commands.dumped)
        self.assertFalse(self.commands.tags)

    def test_public_private_bucket_is_preserved_as_evidence_but_never_reports_ready(self):
        report = guard.execute("backup", "public", ENV, self.commands, FakePolicies(anonymous_private=True), self.now)
        self.assertTrue(report["complete"])
        self.assertFalse(report["privateBucketsReady"])
        self.assertTrue(report["policies"]["coffeepeek.checkins"]["anonymousAllow"])

    def test_concurrent_deployment_aborts_before_rollback_tags(self):
        self.commands.mutate = True
        with self.assertRaises(guard.GuardError):
            guard.execute("backup", "concurrent", ENV, self.commands, FakePolicies(), self.now)
        self.assertFalse(self.commands.tags)

    def test_existing_rollback_tag_cannot_be_overwritten_with_different_image(self):
        self.commands.tags["coffeepeek-rollback/account:photo-existing"] = "sha256:" + "1" * 64
        with self.assertRaises(guard.GuardError):
            guard.execute("backup", "existing", ENV, self.commands, FakePolicies(), self.now)
        self.assertEqual("sha256:" + "1" * 64, self.commands.tags["coffeepeek-rollback/account:photo-existing"])

    def test_path_traversal_invalid_operations_and_existing_backup_are_rejected(self):
        for operation, run_id in (("restore", "1"), ("backup", "../../unsafe"), ("backup", "")):
            with self.assertRaises(guard.GuardError):
                guard.execute(operation, run_id, ENV, self.commands, FakePolicies(), self.now)
        guard.execute("storage", "same", ENV, self.commands, FakePolicies(), self.now)
        with self.assertRaises(FileExistsError):
            guard.execute("storage", "same", ENV, self.commands, FakePolicies(), self.now)

    def test_signed_requests_bind_fixed_bucket_ip_get_empty_query_and_timestamp(self):
        headers = guard.signed_headers("172.18.0.2", "coffeepeek.checkins", "example", "test-only", self.now)
        self.assertEqual("172.18.0.2:9000", headers["Host"])
        self.assertEqual("20261018T000000Z", headers["x-amz-date"])
        self.assertEqual(hashlib.sha256(b"").hexdigest(), headers["x-amz-content-sha256"])
        self.assertIn("/20261018/us-east-1/s3/aws4_request", headers["Authorization"])
        self.assertIn("SignedHeaders=host;x-amz-content-sha256;x-amz-date", headers["Authorization"])
        self.assertNotEqual(headers["Authorization"], guard.signed_headers("172.18.0.2", "coffeepeek.shops", "example", "test-only", self.now)["Authorization"])
        for ip in ("127.0.0.1", "169.254.169.254", "8.8.8.8", "::1"):
            with self.assertRaises((guard.GuardError, ValueError)):
                guard.signed_headers(ip, "coffeepeek.checkins", "example", "test-only", self.now)
        with self.assertRaises(guard.GuardError):
            guard.signed_headers("172.18.0.2", "arbitrary-bucket", "example", "test-only", self.now)

    def test_network_must_be_observed_private_local_docker_bridge(self):
        observed = guard.container(self.commands, "minio")
        self.assertEqual("172.18.0.2", guard.storage_ip(self.commands, observed))
        for ip in ("127.0.0.1", "169.254.169.254", "8.8.8.8", ""):
            observed["networks"]["coffeepeek_default"]["IPAddress"] = ip
            with self.assertRaises(guard.GuardError):
                guard.storage_ip(self.commands, observed)
        for forbidden in (".Config.Env", ".Config.Cmd", ".State.Error", ".State.Health.Log"):
            self.assertNotIn(forbidden, guard.INSPECT)

    def test_dump_streams_private_custom_format_validates_toc_and_passes_credentials_only_in_environment(self):
        destination = self.base / "database.dump"
        def run(args, **kwargs):
            self.assertNotIn(PRIVATE, " ".join(args))
            self.assertEqual(PRIVATE, kwargs["env"]["PGPASSWORD"])
            self.assertEqual(subprocess.DEVNULL, kwargs["stderr"])
            self.assertFalse(kwargs.get("shell", False))
            if "pg_dump" in args:
                self.assertIn("--format=custom", args)
                self.assertIn("default_transaction_read_only=on", kwargs["env"]["PGOPTIONS"])
                kwargs["stdout"].write(b"PGDMPsynthetic")
            else:
                self.assertIn("--list", args)
                self.assertEqual(b"PGDMP", kwargs["stdin"].read(5))
            return subprocess.CompletedProcess(args, 0)
        with patch.object(guard.subprocess, "run", side_effect=run) as mocked:
            guard.Commands(ENV).dump("a" * 64, "cpmediadb", destination)
        self.assertEqual(2, mocked.call_count)
        if os.name != "nt":
            self.assertEqual(0o600, destination.stat().st_mode & 0o777)

    def test_main_suppresses_all_raw_output_and_exceptions(self):
        stream = io.StringIO()
        with patch.object(guard, "execute", side_effect=RuntimeError(PRIVATE)), contextlib.redirect_stdout(stream):
            self.assertEqual(1, guard.main(["backup", "1"]))
        self.assertNotIn(PRIVATE, stream.getvalue())
        self.assertFalse(json.loads(stream.getvalue())["complete"])


class PolicyTests(unittest.TestCase):
    def test_missing_policy_and_missing_bucket_are_distinct_known_baselines(self):
        for code, status in (("NoSuchBucketPolicy", "missing"), ("NoSuchBucket", "missing_bucket")):
            client = guard.Policies("172.18.0.2", ENV)
            error = urllib.error.HTTPError("http://private/", 404, PRIVATE, {}, io.BytesIO(("<Error><Code>" + code + "</Code></Error>").encode()))
            client.opener = Mock()
            client.opener.open.side_effect = error
            body, result = client.read("coffeepeek.checkins")
            self.assertEqual(status, result["status"])
            self.assertEqual(code == "NoSuchBucketPolicy", result["private"])
            self.assertNotIn(PRIVATE, body.decode())
            request = client.opener.open.call_args.args[0]
            self.assertEqual("GET", request.method)
            self.assertEqual("http://172.18.0.2:9000/coffeepeek.checkins?policy=", request.full_url)

    def test_403_unknown_404_dtd_and_oversized_error_are_not_private(self):
        for status, body in ((403, b"<Error><Code>NoSuchBucketPolicy</Code></Error>"),
                             (404, b"<Error><Code>AccessDenied</Code></Error>"),
                             (404, b"<!DOCTYPE Error><Error><Code>NoSuchBucketPolicy</Code></Error>"), (404, b"x" * 4097)):
            client = guard.Policies("172.18.0.2", ENV)
            client.opener = Mock()
            client.opener.open.side_effect = urllib.error.HTTPError("http://private", status, PRIVATE, {}, io.BytesIO(body))
            with self.assertRaises(guard.GuardError):
                client.read("coffeepeek.checkins")

    def test_anonymous_allow_is_conservative_under_conditions_and_deny(self):
        policy = {"Statement": [{"Effect": "Allow", "Principal": {"AWS": "*"}, "Action": "s3:GetObject",
                                  "Resource": PRIVATE, "Condition": {"StringEquals": {"aws:PrincipalType": "User"}}},
                                 {"Effect": "Deny", "Principal": "*", "Action": "s3:GetObject", "Resource": "*"}]}
        body = json.dumps(policy).encode()
        result = guard.policy_view(policy, body)
        self.assertTrue(result["anonymousAllow"])
        self.assertFalse(result["private"])
        self.assertNotIn(PRIVATE, json.dumps(result))
        for malformed in ({}, {"Statement": "invalid"}, {"Statement": [{"Effect": "Allow", "NotPrincipal": "*", "Action": "*"}]}):
            with self.assertRaises(guard.GuardError):
                guard.policy_view(malformed, b"synthetic")

    def test_no_redirect_and_no_proxy_are_used(self):
        with patch.object(guard.urllib.request, "build_opener") as built, patch.object(guard.urllib.request, "ProxyHandler") as proxy:
            guard.Policies("172.18.0.2", ENV)
        proxy.assert_called_once_with({})
        self.assertEqual(2, len(built.call_args.args))
        self.assertIsNone(guard.NoRedirect().redirect_request(None, None, 307, "redirect", {}, "http://untrusted"))


if __name__ == "__main__":
    unittest.main()
