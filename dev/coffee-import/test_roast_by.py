import html
import json
import unittest

from bs4 import BeautifulSoup

from roast_by import (ParseError, catalogue_page, collect, decode_unicode, money,
                      origins, parse_product, product_card_arguments, unique_groups,
                      weight_grams)


def offer_payload():
    return {
        "combines": {"100": {"MASSA": "small"}, "101": {"MASSA": "large"}},
        "props": {"MASSA": {"VALUES": {"small": {"VALUE": "200"}, "large": {"VALUE": "1000"}}}},
        "additionalData": {
            "100": {"price": {"value": "19.20 руб."}, "canBuy": 1, "max": 0},
            "101": {"price": {"value": "78 руб."}, "canBuy": 0, "max": 0},
        },
    }


def card(payload=None, *, detail=False):
    payload = offer_payload() if payload is None else payload
    expression = "productCard($el, 100, 100, " + json.dumps({"max": 0}) + ", " + json.dumps(payload) + ", `Не молоть`)"
    css = "product-detail-js" if detail else "product-block"
    return '<div class="' + css + '" data-element-id="100" x-data="' + html.escape(expression, quote=True) + '">'


def detail(*, payload=None, country="Бразилия", description="", features="Арабика", name="Кофе"):
    props = [{"name": "Особенности кофе", "value": features}]
    if country is not None:
        props.append({"name": "Страна произрастания", "value": country})
    structured = {"@type": "Product", "name": name, "additionalProperty": props,
                  "offers": {"@type": "Offer", "price": 19.2, "priceCurrency": "BYN",
                             "availability": "https://schema.org/InStock"}}
    return ('<h1>' + name + '</h1>' + card(payload, detail=True) + '</div>'
            '<script type="application/ld+json">' + json.dumps(structured) + '</script>'
            '<div class="tabs-container-v2__item" :class="hidden: activeTab !== \'text\'">'
            '<p>' + description + '</p></div>')


class RoastByParserTests(unittest.TestCase):
    def test_weights_and_exact_decimal_prices(self):
        self.assertEqual(200, weight_grams("200"))
        self.assertEqual(1000, weight_grams("1 кг"))
        self.assertEqual(250, weight_grams("0,25 kg"))
        self.assertIsNone(weight_grams("5 x 200"))
        self.assertEqual("1234.50", money("1\u00a0234,50 руб."))
        self.assertEqual("19.20", money(19.2))

    def test_double_escaped_unicode_preserves_existing_cyrillic(self):
        self.assertEqual({"name": "Кофе Смесь"}, decode_unicode({"name": "Кофе \\u0421\\u043c\\u0435\\u0441\\u044c"}))

    def test_price_and_availability_are_per_variant(self):
        product = parse_product(detail(), "https://roast.by/product/", "roasted_beans")
        self.assertEqual(2, len(product["variants"]))
        self.assertEqual("19.20", product["variants"][0]["price"])
        self.assertTrue(product["variants"][0]["availableForPurchase"])
        self.assertFalse(product["variants"][1]["availableForPurchase"])
        # Made-to-order purchase availability is not a physical inventory count.
        self.assertTrue(product["availableForPurchase"])

    def test_explicit_country_has_precedence_over_description(self):
        product = parse_product(detail(description="Также попробуйте кофе из Эфиопии."),
                                "https://roast.by/product/", "roasted_beans")
        self.assertEqual(["BR"], [o["countryCode"] for o in product["origins"]])
        self.assertNotIn("origin_not_specified", product["reviewWarnings"])
        self.assertEqual("unknown", product["compositionKind"])

    def test_blend_origins_from_composition_keep_unknown_percentages(self):
        product = parse_product(detail(country=None, name="Смесь №8", features="Арабика,Смесь",
                                       description="В составе арабика из <b>Бразилии</b> и <b>Эфиопии</b>."),
                                "https://roast.by/product/", "roasted_beans")
        self.assertEqual(["BR", "ET"], [o["countryCode"] for o in product["origins"]])
        self.assertTrue(all(o["percentage"] is None for o in product["origins"]))
        self.assertEqual("blend", product["compositionKind"])
        self.assertIn("origin_extracted_from_description.composition", product["reviewWarnings"])

    def test_general_description_is_not_blend_composition(self):
        product = parse_product(detail(country=None, name="Смесь", features="Смесь",
                                       description="В Бразилии выращивают кофе. Обработка в Эфиопии отличается."),
                                "https://roast.by/product/", "roasted_beans")
        self.assertEqual([], product["origins"])
        self.assertIn("origin_not_specified", product["reviewWarnings"])

    def test_ambiguous_congo_is_not_assumed_to_be_seed_cg(self):
        result, warnings = origins({"Страна произрастания": "Конго"}, "Конго", "", False)
        self.assertEqual([], result)
        self.assertIn("ambiguous_congo_country", warnings)

    def test_single_country_name_is_accepted_and_keeps_name_evidence(self):
        for name, code in (("Бразилия Серрадо", "BR"), ("Ethiopia Guji", "ET"),
                           ("Фильтр Кения Ньери", "KE")):
            with self.subTest(name=name):
                result, warnings = origins({}, name, "Например, кофе из Колумбии бывает ярким.", False)
                self.assertEqual([code], [o["countryCode"] for o in result])
                self.assertEqual("name", result[0]["evidenceField"])
                self.assertIsNone(result[0]["percentage"])
                self.assertEqual([], warnings)

    def test_uncertain_name_origins_keep_review_warnings(self):
        cases = (
            ("Смесь Бразилия", True, ["BR"], {"origin_extracted_from_name", "blend_composition_may_be_incomplete"}),
            ("Бразилия Эфиопия", False, ["BR", "ET"], {"origin_extracted_from_name"}),
            ("Бразилия и Конго", False, ["BR"], {"origin_extracted_from_name", "ambiguous_congo_country"}),
            ("Конго", False, [], {"origin_not_specified", "ambiguous_congo_country"}),
            ("Смесь", True, [], {"origin_not_specified", "blend_composition_may_be_incomplete"}),
            ("Ява Вайни", False, [], {"origin_not_specified"}),
        )
        for name, blend, codes, expected in cases:
            with self.subTest(name=name):
                result, warnings = origins({}, name, "", blend)
                self.assertEqual(codes, [o["countryCode"] for o in result])
                self.assertEqual(expected, set(warnings))
                self.assertTrue(all(o["evidenceField"] == "name" for o in result))

    def test_name_policy_does_not_override_explicit_origin_or_composition(self):
        result, warnings = origins({"Страна произрастания": "Эфиопия"}, "Бразилия", "", False)
        self.assertEqual(["ET"], [o["countryCode"] for o in result])
        self.assertEqual("properties.Страна произрастания", result[0]["evidenceField"])
        self.assertEqual([], warnings)
        result, warnings = origins({}, "Бразилия", "Кофе из Эфиопии.", False)
        self.assertEqual(["ET"], [o["countryCode"] for o in result])
        self.assertEqual("description.composition", result[0]["evidenceField"])
        self.assertEqual(["origin_extracted_from_description.composition"], warnings)

    def test_source_javascript_is_never_evaluated(self):
        node = BeautifulSoup('<div x-data="productCard($el, 100, 100, doSomething(), {})"></div>', "html.parser").div
        with self.assertRaises(ParseError):
            product_card_arguments(node)

    def test_single_offer_green_coffee_weight_and_stock(self):
        product = parse_product(detail(payload=False, description="Зеленый кофе Колумбия Супремо, 350 грамм"),
                                "https://roast.by/product/", "green_beans")
        self.assertEqual(1, len(product["variants"]))
        self.assertEqual(350, product["variants"][0]["weightGrams"])
        self.assertEqual("19.20", product["variants"][0]["price"])
        self.assertTrue(product["availableForPurchase"])

    def test_grind_options_are_read_without_creating_fictitious_skus(self):
        markup = detail().replace('</div><script',
                    '<div class="select"><ul class="select__body-list">'
                    '<li><span class="text" @click="pomol = `Не молоть`">Не молоть</span></li>'
                    '<li><span class="text" @click="pomol = `Турка`">Турка</span></li></ul></div></div><script', 1)
        product = parse_product(markup, "https://roast.by/product/", "roasted_beans")
        self.assertEqual(["Не молоть", "Турка"], product["grindOptions"])
        self.assertEqual(2, len(product["variants"]))
        self.assertEqual("whole_beans", product["productForm"])

    def test_ground_only_product_is_distinguished_from_beans(self):
        markup = detail().replace('</div><script',
                    '<div class="select"><ul class="select__body-list">'
                    '<li><span class="text" @click="pomol = `Турка`">Турка</span></li>'
                    '</ul></div></div><script', 1)
        product = parse_product(markup, "https://roast.by/product/", "roasted_beans")
        self.assertEqual("ground_only", product["productForm"])

    def test_composition_from_html_list_and_explicit_taste_roles(self):
        markup = detail(country=None, name="Смесь", features="Смесь",
                        description="В этой смеси мы взяли три составляющие:</p>"
                        "<ul><li>Бразилия Сантос</li><li>Эфиопия Сидамо</li>"
                        "<li>Уганда робуста добавляет смеси дымных ноток.</li></ul><p>")
        product = parse_product(markup, "https://roast.by/product/", "roasted_beans")
        self.assertEqual(["BR", "ET", "UG"], [o["countryCode"] for o in product["origins"]])
        result, _ = origins({}, "Эспрессо смесь", "Зерна из Эфиопии. Бразилия балансирует вкус. Уганда робуста добавляет смеси плотности.", True)
        self.assertEqual(["ET", "BR", "UG"], [o["countryCode"] for o in result])

    def test_missing_variant_price_or_availability_fails(self):
        for key in ("price", "canBuy"):
            payload = offer_payload()
            del payload["additionalData"]["101"][key]
            with self.assertRaises(ParseError):
                parse_product(detail(payload=payload), "https://roast.by/product/", "roasted_beans")

    def test_duplicate_groups_are_matched_by_variant_ids_not_name(self):
        a = {"name": "Same name", "variants": [{"externalVariantId": "1"}]}
        b = {"name": "Same name", "variants": [{"externalVariantId": "2"}]}
        self.assertEqual([a, b], unique_groups([a, a, b]))
        with self.assertRaises(ParseError):
            unique_groups([a, {"variants": [{"externalVariantId": "1"}, {"externalVariantId": "2"}]}])

    def test_catalogue_pagination_is_scoped_and_does_not_parse_related_cards(self):
        markup = ('<div class="catalog-product-list">' + card()
                  + '<a class="product-block__title" href="/product/">Кофе</a></div></div>'
                  + '<a href="?PAGEN_1=2">2</a><a href="https://other.example/?PAGEN_1=3">3</a>'
                  + '<a href="/another/?PAGEN_1=4">4</a>'
                  + '<script type="application/ld+json">{"@type":"Product","offers":{"@type":"AggregateOffer","offercount":"1"}}</script>')
        items, pages, expected = catalogue_page(markup, "https://roast.by/catalog/")
        self.assertEqual(1, len(items))
        self.assertEqual(["https://roast.by/catalog/?PAGEN_1=2"], pages)
        self.assertEqual(1, expected)

    def test_incomplete_catalogue_cannot_be_exported_as_complete(self):
        markup = ('<div class="catalog-product-list">' + card()
                  + '<a class="product-block__title" href="/product/">Кофе</a></div></div>'
                  + '<script type="application/ld+json">{"@type":"Product","offers":{"@type":"AggregateOffer","offercount":"2"}}</script>')

        class FakeFetcher:
            def fetch(self, url):
                return markup

        with self.assertRaisesRegex(ParseError, "Incomplete"):
            collect(FakeFetcher(), {"roasted_beans": "https://roast.by/catalog/"})


if __name__ == "__main__":
    unittest.main()
