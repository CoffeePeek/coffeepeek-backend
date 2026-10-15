# Secrets and the 2026-10-15 exposure review

The owner confirmed on 2026-10-15 that the exposed Google OAuth client secret,
Yandex Geocoder key and Resend token were replaced. Other credentials identified
in the review belong to retired infrastructure, as confirmed by the owner.
This closes the credential triage; do not reopen the same historical findings
without evidence that a value is being used again. Replacement was reported by
the owner; the scanner does not contact providers to validate credentials.

Old values remain visible in Git history. Rewriting published history is not
required for this prevention setup and was not performed.

## Store replacements outside Git

Production credentials belong in `deploy/.env` on the server or the deployment's
secret store. Use `GOOGLE_CLIENT_SECRET`, `YANDEX_GEOCODER_API_KEY` and
`RESEND_API_TOKEN`; Docker Compose maps them to the relevant service options.
Local credentials belong in .NET user secrets or ignored development configuration.
Do not paste replacements into issues, documentation, tests, CI logs or chat.

`.env` files and private key files are ignored. `deploy/.env.example` stays tracked
and must contain placeholders. `dev/dev.env` is a deliberate exception containing
public local-development defaults; never replace its defaults with server credentials.

## Checks before commit, push and deployment

From the repository root, with Python 3.12 or later and Git installed:

```text
python dev/security/scan_secrets.py install --hooks
python dev/security/scan_secrets.py scan
```

The installer downloads Gitleaks 8.30.1 from its official release and verifies its
pinned SHA-256. The executable stays in ignored `.tools/gitleaks/`. Hooks are enabled
for this checkout using `core.hooksPath=.githooks`; repeat installation after cloning.
Existing hooks are preserved: the installer stops if integration is needed.

The pre-commit hook scans staged additions, and the pre-push hook scans all locally
reachable history. Missing scanner or scanner failure blocks the operation. Findings
are fully redacted. Inline `gitleaks:allow` comments cannot disable these checks.

GitHub Actions checks every push and pull request using a complete checkout. CI/CD
calls the reusable scan for `main`/`dev`, and image publishing requires it to pass.
Other branches run the same workflow directly. Local hooks stop a normal push before
uploading; CI is a second check after GitHub receives the commit. GitHub's native
Secret Scanning and Push Protection provide additional provider-specific protection.

`.gitleaks.toml` extends the standard rules and adds explicit detection for Google
OAuth client secrets, Resend tokens and UUID-shaped geocoder key assignments.
Provider rules also apply to tests and `.env.example`; those paths are not excluded.

## Historical baseline

`.gitleaksignore` contains the exact fingerprints of findings reviewed on 2026-10-15,
including retired credentials and artificial examples. Each exception identifies one
commit, file, rule and line. It does not allow a provider, file or whole commit.
Reintroducing the same credential in a new commit is detected again.

Do not regenerate the baseline to make a failed check green. Resolve a new finding
by removing the credential from the proposed changes and, if it was published,
rotating it. Review genuine false positives individually and keep exceptions narrow.

To verify the detector with synthetic credentials:

```text
python -m unittest discover -s dev/security -p "test_*.py" -v
```
