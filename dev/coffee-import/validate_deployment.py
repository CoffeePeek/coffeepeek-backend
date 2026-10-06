"""Validate the VPS collector configuration without exposing credentials."""

import argparse
import json
import os
import shlex
import sys
import uuid
from pathlib import Path
from urllib.parse import urlsplit


EXPECTED = json.loads((Path(__file__).parent / "roaster-mapping.example.json").read_text(encoding="utf-8"))["CoffeeImportOptions"]["Roasters"]
CLI_SOURCES = {key: ("сова.бел" if key == "xn--80ad9al.xn--90ais" else key) for key in EXPECTED}


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if any(existing.casefold() == key.casefold() for existing in result):
            raise ValueError("Mapping contains duplicate configuration keys")
        result[key] = value
    return result


def validate_mapping(path, *, allow_empty=False):
    value = json.loads(Path(path).read_text(encoding="utf-8"), object_pairs_hook=unique_object)
    if not isinstance(value, dict) or set(value) != {"CoffeeImportOptions"}:
        raise ValueError("Mapping must contain only CoffeeImportOptions")
    options = value["CoffeeImportOptions"]
    if not isinstance(options, dict) or set(options) != {"Roasters"} or not isinstance(options["Roasters"], dict):
        raise ValueError("Mapping must contain CoffeeImportOptions.Roasters")
    sources = options["Roasters"]
    if not sources and not allow_empty:
        raise ValueError("Configure at least one source before enabling coffee import")
    for source, brands in sources.items():
        if source not in EXPECTED or not isinstance(brands, dict) or set(brands) != set(EXPECTED[source]):
            raise ValueError("Mapping must use an observed source and all of its exact brand names")
        for identifier in brands.values():
            if not isinstance(identifier, str) or not uuid.UUID(identifier).int:
                raise ValueError("Replace every mapped roaster with a real, nonzero GUID")
    return sources


def validate_token(token):
    if len(token) < 32 or token != token.strip() or token.lower().startswith(("change-me", "your-", "replace-")):
        raise ValueError("Coffee import token must be a private value of at least 32 characters")


def selected_sources(value):
    sources = value.split(",") if value else []
    if not sources or len(sources) != len(set(sources)) or any(source not in CLI_SOURCES.values() for source in sources):
        raise ValueError("COFFEE_IMPORT_SOURCES must list unique, supported collector names")
    return [source.encode("idna").decode("ascii") for source in sources]


def read_env(path):
    result = {}
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        key, separator, value = line.partition("=")
        if not separator or key in result:
            raise ValueError("Invalid or duplicate job environment entry")
        words = shlex.split(value, comments=True)
        if len(words) > 1:
            raise ValueError("Quote job environment values containing spaces")
        result[key] = words[0] if words else ""
    return result


def validate_job(values):
    validate_token(values.get("COFFEE_IMPORT_TOKEN", ""))
    for key in ("COFFEE_IMPORT_SHOPS_URL", "COFFEE_IMPORT_MEDIA_URL"):
        url = urlsplit(values.get(key, ""))
        if (url.scheme != "http" or url.hostname not in ("localhost", "127.0.0.1") or
                url.username or url.password or url.query or url.fragment or url.path not in ("", "/") or
                not url.port or not 1 <= url.port <= 65535):
            raise ValueError("The VPS job must use loopback-only Shops and Media HTTP URLs with explicit ports")
    if values["COFFEE_IMPORT_SHOPS_URL"].rstrip("/") == values["COFFEE_IMPORT_MEDIA_URL"].rstrip("/"):
        raise ValueError("Shops and Media must have separate loopback ports")
    return selected_sources(values.get("COFFEE_IMPORT_SOURCES", ""))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mapping", type=Path)
    parser.add_argument("--mapping-only", action="store_true", help="Check GUIDs before generating the shared token")
    parser.add_argument("--allow-disabled", action="store_true")
    parser.add_argument("--job", action="store_true", help="Validate the current systemd environment")
    parser.add_argument("--job-env", type=Path, help="Also compare the job's private env file with deploy/.env")
    parser.add_argument("--print-sources", action="store_true")
    args = parser.parse_args()
    try:
        token = os.environ.get("COFFEE_IMPORT_TOKEN", "")
        if args.job:
            validate_job(os.environ)
        if args.mapping:
            disabled = args.allow_disabled and not token
            mapping = validate_mapping(args.mapping, allow_empty=disabled)
            if not args.mapping_only:
                if disabled and mapping:
                    raise ValueError("A populated mapping requires the shared token")
                if not disabled:
                    validate_token(token)
            if args.job_env:
                job = read_env(args.job_env)
                sources = validate_job(job)
                if job["COFFEE_IMPORT_TOKEN"] != token:
                    raise ValueError("The systemd job and Compose must use the same token")
                if not set(sources).issubset(mapping):
                    raise ValueError("Every enabled collector needs a complete reviewed mapping")
                for service, default in (("SHOPS", "5243"), ("MEDIA", "5025")):
                    if urlsplit(job[f"COFFEE_IMPORT_{service}_URL"]).port != int(os.environ.get(f"COFFEE_IMPORT_{service}_PORT") or default):
                        raise ValueError("The job URL must use the matching Compose loopback port")
            if args.print_sources:
                print(",".join(CLI_SOURCES[source] for source in mapping))
                return 0
        print("Coffee import configuration validated")
        return 0
    except (ValueError, OSError, KeyError, TypeError) as error:
        print(f"Coffee import configuration is invalid: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
