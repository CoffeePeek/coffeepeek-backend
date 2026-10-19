#!/usr/bin/env python3
"""Fixed-host private photo pilot. PHOTO_OPERATION selects one explicit operation; no automatic retries.

Run on the reviewed VPS checkout only. Credentials and database observations stay in the private
state directory. No user-supplied account, path, SQL, service or feature-flag arguments are accepted.
"""
import contextlib
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile
import time

import photo_production_canary as canary

ROOT = Path("/opt/coffeepeek")
PRIVATE = Path("/var/lib/coffeepeek/photo-backfill")
OPERATIONS = {"canary-prepare", "canary-activate", "canary-create", "canary-verify", "canary-cleanup"}
APPS = ("account", "shops", "moderation", "media", "gateway")
SHA = re.compile(r"[0-9a-f]{40}\Z")
DIGEST = re.compile(r"[0-9a-f]{64}\Z")
EMAIL = re.compile(r"photo_canary_[0-9a-f]{16}@photo-canary\.invalid\Z")
MEDIA_TABLES = ("PhotoVariantJobs", "PhotoVariants", "PhotoVariantArtifacts", "CheckInPhotoTombstones", "PhotoBackfillRuns")
MIGRATIONS = {"cpmediadb": ("20261018123301_AddPrivatePhotoVariants", "20261018132514_AddPhotoBackfillRuns"),
              "cpshopsdb": ("20261018130514_AddCheckInPhotoVariantProjection",)}
ASSIGNMENT = re.compile(rb"^[ \t]*(?:export[ \t]+)?([A-Za-z_][A-Za-z0-9_]*)[ \t]*=")
STAGES = {"validation", "private_state", "registration", "release", "migrations", "confirmation",
          "image_tag", "environment", "restart", "health", "runtime_flags", "creation", "old_photo", "verification", "cleanup"}


OBSOLETE = {"CHECKIN_VARIANTS_CACHE_ENABLED", "CheckInPhotoDeliveryOptions__CachePublicVariants"}


def pilot_flags(user_id):
    return {"CHECKIN_VARIANTS_ENABLED": "true", "CHECKIN_VARIANTS_READ_ENABLED": "true",
            "CHECKIN_VARIANTS_SERVE_ENABLED": "true", "CHECKIN_VARIANTS_CANARY_ONLY": "true",
            "CHECKIN_VARIANTS_CANARY_USER_IDS": canary.identity(user_id),
            "CHECKIN_VARIANTS_ADVERTISE_ENABLED": "true",
            "CHECKIN_VARIANTS_CLEANUP_ENABLED": "false", "CHECKIN_VARIANTS_CLEANUP_DRY_RUN": "true"}


def assignment_lines(original, targets):
    """Find fixed assignments outside quoted/continued values. Fail on ambiguity."""
    canary.require(isinstance(original, bytes) and len(original) <= 256 * 1024 and b"\0" not in original)
    lines, found = original.splitlines(keepends=True), {}
    quote, continued = None, False
    for index, line in enumerate(lines):
        match = ASSIGNMENT.match(line) if quote is None and not continued else None
        key = match[1].decode("ascii") if match else None
        targeted = key in targets
        if targeted:
            canary.require(key not in found)
            found[key] = index
        continued = False
        position = 0
        while position < len(line):
            char = line[position:position + 1]
            if quote == b"'":
                if char == b"'":
                    quote = None
            elif char == b"\\":
                continued = quote is None and line[position + 1:] in (b"\n", b"\r\n")
                position += 1
            elif quote == b"ansi":
                if char == b"'":
                    quote = None
            elif char == b"`" or line[position:position + 2] == b"$(":
                canary.require(False)
            elif quote == b'"':
                if char == b'"':
                    quote = None
            elif char == b"#" and (position == 0 or line[position - 1:position].isspace()):
                break
            elif line[position:position + 2] == b"$'":
                quote = b"ansi"
                position += 1
            elif char in (b"'", b'"'):
                quote = char
            position += 1
        if targeted:
            canary.require(quote is None and not continued)
    canary.require(quote is None and not continued)
    return lines, found


def environment_replacement(original, user_id):
    """Replace only the fixed pilot assignments, preserving other configuration bytes."""
    values = pilot_flags(user_id)
    lines, found = assignment_lines(original, set(values) | OBSOLETE)
    newline = b"\r\n" if b"\r\n" in original else b"\n"
    for key in OBSOLETE:
        if key in found:
            lines[found[key]] = b""
    for key, value in values.items():
        assignment = key.encode() + b"=" + value.encode("ascii")
        if key in found:
            index = found[key]
            line = lines[index]
            ending = b"\r\n" if line.endswith(b"\r\n") else b"\n" if line.endswith(b"\n") else b""
            prefix = ASSIGNMENT.match(line)[0]
            lines[index] = prefix + value.encode("ascii") + ending
        else:
            if lines and not lines[-1].endswith(b"\n"):
                lines.append(newline)
            lines.append(assignment + newline)
    return b"".join(lines)


def persist_environment(user_id):
    path = ROOT / "deploy" / ".env"
    canary.require(not path.is_symlink())
    with path.open("rb") as source:
        metadata = os.fstat(source.fileno())
        canary.require(stat.S_ISREG(metadata.st_mode))
        original = source.read(256 * 1024 + 1)
    updated = environment_replacement(original, user_id)
    descriptor, name = tempfile.mkstemp(prefix=".photo-pilot-env-", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as target:
            target.write(updated)
            target.flush()
            os.fsync(target.fileno())
        os.chmod(name, stat.S_IMODE(metadata.st_mode))
        if hasattr(os, "chown"):
            os.chown(name, metadata.st_uid, metadata.st_gid)
        canary.require(not path.is_symlink() and path.read_bytes() == original)
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


@contextlib.contextmanager
def private_lock():
    PRIVATE.mkdir(parents=True, exist_ok=True, mode=0o700)
    canary.require(PRIVATE.resolve() == PRIVATE.absolute() and not PRIVATE.is_symlink())
    metadata = PRIVATE.stat()
    canary.require(os.name != "nt" and stat.S_ISDIR(metadata.st_mode)
                   and metadata.st_mode & 0o077 == 0 and metadata.st_uid == os.geteuid())
    import fcntl
    descriptor = os.open(PRIVATE / ".activation.lock", os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    try:
        canary.require(stat.S_ISREG(os.fstat(descriptor).st_mode))
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield
    finally:
        os.close(descriptor)


class Commands:
    def __init__(self, environment):
        self.environment = dict(environment)

    def run(self, args, stdin=None, environment=None, timeout=25):
        try:
            result = subprocess.run(args, input=stdin, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                    text=True, cwd=ROOT, env=environment or self.environment,
                                    timeout=timeout, check=False)
            canary.require(result.returncode == 0 and len(result.stdout) <= canary.MAX_JSON)
            return result.stdout.strip()
        except (OSError, ValueError, subprocess.SubprocessError):
            raise canary.CanaryError("Pilot command failed; inspect private state and service health.") from None

    def compose(self, *args):
        return self.run(["docker", "compose", "--project-directory", str(ROOT / "deploy"),
                         "--project-name", "coffeepeek", "-f", str(ROOT / "deploy" / "docker-compose.yml"), *args])


class Activation:
    def __init__(self, environment, commands=None, client=None):
        self.environment = dict(environment)
        self.commands = commands or Commands(environment)
        self.client = client or canary.Client()
        self.state_path = PRIVATE / "canary.json"
        self.confirmation_path = PRIVATE / "confirmation.json"
        self.old_path = PRIVATE / "old-photo.json"
        self.stage = "private_state"

    def state(self):
        state = canary.private_read(self.state_path)
        canary.validate_state(state)
        canary.require(EMAIL.fullmatch(state["email"]) and state.get("registered") is True)
        return state

    def expected_sha(self):
        value = self.environment.get("EXPECTED_DEPLOY_SHA", "")
        canary.require(SHA.fullmatch(value))
        return value

    def container(self, service):
        canary.require(service in (*APPS, "postgres"))
        value = self.commands.compose("ps", "-a", "-q", service)
        canary.require(re.fullmatch(r"[0-9a-f]{64}", value))
        return value

    def running_release(self):
        expected = self.expected_sha()
        for service in APPS:
            raw = json.loads(self.commands.run(["docker", "inspect", "--format",
                '{"running":{{json .State.Running}},"image":{{json .Image}}}', self.container(service)]))
            canary.require(raw.get("running") is True and re.fullmatch(r"sha256:[0-9a-f]{64}", raw.get("image", "")))
            revision = self.commands.run(["docker", "image", "inspect", "--format",
                '{{index .Config.Labels "org.opencontainers.image.revision"}}', raw["image"]])
            canary.require(revision == expected)

    def query(self, database, sql):
        canary.require(database in ("cpaccountdb", "cpshopsdb", "cpmediadb"))
        canary.require(self.environment.get("POSTGRES_USER") and self.environment.get("POSTGRES_PASSWORD"))
        environment = dict(self.commands.environment)
        environment.update(PGUSER=self.environment["POSTGRES_USER"], PGPASSWORD=self.environment["POSTGRES_PASSWORD"])
        script = ("BEGIN READ ONLY;\nSET LOCAL statement_timeout = '5s';\nSET LOCAL lock_timeout = '2s';\n"
                  "SET LOCAL idle_in_transaction_session_timeout = '10s';\n" + sql + "\nROLLBACK;\n")
        raw = self.commands.run(["docker", "exec", "-i", "-e", "PGUSER", "-e", "PGPASSWORD", self.container("postgres"),
            "psql", "-X", "-q", "-A", "-t", "-v", "ON_ERROR_STOP=1", "-d", database, "-f", "-"],
            stdin=script, environment=environment, timeout=15)
        result = json.loads(raw)
        canary.require(isinstance(result, dict))
        return result

    def migrations(self):
        for database, migrations in MIGRATIONS.items():
            tables = MEDIA_TABLES if database == "cpmediadb" else ("CheckInPhotoVariantManifests",)
            table_checks = " AND ".join("to_regclass('public.\"" + table + "\"') IS NOT NULL" for table in tables)
            if database == "cpmediadb":
                table_checks += ''' AND (SELECT count(*) FROM information_schema.columns WHERE table_schema='public'
                  AND table_name='PhotoVariantJobs' AND column_name IN ('Priority','BackfillRunId'))=2'''
            names = ",".join("'" + name + "'" for name in migrations)
            sql = f'''SELECT json_build_object('ready', ({table_checks}) AND
              (SELECT count(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" IN ({names}))={len(migrations)});
            '''
            canary.require(self.query(database, sql).get("ready") is True)

    def image_tag(self):
        path = ROOT / "deploy" / ".env"
        canary.require(not path.is_symlink() and path.stat().st_size <= 256 * 1024)
        lines, found = assignment_lines(path.read_bytes(), {"IMAGE_TAG"})
        canary.require("IMAGE_TAG" in found)
        line = lines[found["IMAGE_TAG"]]
        raw = line[ASSIGNMENT.match(line).end():].strip()
        match = re.fullmatch(rb'''(sha-[0-9a-f]{40}|'sha-[0-9a-f]{40}'|"sha-[0-9a-f]{40}")[ \t]*(?:\#.*)?''', raw)
        canary.require(match)
        tag = match[1].strip(b"'\"").decode("ascii")
        expected = "sha-" + self.expected_sha()
        canary.require(tag == expected and self.environment.get("IMAGE_TAG", expected) == expected)
        self.commands.environment["IMAGE_TAG"] = expected

    def confirm_observation(self, state):
        # The generated reserved-domain email is validated before it can enter SQL. Never list Users.
        email = state["email"]
        canary.require(EMAIL.fullmatch(email))
        observed = self.query("cpaccountdb", f'''SELECT COALESCE((SELECT json_build_object(
          'email', "Email", 'userId', "Id", 'confirmationToken', "EmailConfirmationToken")
          FROM "Users" WHERE "Email"='{email}' AND "Username"='{state["userName"]}'
            AND "EmailConfirmed"=FALSE AND "EmailConfirmationExpiresAt">now()
          LIMIT 1), '{{}}'::json);''')
        canary.require(observed.get("email") == email and isinstance(observed.get("confirmationToken"), str)
                       and 0 < len(observed["confirmationToken"]) <= 1024)
        user_id = canary.identity(observed["userId"])
        canary.require(not state.get("userId") or canary.identity(state["userId"]) == user_id)
        observed["userId"] = user_id
        canary.private_write(self.confirmation_path, observed)
        state["userId"] = user_id
        return state

    def healthy(self, service):
        for attempt in range(40):
            status = self.commands.run(["docker", "inspect", "--format",
                '{{if .State.Running}}{{if .State.Health}}{{.State.Health.Status}}{{else}}missing{{end}}{{else}}stopped{{end}}',
                self.container(service)])
            if status == "healthy":
                return
            canary.require(status in ("starting", "unhealthy"))
            if attempt < 39:
                time.sleep(3)
        canary.require(False)

    def activated(self):
        state = self.state()
        canary.require(state.get("activation", {}).get("deploySha") == self.expected_sha()
                       and state["activation"].get("healthy") is True)
        self.stage = "release"
        self.running_release()
        self.stage = "runtime_flags"
        # Check the actual recreated hosts, rather than the operator shell's possibly stale env.
        expected = pilot_flags(state["userId"])
        mapping = {"media": {"PhotoVariantOptions__Enabled": expected["CHECKIN_VARIANTS_ENABLED"],
                            "PhotoVariantOptions__ReadEnabled": "true", "PhotoVariantOptions__CleanupEnabled": "false",
                            "PhotoVariantOptions__CleanupDryRun": "true"},
                   "shops": {"CheckInPhotoDeliveryOptions__ServeVariants": "true",
                             "CheckInPhotoDeliveryOptions__AdvertiseVariants": "true",
                             "CheckInPhotoDeliveryOptions__AdvertiseCanaryOnly": "true",
                             "CheckInPhotoDeliveryOptions__AdvertiseCanaryUserIds": state["userId"]}}
        for service, values in mapping.items():
            conditions = " ".join('(eq (index (split . "=") 0) "' + key + '")' for key in values)
            template = '{{range .Config.Env}}{{if or ' + conditions + '}}{{println .}}{{end}}{{end}}'
            environment = self.commands.run(["docker", "inspect", "--format", template, self.container(service)]).splitlines()
            for key, value in values.items():
                entries = [entry.split("=", 1)[1] for entry in environment if entry.startswith(key + "=")]
                canary.require(entries == [value])
        return state

    def activate(self):
        state = self.state()
        canary.require(not state.get("creationAttempted"))
        self.stage = "release"
        self.running_release()
        self.stage = "migrations"
        self.migrations()
        self.stage = "image_tag"
        self.image_tag()
        self.stage = "confirmation"
        state = self.confirm_observation(state)
        state["activation"] = {"deploySha": self.expected_sha(), "healthy": False,
                               "beforeUtc": state.get("activation", {}).get("beforeUtc")
                               or dt.datetime.now(dt.timezone.utc).isoformat()}
        canary.private_write(self.state_path, state)
        self.stage = "environment"
        persist_environment(state["userId"])
        self.commands.environment.update(pilot_flags(state["userId"]))
        for key in OBSOLETE:
            self.commands.environment.pop(key, None)
        self.stage = "restart"
        self.commands.compose("up", "-d", "--no-deps", "shops", "media")
        self.stage = "health"
        for service in ("shops", "media"):
            self.healthy(service)
        self.running_release()
        state["activation"]["healthy"] = True
        canary.private_write(self.state_path, state)
        self.activated()
        return {"runningAppsVerified": 5, "migratedDatabasesVerified": 2, "canaryUsers": 1, "restartedServices": 2}

    def old_photo(self, state):
        user_id = canary.identity(state["userId"])
        before = dt.datetime.fromisoformat(state["activation"]["beforeUtc"])
        canary.require(before.tzinfo is not None)
        before_sql = before.astimezone(dt.timezone.utc).isoformat()
        old = self.query("cpshopsdb", f'''SELECT COALESCE((SELECT json_build_object(
          'public', TRUE, 'checkInId', c."Id", 'photoId', p."Id", 'ownerId', c."UserId",
          'storageKey', p."StorageKey", 'sourceVersion', m."SourceVersion", 'recipeVersion', m."RecipeVersion",
          'variants', m."VariantsJson")
          FROM "CanonicalCheckIns" c JOIN "CheckInPhotos" p ON p."CanonicalCheckInId"=c."Id" AND p."OwnerId"=c."UserId"
          JOIN "CheckInPhotoVariantManifests" m ON m."PhotoId"=p."Id" AND m."CheckInId"=c."Id"
          WHERE c."DeletedAtUtc" IS NULL AND c."Visibility"=1 AND c."ModerationState"=2
            AND c."ApprovedRevision"=c."ContentRevision" AND c."UserId"<>'{user_id}'::uuid
            AND c."CreatedAtUtc"<'{before_sql}'::timestamptz AND m."RecipeVersion"=1
            AND (c."Kind"=1 OR (c."Kind"=0 AND EXISTS(SELECT 1 FROM "Shops" s WHERE s."Id"=c."ShopId" AND NOT s."IsHidden")))
            AND NOT EXISTS(SELECT 1 FROM "CheckInPhotoVariantManifests" n WHERE n."PhotoId"=m."PhotoId"
              AND n."SourceVersion"=m."SourceVersion" AND n."RecipeVersion">m."RecipeVersion")
          ORDER BY c."CreatedAtUtc", c."Id", p."Id" LIMIT 1), '{{}}'::json);''')
        canary.require(old.get("public") is True and old.get("recipeVersion") == 1)
        photo_id, check_in, owner = (canary.identity(old[key]) for key in ("photoId", "checkInId", "ownerId"))
        key, source = old["storageKey"], old["sourceVersion"]
        canary.require(re.fullmatch("check-ins/" + photo_id.replace("-", "") + r"\.[A-Za-z0-9]{1,11}", key)
                       and DIGEST.fullmatch(source)
                       and source == hashlib.sha256(("checkin:" + photo_id.replace("-", "") + ":" + key).encode()).hexdigest())
        ready = self.query("cpmediadb", f'''SELECT COALESCE((SELECT json_build_object('ready', TRUE,
          'originalSha256', j."SourceDigest", 'variants', (SELECT json_agg(json_build_object(
            'Purpose', v."Purpose", 'Format', v."Format", 'ContentType', v."ContentType", 'Width', v."Width",
            'Height', v."Height", 'SizeBytes', v."SizeBytes", 'Digest', v."Digest")) FROM "PhotoVariants" v WHERE v."JobId"=j."Id"))
          FROM "Photos" p JOIN "PhotoVariantJobs" j ON j."PhotoId"=p."Id"
          WHERE p."Id"='{photo_id}'::uuid AND p."OwnerId"='{owner}'::uuid AND p."CheckInId"='{check_in}'::uuid
            AND p."StorageKey"='{key}' AND p."Status"='Confirmed' AND p."BucketType"='CheckIn' AND p."ShopAttachmentId" IS NULL
            AND j."CheckInId"=p."CheckInId" AND j."OwnerId"=p."OwnerId" AND j."SourceKey"=p."StorageKey"
            AND j."Status"='Ready' AND j."SourceVersion"='{source}' AND j."RecipeVersion"=1
            AND NOT EXISTS(SELECT 1 FROM "CheckInPhotoTombstones" t WHERE t."CheckInId"=p."CheckInId")
          LIMIT 1), '{{}}'::json);''')
        canary.require(ready.get("ready") is True and DIGEST.fullmatch(ready.get("originalSha256", "")))
        def normalized(rows):
            canary.require(isinstance(rows, list) and len(rows) == 6)
            result = {}
            for raw in rows:
                row = {name[0].lower() + name[1:]: value for name, value in raw.items()}
                purpose, fmt = row.get("purpose"), row.get("format")
                canary.require(purpose in canary.PURPOSES and fmt in ("webp", "jpeg", "png")
                               and row.get("contentType") == "image/" + fmt and (purpose, fmt) not in result
                               and DIGEST.fullmatch(row.get("digest", ""))
                               and all(isinstance(row.get(field), int) and not isinstance(row[field], bool) and row[field] > 0
                                       for field in ("width", "height", "sizeBytes"))
                               and row["width"] <= canary.PURPOSES[purpose] and row["height"] <= canary.PURPOSES[purpose]
                               and row["sizeBytes"] <= 8 * 1024 * 1024)
                result[purpose, fmt] = row
            canary.require(all((purpose, "webp") in result and sum(p == purpose and f in ("jpeg", "png") for p, f in result) == 1
                               for purpose in canary.PURPOSES))
            return result
        community, media = normalized(old["variants"]), normalized(ready["variants"])
        canary.require(community == media)
        observation = {"public": True, "checkInId": check_in, "photoId": photo_id, "sourceVersion": source,
                       "recipeVersion": 1, "variants": list(community.values()), "originalSha256": ready["originalSha256"]}
        canary.private_write(self.old_path, observation)
        return observation

    def verify(self):
        state = self.activated()
        canary.require(state.get("confirmed") and not state.get("deleted"))
        self.stage = "old_photo"
        old = self.old_photo(state)
        self.stage = "verification"
        result = canary.verify(self.state_path, self.old_path, self.client, recipe=1)
        state = self.state()
        token = state["accessToken"]
        home = self.client.api("GET", "/api/v1/check-ins/home/" + canary.identity(state["homeId"]), token)
        canary.validate_home(state, home)
        for index, photo in enumerate((home["photos"][0], home["drinkPhoto"])):
            base = canary.base_path(state["homeId"], photo["id"])
            source = hashlib.sha256(("checkin:" + canary.identity(photo["id"]).replace("-", "") + ":" + photo["storageKey"]).encode()).hexdigest()
            variants = photo.get("variants", [])
            canary.require(len(variants) == 6 and photo["url"] == base)
            expected = {(purpose, fmt) for purpose in canary.PURPOSES for fmt in ("webp", "png" if index else "jpeg")}
            canary.require({(variant["purpose"], variant["format"]) for variant in variants} == expected)
            for variant in variants:
                purpose, fmt = variant["purpose"], variant["format"]
                canary.require(variant["sourceVersion"] == source and variant["recipeVersion"] == 1
                               and variant["contentType"] == "image/" + fmt and variant["sizeBytes"] > 0
                               and 0 < variant["width"] <= canary.PURPOSES[purpose]
                               and 0 < variant["height"] <= canary.PURPOSES[purpose]
                               and variant["url"] == base + "/variants/" + source + "/1/" + purpose + "." + fmt)
        public = self.client.api("GET", "/api/v1/check-ins/" + old["checkInId"])
        old_base = canary.base_path(old["checkInId"], old["photoId"])
        canary.require((any(photo.get("id") == old["photoId"] and photo.get("url") == old_base for photo in public["photos"])
                        or public.get("drinkPhotoUrl") == old_base)
                       and all(not photo.get("variants") for photo in public["photos"]) and not public.get("drinkPhotoVariants"))
        public_canary = self.client.api("GET", "/api/v1/check-ins/" + old["checkInId"], token)
        selected = [photo for photo in public_canary["photos"] if photo.get("id") == old["photoId"]]
        canary.require(len(selected) <= 1)
        if selected:
            canary.require(selected[0].get("url") == old_base)
            variants = selected[0].get("variants")
        else:
            canary.require(public_canary.get("drinkPhotoUrl") == old_base)
            variants = public_canary.get("drinkPhotoVariants")
        canary.require(isinstance(variants, list) and len(variants) == 6)
        expected = {(variant["purpose"], variant["format"]): variant for variant in old["variants"]}
        seen = set()
        for variant in variants:
            canary.require(isinstance(variant, dict))
            pair = variant.get("purpose"), variant.get("format")
            canary.require(pair in expected and pair not in seen)
            seen.add(pair)
            purpose, fmt = pair
            canary.require(variant.get("sourceVersion") == old["sourceVersion"]
                           and type(variant.get("recipeVersion")) is int and variant["recipeVersion"] == old["recipeVersion"]
                           and all(variant.get(field) == expected[pair][field] for field in ("contentType", "width", "height", "sizeBytes"))
                           and all(type(variant.get(field)) is int for field in ("width", "height", "sizeBytes"))
                           and variant.get("url") == old_base + "/variants/" + old["sourceVersion"] + "/"
                               + str(old["recipeVersion"]) + "/" + purpose + "." + fmt)
        result["descriptorGateVerified"] = True
        result["publicCanaryDescriptorGateVerified"] = True
        state["verification"] = result
        canary.private_write(self.state_path, state)
        return result

    def run(self, operation):
        canary.require(operation in OPERATIONS)
        if operation == "canary-prepare":
            self.stage = "registration"
            return canary.prepare(self.state_path, self.client)
        if operation == "canary-activate":
            return self.activate()
        if operation == "canary-create":
            self.activated()
            self.stage = "creation"
            return canary.confirm_create(self.state_path, self.confirmation_path, self.client)
        if operation == "canary-verify":
            return self.verify()
        self.stage = "cleanup"
        return canary.cleanup(self.state_path, self.client)


def main(argv=None, environment=None, commands=None, client=None):
    operator = None
    operation = None
    try:
        canary.require(not (sys.argv[1:] if argv is None else argv))
        environment = dict(os.environ if environment is None else environment)
        operation = environment.get("PHOTO_OPERATION", "")
        canary.require(operation in OPERATIONS)
        with private_lock():
            operator = Activation(environment, commands, client)
            result = operator.run(operation)
        print(json.dumps({"operation": operation, "success": True, "result": result}, allow_nan=False))
        return 0
    except Exception:
        stage = operator.stage if operator and operator.stage in STAGES else "validation"
        print(json.dumps({"operation": operation if operation in OPERATIONS else None, "success": False,
                          "stage": stage, "mutationRetried": False}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
