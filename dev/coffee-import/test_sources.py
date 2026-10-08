import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from import_coffees import run
from roast_by import Fetcher, ParseError
from sources import collect_spiritus, river_products, spiritus_product


class NewSourcesCheck(unittest.TestCase):
    def test_catalogues_and_failed_job_preserve_identity_and_previous_snapshot(self):
        spiritus = {"uid": 100, "title": "Кения &amp; Киринияга", "url": "https://spiritus.by/catalog/tproduct/100",
                    "text": "Во вкусе: ягоды, шоколад<br>Обработка: мытая", "partuids": "[123]",
                    "mark": "filter", "editions": [
                        {"uid": 101, "Тип": "Зерно", "Вес": "250 гр", "quantity": "", "price": "33.0000"},
                        {"uid": 102, "Тип": "Зерно", "Вес": "1кг", "quantity": "0", "price": "105.00"},
                        {"uid": 103, "Тип": "Молотый", "Вес": "250 гр", "quantity": "", "price": "33.00"}]}
        product = spiritus_product(spiritus)
        self.assertEqual("Кения & Киринияга", product["name"])
        self.assertEqual(["KE"], [o["countryCode"] for o in product["origins"]])
        self.assertEqual([250, 1000], [v["weightGrams"] for v in product["variants"]])
        self.assertEqual([True, False], [v["availableForPurchase"] for v in product["variants"]])
        self.assertEqual("33.00", product["variants"][0]["price"])
        self.assertEqual("мытая", product["processing"])
        other_roast = spiritus_product(spiritus | {"uid": 200, "mark": "espresso"})
        self.assertNotEqual(product["sourceProductId"], other_roast["sourceProductId"])

        class Feed:
            sources = {}

            def fetch(self, url):
                if url.endswith("/catalog"):
                    return "var options={recid:'1',storepart:'123',currencyTxt:'BYN'}"
                return json.dumps({"partuid": 123, "parts": [{"uid": 123, "root": True}],
                                   "products": [spiritus], "total": 2})

        with self.assertRaisesRegex(ParseError, "Incomplete"):
            collect_spiritus(Feed())

        data = {"lastUpdateTime": "05.10.2026, 10:18:47", "tableData": [
            {"name": "Эспрессо Бразилия Велосо", "weight": "250 г", "count": "0", "price": "30.00"},
            {"name": "Эспрессо Бразилия Велосо", "weight": "1 кг", "count": "5", "price": "84.50"},
            {"name": "Фильтр Бразилия Велосо", "weight": "250 г", "count": "2", "price": "30.00"},
            {"name": "Дрипы Колумбия", "weight": "8 шт.", "count": "3", "price": "36.50"}]}
        products, excluded = river_products(data)
        self.assertEqual(2, len(products))
        self.assertEqual(1, len(excluded))
        self.assertEqual([False, True], [v["availableForPurchase"] for v in products[0]["variants"]])
        unavailable, sold_out = river_products(data | {"tableData": [data["tableData"][0]]})
        self.assertEqual([], unavailable)
        self.assertEqual(products[0]["sourceProductId"], sold_out[0]["sourceProductId"])
        self.assertFalse(sold_out[0]["variants"][0]["availableForPurchase"])
        reversed_products, _ = river_products(data | {"tableData": list(reversed(data["tableData"]))})
        self.assertEqual({p["sourceProductId"] for p in products}, {p["sourceProductId"] for p in reversed_products})
        with self.assertRaisesRegex(ParseError, "Duplicate"):
            river_products(data | {"tableData": data["tableData"] + [data["tableData"][0]]})
        with self.assertRaises(ParseError):
            river_products(data | {"tableData": [data["tableData"][0] | {"count": "unknown"}]})

        def broken_source(_):
            raise ParseError("Incomplete catalogue")

        with tempfile.TemporaryDirectory(dir=Path(__file__).parent, prefix=".test-") as directory:
            root = Path(directory)
            previous = root / "failed" / "coffees.json"
            previous.parent.mkdir()
            previous.write_text("previous snapshot", encoding="utf-8")
            complete = {"source": "https://ok.example", "collectedAtUtc": "2026-10-05T00:00:00+00:00",
                        "complete": True, "products": [], "excluded": [], "coverage": []}
            with patch("import_coffees.SOURCES", {"failed": ("failed", (), broken_source),
                                                  "ok": ("ok", (), lambda _: complete)}):
                self.assertEqual(1, run(root, ["failed", "ok"], offline=True))
            self.assertEqual("previous snapshot", previous.read_text(encoding="utf-8"))
            self.assertTrue((root / "ok" / "coffees.json").exists())
            self.assertEqual(["failed", "ok"], [s["status"] for s in json.loads((root / "last-run.json").read_text())["sources"]])
            with self.assertRaisesRegex(ParseError, "Unexpected"):
                Fetcher(root).fetch("https://localhost/private")


if __name__ == "__main__":
    unittest.main()
