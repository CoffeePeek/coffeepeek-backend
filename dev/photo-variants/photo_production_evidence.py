#!/usr/bin/env python3
"""Read-only, bounded VPS pilot evidence. Private per-ID fingerprints; aggregate JSON stdout.

Run without arguments, with PHOTO_OPERATION=evidence-before or evidence-after and the
reviewed EXPECTED_DEPLOY_SHA. The baseline is immutable. Real traffic may change business
rows; differences require investigation and are not automatically attributed to backfill.
"""
import datetime as dt
import json
import math
import os
from pathlib import Path
import re
import sys

import photo_production_activation as activation
import photo_production_canary as canary

OPERATIONS = {"evidence-before", "evidence-after"}
MARKER = "coffeepeek-photo-production-evidence-v1"
MAX_ROWS = 4000
TABLES = ("CanonicalCheckIns", "CheckInPhotos")
STAT_SERVICES = ("checkin-renderer", "media", "gateway")
JOB_STATES = {"Pending", "Processing", "Ready", "RetryScheduled", "Failed", "Cancelled"}
LATENCY_FIELDS = ("samples", "below1Second", "from1To10Seconds", "from10To60Seconds", "atLeast60Seconds",
                  "negativeClockSamples", "p50Ms", "p95Ms", "maxMs")
NUMBER = re.compile(r"([0-9]+(?:\.[0-9]+)?)\s*(B|kB|KB|MB|GB|TB|KiB|MiB|GiB|TiB)\Z")


def number(value, integer=False):
    canary.require(isinstance(value, (int, float)) and not isinstance(value, bool)
                   and math.isfinite(value) and value >= 0 and (not integer or isinstance(value, int)))
    return value


def memory_bytes(value):
    canary.require(isinstance(value, str))
    match = NUMBER.fullmatch(value.strip())
    canary.require(match)
    unit = match[2]
    exponent = {"B": 0, "kB": 1, "KB": 1, "MB": 2, "GB": 3, "TB": 4,
                "KiB": 1, "MiB": 2, "GiB": 3, "TiB": 4}[unit]
    return int(number(float(match[1])) * ((1024 if "i" in unit else 1000) ** exponent))


def compare_rows(before, after):
    result, private = {}, {}
    for table in TABLES:
        old, new = before[table], after[table]
        added, removed = sorted(new.keys() - old.keys()), sorted(old.keys() - new.keys())
        changed = sorted(key for key in old.keys() & new.keys() if old[key] != new[key])
        private[table] = {"added": added, "removed": removed, "changed": changed}
        result[table] = {"before": len(old), "after": len(new), "added": len(added),
                         "removed": len(removed), "changed": len(changed),
                         "unchanged": len(old.keys() & new.keys()) - len(changed)}
    return result, private


class Evidence:
    def __init__(self, environment, commands=None):
        self.environment = dict(environment)
        self.operator = activation.Activation(environment, commands)
        self.root = activation.PRIVATE
        self.stage = "validation"

    def excluded_canary(self):
        path = self.root / "canary.json"
        if not path.exists():
            return None
        state = canary.private_read(path)
        canary.validate_state(state)
        return canary.identity(state["homeId"]) if state.get("homeId") else None

    def business(self, excluded):
        # A single SELECT gives both tables the same PostgreSQL statement snapshot.
        exclusion = "'" + canary.identity(excluded) + "'::uuid" if excluded else "NULL::uuid"
        parts = []
        for table, parent in (("CanonicalCheckIns", "Id"), ("CheckInPhotos", "CanonicalCheckInId")):
            parts.append(f''''{table}', COALESCE((SELECT json_agg(json_build_object(
              'id', bounded."Id", 'fingerprint', md5(to_jsonb(bounded)::text)))
              FROM (SELECT * FROM "{table}" WHERE {exclusion} IS NULL OR "{parent}"<>{exclusion}
                    ORDER BY "Id" LIMIT {MAX_ROWS + 1}) bounded), '[]'::json)''')
        observed = self.operator.query("cpshopsdb", "SELECT json_build_object(" + ",".join(parts) + ");")
        result = {}
        for table in TABLES:
            rows = observed.get(table)
            canary.require(isinstance(rows, list) and len(rows) <= MAX_ROWS)
            mapped = {}
            for row in rows:
                canary.require(isinstance(row, dict))
                identity = canary.identity(row["id"])
                fingerprint = row.get("fingerprint", "")
                canary.require(re.fullmatch(r"[0-9a-f]{32}", fingerprint) and identity not in mapped)
                mapped[identity] = fingerprint
            result[table] = mapped
        return result

    def resources(self):
        result = {}
        for service in STAT_SERVICES:
            container = self.operator.commands.compose("ps", "-a", "-q", service)
            canary.require(re.fullmatch(r"[0-9a-f]{64}", container))
            raw = json.loads(self.operator.commands.run(["docker", "stats", "--no-stream", "--format",
                '{"cpu":{{json .CPUPerc}},"memory":{{json .MemUsage}},"pids":{{json .PIDs}}}', container], timeout=20))
            canary.require(isinstance(raw.get("cpu"), str) and re.fullmatch(r"[0-9]+(?:\.[0-9]+)?%", raw["cpu"]))
            cpu = number(float(raw["cpu"][:-1]))
            canary.require(cpu <= 100000 and isinstance(raw.get("memory"), str)
                           and isinstance(raw.get("pids"), str) and raw["pids"].isdigit())
            memory = raw["memory"].split("/")
            canary.require(len(memory) == 2)
            rss = self.operator.commands.run(["docker", "top", container, "-eo", "rss"]).splitlines()
            canary.require(rss and rss[0].strip() == "RSS" and 1 <= len(rss) - 1 <= 1024
                           and all(line.strip().isdigit() for line in rss[1:]))
            result[service] = {"cpuPercent": cpu, "memoryWorkingSetBytes": memory_bytes(memory[0]),
                               "memoryLimitBytes": memory_bytes(memory[1]), "pids": int(raw["pids"]),
                               "processRssSumBytes": sum(int(line) * 1024 for line in rss[1:])}
        return result

    def media(self):
        run_path = self.root / "pilot.json"
        pilot = None
        if run_path.exists():
            state = canary.private_read(run_path)
            canary.require(state.get("marker") == "coffeepeek-photo-production-inventory-v1"
                           and state.get("deploymentSha") == self.operator.expected_sha())
            if state.get("runId"):
                pilot = canary.identity(state["runId"])
        run_filter = "'" + pilot + "'::uuid" if pilot else "NULL::uuid"
        # Source byte count is metadata, explicitly labelled. Pure renderer timing is not stored.
        sql = f'''WITH pilot_jobs AS (SELECT * FROM "PhotoVariantJobs" WHERE "BackfillRunId"={run_filter}),
          ready AS (SELECT j.*, p."SizeBytes" AS source_bytes,
                     EXTRACT(EPOCH FROM (j."UpdatedAtUtc"-j."CreatedAtUtc"))*1000 AS duration_ms
                    FROM pilot_jobs j JOIN "Photos" p ON p."Id"=j."PhotoId" AND p."StorageKey"=j."SourceKey"
                    WHERE j."Status"='Ready'),
          variants AS (SELECT v.* FROM "PhotoVariants" v JOIN ready j ON j."Id"=v."JobId")
          SELECT json_build_object(
            'queues', COALESCE((SELECT json_agg(q) FROM (SELECT "Priority" AS priority,"Status" AS status,count(*) AS count
                         FROM "PhotoVariantJobs" GROUP BY "Priority","Status") q),'[]'::json),
            'pilot', json_build_object(
              'readyJobs',(SELECT count(*) FROM ready),
              'declaredReadySourceBytes',COALESCE((SELECT sum(source_bytes) FROM ready),0),
              'readyVariantBytes',COALESCE((SELECT sum("SizeBytes") FROM variants),0),
              'variants',COALESCE((SELECT json_agg(v) FROM (SELECT "Purpose" AS purpose,"Format" AS format,
                          count(*) AS count,sum("SizeBytes") AS bytes FROM variants GROUP BY "Purpose","Format") v),'[]'::json),
              'queueToReadyLatency', (SELECT json_build_object(
                'samples',count(*),'below1Second',count(*) FILTER(WHERE duration_ms>=0 AND duration_ms<1000),
                'from1To10Seconds',count(*) FILTER(WHERE duration_ms>=1000 AND duration_ms<10000),
                'from10To60Seconds',count(*) FILTER(WHERE duration_ms>=10000 AND duration_ms<60000),
                'atLeast60Seconds',count(*) FILTER(WHERE duration_ms>=60000),
                'negativeClockSamples',count(*) FILTER(WHERE duration_ms<0),
                'p50Ms',percentile_cont(0.5) WITHIN GROUP(ORDER BY duration_ms) FILTER(WHERE duration_ms>=0),
                'p95Ms',percentile_cont(0.95) WITHIN GROUP(ORDER BY duration_ms) FILTER(WHERE duration_ms>=0),
                'maxMs',max(duration_ms) FILTER(WHERE duration_ms>=0)) FROM ready)));
        '''
        raw = self.operator.query("cpmediadb", sql)
        canary.require(isinstance(raw.get("queues"), list) and len(raw["queues"]) <= 12)
        result = {"queues": [], "pilot": {"identified": pilot is not None}}
        for queue in raw["queues"]:
            canary.require(isinstance(queue, dict) and queue.get("priority") in (0, 1)
                           and not isinstance(queue["priority"], bool) and queue.get("status") in JOB_STATES)
            result["queues"].append({"priority": queue["priority"], "status": queue["status"],
                                     "count": number(queue.get("count"), True)})
        observed = raw.get("pilot")
        canary.require(isinstance(observed, dict))
        for field in ("readyJobs", "declaredReadySourceBytes", "readyVariantBytes"):
            result["pilot"][field] = number(observed.get(field), True)
        rows = observed.get("variants")
        canary.require(isinstance(rows, list) and len(rows) <= 9)
        result["pilot"]["variants"] = []
        for row in rows:
            canary.require(row.get("purpose") in ("thumb", "feed", "detail") and row.get("format") in ("jpeg", "png", "webp"))
            result["pilot"]["variants"].append({"purpose": row["purpose"], "format": row["format"],
                "count": number(row.get("count"), True), "bytes": number(row.get("bytes"), True)})
        latency = observed.get("queueToReadyLatency")
        canary.require(isinstance(latency, dict))
        result["pilot"]["queueToReadyLatency"] = {key: None if latency.get(key) is None and key.endswith("Ms")
            else number(latency.get(key), not key.endswith("Ms")) for key in LATENCY_FIELDS}
        return result

    def run(self, operation):
        canary.require(operation in OPERATIONS)
        before_path = self.root / "evidence-before.json"
        if operation == "evidence-before":
            canary.require(not before_path.exists() and not before_path.is_symlink())
            baseline = None
        else:
            baseline = canary.private_read(before_path)
            canary.require(baseline.get("marker") == MARKER and baseline.get("deploymentSha") == self.operator.expected_sha())
        self.stage = "release"
        self.operator.running_release()
        self.operator.image_tag()
        self.stage = "business"
        excluded = self.excluded_canary()
        if baseline and baseline.get("excludedCanaryId"):
            canary.require(excluded == baseline["excludedCanaryId"])
        rows = self.business(excluded)
        self.stage = "resources"
        resources = self.resources()
        self.stage = "media"
        media = self.media()
        snapshot = {"marker": MARKER, "deploymentSha": self.operator.expected_sha(),
                    "observedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(), "excludedCanaryId": excluded,
                    "rows": rows, "resources": resources, "media": media}
        result = {"operation": operation, "success": True, "observedAtUtc": snapshot["observedAtUtc"],
                  "excludedCanaryRows": excluded is not None, "businessRows": {table: len(rows[table]) for table in TABLES},
                  "resources": resources, "media": media,
                  "limitations": ["Separate database observations are not one cross-database snapshot.",
                    "Changed/added/removed business rows may reflect real traffic; private fingerprints require investigation.",
                    "Ready source sizes are declared metadata; use verified inventory for actual source lengths.",
                    "Queue-to-Ready latency includes queueing and retries; it is not pure renderer duration.",
                    "Docker CPU is an instantaneous sample; RSS sums process resident pages and can double-count shared pages."]}
        self.stage = "private_state"
        if baseline is None:
            # Exclusive creation prevents accidental baseline replacement, even after a partial prior write.
            descriptor = os.open(before_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY | getattr(os, "O_NOFOLLOW", 0), 0o600)
            with os.fdopen(descriptor, "w", encoding="utf-8") as target:
                json.dump(snapshot, target, allow_nan=False); target.flush(); os.fsync(target.fileno())
        else:
            result["businessComparison"], snapshot["differences"] = compare_rows(baseline["rows"], rows)
            result["businessDifferencesObserved"] = any(group[key] for group in result["businessComparison"].values()
                                                        for key in ("added", "changed", "removed"))
            canary.private_write(self.root / "evidence-after.json", snapshot)
        canary.private_write(self.root / (operation + "-report.json"), result)
        return result


def main(argv=None, environment=None, commands=None):
    operator = None
    try:
        canary.require(not (sys.argv[1:] if argv is None else argv))
        environment = dict(os.environ if environment is None else environment)
        operation = environment.get("PHOTO_OPERATION")
        canary.require(operation in OPERATIONS and Path.cwd().resolve() == activation.ROOT)
        with activation.private_lock():
            operator = Evidence(environment, commands)
            result = operator.run(operation)
        print(json.dumps(result, allow_nan=False))
        return 0
    except Exception:
        print(json.dumps({"success": False, "stage": operator.stage if operator else "validation",
                          "requiresPrivateStateReview": True}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
