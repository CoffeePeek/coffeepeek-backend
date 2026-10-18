import contextlib
import importlib.util
import io
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile
import textwrap
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "deploy" / "scripts"
SPEC = importlib.util.spec_from_file_location("persist_image_tag", SCRIPTS / "persist_image_tag.py")
tag_tool = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(tag_tool)
TAG = "sha-" + "a" * 40
PRIVATE = "private-password-with-special-quote'and-dollar$sign"
BASH = shutil.which("bash") or (str(Path("C:/Program Files/Git/bin/bash.exe")) if Path("C:/Program Files/Git/bin/bash.exe").is_file() else None)


class ImageTagTests(unittest.TestCase):
    def test_exact_sha_tag_replaces_only_assignment_preserving_quotes_secrets_crlf_and_mode(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"
            original = b'# release\r\nPASSWORD=\'s\'"\'"\'ecret$with;spaces\'\r\nIMAGE_TAG="dev"\r\nOTHER="literal \\" quote"\r\n'
            # Keep syntax valid while covering bytes that must never be re-serialized.
            original = original.replace(b'OTHER="literal \\" quote"', b'OTHER=\'literal \\" quote\'')
            path.write_bytes(original); path.chmod(0o600)
            before_mode = stat.S_IMODE(path.stat().st_mode)
            tag_tool.persist(path, TAG)
            self.assertEqual(original.replace(b'IMAGE_TAG="dev"', ("IMAGE_TAG=" + TAG).encode()), path.read_bytes())
            self.assertEqual(before_mode, stat.S_IMODE(path.stat().st_mode))
            self.assertFalse(list(path.parent.glob(".image-tag-*")))

    def test_duplicate_assignments_and_invalid_override_leave_file_untouched_without_secret_output(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"
            original = ("PASSWORD='secret'\nIMAGE_TAG=dev\nexport IMAGE_TAG=older\n").encode()
            path.write_bytes(original)
            for value in (TAG, "dev", "sha-short", TAG + "; echo " + PRIVATE, "sha-" + "A" * 40):
                output = io.StringIO()
                with contextlib.redirect_stdout(output):
                    self.assertEqual(1, tag_tool.main([str(path), value]))
                self.assertNotIn(PRIVATE, output.getvalue())
                self.assertEqual(original, path.read_bytes())

    def test_multiline_secret_that_contains_image_assignment_text_is_preserved(self):
        original = b"PASSWORD='first line\nIMAGE_TAG=inside-secret\nlast line'\nIMAGE_TAG=dev\n"
        self.assertEqual(original.replace(b"IMAGE_TAG=dev", ("IMAGE_TAG=" + TAG).encode()), tag_tool.replacement(original, TAG))
        self.assertEqual(b"PASSWORD='first line\nIMAGE_TAG=inside-secret\nlast line'\nIMAGE_TAG=" + TAG.encode() + b"\n",
                         tag_tool.replacement(original.rsplit(b"IMAGE_TAG=dev\n", 1)[0], TAG))

    def test_executable_or_multiline_image_tag_syntax_is_rejected_without_evaluation(self):
        for original in (b"SECRET=$(echo sensitive)\nIMAGE_TAG=dev\n", b"IMAGE_TAG='multi\nline'\n",
                         b"IMAGE_TAG=dev\nSECRET='unclosed\n"):
            with self.assertRaises(ValueError):
                tag_tool.replacement(original, TAG)

    def test_failed_atomic_replace_keeps_original_and_removes_temporary_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"; path.write_bytes(b"IMAGE_TAG=dev\nPASSWORD='secret'\n")
            original = path.read_bytes()
            with patch.object(tag_tool.os, "replace", side_effect=OSError("injected")):
                with self.assertRaises(OSError):
                    tag_tool.persist(path, TAG)
            self.assertEqual(original, path.read_bytes())
            self.assertFalse(list(path.parent.glob(".image-tag-*")))

    def test_check_only_and_idempotent_apply_do_not_replace_the_environment_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"; path.write_bytes(b"IMAGE_TAG=dev\n")
            with patch.object(tag_tool.os, "replace", side_effect=AssertionError("unexpected replace")):
                tag_tool.persist(path, TAG, check_only=True)
            tag_tool.persist(path, TAG)
            with patch.object(tag_tool.os, "replace", side_effect=AssertionError("unexpected replace")):
                tag_tool.persist(path, TAG)


@unittest.skipUnless(BASH, "Bash is required for recorded Docker deployment integration tests")
class ReleaseScriptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.deploy = self.root / "deploy"
        self.scripts = self.deploy / "scripts"; self.scripts.mkdir(parents=True)
        self.bin = self.root / "bin"; self.bin.mkdir()
        bootstrap = self.root / "test-bootstrap.sh"
        bootstrap.write_text('''if command -v cygpath >/dev/null 2>&1; then
  export PATH="$(cygpath -u "$PHOTO_TEST_BIN"):$PATH"
else
  export PATH="$PHOTO_TEST_BIN:$PATH"
fi
''', encoding="utf-8", newline="\n")
        for name in ("ci-deploy.sh", "update.sh", "prepare-images.sh", "prepare-migrations.sh", "apply-migrations.sh", "persist_image_tag.py"):
            shutil.copyfile(SCRIPTS / name, self.scripts / name)
        for name in ("ensure-secrets.sh", "check-coffee-import.sh"):
            (self.scripts / name).write_text("#!/usr/bin/env bash\nset -euo pipefail\n", encoding="utf-8")
        (self.root / "dev").mkdir()
        (self.root / "dev" / "backfill-profile-roaster-counts.sh").write_text("#!/usr/bin/env bash\nexit 0\n")
        self.write_bin("docker", '''#!/usr/bin/env bash
set -euo pipefail
printf 'tag=%s|%s\\n' "${IMAGE_TAG:-unset}" "$*" >> "$PHOTO_TEST_EVENT_LOG"
if [[ "${1:-}" == login ]]; then cat >/dev/null; fi
if [[ "${PHOTO_FAIL:-}" == app && "$*" == 'compose pull account shops moderation media gateway' ]]; then exit 19; fi
if [[ "${PHOTO_FAIL:-}" == sdk && "$*" == 'pull mcr.microsoft.com/dotnet/sdk:10.0' ]]; then exit 20; fi
if [[ "$*" == 'image inspect --format {{.Id}} mcr.microsoft.com/dotnet/sdk:10.0' ]]; then
  printf 'sha256:%064d\\n' 1
fi
if [[ "${1:-}" == run && "$*" == *'bash deploy/scripts/prepare-migrations.sh' ]]; then
  env -u POSTGRES_USER -u POSTGRES_PASSWORD -u PGUSER -u PGPASSWORD -u PostgresCpOptions__ConnectionString \
    bash "$PHOTO_TEST_ROOT/deploy/scripts/prepare-migrations.sh"
fi
if [[ "${1:-}" == run && "$*" == *'bash deploy/scripts/apply-migrations.sh --prepared' ]]; then
  bash "$PHOTO_TEST_ROOT/deploy/scripts/apply-migrations.sh" --prepared
fi
exit 0
''')
        self.write_bin("dotnet", '''#!/usr/bin/env bash
set -euo pipefail
printf 'tag=%s|dotnet %s\\n' "${IMAGE_TAG:-unset}" "$*" >> "$PHOTO_TEST_EVENT_LOG"
if [[ "$*" == 'tool restore' && "${PHOTO_FAIL:-}" == tool_restore ]]; then exit 21; fi
if [[ "${1:-}" == restore && "${PHOTO_FAIL:-}" == package_restore ]]; then exit 22; fi
if [[ "${1:-}" == build && "${PHOTO_FAIL:-}" == migration_build ]]; then exit 23; fi
if [[ "${1:-}" == ef && "$*" != *--no-build* ]]; then exit 24; fi
exit 0
''')
        self.write_bin("curl", "#!/usr/bin/env bash\nexit 0\n")
        self.write_bin("python3", '''#!/usr/bin/env bash
target="$PHOTO_TEST_PYTHON"
if command -v cygpath >/dev/null 2>&1; then target="$(cygpath -u "$target")"; fi
exec "$target" "$@"
''')
        self.original = b"IMAGE_TAG=dev\nPASSWORD='s'\"'\"'ecret$with;spaces'\nCHECKIN_VARIANTS_ENABLED=false\nUSE_HTTPS=0\nPOSTGRES_USER=test_role\nPOSTGRES_PASSWORD=test-database-password\n"
        (self.deploy / ".env").write_bytes(self.original)
        self.environment = os.environ | {"PATH": str(self.bin) + os.pathsep + os.environ.get("PATH", ""),
            "BASH_ENV": bootstrap.as_posix(), "PHOTO_TEST_BIN": str(self.bin),
            "PHOTO_TEST_ROOT": self.root.as_posix(), "PHOTO_TEST_EVENT_LOG": (self.deploy / "docker.log").as_posix(),
            "PHOTO_TEST_PYTHON": sys.executable, "DEPLOY_IMAGE_TAG": TAG,
            "DOCKER_HUB_USERNAME": "test-user", "DOCKER_HUB_PASSWORD": PRIVATE}

    def write_bin(self, name, content):
        path = self.bin / name; path.write_text(content, encoding="utf-8", newline="\n"); path.chmod(0o755)

    def run_script(self, name, **overrides):
        environment = self.environment | overrides
        return subprocess.run([BASH, str(self.scripts / name).replace("\\", "/")], cwd=self.root,
                              env=environment, capture_output=True, text=True, timeout=30)

    def events(self):
        path = self.deploy / "docker.log"
        return path.read_text().splitlines() if path.exists() else []

    def test_all_images_and_login_are_prepared_before_writer_stop_and_tag_is_persisted(self):
        result = self.run_script("ci-deploy.sh")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertNotIn(PRIVATE, result.stdout + result.stderr)
        events = self.events()
        stop = next(i for i, line in enumerate(events) if "|compose stop gateway account shops moderation media" in line)
        required = ("|login -u test-user --password-stdin", "|compose pull account shops moderation media gateway",
                    "|pull mcr.microsoft.com/dotnet/sdk:10.0", "|compose build minio-init", "|compose pull checkin-renderer")
        for action in required:
            matches = [i for i, line in enumerate(events) if action in line]
            self.assertEqual(1, len(matches), action)
            self.assertLess(matches[0], stop)
        provision = next(i for i, line in enumerate(events) if "|compose run --rm --no-deps minio-init" in line)
        self.assertGreater(provision, stop)
        self.assertEqual(self.original.replace(b"IMAGE_TAG=dev", ("IMAGE_TAG=" + TAG).encode()), (self.deploy / ".env").read_bytes())
        self.assertTrue(all(line.startswith("tag=" + TAG + "|") for line in events))
        # A later manual update has no override and must continue using the persisted immutable tag.
        self.environment.pop("DEPLOY_IMAGE_TAG")
        (self.deploy / "docker.log").unlink()
        result = self.run_script("update.sh")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertTrue(all(line.startswith("tag=" + TAG + "|") for line in self.events()))

    def test_download_failure_never_stops_writers_or_provisions_storage_or_persists_tag(self):
        for failure in ("app", "sdk"):
            with self.subTest(failure=failure):
                path = self.deploy / "docker.log"
                if path.exists(): path.unlink()
                result = self.run_script("ci-deploy.sh", PHOTO_FAIL=failure)
                self.assertNotEqual(0, result.returncode)
                self.assertFalse(any("|compose stop " in line or "|compose run " in line or "|compose up " in line for line in self.events()))
                self.assertEqual(self.original, (self.deploy / ".env").read_bytes())
                self.assertNotIn(PRIVATE, result.stdout + result.stderr)

    def test_migration_dependencies_and_serial_builds_precede_stop_and_apply_reuses_local_sdk_and_caches(self):
        result = self.run_script("ci-deploy.sh")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        events = self.events()
        stop = next(i for i, line in enumerate(events) if "|compose stop " in line)
        tool = [i for i, line in enumerate(events) if "|dotnet tool restore" in line]
        restores = [i for i, line in enumerate(events) if "|dotnet restore " in line]
        builds = [i for i, line in enumerate(events) if "|dotnet build " in line]
        migrations = [i for i, line in enumerate(events) if "|dotnet ef database update " in line]
        self.assertEqual(1, len(tool))
        self.assertEqual(4, len(restores))
        self.assertEqual(4, len(builds))
        self.assertEqual(4, len(migrations))
        self.assertTrue(all(i < stop for i in tool + restores + builds))
        self.assertTrue(all(i > stop and "--no-build" in events[i] for i in migrations))
        self.assertTrue(all("--no-restore -m:1" in events[i] for i in builds))
        self.assertTrue(all("--disable-parallel" in events[i] for i in restores))
        for startup in ("Account", "Shops", "Moderation", "Media"):
            restore = next(i for i in restores if "CoffeePeek." + startup + "Service/" in events[i])
            build = next(i for i in builds if "CoffeePeek." + startup + "Service/" in events[i])
            self.assertLess(restore, build)
        sdk_runs = [line for line in events if "|run " in line and "migrations.sh" in line]
        self.assertEqual(2, len(sdk_runs))
        for line in sdk_runs:
            self.assertIn("--pull=never", line)
            self.assertIn("coffeepeek_migration_nuget:/root/.nuget", line)
            self.assertIn("coffeepeek_migration_dotnet:/root/.dotnet", line)
            self.assertIn("sha256:" + "0" * 63 + "1 bash", line)
        self.assertNotIn("-e POSTGRES_", sdk_runs[0])
        self.assertNotIn("--network coffeepeek_default", sdk_runs[0])
        self.assertIn("--network coffeepeek_default", sdk_runs[1])
        self.assertNotIn(PRIVATE, result.stdout + result.stderr + "\n".join(events))

    def test_migration_preparation_failure_never_stops_writers_applies_migrations_or_persists_tag(self):
        for failure in ("tool_restore", "package_restore", "migration_build"):
            with self.subTest(failure=failure):
                path = self.deploy / "docker.log"
                if path.exists(): path.unlink()
                result = self.run_script("ci-deploy.sh", PHOTO_FAIL=failure)
                self.assertNotEqual(0, result.returncode)
                self.assertFalse(any("|compose stop " in line or "|compose up " in line or
                                     "apply-migrations.sh" in line or "|dotnet ef " in line for line in self.events()))
                self.assertEqual(self.original, (self.deploy / ".env").read_bytes())
                self.assertNotIn(PRIVATE, result.stdout + result.stderr)

    def test_no_registry_credentials_builds_applications_before_stop(self):
        result = self.run_script("ci-deploy.sh", DOCKER_HUB_USERNAME="", DOCKER_HUB_PASSWORD="")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        events = self.events()
        build = next(i for i, line in enumerate(events) if "|compose build account shops moderation media gateway" in line)
        stop = next(i for i, line in enumerate(events) if "|compose stop " in line)
        self.assertLess(build, stop)
        self.assertFalse(any("|compose pull account " in line or "|login " in line for line in events))

    def test_invalid_override_or_ambiguous_env_fails_before_any_docker_operation(self):
        result = self.run_script("ci-deploy.sh", DEPLOY_IMAGE_TAG="dev; " + PRIVATE)
        self.assertNotEqual(0, result.returncode); self.assertEqual([], self.events())
        self.assertNotIn(PRIVATE, result.stdout + result.stderr)
        path = self.deploy / ".env"
        original = path.read_bytes() + b"export IMAGE_TAG=other\n"; path.write_bytes(original)
        result = self.run_script("ci-deploy.sh")
        self.assertNotEqual(0, result.returncode); self.assertEqual([], self.events())
        self.assertEqual(original, path.read_bytes())

    def test_manual_update_without_override_preserves_existing_configuration_and_tag(self):
        self.environment.pop("DEPLOY_IMAGE_TAG")
        result = self.run_script("update.sh")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual(self.original, (self.deploy / ".env").read_bytes())
        self.assertTrue(all(line.startswith("tag=dev|") for line in self.events()))

    def test_actual_ci_ssh_snippet_transports_quoted_credentials_via_stdin_without_argv_exposure(self):
        workflow = (ROOT / ".github" / "workflows" / "ci-cd.yml").read_text(encoding="utf-8")
        snippet = textwrap.dedent(workflow.split("      - name: Deploy on VPS\n", 1)[1].split("        run: |\n", 1)[1])
        # Run the actual workflow shell, replacing only the VPS absolute filesystem location.
        snippet = snippet.replace("cd /opt/coffeepeek/deploy", 'cd "$PHOTO_TEST_DEPLOY"')
        script = self.root / "workflow-ssh.sh"; script.write_text(snippet, encoding="utf-8", newline="\n")
        self.write_bin("ssh", '''#!/usr/bin/env bash
set -euo pipefail
printf '%s\\n' "$*" > "$PHOTO_TEST_SSH_LOG"
exec bash -c "${!#}"
''')
        environment = self.environment | {"VPS_USER": "test", "VPS_HOST": "unused.invalid", "SSH_OPTS": "",
            "DOCKER_HUB_PASSWORD": PRIVATE + "\nsecond line", "PHOTO_TEST_DEPLOY": self.deploy.as_posix(),
            "PHOTO_TEST_SSH_LOG": (self.root / "ssh.log").as_posix()}
        result = subprocess.run([BASH, script.as_posix()], cwd=self.root, env=environment,
                                capture_output=True, text=True, timeout=30)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertNotIn(PRIVATE, (self.root / "ssh.log").read_text() + result.stdout + result.stderr)
        self.assertTrue(all(line.startswith("tag=" + TAG + "|") for line in self.events()))


if __name__ == "__main__":
    unittest.main()
