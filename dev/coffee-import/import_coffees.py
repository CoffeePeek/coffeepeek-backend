"""Run all catalogue collectors once; schedule this command on the server."""

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

from roast_by import Fetcher, collect, write_exports
from sources import TILDA_ENDPOINTS, collect_river, collect_spiritus
from catalogues import (collect_blaser, collect_daloni, collect_ideal, collect_kcr, collect_marconi,
                        collect_mcoffee, collect_sense, collect_singlestep, collect_sorso,
                        collect_sova, collect_tasty, collect_welder)


SOURCES = {
    "roast.by": ("roast-by", ("roast.by",), collect),
    "spiritus.by": ("spiritus-by", ("spiritus.by",) + TILDA_ENDPOINTS, collect_spiritus),
    "espressoclub.by": ("espressoclub-by", ("espressoclub.by", "raw.githubusercontent.com"), collect_river),
    "theweldercatherine.ru": ("welder-catherine", ("theweldercatherine.ru",), collect_welder),
    "shop.tastycoffee.ru": ("tastycoffee", ("shop.tastycoffee.ru",), collect_tasty),
    "сова.бел": ("sova", ("xn--80ad9al.xn--90ais",) + TILDA_ENDPOINTS, collect_sova),
    "sorso.by": ("sorso", ("sorso.by",), collect_sorso),
    "singlestep.by": ("singlestep", ("singlestep.by",) + TILDA_ENDPOINTS, collect_singlestep),
    "sensecoffee.by": ("sensecoffee", ("sensecoffee.by",), collect_sense),
    "marconi.by": ("marconi", ("marconi.by",), collect_marconi),
    "mcoffee.by": ("mcoffee", ("mcoffee.by",), collect_mcoffee),
    "kcr.by": ("kcr", ("kcr.by",), collect_kcr),
    "idealcoffee.by": ("idealcoffee", ("idealcoffee.by",), collect_ideal),
    "dalonicoffee.by": ("dalonicoffee", ("dalonicoffee.by",), collect_daloni),
    "www.blasercafe.by": ("blasercafe", ("www.blasercafe.by",), collect_blaser),
}


def run(output, selected, *, offline=False, publisher=None):
    results = []
    for source in selected:
        folder, hosts, collector = SOURCES[source]
        target = output / folder
        fetcher = Fetcher(target / ".cache", hosts=hosts, offline=offline, refresh=not offline)
        stage = "collect"
        try:
            snapshot = collector(fetcher)
            write_exports(snapshot, target)
            stage = "photos"
            imported = publisher.publish(snapshot) if publisher is not None else None
            results.append({"source": source, "status": "ok", "products": len(snapshot["products"]),
                            "variants": sum(len(p["variants"]) for p in snapshot["products"]),
                            "collectedAtUtc": snapshot["collectedAtUtc"]})
            if imported is not None:
                results[-1]["import"] = imported
            print(f"{source}: {results[-1]['products']} products, {results[-1]['variants']} variants", flush=True)
        except Exception as exc:
            if publisher is not None:
                try:
                    publisher.report_failure(hosts[0], getattr(exc, "stage", stage), exc)
                except Exception as report_error:
                    print(f"{source}: could not record failure: {report_error}", file=sys.stderr, flush=True)
            # A failed source keeps its previous JSON; the other roasters still refresh.
            results.append({"source": source, "status": "failed", "error": str(exc)})
            print(f"{source}: FAILED: {exc}", file=sys.stderr, flush=True)
    report = {"finishedAtUtc": datetime.now(timezone.utc).isoformat(), "sources": results}
    output.mkdir(parents=True, exist_ok=True)
    temporary = output / "last-run.json.tmp"
    temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(output / "last-run.json")
    return 1 if any(r["status"] == "failed" for r in results) else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "imports")
    parser.add_argument("--source", action="append", choices=SOURCES, help="Repeat to select sources; default: all registered")
    parser.add_argument("--offline", action="store_true", help="Replay captured source responses without network access")
    parser.add_argument("--push", action="store_true", help="Store photos in Media and apply each successful snapshot in Shops")
    args = parser.parse_args()
    if args.push and args.offline:
        parser.error("--offline cannot refresh the live catalogue; use publish_coffees.py to replay an original snapshot")
    publisher = None
    if args.push:
        from publish_coffees import Publisher
        publisher = Publisher.from_environment(args.output)
    return run(args.output, list(dict.fromkeys(args.source or SOURCES)), offline=args.offline, publisher=publisher)


if __name__ == "__main__":
    raise SystemExit(main())
