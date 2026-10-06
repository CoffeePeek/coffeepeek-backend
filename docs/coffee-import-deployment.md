# Daily coffee import deployment

The Compose stack starts with coffee import disabled. The empty mapping is mounted read-only as
`/app/appsettings.Production.json` in Shops; ASP.NET Core loads `CoffeeImportOptions.Roasters`
through its standard Production JSON provider. An empty `COFFEE_IMPORT_TOKEN` disables the
private import endpoints. Shops and Media always use the same Compose token.

The host collector reaches Shops on `127.0.0.1:5243` and Media on `127.0.0.1:5025`.
These ports are bound only to loopback; Caddy and the public gateway do not expose the private APIs.
Override `COFFEE_IMPORT_SHOPS_PORT` and `COFFEE_IMPORT_MEDIA_PORT` in `deploy/.env` if necessary.

## First installation on the VPS

The checkout must be `/opt/coffeepeek`, with Python 3.10+, `python3-venv`, OpenSSL, Docker Compose,
and systemd installed. `setup-vps.sh` installs the Python/OpenSSL prerequisites on a fresh server.
For an existing Debian/Ubuntu server: `sudo apt-get install -y python3 python3-venv openssl`.

1. Copy `dev/coffee-import/roaster-mapping.example.json` to a working file. Keep only the sources
   being enabled and replace every zero GUID with an existing CoffeePeek roaster GUID.
   Keep the exact source and brand strings. The Sova source requires both `KOF` and `Kanga`.
   A partial rollout is supported: only the explicitly mapped sources will run.

2. Install the runtime and configuration:

   ```bash
   cd /opt/coffeepeek
   sudo bash deploy/scripts/setup-coffee-import.sh --mapping /path/to/reviewed-roaster-mapping.json
   ```

   Setup generates a private shared token if none exists, persists its Compose value in
   `deploy/.env`, and writes `/etc/coffeepeek/coffee-import.env` with mode `0600`.
   The reviewed mapping is copied to `/etc/coffeepeek/roaster-mapping.json` and referenced by
   `COFFEE_IMPORT_MAPPING_FILE` in `deploy/.env`. Mapping GUIDs are not secrets; the mapping is
   readable by the unprivileged Shops container. The Python venv and dependency fingerprint live
   in `/opt/coffeepeek-coffee-import`, outside the checkout. CI's `rsync --delete` preserves these
   paths and `deploy/.env`. Setup installs the units and leaves the timer disabled.

3. Deploy Shops and Media after setup so that they receive the token and mapping. The existing
   deployment applies all EF migrations, including the coffee catalog migration, unless
   `SKIP_MIGRATIONS=1`. Keep migrations enabled for this rollout. Existing MinIO buckets and
   `MEDIA_PUBLIC_URL` / `IMAGE_PROXY_URL` remain the photo storage/public URL configuration.

   ```bash
   cd /opt/coffeepeek/deploy
   sudo bash scripts/ci-deploy.sh
   ```

   Use the normal deployment identity instead of `sudo` when it has the required Docker access.
   The deployment preflight rejects invalid GUID placeholders, missing brand mappings,
   differing job/Compose tokens, mismatched loopback ports, or stale/missing Python dependencies
   before restarting the applications. It checks configuration without printing credentials.
   With import enabled, Shops and Media are recreated even when their image tag is unchanged,
   so a replaced mapping file and cached configuration options are loaded reliably.

4. Run a real single-source smoke test. Choose a mapped source that supplies photographs;
   `roast.by` is suitable. River supplies no photos and cannot verify the MinIO path.

   ```bash
   cd /opt/coffeepeek
   sudo bash deploy/scripts/smoke-coffee-import.sh --source roast.by
   ```

   Smoke verifies that mapped GUIDs exist in PostgreSQL, starts the regular systemd service
   with a transient one-source override, and checks the actual persisted products, prices,
   weights, currencies, availability, coffee/Media photo links, and MinIO image bytes.
   Replaying the exact applied snapshot must return the same run ID and counts.
   Every new product must be `Draft`; existing publication states and protected photos are
   respected. The test imports real source data and does not fabricate or publish products.
   The transient override is removed when the command exits. Failures leave the timer disabled.

5. Review the imported drafts and photographs in the admin UI. New products become public only
   after an administrator publishes them. Then explicitly enable daily imports:

   ```bash
   sudo bash deploy/scripts/enable-coffee-import.sh
   ```

   This command validates the configuration and successful smoke record, then enables the timer.
   It never prompts for an extra approval. Setup, normal deployments, and PR CI never enable it.
   The schedule is daily at **03:00 UTC** (**06:00 Europe/Kyiv while daylight saving time applies**).
   `Persistent=yes` catches up a missed run after downtime; enabling the timer may therefore
   immediately trigger a missed run. The service has a three-hour timeout and systemd runs one
   instance at a time.

## Operations and updates

```bash
# Manual full refresh through the same service; all configured sources are selected.
sudo systemctl start coffeepeek-coffee-import.service

systemctl list-timers coffeepeek-coffee-import.timer
journalctl -u coffeepeek-coffee-import.service -n 100 --no-pager

# Pause imports before updating mapping, token, dependencies, or units.
sudo systemctl disable --now coffeepeek-coffee-import.timer
sudo systemctl stop coffeepeek-coffee-import.service
```

Re-run setup with the reviewed map after changing dependencies or import configuration. Then
deploy/recreate Shops and Media, repeat the single-source smoke, and explicitly re-enable the
timer. A changed token must reach both containers and the job. Do not rotate only the job's token.
The smoke marker is bound to the mapping, job environment and requirements; changing any of
them requires a fresh smoke run before enabling the timer.

For ordinary application updates with unchanged import configuration and dependencies, the
existing runtime survives CI sync. The collector cache, export files, replay history and
`last-run.json` remain under `/var/lib/coffeepeek-coffee-import`; systemd manages the unprivileged
service identity and writable state directory. Avoid direct root-run imports into that directory.

The PR CI runs the existing backend tests, `CoffeePeek.MediaService.Tests`, all Python collector
and deployment validation tests, and shell syntax checks. A separate live-test step uses a
disposable PostgreSQL 17 service for the discovery migration, classifications, concurrent edits,
and search queries. Its fixed credentials are CI fixtures; the password is passed through an
outside-repository `0600` pgpass file and the live-test environment is scoped to that step.
The existing guarded harness creates and cleans up only its owned `cp_live_*` throwaway databases.
The real photo/import VPS smoke remains explicit: it needs actual roaster GUIDs, MinIO and live
source access. Preparing these scripts does not imply that a real VPS smoke has passed.
