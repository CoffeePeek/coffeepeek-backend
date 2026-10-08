import gzip
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from bs4 import BeautifulSoup

from catalogues import (aspro_details, collect_mcoffee, collect_woo, daloni_details, mcoffee_details, product, tasty_details,
                        tilda_product, variant, welder_details)
from roast_by import Fetcher, ParseError, clean, property_labels
from sources import river_products, spiritus_product

FIXTURES = Path(__file__).parent / "fixtures"


def detail(name):
    return BeautifulSoup((FIXTURES / (name + ".html")).read_text(encoding="utf-8"), "html.parser")


def coffee(properties, description=""):
    return product("example.org", "p1", "https://example.org/p1", "Бразилия", "Test",
                   [variant("sku1", "250 г", "27.50", "BYN", True)],
                   properties=properties, description=description)


class CharacteristicRegressionTests(unittest.TestCase):
    def test_explicit_quality_aliases_preserve_raw_score_and_no_guessed_score(self):
        for label in ("Q", "Quality", "SCA", "Оценка Q-грейдера", "Оценка по протоколу SCA"):
            with self.subTest(label=label):
                self.assertEqual("85,50 / 100", coffee({label: "85,50 / 100"})["qGraderScoreRaw"])
        self.assertIsNone(coffee({})["qGraderScoreRaw"])
        self.assertEqual("single_origin", coffee({"Происхождение": "моносорт"})["compositionKind"])
        self.assertEqual("blend", coffee({"Происхождение": "бленд"})["compositionKind"])
        self.assertEqual("unknown", coffee({"Страна": "Бразилия"})["compositionKind"])
        self.assertEqual(["BR"], [o["countryCode"] for o in coffee({"Происхождение": "моносорт"})["origins"]])
        self.assertEqual("0", clean(0))
        self.assertEqual("0", coffee({"Кислотность": 0})["acidity"])
        self.assertIsNone(coffee({"Обработка": None})["processing"])

    def test_aspro_actual_product_sections_map_sense_score_and_sibling_fields(self):
        properties, description = aspro_details(detail("sensecoffee"))
        result = coffee(properties, description)
        self.assertEqual("81,50", result["qGraderScoreRaw"])
        self.assertEqual("натуральная", result["processing"])
        self.assertEqual("ниже средней", result["acidity"])
        self.assertEqual("среднее", result["body"])
        self.assertEqual(["молочный шоколад", "орехи", "сухофрукты"], result["tasteDescriptors"])
        self.assertTrue(result["description"])
        soup = detail("sensecoffee")
        soup.append(BeautifulSoup('<a class="back-url" href="/catalog/espresso/">Назад к списку</a>', "html.parser"))
        props, desc = aspro_details(soup, {"/catalog/espresso/": "Эспрессо"})
        self.assertEqual(["Эспрессо"], coffee(props, desc)["brewRecommendations"])
        props, desc = aspro_details(soup)
        self.assertEqual([], coffee(props, desc)["brewRecommendations"])
        for source, roast, tastes in (("sorso", "Средняя/Светлая", ["Шоколад", "фундук", "выпечка"]),
                                      ("marconi", "Светлая", ["Шоколад", "орехи", "карамель"])):
            props, desc = aspro_details(detail(source))
            parsed = coffee(props, desc)
            self.assertEqual(roast, parsed["roastLevel"])
            self.assertEqual(tastes, parsed["tasteDescriptors"])
        with self.assertRaises(ParseError):
            aspro_details(BeautifulSoup('<aside>Обработка: мытая</aside>', "html.parser"))

    def test_manufactura_specs_take_priority_over_educational_origin_prose(self):
        properties, description = mcoffee_details(detail("mcoffee"))
        result = coffee(properties, description + " История кофе в Эфиопии и Кении.")
        self.assertEqual("средняя обжарка кофе", result["roastLevel"])
        self.assertEqual("мытый", result["processing"])
        self.assertEqual("Высокая", result["acidity"])
        self.assertEqual("Высокая", result["body"])
        self.assertEqual(["TZ"], [o["countryCode"] for o in result["origins"]])
        self.assertEqual(["кофе для эспрессо"], result["brewRecommendations"])
        self.assertIsNone(result["qGraderScoreRaw"])
        self.assertEqual("sku1", result["variants"][0]["externalVariantId"])
        self.assertEqual("27.50", result["variants"][0]["price"])

    def test_manufactura_missing_detail_descriptors_keeps_catalogue_taste(self):
        form = BeautifulSoup(
            '<form class="product-card" data-product-item="123">'
            '<a class="product-category__subTitle" href="/products/brazil">Бразилия</a>'
            '<input name="product_id" value="42"><input data-name="variants_data[]" id="weight" value="1" checked>'
            '<label for="weight">250 г</label><span class="current-price">27.50</span>'
            '<button class="add_to_cart"></button><input name="options[]" id="grind" checked>'
            '<label for="grind">В зёрнах</label><div class="compound">Шоколад, фундук</div>'
            '<div class="processing">мытый</div></form>', "html.parser").form
        specs = detail("mcoffee")
        for row in specs.select(".spec"):
            if row.select_one(".spec__label").get_text() == "Дескрипторы":
                row.decompose()
        metadata = '<script type="application/ld+json">{"@type":"Product","offers":{"priceCurrency":"BYN"}}</script>'
        class Mcoffee:
            offline = True
            sources = {}
            def fetch(self, url):
                return '<meta name="csrf-token" content="test">' if "/catalog/" in url else metadata + str(specs)
        with patch("catalogues.html_pages", return_value=([(None, form)], 1)):
            result = collect_mcoffee(Mcoffee())["products"][0]
        self.assertEqual(["Шоколад", "фундук"], result["tasteDescriptors"])
        form.select_one(".compound").decompose()
        form.select_one(".processing").decompose()
        for row in specs.select(".spec"):
            if row.select_one(".spec__label").get_text() == "Способ обработки":
                row.decompose()
        with patch("catalogues.html_pages", return_value=([(None, form)], 1)):
            absent = collect_mcoffee(Mcoffee())["products"][0]
        self.assertEqual([], absent["tasteDescriptors"])
        self.assertIsNone(absent["processing"])
        raw = "средняя обжарка кофе, эспрессо, средняя обжарка кофе"
        parsed = coffee({"Степень обжарки": raw})
        self.assertEqual("средняя обжарка кофе, эспрессо", parsed["roastLevel"])
        self.assertEqual(raw, parsed["sourceProperties"]["Степень обжарки"])

    def test_welder_total_is_quality_but_judge_subscores_are_not_intensity(self):
        properties, description = welder_details(detail("welder-catherine"))
        result = coffee(properties, description)
        self.assertEqual("86.5", result["qGraderScoreRaw"])
        self.assertEqual("натуральный", result["processing"])
        self.assertEqual("среднее, шелковистое", result["body"])
        self.assertIsNone(result["acidity"])
        self.assertIsNone(result["roastLevel"])
        self.assertEqual(["эспрессо"], result["brewRecommendations"])
        self.assertEqual("86.5", properties["cuppingSubscores"]["Итоговая оценка"])
        props, desc = welder_details(detail("welder-zero"))
        parsed = coffee(props, desc)
        self.assertIsNone(parsed["qGraderScoreRaw"])
        self.assertEqual("0", parsed["sourceProperties"]["Общая итоговая оценка"])

    def test_daloni_sca_and_descriptors_are_from_product_not_recommendations(self):
        soup = detail("dalonicoffee")
        soup.append(BeautifulSoup('<aside>Обработка: мытая<br>Оценка SCA: 99</aside>', "html.parser"))
        properties, description = daloni_details(soup)
        result = coffee(properties, description)
        self.assertEqual("86.75", result["qGraderScoreRaw"])
        self.assertEqual(["дубовая кора", "сбор трав", "ром", "ваниль"], result["tasteDescriptors"])
        self.assertIsNone(result["processing"])

    def test_tasty_detail_roast_descriptors_and_unpublished_zero_quality(self):
        data = json.loads((FIXTURES / "tastycoffee.json").read_text(encoding="utf-8"))
        properties, description = tasty_details(data)
        result = coffee(properties, description)
        self.assertEqual("тёмная, эспрессо", result["roastLevel"])
        self.assertEqual(["жареных орехов", "печенья", "нуги"], result["tasteDescriptors"])
        self.assertIsNone(result["qGraderScoreRaw"])
        self.assertEqual("0", result["acidity"])
        properties, description = tasty_details(data | {"rating_q": 87.25, "is_skip_q_label": False})
        self.assertEqual("87.25", coffee(properties, description)["qGraderScoreRaw"])

    def test_tilda_dash_labels_and_no_colon_body_ratings_are_preserved(self):
        rows = json.loads((FIXTURES / "sova.json").read_text(encoding="utf-8"))
        scores = [tilda_product(p, "xn--80ad9al.xn--90ais")["qGraderScoreRaw"] for p in rows]
        self.assertEqual(["83"], scores)
        self.assertEqual("83", property_labels("Оценка Q-грейдера - 83")["Оценка Q-грейдера"])
        single = {"uid": 10, "title": "Колумбия декаф", "url": "https://singlestep.by/coffee/10",
                  "partuids": "[688675940832]", "editions": [{"uid": 10, "price": "28", "quantity": ""}],
                  "text": "Подходит для тех, кому не подходит кофеин.", "descr": "Шоколад, карамель",
                  "properties": [{"title": "Вес", "values": "0,25 кг=28"}, {"title": "Помол", "values": "Не молоть"}]}
        self.assertEqual([], tilda_product(single, "singlestep.by")["brewRecommendations"])
        self.assertIsNone(coffee({"Обжарка": "под молочко"})["roastLevel"])
        self.assertEqual({}, property_labels("Степень обжарки сможете указать в комментарии при оформлении заказа"))
        self.assertEqual([], coffee({}, "Фермеры используют органические удобрения.")["features"])
        self.assertEqual([], coffee({}, "Попробуйте кофе без кофеина. Это не органический кофе.")["features"])
        rows = json.loads((FIXTURES / "spiritus.json").read_text(encoding="utf-8"))
        products = [spiritus_product(p) for p in rows]
        self.assertEqual({"2/5", "3/5"}, {p["body"] for p in products})
        self.assertTrue(all(p["description"] for p in products))

    def test_blend_origins_require_affirmative_product_composition(self):
        result = coffee({}, "Наша фирменная смесь двух арабик из Бразилии и Эфиопии.")
        self.assertEqual("blend", result["compositionKind"])
        self.assertEqual(["BR", "ET"], [o["countryCode"] for o in result["origins"]])
        result = coffee({}, "Это не смесь. В смесях бывает кофе из Эфиопии.")
        self.assertEqual("unknown", result["compositionKind"])
        self.assertEqual(["BR"], [o["countryCode"] for o in result["origins"]])

    def test_woo_conflicts_preserve_source_evidence_and_current_native_skus(self):
        fixture = json.loads((FIXTURES / "kcr.json").read_text(encoding="utf-8"))
        class Woo:
            sources = {}
            def fetch(self, url):
                rows = fixture["variations"] if "type=variation" in url else [fixture["product"]]
                self.sources[url] = {"url": url, "paginationHeaders": {"X-WP-Total": str(len(rows)), "X-WP-TotalPages": "1"}}
                return json.dumps(rows)
        parsed = collect_woo(Woo(), "kcr.by", 16, "Kitchen Coffee Roasters")["products"][0]
        self.assertEqual("Мытый", parsed["processing"])
        self.assertIn("conflicting_source_properties", parsed["reviewWarnings"])
        self.assertEqual("Натуральный / Анаэробный",
                         parsed["sourceProperties"]["conflictingProperties"]["способ обработки"]["description"])
        self.assertEqual({str(v["id"]) for v in fixture["variations"]},
                         {v["externalVariantId"] for v in parsed["variants"]})

    def test_manufactura_transport_requests_supported_gzip_and_decodes_it(self):
        class Response:
            url = "https://mcoffee.by/products/test"
            headers = {}
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def read(self, limit): return gzip.compress("Явные характеристики".encode("utf-8"))

        class Opener:
            def open(self, request, timeout):
                self.encoding = request.get_header("Accept-encoding")
                return Response()

        with tempfile.TemporaryDirectory() as directory:
            fetcher = Fetcher(Path(directory), hosts=("mcoffee.by",), delay=0)
            fetcher.opener = Opener()
            self.assertEqual("Явные характеристики", fetcher.fetch(Response.url))
            self.assertEqual("gzip", fetcher.opener.encoding)

    def test_river_count_with_explicit_piece_unit_preserves_strict_stock(self):
        data = {"lastUpdateTime": "2026-10-07", "tableData": [
            {"name": "Фильтр Кения Чоронги", "weight": "250г", "count": "4 шт", "price": "34.50"},
            {"name": "Дрипы Кения", "weight": "8 шт", "count": "8 шт", "price": "20"}]}
        products, excluded = river_products(data)
        self.assertTrue(products[0]["availableForPurchase"])
        self.assertEqual(1, len(excluded))
        with self.assertRaises(ParseError):
            river_products(data | {"tableData": [data["tableData"][0] | {"count": "4 или 5"}]})


if __name__ == "__main__":
    unittest.main()
