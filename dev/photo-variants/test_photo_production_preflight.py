import contextlib
import io
import json
import subprocess
import unittest
from unittest.mock import Mock, patch

import photo_production_preflight as tool

PRIVATE = "private-photo-key?password=do-not-print-this"
ID = "a" * 64
ENV = {"POSTGRES_USER": "test_role", "POSTGRES_PASSWORD": PRIVATE, "GATEWAY_AUTH_SECRET": PRIVATE,
       "CHECKIN_VARIANTS_ENABLED": "false", "CHECKIN_VARIANTS_CACHE_ENABLED": PRIVATE}


class PreflightTests(unittest.TestCase):
    def test_command_failure_drops_stdout_stderr_and_exception_text(self):
        commands = tool.Commands("/unused", ENV)
        with patch.object(tool.subprocess, "run", return_value=subprocess.CompletedProcess([], 1, PRIVATE, PRIVATE)) as run:
            with self.assertRaises(tool.ObservationUnavailable) as error:
                commands.run(["docker", "version"])
        self.assertNotIn(PRIVATE, str(error.exception))
        self.assertEqual(subprocess.DEVNULL, run.call_args.kwargs["stderr"])
        self.assertFalse(run.call_args.kwargs.get("shell", False))

    def test_flags_never_echo_arbitrary_values_and_presence_is_boolean(self):
        result = tool.flags(ENV)
        self.assertEqual({"configured": True, "value": False}, result["CHECKIN_VARIANTS_ENABLED"])
        self.assertEqual({"configured": True, "invalid": True}, result["CHECKIN_VARIANTS_CACHE_ENABLED"])
        self.assertEqual({"configured": False, "composeDefault": True}, result["CHECKIN_VARIANTS_CLEANUP_DRY_RUN"])
        self.assertNotIn(PRIVATE, json.dumps(result))

    def test_container_inspection_selects_no_environment_logs_commands_or_errors(self):
        for forbidden in (".Config.Env", ".Config.Cmd", ".State.Error", ".State.Health.Log"):
            self.assertNotIn(forbidden, tool.INSPECT)
        raw = {"id": ID, "imageId": "sha256:" + ID, "image": "example/media:dev", "status": "running",
               "running": True, "networkCount": 2, "health": "healthy", "Env": [PRIVATE], "error": PRIVATE,
               "ports": {"8080/tcp": [{"HostPort": "9123", "HostIp": "127.0.0.1"}], PRIVATE: []}}
        result = tool.container_view(raw)
        self.assertEqual(9123, result["publishedPorts"][0]["hostPort"])
        self.assertTrue(result["publishedPorts"][0]["loopbackOnly"])
        self.assertNotIn(PRIVATE, json.dumps(result))

    def test_gateway_probe_discovers_published_port_and_known_public_domain(self):
        containers = {"gateway": {"publishedPorts": [{"protocol": "tcp", "hostPort": 9123, "loopbackOnly": True}]}}
        probe = Mock(return_value={"healthy": True})
        tool.observe_health(containers, {"DOMAIN": "api.example.com"}, probe)
        self.assertEqual(["http://127.0.0.1:9123/health/gateway", "http://127.0.0.1:9123/health",
                          "https://api.example.com/health/gateway"], [call.args[0] for call in probe.call_args_list])

    def test_health_rejects_secret_url_before_opening_any_connection(self):
        with patch.object(tool.urllib.request, "build_opener") as opened:
            for url in ("https://user:password@api.example.com/health", "https://api.example.com/health?token=" + PRIVATE,
                        "file:///health", "https://api.example.com/not-health"):
                self.assertEqual("invalid_target", tool.health(url)["state"])
            opened.assert_not_called()

    def test_all_postgres_queries_start_readonly_rollback_and_never_pass_password_in_argv(self):
        commands = Mock()
        commands.run.return_value = '{"readOnly":true}'
        for database in tool.DATABASES:
            self.assertTrue(tool.pg_query(commands, ID, database, "SELECT json_build_object('readOnly',true);", ENV)["readOnly"])
        for call in commands.run.call_args_list:
            self.assertNotIn(PRIVATE, " ".join(call.args[0]))
            self.assertIn("PGPASSWORD", call.args[0])
            self.assertEqual(PRIVATE, call.kwargs["environment"]["PGPASSWORD"])
            self.assertTrue(call.kwargs["stdin"].startswith("BEGIN READ ONLY;\nSET LOCAL statement_timeout"))
            self.assertTrue(call.kwargs["stdin"].endswith("ROLLBACK;\n"))
            self.assertIn("SET LOCAL lock_timeout", call.kwargs["stdin"])
            self.assertIn("ON_ERROR_STOP=1", call.args[0])

    def test_schema_on_legacy_main_without_new_variant_tables_remains_readable(self):
        legacy = {"readOnly": True, "serverVersion": 170008, "databaseSizeBytes": 123,
                  "tables": {"__EFMigrationsHistory": True, "Photos": True},
                  "columns": {"Photos": ["Id", "StorageKey", "BucketType", "Status", "CheckInId", "ShopAttachmentId", "UploadedAt"]}}
        calls = []
        def query(commands, container, database, sql, environment):
            calls.append((database, sql))
            if "information_schema.columns" in sql:
                return legacy
            if "MigrationId" in sql:
                return {"latest": "20261008211500_ReserveShopPhotoAttachments", "count": 7, "unexpected": PRIVATE}
            return {"total": 5, "canonicalPrivateBound": 3, "unexpected": PRIVATE}
        with patch.object(tool, "pg_query", side_effect=query):
            result = tool.observe_databases(Mock(), ID, ENV)
        media = result["cpmediadb"]
        self.assertEqual("observed", media["state"])
        self.assertFalse(media["schema"]["tables"]["PhotoVariantJobs"])
        self.assertNotIn("jobs", media["counts"])
        self.assertEqual(123, media["schema"]["databaseSizeBytes"])
        self.assertNotIn(PRIVATE, json.dumps(result))
        self.assertFalse(any('FROM "PhotoVariantJobs"' in sql for _, sql in calls))

    def test_jobs_without_priority_column_use_explicit_missing_bucket(self):
        schema = tool.clean_schema({"readOnly": True, "tables": {"PhotoVariantJobs": True},
                                    "columns": {"PhotoVariantJobs": ["Status"]}})
        query = tool.count_queries("cpmediadb", schema)["jobs"]
        self.assertNotIn('"Priority"', query)
        self.assertIn("PriorityColumnMissing", query)

    def test_schema_and_counts_allow_only_expected_aggregate_fields(self):
        schema = tool.clean_schema({"readOnly": True, "tables": {"Photos": True, PRIVATE: True},
                                    "columns": {"Photos": ["Id", PRIVATE]}})
        counts = tool.clean_counts({"total": 3, PRIVATE: 6, "counts": [
            {"status": "Ready", "priority": "Backfill", "count": 2, "storageKey": PRIVATE},
            {"status": PRIVATE, "priority": "Upload", "count": 5}]})
        self.assertNotIn(PRIVATE, json.dumps([schema, counts]))
        self.assertEqual([{"status": "Ready", "priority": "Backfill", "count": 2}], counts["counts"])

    def test_unconfirmed_readonly_prevents_followup_aggregate_reads(self):
        with patch.object(tool, "pg_query", return_value={"readOnly": False}) as query:
            result = tool.observe_databases(Mock(), ID, ENV)
        self.assertTrue(all(item["state"] == "unavailable" for item in result.values()))
        self.assertEqual(4, query.call_count)

    def test_docker_storage_is_readonly_and_discards_unexpected_fields(self):
        commands = Mock()
        commands.run.return_value = json.dumps({"Type": "Images", "TotalCount": "7", "Active": "5", "Size": "13.2GB",
                                               "Reclaimable": "4.1GB (31%)", "Env": PRIVATE})
        result = tool.docker_storage(commands)
        self.assertEqual("13.2GB", result["items"][0]["size"])
        self.assertEqual(7, result["items"][0]["totalCount"])
        self.assertNotIn(PRIVATE, json.dumps(result))
        self.assertEqual(["docker", "system", "df", "--format", "{{json .}}"], commands.run.call_args.args[0])

    def test_fatal_exception_prints_json_only_without_raw_details_or_traceback(self):
        output = io.StringIO()
        with patch.object(tool, "report", side_effect=RuntimeError(PRIVATE)), contextlib.redirect_stdout(output):
            self.assertEqual(1, tool.main())
        self.assertEqual("preflight_unavailable", json.loads(output.getvalue())["state"])
        self.assertNotIn(PRIVATE, output.getvalue())

    def test_storage_volume_identity_is_observed_without_compose_or_environment(self):
        commands = Mock()
        raw = {"project": "coffeepeek", "service": "minio", "running": True, "imageId": "sha256:" + "a" * 64,
               "mounts": [{"Type": "volume", "Name": "coffeepeek_minio_data", "Destination": "/data", "RW": True,
                           "Source": PRIVATE}], "Env": PRIVATE}
        commands.run.return_value = json.dumps(raw)
        result = tool.storage_topology(commands)
        self.assertEqual("observed", result["state"])
        self.assertEqual("coffeepeek_minio_data", result["mounts"][0]["name"])
        self.assertNotIn(PRIVATE, json.dumps(result))
        commands.compose.assert_not_called()
        raw["project"] = "another-project"
        commands.run.return_value = json.dumps(raw)
        self.assertEqual({"state": "unavailable"}, tool.storage_topology(commands))

    def test_schema_sql_has_sizes_but_no_keys_ids_credentials_or_writes_in_result(self):
        sql = tool.schema_sql()
        self.assertIn("pg_database_size(current_database())", sql)
        self.assertIn("transaction_read_only", sql)
        for mutation in ("INSERT ", "UPDATE ", "DELETE ", "CREATE ", "ALTER ", "DROP ", "COPY "):
            self.assertNotIn(mutation, sql.upper())


if __name__ == "__main__":
    unittest.main()
