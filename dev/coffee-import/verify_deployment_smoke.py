"""Verify a real import against PostgreSQL rows and its stored MinIO image."""

import argparse
import csv
import hashlib
import json
import re
from decimal import Decimal
from pathlib import Path


def rows(path):
    with Path(path).open(newline="", encoding="utf-8") as stream:
        return list(csv.reader(stream))


def applied_request(state, source):
    report = json.loads((Path(state) / "last-run.json").read_text(encoding="utf-8"))
    result = next((item for item in report["sources"] if item["source"].encode("idna").decode("ascii") == source), None)
    if not result or result["status"] != "ok" or result.get("import", {}).get("status") != "Applied":
        raise ValueError("The selected source did not finish with an Applied import")
    identifier = result["import"]["snapshotId"]
    if not re.fullmatch(r"[a-f0-9]{64}", identifier):
        raise ValueError("The service returned an invalid snapshot identity")
    request = json.loads((Path(state) / "history" / source / (identifier + ".json")).read_text(encoding="utf-8"))
    if request["sourceKey"] != source or request["snapshotId"] != identifier:
        raise ValueError("Stored replay document does not match the applied source")
    return result["import"], request


def verify_rows(snapshot, before, after, variants, photos, media_photos):
    old_statuses = {identifier: status for _, identifier, status, _ in before}
    products = {external: (identifier, status, json.loads(protected)) for external, identifier, status, protected in after}
    if not snapshot["products"]:
        raise ValueError("The smoke import must contain products")
    for product in snapshot["products"]:
        identifier, status, _ = products[product["sourceProductId"]]
        if status != old_statuses.get(identifier, "Draft"):
            raise ValueError("Import changed moderation state or published a new product")
    offers = {row[0]: row[1:] for row in variants}
    links = {(external, key, identifier) for external, key, identifier in photos}
    stored = {(key, identifier) for key, identifier in media_photos}
    probe = None
    for product in snapshot["products"]:
        for variant in product["variants"]:
            price, currency, weight, availability = offers[variant["externalVariantId"]]
            if (Decimal(price) != Decimal(variant["price"]) or currency != variant["currency"] or
                    (int(weight) if weight else None) != variant["weightGrams"] or
                    availability != ("InStock" if variant["availableForPurchase"] else "OutOfStock")):
                raise ValueError("PostgreSQL offer differs from the applied snapshot")
        for photo in product.get("storedPhotos", []):
            key, identifier = photo["storageKey"], photo["id"]
            protected = products[product["sourceProductId"]][2]
            if (("Photos" not in protected and (product["sourceProductId"], key, identifier) not in links) or
                    (key, identifier) not in stored):
                raise ValueError("Coffee and Media PostgreSQL photo references are incomplete")
            if not re.fullmatch(r"coffees/[a-f0-9]{64}\.(jpg|png|webp|gif|avif)", key):
                raise ValueError("Unexpected MinIO storage key")
            probe = probe or {"storageKey": key, "sizeBytes": photo["sizeBytes"]}
    if probe is None:
        raise ValueError("Select a source with photographs to exercise the MinIO upload path")
    return probe


def fingerprint(mapping, job_env, requirements):
    content = [Path(path).read_bytes() for path in (mapping, job_env, requirements)]
    return hashlib.sha256(b"\0".join(content)).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--state", type=Path, required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--rows", type=Path, required=True)
    args = parser.parse_args()
    imported, request = applied_request(args.state, args.source)
    probe = verify_rows(request["snapshot"], *(rows(args.rows / (name + ".csv")) for name in (
        "before", "after", "variants", "photos", "media-photos")))
    # Replaying the exact document exercises relational idempotence without re-collecting prices.
    from publish_coffees import Publisher
    publisher = Publisher.from_environment(args.state)
    replay = publisher.api(publisher.shops_url, "/internal/coffee-import/snapshots",
                           json.dumps(request, ensure_ascii=False).encode(), "application/json")
    # PostgreSQL stores microsecond timestamps; compare the stable result fields.
    if {key: value for key, value in replay.items() if key != "appliedAtUtc"} != {key: value for key, value in imported.items() if key != "appliedAtUtc"}:
        raise ValueError("Replaying the applied snapshot did not return the same import result")
    (args.rows / "probe.json").write_text(json.dumps(probe), encoding="utf-8")
    print("PostgreSQL products, offers, photo links and replay idempotence verified")


if __name__ == "__main__":
    main()
