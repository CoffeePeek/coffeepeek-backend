#!/usr/bin/env python3
"""Controlled pre-deployment storage observation and private backups; stdlib only.

Run as `python3 - storage RUN_ID` or `python3 - backup RUN_ID` when delivered over stdin.
PHOTO_OPERATION/GITHUB_RUN_ID are fallbacks. Credentials come only from the sourced environment.
No bucket policy changes, database writes, service stops, restore or automatic rollback are performed.
S3 signing follows https://docs.aws.amazon.com/AmazonS3/latest/API/sig-v4-header-based-auth.html.
"""
import datetime as dt
import hashlib
import hmac
import ipaddress
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import threading
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET

ROOT = Path("/opt/coffeepeek")
OUTPUT = Path("/var/lib/coffeepeek/photo-backfill/backups")
BUCKETS = ("coffeepeek.shops", "coffeepeek.checkins", "coffeepeek.checkin-variants")
DATABASES = ("cpaccountdb", "cpshopsdb", "cpmoderationdb", "cpmediadb")
APPLICATIONS = ("account", "shops", "moderation", "media", "gateway")
FILES = ("deploy/.env", "deploy/docker-compose.yml", "deploy/Caddyfile", "deploy/imgproxy/checkin-renderer.conf",
         "deploy/minio/provision.sh", "deploy/minio/checkin-reader-policy.json",
         "deploy/minio/checkin-variant-reader-policy.json", "deploy/minio/checkin-variant-writer-policy.json")
IDENTIFIER = re.compile(r"[0-9a-f]{64}\Z")
IMAGE_ID = re.compile(r"sha256:[0-9a-f]{64}\Z")
RUN_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9_-]{0,63}\Z")
PRIVATE_V4 = tuple(ipaddress.ip_network(value) for value in ("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"))
INSPECT = ('{"id":{{json .Id}},"name":{{json .Name}},"imageId":{{json .Image}},"running":{{json .State.Running}},'
           '"project":{{json (index .Config.Labels "com.docker.compose.project")}},'
           '"service":{{json (index .Config.Labels "com.docker.compose.service")}},'
           '"networks":{{json .NetworkSettings.Networks}}}')
NETWORK = '{"id":{{json .Id}},"driver":{{json .Driver}},"scope":{{json .Scope}},"config":{{json .IPAM.Config}}}'
DIAGNOSTIC_STAGES = frozenset(("initialization", "arguments", "output", "storage_container", "network", "policy_1",
    "policy_2", "policy_3", "application_containers", "postgres_container", "config", "tags", "summary", "complete",
    *("pg_dump_" + database for database in DATABASES), *("pg_restore_" + database for database in DATABASES)))
STORAGE_ERROR_CODES = frozenset(("AccessDenied", "SignatureDoesNotMatch", "RequestTimeTooSkewed", "InvalidAccessKeyId"))
DATABASE_ERROR_REASONS = frozenset(("auth", "permission", "read_only_tx", "pg_dump_failed", "restore_toc_failed"))
MAX_DATABASE_STDERR_BYTES = 64 * 1024
_diagnostic = {}


class GuardError(Exception):
    pass


def require(condition):
    if not condition:
        raise GuardError("guard_unavailable")


def diagnostic_stage(stage):
    require(stage in DIAGNOSTIC_STAGES)
    _diagnostic.clear()
    _diagnostic["diagnosticStage"] = stage


def diagnostic_http(status, code):
    if type(status) is int and 300 <= status <= 599:
        _diagnostic["httpStatus"] = status
    if isinstance(code, str) and code in STORAGE_ERROR_CODES:
        _diagnostic["storageErrorCode"] = code


def safe_diagnostic():
    # Never serialize exception attributes, arbitrary XML fields or the whole diagnostic mapping.
    stage = _diagnostic.get("diagnosticStage")
    result = {"diagnosticStage": stage if isinstance(stage, str) and stage in DIAGNOSTIC_STAGES else "unknown"}
    status, code = _diagnostic.get("httpStatus"), _diagnostic.get("storageErrorCode")
    if type(status) is int and 300 <= status <= 599:
        result["httpStatus"] = status
    if isinstance(code, str) and code in STORAGE_ERROR_CODES:
        result["storageErrorCode"] = code
    reason = _diagnostic.get("reason")
    if isinstance(stage, str) and stage in {"pg_dump_" + db for db in DATABASES} | {"pg_restore_" + db for db in DATABASES} \
            and isinstance(reason, str) and reason in DATABASE_ERROR_REASONS:
        result["reason"] = reason
    return result


def storage_error_code(body):
    if len(body) > 4096 or b"<!DOCTYPE" in body.upper() or b"<!ENTITY" in body.upper():
        return None
    try:
        root = ET.fromstring(body)
        if root.tag.rsplit("}", 1)[-1] != "Error":
            return None
        codes = [node.text for node in root if node.tag.rsplit("}", 1)[-1] == "Code"]
        return codes[0] if len(codes) == 1 else None
    except (ValueError, ET.ParseError):
        return None


class Commands:
    def __init__(self, environment):
        self.environment = dict(environment)

    def run(self, args, timeout=30):
        try:
            result = subprocess.run(args, cwd=ROOT, env=self.environment, stdout=subprocess.PIPE,
                                    stderr=subprocess.DEVNULL, text=True, timeout=timeout, check=False)
            require(result.returncode == 0 and len(result.stdout) <= 1024 * 1024)
            return result.stdout.strip()
        except (OSError, ValueError, subprocess.SubprocessError):
            raise GuardError("command_unavailable") from None

    def dump(self, container, database, destination):
        require(IDENTIFIER.fullmatch(container) and database in DATABASES)
        diagnostic_stage("pg_dump_" + database)
        environment = dict(self.environment)
        require(environment.get("POSTGRES_USER") and environment.get("POSTGRES_PASSWORD"))
        environment.update(PGUSER=environment["POSTGRES_USER"], PGPASSWORD=environment["POSTGRES_PASSWORD"],
                           PGOPTIONS="-c default_transaction_read_only=on -c lock_timeout=5000")
        args = ["docker", "exec", "-e", "PGUSER", "-e", "PGPASSWORD", "-e", "PGOPTIONS", container,
                "pg_dump", "--format=custom", "--no-password", "--dbname=" + database]

        def failure_reason(stderr):
            lowered = stderr.lower()
            if any(marker in lowered for marker in (b"read-only transaction", b"read only transaction")):
                return "read_only_tx"
            if any(marker in lowered for marker in (b"password authentication failed", b"authentication failed",
                    b"no password supplied", b"no pg_hba.conf entry", b"pg_hba.conf rejects", b"fe_sendauth")):
                return "auth"
            if any(marker in lowered for marker in (b"permission denied", b"insufficient privilege",
                    b"must be owner", b"must be superuser")):
                return "permission"
            return "restore_toc_failed" if _diagnostic.get("diagnosticStage") == "pg_restore_" + database else "pg_dump_failed"

        def run_database(command, stderr_path, timeout, **streams):
            # Drain the entire pipe to avoid blocking pg_dump, but retain only a fixed private prefix.
            # Do not use communicate(): an unexpected unbounded diagnostic must not fill memory.
            captured = bytearray()
            read_failed = []
            try:
                with private_open(stderr_path) as errors:
                    with subprocess.Popen(command, cwd=ROOT, env=environment, stderr=subprocess.PIPE, **streams) as process:
                        def drain():
                            try:
                                while chunk := process.stderr.read(8192):
                                    remaining = MAX_DATABASE_STDERR_BYTES - len(captured)
                                    if remaining > 0:
                                        prefix = chunk[:remaining]
                                        captured.extend(prefix)
                                        errors.write(prefix)
                            except (OSError, ValueError):
                                read_failed.append(True)
                        reader = threading.Thread(target=drain, daemon=True)
                        reader.start()
                        try:
                            status = process.wait(timeout=timeout)
                        except subprocess.TimeoutExpired:
                            process.kill()
                            process.wait(timeout=10)
                            raise
                        finally:
                            reader.join(timeout=10)
                        require(not reader.is_alive() and not read_failed)
                        errors.flush()
                        os.fsync(errors.fileno())
                        if status != 0:
                            _diagnostic["reason"] = failure_reason(captured)
                        require(status == 0)
            except (OSError, ValueError, subprocess.SubprocessError, GuardError):
                _diagnostic["reason"] = failure_reason(captured)
                raise GuardError("database_backup_unavailable") from None

        try:
            with private_open(destination) as target:
                run_database(args, Path(str(destination) + ".pg_dump.stderr"), 600, stdout=target)
                target.flush()
                os.fsync(target.fileno())
            # pg_restore inherits the OS file descriptor, not Python's buffered logical cursor.
            # The signature probe must rewind the actual descriptor before handing it to stdin.
            with destination.open("rb", buffering=0) as source:
                require(source.read(5) == b"PGDMP")
                source.seek(0)
                diagnostic_stage("pg_restore_" + database)
                run_database(["docker", "exec", "-i", container, "pg_restore", "--list"],
                             Path(str(destination) + ".pg_restore.stderr"), 120, stdin=source, stdout=subprocess.DEVNULL)
        except (OSError, ValueError, subprocess.SubprocessError, GuardError):
            if _diagnostic.get("reason") not in DATABASE_ERROR_REASONS:
                _diagnostic["reason"] = failure_reason(b"")
            raise GuardError("database_backup_unavailable") from None


def no_symlinks(path):
    require(not any(parent.is_symlink() for parent in (path, *path.parents)))


def private_open(path):
    path = Path(path)
    no_symlinks(path)
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    return os.fdopen(descriptor, "wb")


def write_bytes(path, data):
    with private_open(path) as target:
        target.write(data)
        target.flush()
        os.fsync(target.fileno())


def write_json(path, value):
    write_bytes(path, (json.dumps(value, indent=2, allow_nan=False) + "\n").encode())


def file_digest(path):
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while chunk := source.read(1024 * 1024):
            digest.update(chunk)
    return {"sha256": digest.hexdigest(), "sizeBytes": path.stat().st_size}


def container(commands, service):
    require(service in (*APPLICATIONS, "minio", "postgres"))
    found = commands.run(["docker", "ps", "--all", "--quiet", "--no-trunc", "--filter", "label=com.docker.compose.project=coffeepeek",
                          "--filter", "label=com.docker.compose.service=" + service]).splitlines()
    require(len(found) <= 1 and all(IDENTIFIER.fullmatch(value) for value in found))
    target = found[0] if found else "coffeepeek-" + service + "-1"
    value = json.loads(commands.run(["docker", "inspect", "--format", INSPECT, target]))
    require(value.get("running") is True and IDENTIFIER.fullmatch(value.get("id", ""))
            and IMAGE_ID.fullmatch(value.get("imageId", "")))
    labelled = value.get("project") == "coffeepeek" and value.get("service") == service
    exact_fallback = not found and value.get("name") == "/coffeepeek-" + service + "-1"
    require(labelled or exact_fallback)
    return value


def storage_ip(commands, observed):
    addresses = []
    for network in observed.get("networks", {}).values():
        try:
            ip = ipaddress.IPv4Address(network.get("IPAddress", ""))
            network_id = network.get("NetworkID", "")
            if not IDENTIFIER.fullmatch(network_id) or not any(ip in subnet for subnet in PRIVATE_V4):
                continue
            data = json.loads(commands.run(["docker", "network", "inspect", "--format", NETWORK, network_id]))
            if data.get("id") != network_id or data.get("driver") != "bridge" or data.get("scope") != "local":
                continue
            if any(ip in ipaddress.ip_network(config.get("Subnet", ""), strict=False)
                   for config in data.get("config", []) if ":" not in config.get("Subnet", "")):
                addresses.append(str(ip))
        except (ValueError, TypeError, KeyError):
            continue
    require(addresses)
    return sorted(set(addresses))[0]


def signed_headers(ip, bucket, access, secret, now):
    require(bucket in BUCKETS and access and secret)
    address = ipaddress.IPv4Address(ip)
    require(any(address in network for network in PRIVATE_V4))
    host = str(address) + ":9000"
    timestamp, date = now.strftime("%Y%m%dT%H%M%SZ"), now.strftime("%Y%m%d")
    empty = hashlib.sha256(b"").hexdigest()
    signed = "host;x-amz-content-sha256;x-amz-date"
    canonical = "GET\n/" + bucket + "\npolicy=\nhost:" + host + "\nx-amz-content-sha256:" + empty + "\nx-amz-date:" + timestamp + "\n\n" + signed + "\n" + empty
    scope = date + "/us-east-1/s3/aws4_request"
    string_to_sign = "AWS4-HMAC-SHA256\n" + timestamp + "\n" + scope + "\n" + hashlib.sha256(canonical.encode()).hexdigest()
    key = ("AWS4" + secret).encode()
    for part in (date, "us-east-1", "s3", "aws4_request"):
        key = hmac.new(key, part.encode(), hashlib.sha256).digest()
    signature = hmac.new(key, string_to_sign.encode(), hashlib.sha256).hexdigest()
    return {"Host": host, "x-amz-date": timestamp, "x-amz-content-sha256": empty,
            "Authorization": "AWS4-HMAC-SHA256 Credential=" + access + "/" + scope + ",SignedHeaders=" + signed + ",Signature=" + signature}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Policies:
    def __init__(self, ip, environment):
        self.ip, self.environment = ip, environment
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def read(self, bucket):
        require(bucket in BUCKETS)
        headers = signed_headers(self.ip, bucket, self.environment.get("MINIO_ROOT_USER"),
                                 self.environment.get("MINIO_ROOT_PASSWORD"), dt.datetime.now(dt.timezone.utc))
        request = urllib.request.Request("http://" + self.ip + ":9000/" + bucket + "?policy=", method="GET", headers=headers)
        try:
            with self.opener.open(request, timeout=10) as response:
                require(response.status == 200)
                body = response.read(1024 * 1024 + 1)
                require(len(body) <= 1024 * 1024)
                return body, policy_view(json.loads(body), body)
        except urllib.error.HTTPError as error:
            try:
                diagnostic_http(error.code, None)
                body = error.read(4097)
                code = storage_error_code(body)
                diagnostic_http(error.code, code)
                require(error.code == 404 and code in ("NoSuchBucketPolicy", "NoSuchBucket"))
                status = "missing" if code == "NoSuchBucketPolicy" else "missing_bucket"
                saved = json.dumps({"status": status, "policy": None}).encode()
                return saved, {"status": status, "sha256": hashlib.sha256(saved).hexdigest(), "statementCount": 0,
                               "anonymousAllow": False, "anonymousDenyCount": 0, "checkInPrivateMarker": False,
                               "private": status == "missing"}
            finally:
                error.close()
        except (OSError, ValueError, urllib.error.URLError):
            raise GuardError("storage_policy_unknown") from None


def policy_view(policy, body):
    require(isinstance(policy, dict) and isinstance(policy.get("Statement"), (list, dict)))
    statements = policy["Statement"] if isinstance(policy["Statement"], list) else [policy["Statement"]]
    anonymous, anonymous_denies = False, 0
    for statement in statements:
        require(isinstance(statement, dict) and statement.get("Effect") in ("Allow", "Deny"))
        if statement["Effect"] == "Deny":
            principal = statement.get("Principal")
            aws = principal.get("AWS") if isinstance(principal, dict) else None
            # Count explicit wildcard-principal statements, including conditional denies; this is not coverage proof.
            anonymous_denies += int(principal == "*" or aws == "*" or isinstance(aws, list) and "*" in aws)
            continue
        require("NotPrincipal" not in statement and "NotAction" not in statement)
        actions = statement.get("Action")
        require(isinstance(actions, str) or isinstance(actions, list) and actions and all(isinstance(item, str) for item in actions))
        principal = statement.get("Principal")
        if principal == "*":
            anonymous = True
        elif isinstance(principal, dict) and principal and set(principal).issubset({"AWS", "Service", "CanonicalUser"}):
            values = [value for item in principal.values() for value in (item if isinstance(item, list) else [item])]
            require(all(isinstance(value, str) for value in values))
            anonymous |= "*" in values
        else:
            require(False)
    # Conditions/denies may further restrict an Allow; treating it as potentially anonymous fails closed.
    return {"status": "anonymous_allow" if anonymous else "private", "sha256": hashlib.sha256(body).hexdigest(),
            "statementCount": len(statements), "anonymousAllow": anonymous, "anonymousDenyCount": anonymous_denies,
            "checkInPrivateMarker": b"CheckInPrivate" in body, "private": not anonymous}


def inspect_storage(commands, environment, destination, policies=None):
    diagnostic_stage("storage_container")
    observed = container(commands, "minio")
    diagnostic_stage("network")
    ip = storage_ip(commands, observed)
    policies = policies or Policies(ip, environment)
    result = {}
    for index, bucket in enumerate(BUCKETS, 1):
        diagnostic_stage("policy_" + str(index))
        body, summary = policies.read(bucket)
        write_bytes(destination / (bucket + ".policy.json"), body)
        result[bucket] = summary
    return {"policies": result, "privateBucketsReady": all(result[bucket]["private"] for bucket in BUCKETS[1:])}


def copy_configuration(destination):
    records = {}
    for relative in FILES:
        source = ROOT / relative
        no_symlinks(source)
        if not source.exists():
            require(relative not in ("deploy/.env", "deploy/docker-compose.yml"))
            records[relative] = {"missing": True}
            continue
        require(source.is_file())
        target = destination / (relative.replace("/", "_") + ".backup")
        with source.open("rb") as original, private_open(target) as copied:
            require(stat.S_ISREG(os.fstat(original.fileno()).st_mode))
            while chunk := original.read(1024 * 1024):
                copied.write(chunk)
            copied.flush()
            os.fsync(copied.fileno())
        records[relative] = file_digest(target)
    return records


def backup(commands, environment, destination, run_id, policies=None):
    result = inspect_storage(commands, environment, destination, policies)
    diagnostic_stage("application_containers")
    applications = {service: container(commands, service) for service in APPLICATIONS}
    diagnostic_stage("postgres_container")
    postgres = container(commands, "postgres")
    result["databases"] = {}
    for database in DATABASES:
        diagnostic_stage("pg_dump_" + database)
        archive = destination / (database + ".dump")
        commands.dump(postgres["id"], database, archive)
        result["databases"][database] = dict(file_digest(archive), validatedToc=True)
    diagnostic_stage("config")
    configuration = copy_configuration(destination)
    result["configurationFiles"] = sum(not value.get("missing") for value in configuration.values())
    # A concurrent deployment invalidates the rollback baseline instead of silently mixing generations.
    diagnostic_stage("tags")
    for service in APPLICATIONS:
        current = container(commands, service)
        require(current["id"] == applications[service]["id"] and current["imageId"] == applications[service]["imageId"])
    rollback = {}
    for service, observed in applications.items():
        tag = "coffeepeek-rollback/" + service + ":photo-" + run_id
        existing = commands.run(["docker", "image", "ls", "--quiet", "--no-trunc", tag]).splitlines()
        require(len(existing) <= 1 and (not existing or existing[0] == observed["imageId"]))
        if not existing:
            commands.run(["docker", "image", "tag", observed["imageId"], tag])
        verified = commands.run(["docker", "image", "inspect", "--format", "{{.Id}}", tag])
        require(verified == observed["imageId"])
        rollback[service] = {"imageId": observed["imageId"], "tag": tag}
    write_json(destination / "rollback-manifest.json", {"complete": True, "createdAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "databaseConsistency": "individual-database-snapshots", "databaseBackups": result["databases"],
        "configuration": configuration, "applications": rollback, "storage": result,
        "automaticRestore": False, "automaticSchemaDown": False})
    result["rollbackImages"] = rollback
    return result


def execute(operation, run_id, environment=None, commands=None, policies=None, now=None):
    diagnostic_stage("arguments")
    require(operation in ("storage", "inspect-storage", "backup") and isinstance(run_id, str) and RUN_ID.fullmatch(run_id))
    environment = dict(os.environ if environment is None else environment)
    diagnostic_stage("output")
    no_symlinks(OUTPUT)
    OUTPUT.mkdir(parents=True, exist_ok=True, mode=0o700)
    os.chmod(OUTPUT, 0o700)
    now = now or dt.datetime.now(dt.timezone.utc)
    name = now.strftime("%Y%m%dT%H%M%S%fZ") + "-" + run_id
    destination = OUTPUT / name
    destination.mkdir(mode=0o700)
    commands = commands or Commands(environment)
    result = backup(commands, environment, destination, run_id, policies) if operation == "backup" \
        else inspect_storage(commands, environment, destination, policies)
    diagnostic_stage("summary")
    write_json(destination / "guard-summary.json", result)
    diagnostic_stage("complete")
    return {"operation": operation, "backupId": name, "complete": True, **result}


def main(argv=None):
    import sys
    try:
        diagnostic_stage("initialization")
        args = sys.argv[1:] if argv is None else argv
        require(len(args) in (0, 2))
        operation, run_id = args if args else (os.environ.get("PHOTO_OPERATION"), os.environ.get("GITHUB_RUN_ID"))
        print(json.dumps(execute(operation, run_id), sort_keys=True))
        return 0
    except Exception:
        print(json.dumps({"complete": False, "status": "unknown", "reason": "guard_unavailable_no_automatic_retry", **safe_diagnostic()}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
