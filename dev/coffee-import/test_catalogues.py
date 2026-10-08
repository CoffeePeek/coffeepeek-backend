import json
import tempfile
import unittest
from pathlib import Path

from bs4 import BeautifulSoup

from catalogues import bitrix_options, collect_daloni, collect_tasty, html_pages, tilda_product, variant, woo_rows
from roast_by import Fetcher, ParseError


class CatalogueCheck(unittest.TestCase):
    def test_platform_differences_and_incomplete_data(self):
        single = {"uid": 10, "title": "Бразилия", "url": "https://singlestep.by/coffee/10",
                  "partuids": "[688675940832]", "editions": [{"uid": 10, "price": "28", "quantity": ""}],
                  "properties": [{"title": "Вес", "values": "0,25 кг=28\n1 кг=90"},
                                 {"title": "Помол", "values": "Не молоть\nЭспрессо"}]}
        p = tilda_product(single, "singlestep.by")
        self.assertEqual([250, 1000], [v["weightGrams"] for v in p["variants"]])
        self.assertEqual(["28.00", "90.00"], [v["price"] for v in p["variants"]])
        self.assertNotEqual(p["variants"][0]["externalVariantId"], p["variants"][1]["externalVariantId"])
        self.assertEqual("option_combination", p["variants"][0]["sourceAttributes"]["identityKind"])
        self.assertIsNone(tilda_product(single | {"partuids": "[380432064832]"}, "singlestep.by"))
        sova = single | {"title": "Кофе в зернах KOF. Эфиопия", "brand": "KOF",
                         "url": "https://сова.бел/coffee/10", "properties": [],
                         "editions": [{"uid": 11, "Вид фасовки": "1000 г.", "quantity": "0", "price": "77"}]}
        p = tilda_product(sova, "xn--80ad9al.xn--90ais")
        self.assertEqual("KOF", p["roasterName"])
        self.assertEqual("Сова", p["sellerName"])
        self.assertFalse(p["variants"][0]["availableForPurchase"])
        self.assertEqual(1000, p["variants"][0]["weightGrams"])
        with self.assertRaises(ParseError):
            variant("1", "6 x 11 г", "42", "BYN", True)

        class Daloni:
            sources = {}
            def fetch(self, url):
                if url.endswith("/punch/"):
                    return '<div class="product_info_content">Описание сорта</div>'
                return ('<div class="single_product"><a data-wishlist data-id="527"></a>'
                        '<h4 class="product_name"><a href="/catalog/monosorta/punch/">Punch Blend</a></h4>'
                        '<form data-action="addToCart"><select name="props[GRIND][VALUE]">'
                        '<option value="Не молоть (в зернах)"></option></select><button class="addto_cart_btn"></button>'
                        '<div class="product_tp__item"><input name="productId" value="528" id="offer_527_528" data-is-drip="N">'
                        '<span class="product_tp__value">250 г.</span>'
                        '<span class="product_price">26 <sup class="product_price--old">31</sup> byn.</span>'
                        '</div></form></div>')

        discounted = collect_daloni(Daloni())["products"][0]["variants"][0]
        self.assertEqual("26.00", discounted["price"])

        class Pages:
            sources = {}
            urls = []

            def fetch(self, url):
                self.urls.append(url)
                return ('<div class="card">coffee</div>' + ('<a href="?PAGEN_1=1#">1</a>'
                        '<a href="?PAGEN_1=2&">2</a><a href="?PAGEN_1=02#">2</a>' if "?" not in url else ""))

        pages = Pages()
        cards, count = html_pages(pages, "https://sorso.by/catalog/", ".card")
        self.assertEqual(2, count)
        self.assertEqual(2, len(cards))
        self.assertEqual("https://sorso.by/catalog/?PAGEN_1=2", pages.urls[-1])
        selectors = BeautifulSoup('<div class="sku-props__inner" data-id="1"><button data-onevalue="1" data-title="Не молоть"></button></div>'
                                  '<div class="sku-props__inner" data-id="2"><button data-onevalue="2" data-title="250 г"></button></div>', "html.parser")
        self.assertEqual(("PROP_1", "PROP_2"), bitrix_options(selectors)[1:])
        class MissingHeaders:
            sources = {}
            def fetch(self, url):
                self.sources[url] = {}
                return "[]"

        with self.assertRaisesRegex(ParseError, "pagination counters"):
            woo_rows(MissingHeaders(), "kcr.by", {"category": 16})

        # Build the same flattened reference representation as Nuxt, without executing source JS.
        values = []

        def flatten(value):
            index = len(values)
            values.append(None)
            if isinstance(value, dict):
                values[index] = {k: flatten(v) for k, v in value.items()}
            elif isinstance(value, list):
                values[index] = [flatten(v) for v in value]
            else:
                values[index] = value
            return index

        p = {"id": 780, "name": "Натти", "slug": "/coffee/espresso-nutty", "is_coffee_bean": True,
             "without": 799, "is_archive": False, "price": 739, "processing_method": "смесь",
             "selectable_options": [{"values": [{"id": 1, "grind_size": "grain", "title": "в зёрнах", "price": 0}]}],
             "measurable_option": {"values": [{"id": 2, "title": "1000 г", "price": 1760,
                                                "price_without_discount": 2956}]}}
        branch = flatten([{"meta": {"current_page": 1, "total": 1, "last_page": 1}, "data": [p]}])
        data = len(values); values.append({"catalog-data-coffee--": branch})
        data_wrapper = len(values); values.append(["ShallowReactive", data])
        root = len(values); values.append({"data": data_wrapper})
        values.insert(0, ["ShallowReactive", root + 1])
        # Shift references after inserting the outer wrapper.
        for n in values[1:]:
            if isinstance(n, dict):
                for k in n: n[k] += 1
            elif isinstance(n, list):
                for i in range(1 if n and isinstance(n[0], str) else 0, len(n)): n[i] += 1
        markup = ('<script id="__NUXT_DATA__">' + json.dumps(values) + '</script>'
                  '<div class="product-item"><a href="/coffee/espresso-nutty">Натти</a><button class="tc-btn-pay">Купить</button></div>')

        class Nuxt:
            sources = {}
            def fetch(self, url):
                if url.endswith("/espresso-nutty"):
                    # Detail payload uses a dictionary rather than the catalogue list.
                    detail_values = json.loads(json.dumps(values))
                    catalogue_index = detail_values[detail_values[detail_values[0][1]]["data"]][1]
                    branch_index = detail_values[catalogue_index]["catalog-data-coffee--"]
                    envelope = detail_values[branch_index][0]
                    products_index = detail_values[envelope]["data"]
                    detail_values[envelope]["data"] = detail_values[products_index][0]
                    return '<script id="__NUXT_DATA__">' + json.dumps(detail_values) + '</script>'
                return markup

        result = collect_tasty(Nuxt())
        self.assertEqual("2499.00", result["products"][0]["variants"][0]["price"])
        self.assertEqual("RUB", result["products"][0]["variants"][0]["currency"])
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as directory:
            with self.assertRaisesRegex(ParseError, "read-only"):
                Fetcher(Path(directory), hosts=("mcoffee.by",)).fetch("https://mcoffee.by/cart/add", payload={})


if __name__ == "__main__":
    unittest.main()
