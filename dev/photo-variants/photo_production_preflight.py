#!/usr/bin/env python3
"""Read-only VPS photo release observations. Stdlib only; emits sanitized JSON, never secrets."""
import datetime as dt
import json
import os
import pathlib
import re
import shutil
import subprocess
import urllib.error
import urllib.parse
import urllib.request

SERVICES = ("postgres", "redis", "rabbitmq", "minio", "minio-init", "checkin-renderer",
            "account", "shops", "moderation", "media", "gateway", "caddy")
DATABASES = ("cpaccountdb", "cpshopsdb", "cpmoderationdb", "cpmediadb")
TABLES = ("__EFMigrationsHistory", "Users", "CanonicalCheckIns", "CheckInPhotos", "ShopPhotos", "Shops",
          "CheckInPhotoVariantManifests", "Photos", "PhotoVariantJobs", "PhotoVariants",
          "PhotoVariantArtifacts", "CheckInPhotoTombstones", "PhotoBackfillRuns")
COLUMNS = ("Id", "StorageKey", "OwnerId", "CheckInId", "BucketType", "Status", "ShopAttachmentId", "Priority",
           "UploadedAt", "SourceVersion", "RecipeVersion", "CanonicalCheckInId", "UserId", "Kind", "CreatedAtUtc",
           "DeletedAtUtc", "Visibility", "ModerationState", "ApprovedRevision", "ContentRevision", "ShopId", "IsHidden")
SECRETS = ("POSTGRES_USER", "POSTGRES_PASSWORD", "GATEWAY_AUTH_SECRET", "JWT_SECRET_KEY", "MINIO_ROOT_USER",
           "MINIO_ROOT_PASSWORD", "MINIO_CHECKIN_READ_USER", "MINIO_CHECKIN_READ_PASSWORD",
           "MINIO_CHECKIN_VARIANT_USER", "MINIO_CHECKIN_VARIANT_PASSWORD", "MINIO_CHECKIN_VARIANT_READ_USER",
           "MINIO_CHECKIN_VARIANT_READ_PASSWORD", "CHECKIN_RENDERER_KEY", "CHECKIN_RENDERER_SALT")
FLAGS = {"CHECKIN_VARIANTS_ENABLED": False, "CHECKIN_VARIANTS_READ_ENABLED": False,
         "CHECKIN_VARIANTS_SERVE_ENABLED": False, "CHECKIN_VARIANTS_ADVERTISE_ENABLED": False,
         "CHECKIN_VARIANTS_CACHE_ENABLED": False, "CHECKIN_VARIANTS_CLEANUP_ENABLED": False,
         "CHECKIN_VARIANTS_CLEANUP_DRY_RUN": True}
HEX = re.compile(r"(?:sha256:)?[0-9a-f]{12,64}\Z")
IMAGE = re.compile(r"[a-zA-Z0-9][a-zA-Z0-9./_:@-]{0,255}\Z")
VERSION = re.compile(r"v?\d+(?:\.\d+){1,3}(?:[-+][A-Za-z0-9._-]+)?\Z")
MIGRATION = re.compile(r"[0-9]{14}_[A-Za-z0-9_]{1,180}\Z")
STATES = {"created", "restarting", "running", "removing", "paused", "exited", "dead"}
JOB_STATES = {"Pending", "Processing", "Ready", "RetryScheduled", "Failed", "Cancelled", "Other"}
INSPECT = ('{"id":{{json .Id}},"imageId":{{json .Image}},"image":{{json .Config.Image}},'
           '"status":{{json .State.Status}},"running":{{json .State.Running}},'
           '"health":{{if .State.Health}}{{json .State.Health.Status}}{{else}}null{{end}},'
           '"ports":{{json .NetworkSettings.Ports}},"networkCount":{{len .NetworkSettings.Networks}}}')


class ObservationUnavailable(Exception):
    """Fixed diagnostic only. Do not attach raw command or HTTP exceptions."""


class Commands:
    def __init__(self, repo_root, environment):
        self.root = pathlib.Path(repo_root)
        self.environment = dict(environment)

    def run(self, args, stdin=None, environment=None, timeout=25):
        try:
            result = subprocess.run(args, input=stdin, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                    text=True, cwd=self.root, env=environment or self.environment,
                                    timeout=timeout, check=False)
        except (OSError, subprocess.SubprocessError, ValueError):
            raise ObservationUnavailable("command_unavailable") from None
        if result.returncode != 0 or len(result.stdout) > 4 * 1024 * 1024:
            raise ObservationUnavailable("command_unavailable")
        return result.stdout.strip()

    def compose(self, *args):
        return self.run(["docker", "compose", "--project-directory", str(self.root / "deploy"),
                         "-f", str(self.root / "deploy" / "docker-compose.yml"), *args])


def number(value):
    return value if isinstance(value, int) and not isinstance(value, bool) and 0 <= value <= 2**63 - 1 else None


def version(value):
    return value if isinstance(value, str) and VERSION.fullmatch(value) else "unrecognized"


def flags(environment):
    result = {}
    for name, default in FLAGS.items():
        raw = environment.get(name)
        if raw is None or raw == "":
            result[name] = {"configured": False, "composeDefault": default}
        elif raw.lower() in ("true", "false"):
            result[name] = {"configured": True, "value": raw.lower() == "true"}
        else:
            result[name] = {"configured": True, "invalid": True}
    return result


def container_view(raw):
    if not isinstance(raw, dict):
        raise ObservationUnavailable("container_unavailable")
    result = {"status": raw.get("status") if raw.get("status") in STATES else "unknown",
              "running": raw.get("running") is True,
              "health": raw.get("health") if raw.get("health") in ("healthy", "unhealthy", "starting") else "unknown",
              "networkCount": number(raw.get("networkCount")), "publishedPorts": []}
    for field, regex in (("id", HEX), ("imageId", HEX), ("image", IMAGE)):
        result[field] = raw.get(field) if isinstance(raw.get(field), str) and regex.fullmatch(raw[field]) else "unrecognized"
    for target, bindings in (raw.get("ports") or {}).items():
        match = re.fullmatch(r"([0-9]{1,5})/(tcp|udp)", target)
        if not match or not 1 <= int(match[1]) <= 65535 or not isinstance(bindings, list):
            continue
        for binding in bindings[:8]:
            if not isinstance(binding, dict):
                continue
            port = str(binding.get("HostPort", ""))
            if not port.isascii() or not port.isdigit() or not 1 <= int(port) <= 65535:
                continue
            result["publishedPorts"].append({"containerPort": int(match[1]), "protocol": match[2],
                "hostPort": int(port), "loopbackOnly": binding.get("HostIp") in ("127.0.0.1", "::1")})
    return result


def observe_containers(commands):
    result, ids = {}, {}
    for service in SERVICES:
        try:
            matches = commands.compose("ps", "--all", "--quiet", service).splitlines()
            matches = [item for item in matches if HEX.fullmatch(item)]
            if not matches:
                result[service] = {"state": "absent"}
                continue
            # Report one instance explicitly; multiple replicas require additional review.
            container_id = matches[0]
            view = container_view(json.loads(commands.run(["docker", "inspect", "--format", INSPECT, container_id])))
            view["replicaCount"] = len(matches)
            if HEX.fullmatch(view["imageId"]):
                try:
                    digests = json.loads(commands.run(["docker", "image", "inspect", "--format", "{{json .RepoDigests}}", view["imageId"]]))
                    view["repoDigests"] = [item for item in (digests or []) if isinstance(item, str)
                        and IMAGE.fullmatch(item) and re.search(r"@sha256:[0-9a-f]{64}\Z", item)][:8]
                except (ObservationUnavailable, ValueError, TypeError):
                    view["repoDigests"] = []
            result[service], ids[service] = view, container_id
        except (ObservationUnavailable, ValueError, TypeError, AttributeError):
            result[service] = {"state": "unavailable"}
    return result, ids


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def health(url):
    try:
        parsed = urllib.parse.urlsplit(url)
        if parsed.scheme not in ("http", "https") or not parsed.hostname or parsed.username or parsed.password \
                or parsed.query or parsed.fragment or parsed.path not in ("/health", "/health/gateway"):
            return {"state": "invalid_target"}
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        with opener.open(urllib.request.Request(url, method="GET", headers={"Accept": "application/json"}), timeout=5) as response:
            return {"state": "reachable", "status": response.status, "healthy": 200 <= response.status < 300}
    except urllib.error.HTTPError as error:
        return {"state": "reachable", "status": error.code, "healthy": False}
    except (OSError, ValueError, urllib.error.URLError):
        return {"state": "unavailable"}


def observe_health(containers, environment, probe=health):
    result = {"gatewayLoopback": {"state": "no_published_tcp_port"}, "publicGateway": {"state": "not_configured"}}
    ports = containers.get("gateway", {}).get("publishedPorts", [])
    tcp = [item for item in ports if item["protocol"] == "tcp"]
    # Health paths are fixed; only the observed gateway TCP binding supplies a port.
    for item in sorted(tcp, key=lambda p: not p["loopbackOnly"])[:2]:
        target = f'http://127.0.0.1:{item["hostPort"]}'
        observed = {"hostPort": item["hostPort"], "self": probe(target + "/health/gateway"), "dependencies": probe(target + "/health")}
        result["gatewayLoopback"] = observed
        if observed["self"].get("healthy"):
            break
    public = environment.get("PREFLIGHT_PUBLIC_HEALTH_URL")
    domain = environment.get("DOMAIN", "")
    if not public and re.fullmatch(r"[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?", domain) and "." in domain:
        public = "https://" + domain + "/health/gateway"
    if public:
        result["publicGateway"] = probe(public)
    return result


def sql_array(values):
    # Only module-owned constant identifiers enter these lists.
    return "ARRAY[" + ",".join("'" + value + "'" for value in values) + "]"


def schema_sql():
    return f'''SELECT json_build_object('readOnly', current_setting('transaction_read_only') = 'on',
      'serverVersion', current_setting('server_version_num')::integer,
      'databaseSizeBytes', pg_database_size(current_database()),
      'tables', (SELECT json_object_agg(name, to_regclass(format('public.%I', name)) IS NOT NULL)
        FROM unnest({sql_array(TABLES)}) AS names(name)),
      'columns', COALESCE((SELECT json_object_agg(table_name, names) FROM
        (SELECT table_name, json_agg(column_name) AS names FROM information_schema.columns
         WHERE table_schema = 'public' AND table_name = ANY({sql_array(TABLES)})
         AND column_name = ANY({sql_array(COLUMNS)}) GROUP BY table_name) AS cols), '{{}}'::json));'''


def pg_query(commands, container, database, sql, environment):
    if database not in DATABASES or not HEX.fullmatch(container):
        raise ObservationUnavailable("database_unavailable")
    if not environment.get("POSTGRES_USER") or not environment.get("POSTGRES_PASSWORD"):
        raise ObservationUnavailable("database_credentials_missing")
    safe_env = dict(environment)
    safe_env["PGPASSWORD"] = environment["POSTGRES_PASSWORD"]
    # -e NAME passes a value from the parent environment, not a secret in argv.
    args = ["docker", "exec", "-i", "-e", "PGPASSWORD", container, "psql", "-X", "-q", "-A", "-t",
            "-v", "ON_ERROR_STOP=1", "-U", environment["POSTGRES_USER"], "-d", database, "-f", "-"]
    script = "BEGIN READ ONLY;\nSET LOCAL statement_timeout = '10s';\nSET LOCAL lock_timeout = '2s';\n" + sql + "\nROLLBACK;\n"
    try:
        value = json.loads(commands.run(args, stdin=script, environment=safe_env, timeout=20))
        if not isinstance(value, dict):
            raise ValueError()
        return value
    except (ObservationUnavailable, ValueError, TypeError):
        raise ObservationUnavailable("database_query_unavailable") from None


def clean_schema(raw):
    tables = raw.get("tables") if isinstance(raw.get("tables"), dict) else {}
    columns = raw.get("columns") if isinstance(raw.get("columns"), dict) else {}
    return {"readOnly": raw.get("readOnly") is True, "serverVersion": number(raw.get("serverVersion")),
            "databaseSizeBytes": number(raw.get("databaseSizeBytes")),
            "tables": {name: tables.get(name) is True for name in TABLES},
            "columns": {name: sorted(set(value for value in columns.get(name, []) if value in COLUMNS))
                        for name in TABLES if isinstance(columns.get(name), list)}}


def has(schema, table, *columns):
    return schema["tables"].get(table) is True and set(columns).issubset(schema["columns"].get(table, []))


def count_queries(database, schema):
    queries = {}
    if database == "cpmediadb":
        if has(schema, "Photos", "Id", "StorageKey", "BucketType", "Status", "CheckInId", "ShopAttachmentId", "UploadedAt"):
            queries["photos"] = '''SELECT json_build_object('total', count(*),
              'confirmedPrivateBound', count(*) FILTER (WHERE "Status"='Confirmed' AND "BucketType"='CheckIn' AND "CheckInId" IS NOT NULL AND "ShopAttachmentId" IS NULL),
              'canonicalPrivateBound', count(*) FILTER (WHERE "Status"='Confirmed' AND "BucketType"='CheckIn' AND "CheckInId" IS NOT NULL AND "ShopAttachmentId" IS NULL
                AND "StorageKey" ~ ('^check-ins/' || replace("Id"::text, '-', '') || '\\.[A-Za-z0-9]{1,11}$')),
              'confirmedShopBoundNeedsTransfer', count(*) FILTER (WHERE "Status"='Confirmed' AND "BucketType"='Shop' AND "CheckInId" IS NOT NULL),
              'recent30Days', count(*) FILTER (WHERE "UploadedAt" >= now() - interval '30 days')) FROM "Photos";'''
        if has(schema, "PhotoVariantJobs", "Status"):
            priority = '''CASE WHEN "Priority"=0 THEN 'Upload' WHEN "Priority"=1 THEN 'Backfill' ELSE 'Other' END''' \
                if has(schema, "PhotoVariantJobs", "Priority") else "'PriorityColumnMissing'::text"
            queries["jobs"] = f'''SELECT json_build_object('counts', COALESCE(json_agg(row_to_json(counts)), '[]'::json))
              FROM (SELECT CASE WHEN "Status" IN ('Pending','Processing','Ready','RetryScheduled','Failed','Cancelled')
              THEN "Status" ELSE 'Other' END AS status, {priority} AS priority, count(*) AS count
              FROM "PhotoVariantJobs" GROUP BY 1,2) AS counts;'''
        for table in ("PhotoVariants", "PhotoVariantArtifacts", "CheckInPhotoTombstones", "PhotoBackfillRuns"):
            if schema["tables"].get(table):
                queries[table] = f'''SELECT json_build_object('total', count(*)) FROM "{table}";'''
    if database == "cpshopsdb":
        if has(schema, "CanonicalCheckIns", "CreatedAtUtc", "DeletedAtUtc", "Kind", "Visibility", "ModerationState", "ApprovedRevision", "ContentRevision"):
            queries["checkIns"] = '''SELECT json_build_object('total', count(*), 'active', count(*) FILTER (WHERE "DeletedAtUtc" IS NULL),
              'home', count(*) FILTER (WHERE "DeletedAtUtc" IS NULL AND "Kind"=1),
              'shop', count(*) FILTER (WHERE "DeletedAtUtc" IS NULL AND "Kind"=0),
              'approvedPublicBusinessState', count(*) FILTER (WHERE "DeletedAtUtc" IS NULL AND "Visibility"=1 AND "ModerationState"=2 AND "ApprovedRevision"="ContentRevision"),
              'recent30Days', count(*) FILTER (WHERE "CreatedAtUtc" >= now() - interval '30 days')) FROM "CanonicalCheckIns";'''
        if has(schema, "CheckInPhotos", "CreatedAtUtc"):
            queries["checkInPhotos"] = '''SELECT json_build_object('total', count(*), 'recent30Days', count(*) FILTER
              (WHERE "CreatedAtUtc" >= now() - interval '30 days')) FROM "CheckInPhotos";'''
        if has(schema, "ShopPhotos", "CanonicalCheckInId"):
            queries["legacyCanonicalShopPhotos"] = '''SELECT json_build_object('total', count(*)) FROM "ShopPhotos" WHERE "CanonicalCheckInId" IS NOT NULL;'''
        if schema["tables"].get("CheckInPhotoVariantManifests"):
            queries["communityManifests"] = '''SELECT json_build_object('total', count(*)) FROM "CheckInPhotoVariantManifests";'''
    return queries


COUNT_KEYS = {"total", "confirmedPrivateBound", "canonicalPrivateBound", "confirmedShopBoundNeedsTransfer", "recent30Days",
              "active", "home", "shop", "approvedPublicBusinessState"}


def clean_counts(raw):
    result = {name: number(value) for name, value in raw.items() if name in COUNT_KEYS and number(value) is not None}
    if isinstance(raw.get("counts"), list):
        result["counts"] = [{"status": item["status"], "priority": item["priority"], "count": item["count"]}
            for item in raw["counts"] if isinstance(item, dict) and item.get("status") in JOB_STATES
            and item.get("priority") in ("Upload", "Backfill", "Other", "PriorityColumnMissing") and number(item.get("count")) is not None][:30]
    return result


def docker_storage(commands):
    try:
        rows = commands.run(["docker", "system", "df", "--format", "{{json .}}"])
        result = []
        for line in rows.splitlines():
            raw = json.loads(line)
            if raw.get("Type") not in ("Images", "Containers", "Local Volumes", "Build Cache"):
                continue
            item = {"type": raw["Type"]}
            for source, target in (("TotalCount", "totalCount"), ("Active", "activeCount")):
                value = str(raw.get(source, ""))
                if re.fullmatch(r"[0-9]{1,15}", value):
                    item[target] = int(value)
            for source, target in (("Size", "size"), ("Reclaimable", "reclaimable")):
                value = raw.get(source)
                if isinstance(value, str) and re.fullmatch(r"[0-9]+(?:\.[0-9]+)?(?:B|kB|KB|MB|GB|TB|KiB|MiB|GiB|TiB)(?: \((?:100|[0-9]{1,2})%\))?", value):
                    item[target] = value
            result.append(item)
        return {"state": "observed", "items": result[:4]}
    except (ObservationUnavailable, ValueError, TypeError, AttributeError):
        return {"state": "unavailable"}


def observe_databases(commands, postgres, environment):
    result = {}
    for database in DATABASES:
        if not postgres:
            result[database] = {"state": "postgres_container_unavailable"}
            continue
        try:
            schema = clean_schema(pg_query(commands, postgres, database, schema_sql(), environment))
            if not schema["readOnly"]:
                raise ObservationUnavailable("readonly_not_confirmed")
            observed = {"state": "observed", "schema": schema, "counts": {}}
            if schema["tables"]["__EFMigrationsHistory"]:
                try:
                    history = pg_query(commands, postgres, database,
                        '''SELECT json_build_object('latest', max("MigrationId"), 'count', count(*)) FROM "__EFMigrationsHistory";''', environment)
                    migration = history.get("latest")
                    observed["migrations"] = {"latest": migration if isinstance(migration, str) and MIGRATION.fullmatch(migration) else "unknown",
                                               "count": number(history.get("count"))}
                except ObservationUnavailable:
                    observed["migrations"] = {"state": "unavailable"}
            else:
                observed["migrations"] = {"state": "table_missing"}
            for name, query in count_queries(database, schema).items():
                try:
                    observed["counts"][name] = clean_counts(pg_query(commands, postgres, database, query, environment))
                except ObservationUnavailable:
                    observed["counts"][name] = {"state": "unavailable"}
            result[database] = observed
        except (ObservationUnavailable, ValueError, TypeError, AttributeError):
            result[database] = {"state": "unavailable"}
    return result


def storage_topology(commands):
    """Observe the already-running storage volume without relying on candidate Compose interpolation."""
    template = ('{"id":{{json .Id}},"imageId":{{json .Image}},'
                '"project":{{json (index .Config.Labels "com.docker.compose.project")}},'
                '"service":{{json (index .Config.Labels "com.docker.compose.service")}},'
                '"running":{{json .State.Running}},"mounts":{{json .Mounts}}}')
    try:
        raw = json.loads(commands.run(["docker", "inspect", "--format", template, "coffeepeek-minio-1"]))
        if raw.get("project") != "coffeepeek" or raw.get("service") != "minio" or raw.get("running") is not True:
            raise ObservationUnavailable("storage_identity_unconfirmed")
        mounts = []
        for entry in raw.get("mounts", []):
            if entry.get("Destination") != "/data":
                continue
            name = entry.get("Name", "")
            if entry.get("Type") != "volume" or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}", name):
                raise ObservationUnavailable("storage_mount_unconfirmed")
            mounts.append({"type": "volume", "name": name, "destination": "/data", "writable": entry.get("RW") is True})
        if len(mounts) != 1 or not mounts[0]["writable"] or not HEX.fullmatch(raw.get("imageId", "")):
            raise ObservationUnavailable("storage_mount_unconfirmed")
        return {"state": "observed", "project": "coffeepeek", "service": "minio",
                "imageId": raw["imageId"], "mounts": mounts}
    except (ObservationUnavailable, ValueError, TypeError, AttributeError):
        return {"state": "unavailable"}


def report(environment=None, commands=None, probe=health):
    environment = dict(os.environ if environment is None else environment)
    root = pathlib.Path(environment.get("REPO_ROOT", "/opt/coffeepeek"))
    commands = commands or Commands(root, environment)
    result = {"readOnly": True, "observedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "secretsPresent": {name: bool(environment.get(name)) for name in SECRETS}, "configuredFlags": flags(environment),
        "bucketPolicies": {"state": "unknown", "reason": "not_read_or_modified"},
        "limitations": ["No object read, existence, format, digest or bucket-policy verification.",
            "Environment flags describe sourced deployment configuration, not inspected container environment.",
            "Database aggregates are separate bounded read-only transactions, not a distributed snapshot.",
            "Canonical private key counts are metadata observations, not verified eligible inventory or Ready coverage.",
            "No EXPLAIN, generation, migration, upload, replacement, delete or policy writes were performed."]}
    try:
        usage = shutil.disk_usage(root)
        result["disk"] = {"totalBytes": usage.total, "usedBytes": usage.used, "freeBytes": usage.free}
    except OSError:
        result["disk"] = {"state": "unavailable"}
    for key, args in (("dockerVersion", ["docker", "version", "--format", "{{.Server.Version}}"]),
                      ("composeVersion", ["docker", "compose", "version", "--short"])):
        try:
            result[key] = version(commands.run(args))
        except ObservationUnavailable:
            result[key] = "unavailable"
    try:
        commands.compose("config", "--quiet")
        result["composeConfig"] = {"valid": True}
    except ObservationUnavailable:
        result["composeConfig"] = {"valid": False, "reason": "validation_unavailable_or_invalid"}
    result["containers"], ids = observe_containers(commands)
    result["dockerStorage"] = docker_storage(commands)
    result["storageTopology"] = storage_topology(commands)
    result["health"] = observe_health(result["containers"], environment, probe)
    result["databases"] = observe_databases(commands, ids.get("postgres"), environment)
    return result


def main():
    try:
        result = report()
    except Exception:
        # Shell wrappers may archive stdout. Never print exception/command text or traceback.
        print(json.dumps({"readOnly": True, "state": "preflight_unavailable"}))
        return 1
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
