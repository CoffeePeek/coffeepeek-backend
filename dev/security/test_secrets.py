"""Exercise the real scanner against synthetic credentials in disposable Git repos."""

from __future__ import annotations

import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import uuid

import scan_secrets as scanner


class SecretScanningTests(unittest.TestCase):
    def setUp(self) -> None:
        if not scanner.BINARY.is_file():
            self.fail("Install Gitleaks first: python dev/security/scan_secrets.py install")
        self.sandbox = tempfile.TemporaryDirectory(prefix="scanner-test-", dir=scanner.ROOT / ".tools")
        self.addCleanup(self.sandbox.cleanup)
        self.repo = Path(self.sandbox.name)
        self.git("init", "-q")
        self.git("config", "user.name", "Secret scanner test")
        self.git("config", "user.email", "scanner@example.invalid")
        self.git("config", "core.hooksPath", ".git/hooks")
        shutil.copyfile(scanner.ROOT / ".gitleaks.toml", self.repo / ".gitleaks.toml")
        (self.repo / ".gitleaksignore").write_text("# Empty test baseline\n", encoding="utf-8")
        self.commit()

    def git(self, *args: str) -> None:
        subprocess.run(["git", *args], cwd=self.repo, check=True, capture_output=True)

    def write(self, path: str, content: str) -> None:
        target = self.repo / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")

    def commit(self) -> None:
        self.git("add", "--all")
        self.git("commit", "-qm", "Synthetic scanner fixture")

    def scan(self, *, staged: bool = True, report: bool = False) -> subprocess.CompletedProcess:
        args = [str(scanner.BINARY), "git", str(self.repo),
                "--config", str(self.repo / ".gitleaks.toml"),
                "--gitleaks-ignore-path", str(self.repo / ".gitleaksignore"),
                "--no-banner", "--redact=100", "--ignore-gitleaks-allow"]
        args += ["--staged"] if staged else ["--log-opts=--all"]
        if report:
            args += ["--report-format", "json", "--report-path", str(self.repo / ".git" / "report.json")]
        return subprocess.run(args, cwd=self.repo, capture_output=True, text=True, check=False)

    def test_each_rotated_provider_is_detected_even_in_tests_and_examples(self) -> None:
        cases = [
            ("tests/google.json", "ClientSecret", "GOCSPX" + "-" + "aB9_xY2z" * 4),
            ("deploy/.env.example", "RESEND_API_TOKEN", "re" + "_" + "aB9xY2zM" * 4),
            ("dev/local.env", "YANDEX_GEOCODER_API_KEY", str(uuid.uuid4())),
            ("appsettings.json", "ApiKey", str(uuid.uuid4())),
        ]
        for path, key, value in cases:
            with self.subTest(path=path):
                self.git("reset", "-q")
                self.write(path, json.dumps({key: value}) if path.endswith("json") else f"{key}={value}\n")
                self.git("add", "--", path)
                result = self.scan()
                self.assertEqual(result.returncode, 1, result.stderr)
                self.assertNotIn(value, result.stdout + result.stderr)

    def test_staged_secret_is_detected_after_working_file_is_cleaned(self) -> None:
        value = "GOCSPX" + "-" + "bC8_zY2x" * 4
        self.write("probe.json", json.dumps({"ClientSecret": value}))
        self.git("add", "--", "probe.json")
        self.write("probe.json", json.dumps({"ClientSecret": ""}))
        self.assertEqual(self.scan().returncode, 1)

    def test_inline_allow_comment_cannot_bypass_provider_rule(self) -> None:
        value = "re" + "_" + "cD7yZ3xN" * 4
        self.write("probe.env", f"RESEND_API_TOKEN={value} # gitleaks:allow\n")
        self.git("add", "--", "probe.env")
        self.assertEqual(self.scan().returncode, 1)

    def test_historical_fingerprint_does_not_allow_reintroduction(self) -> None:
        value = str(uuid.uuid4())
        self.write("probe.env", f"YANDEX_GEOCODER_API_KEY={value}\n")
        self.commit()
        self.assertEqual(self.scan(staged=False, report=True).returncode, 1)
        findings = json.loads((self.repo / ".git" / "report.json").read_text(encoding="utf-8"))
        (self.repo / ".gitleaksignore").write_text(
            "\n".join(item["Fingerprint"] for item in findings) + "\n", encoding="utf-8"
        )
        self.assertEqual(self.scan(staged=False).returncode, 0)
        (self.repo / "probe.env").unlink()
        self.commit()
        self.write("probe.env", f"YANDEX_GEOCODER_API_KEY={value}\n")
        self.commit()
        self.assertEqual(self.scan(staged=False).returncode, 1)

    def test_empty_credentials_and_public_client_id_pass(self) -> None:
        self.write("appsettings.json", json.dumps({
            "ClientId": "123456789-public-client.apps.googleusercontent.com",
            "ClientSecret": "", "ApiKey": "", "ApiToken": "",
        }))
        self.git("add", "--all")
        self.assertEqual(self.scan().returncode, 0)


if __name__ == "__main__":
    unittest.main()
