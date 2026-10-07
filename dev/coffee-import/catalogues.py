"""Additional public catalogues: reuse transport and the snapshot contract."""

import json
import re
from decimal import Decimal
from functools import partial
from urllib.parse import parse_qs, urlencode, urljoin, urlsplit, urlunsplit

from bs4 import BeautifulSoup

from roast_by import ParseError, clean, json_products, money, origins, property_labels, property_value, split_values, weight_grams
from sources import collect_tilda, positive_price, quantity, snapshot, text


NON_BEANS = re.compile(r"дрип|drip|капсул|набор|бокс|\bbox\b|\bсет\b|\bset\b|растворим", re.I)


def variant(uid, weight, price, currency, available, attributes=None):
    grams = weight_grams(weight)
    if not clean(uid) or not grams or grams <= 0 or currency not in ("BYN", "RUB") or type(available) is not bool:
        raise ParseError(f"Invalid variant identity/weight/currency/availability: {uid}, {weight}, {currency}")
    return {"externalVariantId": str(uid), "weightGrams": grams, "price": positive_price(price),
            "currency": currency, "availableForPurchase": available, "sourceAttributes": attributes or {}}


def product(source, uid, url, name, roaster, variants, *, properties=None, description="", images=None,
            warnings=None, kind="roasted_beans", blend=None):
    parsed = urlsplit(url)
    url = urlunsplit(parsed._replace(netloc=parsed.netloc.encode("idna").decode("ascii")))
    if not clean(name) or not clean(uid) or urlsplit(url).netloc != source or urlsplit(url).scheme != "https":
        raise ParseError(f"Invalid product identity/URL: {url}")
    properties = dict(properties or {})
    country = property_value(properties, "Страна произрастания", "Страна", "Страны", "Происхождение", "Страна происхождения")
    if country and re.fullmatch(r"моносорт|бленд|смесь|single[-_ ]origin|blend", country, re.I):
        country = None
    if country:
        properties.setdefault("Страна произрастания", country)
    if blend is None:
        identity = name + " " + clean(properties.get("Состав")) + " " + clean(properties.get("Происхождение"))
        blend = bool(re.search(r"\b(?:смесь|бленд|blend|купаж)\b", identity, re.I))
        if not blend:
            blend = any(re.search(r"^\s*(?:[\w-]+\s+){0,4}(?:смесь|бленд|купаж)\b", sentence, re.I)
                        and not re.search(r"\bне\b|\b(?:как|например|попробуйте)\b", sentence, re.I)
                        for sentence in re.split(r"(?<=[.!?])\s+|\n+", description))
    countries, notes = origins(properties, name, description, blend)
    roast = property_value(properties, "Степень обжарки", "Степерь обжарки", "Обжарка")
    brew = property_value(properties, "Приготовление", "Метод приготовления", "Способ приготовления", "Для приготовления")
    if roast and re.fullmatch(r"(?:под )?(?:эспрессо|фильтр|омни(?:рост)?)(?:[,/]\s*(?:эспрессо|фильтр|омни(?:рост)?))*", roast, re.I):
        brew = brew or roast
        roast = None
    if roast and re.fullmatch(r"(?:под )?молочко", roast, re.I):
        roast = None
    if roast:
        roast = ", ".join(dict.fromkeys(split_values(roast)))
    score = property_value(properties, "Q", "Quality", "SCA", "Оценка Q", "Оценка Q-грейдера", "Оценка SCA", "Оценка по протоколу SCA", "Общая итоговая оценка", "Итоговая оценка", "Q-grader", "SCA score", "Quality score")
    if score and re.fullmatch(r"0+(?:[.,]0+)?", score):
        score = None
    composition = property_value(properties, "Происхождение", "Тип состава")
    features = split_values(property_value(properties, "Особенности кофе", "Особенности", "Вид кофе"))
    return {"sourceKey": source, "sourceProductId": str(uid), "sourceUrl": url, "name": clean(name),
            "description": clean(description) or None,
            "roasterName": roaster, "productKind": kind, "productForm": "whole_beans",
            "availableForPurchase": any(v["availableForPurchase"] for v in variants),
            "compositionKind": "blend" if blend else ("single_origin" if composition and re.fullmatch(r"моносорт|single[-_ ]origin", composition, re.I) else "unknown"), "origins": countries,
            "tasteDescriptors": split_values(property_value(properties, "Во вкусе", "Вкус", "Вкусовой профиль", "Ключевые дескрипторы", "Дескрипторы", "Основные вкусы", "Букет", "Профиль")),
            "processing": property_value(properties, "Обработка", "Способ обработки", "Метод обработки"),
            "roastLevel": roast, "qGraderScoreRaw": score,
            "acidity": property_value(properties, "Кислотность", "Кислинка"), "body": property_value(properties, "Плотность", "Тело"),
            "features": list(dict.fromkeys(features)),
            "brewRecommendations": split_values(brew), "grindOptions": [],
            "imageUrls": list(dict.fromkeys(urljoin(url, x) for x in images or [])), "variants": variants,
            "sourceProperties": properties, "reviewWarnings": sorted(set(notes + (warnings or [])))}


def retain(products, excluded, item):
    if item["availableForPurchase"]:
        products.append(item)
    else:
        excluded.append({"sourceProductId": item["sourceProductId"], "sourceUrl": item["sourceUrl"],
                         "name": item["name"], "reason": "not_purchasable", "variants": item["variants"]})


def labels(description):
    return property_labels(description)


def tilda_product(item, source):
    name = clean(item["title"])
    if NON_BEANS.search(name):
        return None
    props = {p["title"]: p["values"] for p in item.get("properties", [])}
    editions = item.get("editions", [])
    if source == "singlestep.by":
        if not set(json.loads(item["partuids"])) & {688675940832, 229942044012}:
            return None
        if "Не молоть" not in props.get("Помол", "") or len(editions) != 1:
            raise ParseError(f"Single Step bean options changed: {name}")
        stock = quantity(editions[0]["quantity"], unlimited=True)
        variants = []
        for option in props.get("Вес", "").splitlines():
            weight, separator, price = option.partition("=")
            if not separator:
                raise ParseError(f"Missing absolute option price: {name}")
            variants.append(variant(str(item["uid"]) + ":weight:" + str(weight_grams(weight)), weight, price,
                                    "BYN", stock is None or stock > 0,
                                    {"identityKind": "option_combination", "weightOption": option,
                                     "editionUid": str(editions[0]["uid"])}))
        roaster = "Single Step Coffee"
    else:
        if not re.search(r"кофе в з[её]рнах", name, re.I):
            raise ParseError(f"Unclassified Sova coffee card: {name}")
        variants = []
        for e in editions:
            stock = quantity(e["quantity"], unlimited=True)
            variants.append(variant(e["uid"], e.get("Вид фасовки"), e["price"], "BYN", stock is None or stock > 0,
                                    {k: v for k, v in e.items() if k not in ("img", "title")}))
        roaster = clean(item.get("brand")) or None
    if not variants:
        raise ParseError(f"No bean variants: {name}")
    description = item.get("text", "") + "\n" + item.get("descr", "")
    properties = {clean(c["title"]): clean(c.get("value")) for c in item.get("characteristics", [])}
    properties.update(labels(description))
    if source == "singlestep.by":
        # The storefront publishes descriptors separately from its prose description.
        properties.setdefault("Во вкусе", text(item.get("descr", "")))
        match = re.search(r"Подходит для ([^.]+)", text(item.get("text", "")), re.I)
        if match:
            methods = re.findall(r"эспрессо|фильтр|чашке|френч-пресс|аэропресс|пуровер|турке", match[1], re.I)
            if methods:
                properties.setdefault("Метод приготовления", ", ".join(methods))
    properties["partuids"] = json.loads(item["partuids"])
    result = product(source, item["uid"], item["url"], name, roaster, variants, properties=properties,
                     description=text(description), images=[g["img"] for g in json.loads(item.get("gallery") or "[]") if g.get("img")])
    result["sellerName"] = "Сова" if source.startswith("xn--") else roaster
    result["grindOptions"] = props.get("Помол", props.get("Степень помола", "")).splitlines()
    return result


collect_sova = partial(collect_tilda, url="https://xn--80ad9al.xn--90ais/coffe",
                       product_parser=partial(tilda_product, source="xn--80ad9al.xn--90ais"), require_root=False)
collect_singlestep = partial(collect_tilda, url="https://singlestep.by/coffeeshop",
                            product_parser=partial(tilda_product, source="singlestep.by"))


def woo_rows(fetcher, host, params):
    rows, expected, total_pages = [], None, None
    for page in range(1, 101):
        url = f"https://{host}/wp-json/wc/store/v1/products?" + urlencode(params | {"per_page": 100, "page": page})
        batch = json.loads(fetcher.fetch(url))
        headers = fetcher.sources[url].get("paginationHeaders", {})
        if not isinstance(batch, list) or not headers.get("X-WP-Total", "").isdigit() or not headers.get("X-WP-TotalPages", "").isdigit():
            raise ParseError("Missing WooCommerce collection or pagination counters")
        current_total, current_pages = int(headers["X-WP-Total"]), int(headers["X-WP-TotalPages"])
        if expected is not None and (expected, total_pages) != (current_total, current_pages):
            raise ParseError("WooCommerce catalogue changed during pagination")
        expected, total_pages = current_total, current_pages
        rows.extend(batch)
        if page >= total_pages:
            break
    if len(rows) != expected or len({r["id"] for r in rows}) != expected:
        raise ParseError(f"Incomplete WooCommerce collection: {len(rows)} of {expected}")
    return rows, page


def collect_woo(fetcher, host, category, roaster):
    rows, pages = woo_rows(fetcher, host, {"category": category})
    if not rows:
        raise ParseError("Empty bean category")
    ids = [str(v["id"]) for p in rows if not NON_BEANS.search(p["name"]) for v in p.get("variations", [])]
    variations = {}
    for offset in range(0, len(ids), 100):
        batch, _ = woo_rows(fetcher, host, {"type": "variation", "include": ",".join(ids[offset:offset + 100])})
        variations.update({v["id"]: v for v in batch})
    if set(map(int, ids)) != set(variations):
        raise ParseError("Missing or unexpected WooCommerce variations")
    products, excluded = [], []
    for p in rows:
        if NON_BEANS.search(p["name"]):
            excluded.append({"sourceProductId": str(p["id"]), "sourceUrl": p["permalink"], "name": p["name"], "reason": "not_whole_beans"})
            continue
        attributes = {a["name"]: ", ".join(t["name"] for t in a.get("terms", [])) for a in p.get("attributes", [])}
        description = text(p["description"] + "\n" + p["short_description"])
        description_props = labels(p["description"] + "\n" + p["short_description"])
        attr_labels = {clean(k).casefold(): k for k in attributes}
        conflicts = {k: {"attribute": attributes[attr_labels[k.casefold()]], "description": v}
                     for k, v in description_props.items() if k.casefold() in attr_labels
                     and clean(attributes[attr_labels[k.casefold()]]).casefold() != clean(v).casefold()}
        properties = {k: v for k, v in description_props.items() if k.casefold() not in attr_labels} | attributes
        if conflicts:
            properties["conflictingProperties"] = conflicts
        if not property_value(properties, "Во вкусе", "Вкус", "Вкусовой профиль", "Дескрипторы") and host == "kcr.by" and p["short_description"]:
            properties["Во вкусе"] = text(p["short_description"]).splitlines()[0].rstrip(".")
        variants = []
        for ref in p.get("variations", []):
            v = variations[ref["id"]]
            if v["parent"] != p["id"]:
                raise ParseError("WooCommerce variant parent changed")
            values = {a["name"]: a.get("value") for a in ref["attributes"]}
            if values.get("Помол") and not re.search(r"зерн|не молоть", values["Помол"], re.I):
                continue
            weight = re.search(r"Вес:\s*([^,]+)", v["variation"])
            price = v["prices"]
            if not str(price["price"]).isdigit() or type(price["currency_minor_unit"]) is not int or not 0 <= price["currency_minor_unit"] <= 4:
                raise ParseError("Invalid WooCommerce minor-unit price")
            if type(v["is_in_stock"]) is not bool or type(v["is_purchasable"]) is not bool:
                raise ParseError("Missing WooCommerce purchase availability")
            variants.append(variant(v["id"], weight[1] if weight else None,
                                    str(Decimal(price["price"]) / (10 ** price["currency_minor_unit"])),
                                    price["currency_code"], v["is_in_stock"] and v["is_purchasable"], values))
        if not variants:
            raise ParseError(f"No supported WooCommerce bean variants: {p['name']}")
        item = product(host, p["id"], p["permalink"], p["name"], roaster, variants, properties=properties,
                       description=description, images=[i["src"] for i in p["images"]],
                       warnings=["conflicting_source_properties"] if conflicts else [])
        retain(products, excluded, item)
    result = snapshot(fetcher, f"https://{host}", products, excluded, len(rows), pages)
    result["coverage"][0]["catalogueUrl"] = f"https://{host}/wp-json/wc/store/v1/products?category={category}"
    result["coverage"][0]["countUnit"] = "source_cards_including_non_beans"
    return result


collect_kcr = partial(collect_woo, host="kcr.by", category=16, roaster="Kitchen Coffee Roasters")
collect_ideal = partial(collect_woo, host="idealcoffee.by", category=905, roaster="Ideal Coffee")


def html_pages(fetcher, root, selector, page_parameter="PAGEN_1"):
    queue, seen, cards = [root], set(), []
    while queue:
        url = queue.pop(0)
        if url in seen:
            continue
        if len(seen) >= 100:
            raise ParseError("HTML catalogue exceeds 100 pages")
        soup = BeautifulSoup(fetcher.fetch(url), "html.parser")
        batch = soup.select(selector)
        if not batch:
            raise ParseError(f"Empty or changed catalogue: {url}")
        cards.extend((url, card) for card in batch)
        seen.add(url)
        for link in soup.select("a[href]"):
            target = urljoin(url, link["href"])
            parsed, original = urlsplit(target), urlsplit(root)
            query = parse_qs(parsed.query)
            if parsed.netloc == original.netloc and parsed.path == original.path and set(query) == {page_parameter}:
                if len(query[page_parameter]) != 1 or not query[page_parameter][0].isdigit():
                    raise ParseError("Invalid HTML pagination")
                number = int(query[page_parameter][0])
                if number < 1:
                    raise ParseError("Invalid HTML page number")
                target = (root if number == 1 else urlunsplit(original._replace(
                    query=urlencode({page_parameter: number}), fragment="")))
                if target not in seen and target not in queue:
                    queue.append(target)
    return cards, len(seen)


def collect_blaser(fetcher):
    root = "https://www.blasercafe.by/coffeeorder/"
    first = BeautifulSoup(fetcher.fetch(root), "html.parser")
    urls = list(dict.fromkeys([root] + [urljoin(root, a["href"]) for a in first.select("a[href]")
                                     if re.fullmatch(r"/coffeeorder/[a-z-]+/", a["href"])]))
    products, excluded = [], []
    for url in urls:
        soup = first if url == root else BeautifulSoup(fetcher.fetch(url), "html.parser")
        card = soup.select_one(".single-product-slider-element")
        if card is None:
            raise ParseError(f"Missing Blaser detailed card: {url}")
        name = text(str(card.select_one(".single-product-slider-top-name")))
        properties = {text(str(n.select_one(".title"))): text(str(n.select_one(".value")))
                      for n in card.select(".single-product-slider-content-param")}
        if not re.search("Зерна", properties.get("Упаковка", ""), re.I):
            if re.search("Молотый", properties.get("Упаковка", ""), re.I):
                excluded.append({"sourceProductId": "url:" + urlsplit(url).path, "sourceUrl": url,
                                 "name": name, "reason": "ground_only"})
                continue
            raise ParseError(f"Unsupported Blaser product form: {name}")
        variants = []
        for n in card.select(".single-product-slider-top-right-element"):
            spans = n.find_all("span", recursive=False)
            buy = n.select_one(".addToCart[data-id]")
            if len(spans) != 3 or buy is None or not re.search(r"\bBYN\b", text(str(spans[-1]))):
                raise ParseError(f"Unknown Blaser weight/price/availability: {name}")
            variants.append(variant(buy["data-id"], text(str(spans[0])), money(text(str(spans[-1]))), "BYN", True))
        if not variants:
            raise ParseError(f"No Blaser variants: {name}")
        item = product("www.blasercafe.by", "url:" + urlsplit(url).path, url, name, "BlaserCafe", variants,
                       properties=properties, description=text(str(card.select_one(".single-product-slider-content-text"))),
                       images=[n["src"] for n in card.select("img.single-product-slider-img[src]")],
                       blend=True if len(properties.get("Происхождение", "").split(",")) > 1 else None)
        retain(products, excluded, item)
    return snapshot(fetcher, root, products, excluded, None, len(urls), discovered=len(urls))


def nuxt_catalogue(html, *, detail=False):
    node = BeautifulSoup(html, "html.parser").select_one("script#__NUXT_DATA__")
    if node is None:
        raise ParseError("Missing Tasty Coffee SSR payload")
    values = json.loads(node.get_text())
    memo = {}

    def unpack(index, depth=0):
        if index in (-1, -2):
            return None
        if type(index) is not int or not 0 <= index < len(values) or depth > 100:
            raise ParseError("Invalid Nuxt reference")
        if index in memo:
            return memo[index]
        value = values[index]
        if isinstance(value, dict):
            result = memo[index] = {}
            result.update((k, unpack(v, depth + 1)) for k, v in value.items())
            return result
        if isinstance(value, list):
            if value and isinstance(value[0], str):
                if value[0] not in ("Reactive", "ShallowReactive", "Ref", "ShallowRef") or len(value) != 2:
                    raise ParseError(f"Unsupported Nuxt tag: {value[0]}")
                return unpack(value[1], depth + 1)
            result = memo[index] = []
            result.extend(unpack(v, depth + 1) for v in value)
            return result
        return value

    # Read only the catalogue branch; no JavaScript execution or runtime configuration export.
    root = values[values[0][1]]
    data = values[values[root["data"]][1]]
    keys = [k for k in data if k.startswith("catalog-data-coffee-")]
    if len(keys) != 1:
        raise ParseError("Unexpected Tasty Coffee catalogue state")
    result = unpack(data[keys[0]])
    if not isinstance(result, list) or not result or not isinstance(result[0], dict):
        raise ParseError("Unexpected Tasty Coffee catalogue payload")
    if detail:
        data = result[0].get("data")
        if not isinstance(data, dict):
            raise ParseError("Unexpected Tasty Coffee detail payload")
        return data
    return result[0]


def tasty_details(data):
    properties = {clean(p["name"]): clean(p.get("value")) for p in data.get("additional_info", [])}
    description = text(data.get("description", ""))
    properties.update({k: v for k, v in labels(data.get("description", "")).items() if k not in properties})
    descriptor_soup = BeautifulSoup(data.get("mini_description") or "", "html.parser")
    descriptors = [clean(n.get_text()) for n in descriptor_soup.select(".descriptor-badge")]
    if descriptors:
        properties["Во вкусе"] = ", ".join(descriptors)
    properties.setdefault("Обработка", clean(data.get("processing_method")))
    properties["Кислотность"] = clean(data.get("sourness"))
    properties["Плотность"] = clean(data.get("saturation"))
    # 0 and is_skip_q_label mean the source does not publish a cupping score.
    if data.get("rating_q") and not data.get("is_skip_q_label"):
        properties["Q"] = clean(data["rating_q"])
    properties["sourceFlags"] = {k: data[k] for k in ("is_decaf", "is_lowcaf", "is_for_milk", "is_skip_q_label")
                                 if k in data}
    return properties, description


def collect_tasty(fetcher):
    root = "https://shop.tastycoffee.ru/coffee"
    products, excluded, seen = [], [], set()
    expected, last_page, discovered = None, None, 0
    for page in range(1, 101):
        url = root if page == 1 else root + "?page=" + str(page)
        html = fetcher.fetch(url)
        data = nuxt_catalogue(html)
        cards = BeautifulSoup(html, "html.parser").select(".product-item")
        meta = data["meta"]
        if meta["current_page"] != page or type(meta["total"]) is not int or not 1 <= meta["last_page"] <= 100:
            raise ParseError("Invalid Tasty Coffee pagination")
        if expected is not None and (expected, last_page) != (meta["total"], meta["last_page"]):
            raise ParseError("Tasty Coffee count changed during crawl")
        expected, last_page = meta["total"], meta["last_page"]
        for p in data["data"]:
            if p["id"] in seen:
                raise ParseError("Duplicate Tasty Coffee product")
            seen.add(p["id"])
            discovered += 1
            if not p.get("is_coffee_bean") or NON_BEANS.search(p["name"]):
                excluded.append({"sourceProductId": str(p["id"]), "sourceUrl": urljoin(root, p["slug"]),
                                 "name": p["name"], "reason": "not_whole_beans"})
                continue
            grind = [v for o in p.get("selectable_options", []) for v in o["values"] if v.get("grind_size") == "grain"]
            if not grind or len(grind) != 1 or Decimal(str(grind[0]["price"])) != 0:
                raise ParseError(f"Unsupported Tasty Coffee grain price: {p['name']}")
            if type(p["is_archive"]) is not bool:
                raise ParseError("Unknown Tasty Coffee purchase availability")
            matching = [c for c in cards if any(urlsplit(a["href"]).path == urlsplit(p["slug"]).path for a in c.select("a[href]"))]
            if len(matching) != 1:
                raise ParseError(f"Missing or duplicate Tasty Coffee display card: {p['name']}")
            buy = matching[0].select_one("button.tc-btn-pay")
            available = buy is not None and not buy.has_attr("disabled") and not p["is_archive"]
            variants = [variant(f"{p['id']}:weight:{v['id']}", v["title"],
                                str(Decimal(str(p["price"])) + Decimal(str(v["price"]))), "RUB", available,
                                {"identityKind": "option_combination", "weightOptionId": v["id"],
                                 "grindOptionId": grind[0]["id"], "priceWithoutDiscount": v.get("price_without_discount")})
                        for v in p["measurable_option"]["values"]]
            if not variants:
                raise ParseError("Missing Tasty Coffee weights")
            detail = nuxt_catalogue(fetcher.fetch(urljoin(root, p["slug"])), detail=True)
            if detail.get("id") != p["id"] or detail.get("slug") != p["slug"]:
                raise ParseError("Tasty Coffee detail/catalogue product mismatch")
            properties, description = tasty_details(detail)
            item = product("shop.tastycoffee.ru", p["id"], urljoin(root, p["slug"]), p["name"], "Tasty Coffee", variants,
                           properties=properties, description=description, images=p.get("images", []),
                           warnings=["availability_reported_at_product_level"],
                           blend=p.get("processing_method") == "смесь" or None)
            item["features"].extend(label for key, label in (("is_decaf", "Без кофеина"), ("is_lowcaf", "Пониженное содержание кофеина"))
                                    if detail.get(key) is True)
            item["sourceProperties"]["priceWithoutDiscount"] = p["without"]
            item["brewRecommendations"] = list(dict.fromkeys(([p["used_for"]] if p.get("used_for") else []) +
                [m["name"] for m in detail.get("cooking_methods", []) if m.get("name")]))
            item["grindOptions"] = list(dict.fromkeys(v["title"] for o in detail.get("selectable_options", [])
                                                      for v in o.get("values", []) if v.get("grind_size")))
            retain(products, excluded, item)
        if page == last_page:
            break
    if discovered != expected:
        raise ParseError(f"Incomplete Tasty Coffee catalogue: {discovered} of {expected}")
    return snapshot(fetcher, root, products, excluded, expected, page, discovered=discovered)


def offer_template(node):
    template = node.select_one(".offers-template-json")
    if template is None:
        raise ParseError("Missing Bitrix variant template")
    child = template.select_one("[data-json]")
    offers = json.loads(child["data-json"] if child else template.get_text())
    if not isinstance(offers, list) or not offers or len({str(o["ID"]) for o in offers}) != len(offers):
        raise ParseError("Invalid Bitrix variant list")
    return offers


def bitrix_options(soup, old=False):
    options = {}
    if old:
        for n in soup.select(".sku_props [data-treevalue]"):
            prop, value = n["data-treevalue"].split("_")
            options.setdefault("PROP_" + prop, {})[value] = clean(n.get_text())
    else:
        for group in soup.select(".sku-props__inner[data-id]"):
            options["PROP_" + group["data-id"]] = {
                n["data-onevalue"]: clean(n.get("data-title", n.get_text()))
                for n in group.select("[data-onevalue]")}
    grain, weights = None, None
    for prop, values in options.items():
        if any(re.fullmatch(r"не молоть|в з[её]рнах|з[её]рна", v, re.I) for v in values.values()):
            grain = prop
        if values and all(weight_grams(v) for v in values.values()):
            weights = prop
    if not grain or not weights:
        raise ParseError("Missing Bitrix whole-bean or weight selector")
    return options, grain, weights


def card_images(card):
    images = []
    for node in card.select("img"):
        url = next((node.get(key) for key in ("data-big", "data-js", "data-src", "src")
                    if node.get(key) and not node[key].startswith("data:")), None)
        if url:
            images.append(url)
        if node.get("dop-img"):
            images.append(node["dop-img"])
    return list(dict.fromkeys(images))


def aspro_details(soup, categories=None):
    sections = soup.select(".catalog-detail__desc-plain .content--max-width, .js-detail-description, .detail-text-wrap")
    if not sections:
        raise ParseError("Missing Aspro product description section")
    description = "\n".join(text(str(n)) for n in sections)
    properties = labels("".join(str(n) for n in sections))
    for row in soup.select(".properties__item, .props_list tr"):
        label = row.select_one(".properties__name, .char_name")
        value = row.select_one(".properties__value, .char_value")
        if label is not None and value is not None:
            properties.setdefault(clean(label.get_text()), clean(value.get_text()))
    back = soup.select_one("a.back-url[href]")
    category = (categories or {}).get(urlsplit(back["href"]).path) if back else None
    if category:
        properties.setdefault("Приготовление", category)
        properties["catalogueCategory"] = {"name": category, "url": back["href"]}
    return properties, description


def collect_aspro(fetcher, host, path, roaster):
    old = host == "marconi.by"
    root = f"https://{host}{path}"
    cards, pages = html_pages(fetcher, root, ".catalog_item_wrapp" if old else ".catalog-block__item")
    categories = {}
    if host == "sensecoffee.by" and cards:
        catalogue = cards[0][1].find_parent("html")
        if catalogue is not None:
            categories = {urlsplit(a["href"]).path: text(str(a)) for a in catalogue.select("a.sections-block__item-link[href]")
                          if re.fullmatch(r"Эспрессо|Фильтр|Омнирост", text(str(a)), re.I)}
    products, excluded, seen = [], [], set()
    for page_url, card in cards:
        match = re.fullmatch(r"bx_\d+_(\d+)", card.get("id", ""))
        if not match:
            raise ParseError("Missing Bitrix parent product ID")
        uid = match[1]
        if uid in seen:
            raise ParseError("Duplicate Bitrix product across catalogue pages")
        seen.add(uid)
        link = next((a for a in card.select("a[href]") if a["href"].startswith(path) and urlsplit(a["href"]).path != path), None)
        if link is None:
            raise ParseError("Missing Bitrix product URL")
        parsed = urlsplit(urljoin(root, link["href"]))
        url = urlunsplit(parsed._replace(query="", fragment=""))
        soup = BeautifulSoup(fetcher.fetch(url), "html.parser")
        name = clean(soup.select_one("h1").get_text())
        if NON_BEANS.search(name):
            excluded.append({"sourceProductId": uid, "sourceUrl": url, "name": name, "reason": "not_whole_beans"})
            continue
        options, grain, weights = bitrix_options(soup, old)
        offers = offer_template(card)
        whole = [o for o in offers if re.fullmatch(r"не молоть|в з[её]рнах|з[её]рна", options[grain].get(str(o["TREE"].get(grain)), ""), re.I)]
        if not whole:
            raise ParseError(f"No Bitrix whole-bean offers: {url}")
        variants = []
        for offer in whole:
            query = "t" if old else "oid"
            sku_url = url + "?" + urlencode({query: offer["ID"]})
            detail = BeautifulSoup(fetcher.fetch(sku_url), "html.parser")
            if old:
                scope = detail.select_one(".product-info")
                buy = scope.select_one(f'.to-cart[data-item="{offer["ID"]}"]') if scope else None
                available = str(offer.get("CAN_BUY")) in ("1", "Y", "True")
                if available and buy is None:
                    raise ParseError("Bitrix selected SKU/purchase button mismatch")
            else:
                scope = detail.select_one(".catalog-detail__main")
                selector = detail.select_one(".sku-props[data-offer-id]")
                if selector is None or selector["data-offer-id"] != str(offer["ID"]) or selector["data-item-id"] != uid:
                    raise ParseError("Bitrix selected SKU/parent mismatch")
                buy = scope.select_one(f'.to_cart[data-id="{offer["ID"]}"]') if scope else None
                state = scope.select_one("[itemprop=availability]") if scope else None
                if buy is not None:
                    available = not buy.has_attr("disabled")
                elif state and state.get("href", "").endswith(("OutOfStock", "SoldOut", "Discontinued")):
                    available = False
                else:
                    raise ParseError("Unknown Bitrix purchase availability")
            # The page also embeds schema metadata for every unselected SKU. Use the visible price block.
            price_scope = scope.select_one(".prices_block" if old else ".price") if scope else None
            prices = {n["content"] for n in price_scope.select('[itemprop=price][content]')} if price_scope else set()
            currencies = {n["content"] for n in price_scope.select('[itemprop=priceCurrency][content]')} if price_scope else set()
            if len(prices) != 1 or currencies != {"BYN"}:
                raise ParseError(f"Ambiguous selected Bitrix price/currency: {sku_url}, {prices}")
            attrs = {k: options.get(k, {}).get(str(v), str(v)) for k, v in offer["TREE"].items()}
            variants.append(variant(offer["ID"], attrs[weights], prices.pop(), "BYN", available, attrs))
        properties, description = aspro_details(soup, categories)
        item = product(host, uid, url, name, roaster, variants, properties=properties,
                       description=description, images=card_images(card))
        item["grindOptions"] = list(dict.fromkeys(options[grain].values()))
        retain(products, excluded, item)
    return snapshot(fetcher, root, products, excluded, None, pages, discovered=len(seen))


collect_sorso = partial(collect_aspro, host="sorso.by", path="/catalog/kofe/", roaster="SORSO")
collect_sense = partial(collect_aspro, host="sensecoffee.by", path="/catalog/", roaster="Sense Coffee")
collect_marconi = partial(collect_aspro, host="marconi.by", path="/coffee/", roaster="Marconi")


def welder_details(soup):
    card = soup.select_one(".coffee-card")
    if card is None:
        raise ParseError("Missing Welder coffee characteristic card")
    properties = labels("".join(str(n) for n in card.select(".coffee-card__top-prop, .coffee-card__props-left__item")))
    # Cupping judge subscores are different from retail intensity; keep them only as evidence.
    properties["cuppingSubscores"] = {clean(n.select_one(".valuer-value-name").get_text(" ", strip=True)):
                                     clean(n.select_one(".valuer-value").get_text())
                                     for n in soup.select(".valuer-value-box")
                                     if n.select_one(".valuer-value-name") and n.select_one(".valuer-value")}
    description = "\n".join(text(str(n)) for n in soup.select(".infobox-desc p"))
    return properties, description


def collect_welder(fetcher):
    host = "theweldercatherine.ru"
    products, excluded, seen, coverage = [], [], set(), []
    identities = {}
    for path in ("/catalog/dlya_espresso/", "/catalog/pod_molochko_1/", "/catalog/dlya_filtra/"):
        root = "https://" + host + path
        cards, pages = html_pages(fetcher, root, ".product-item")
        urls = set()
        for page_url, card in cards:
            link = next((a for a in card.select("a[href]") if a["href"].startswith(path) and urlsplit(a["href"]).path != path), None)
            if link is None:
                raise ParseError("Missing Welder product URL")
            url = urljoin(root, link["href"])
            if url in urls:
                raise ParseError("Duplicate Welder catalogue card")
            urls.add(url)
            if url in seen:
                continue
            seen.add(url)
            html = fetcher.fetch(url)
            soup = BeautifulSoup(html, "html.parser")
            source = json_products(soup)
            if len(source) != 1 or source[0].get("offers", {}).get("priceCurrency") != "RUB":
                raise ParseError("Missing Welder currency/product metadata")
            buy = soup.select_one("#add-to-basket-occ[data-id]")
            match = re.search(r"window\.sku\s*=\s*", html)
            if buy is None or not buy["data-id"].isdigit() or match is None:
                raise ParseError(f"Missing Welder SKU JSON or parent ID: {url}")
            offers, _ = json.JSONDecoder().raw_decode(html, match.end())
            state = source[0]["offers"].get("availability", "").rsplit("/", 1)[-1]
            if state not in ("InStock", "OutOfStock", "PreOrder", "BackOrder"):
                raise ParseError("Unknown Welder product availability")
            available = state != "OutOfStock" and not buy.has_attr("disabled")
            variants = []
            roasting = {n["value"]: text(str(soup.select_one('label[for="' + n["id"] + '"]')))
                        for n in soup.select("input.roasting-checkbox[value][id]")}
            weight_labels = {n["value"]: n.parent.select_one("[data-weight]")["data-weight"]
                             for n in soup.select("input.weight-checkbox[value][id]")}
            for o in offers:
                if o["weight"] not in weight_labels:
                    raise ParseError("Unknown Welder SKU weight")
                attrs = {k: o[k] for k in ("weight", "roasting", "name", "week_active", "week_sale")}
                purpose = re.search(r"\((?:под )?([^()/]+)/\s*\d", o["name"])
                attrs |= {"roastPurpose": roasting.get(o["roasting"]) or (clean(purpose[1]) if purpose else None),
                          "availabilityEvidence": "product_jsonld_and_buy_button"}
                variants.append(variant(o["id"], weight_labels[o["weight"]], str(o["price"]), "RUB", available, attrs))
            name = clean(soup.select_one("h1").get_text())
            properties, description = welder_details(soup)
            item = product(host, buy["data-id"], url, name, "Сварщица Екатерина", variants,
                           properties=properties, description=description,
                           warnings=["availability_reported_at_product_level"],
                           images=[p["src"] for o in offers for p in (o.get("pictures") or [])])
            if item["sourceProductId"] in identities:
                previous = identities[item["sourceProductId"]]
                if sorted(previous["variants"], key=lambda v: v["externalVariantId"]) != sorted(variants, key=lambda v: v["externalVariantId"]):
                    raise ParseError("Inconsistent Welder parent/SKU aliases")
                previous["sourceProperties"].setdefault("sourceAliases", [previous["sourceUrl"]]).append(url)
                continue
            identities[item["sourceProductId"]] = item
            item["brewRecommendations"] = list(dict.fromkeys(v["sourceAttributes"]["roastPurpose"] for v in variants
                                                             if v["sourceAttributes"]["roastPurpose"]))
            retain(products, excluded, item)
        coverage.append({"productKind": "roasted_beans", "catalogueUrl": root, "pages": pages,
                         "expectedProducts": None, "discoveredProducts": len(urls)})
    result = snapshot(fetcher, "https://" + host + "/catalog/", products, excluded, None, 0, discovered=len(seen))
    result["coverage"] = coverage
    result["uniqueProducts"] = len(identities)
    return result


def daloni_details(soup):
    info = soup.select_one(".product_info_content")
    if info is None:
        raise ParseError("Missing Daloni product description section")
    description = text(str(info))
    properties = labels(str(info))
    for node in soup.select(".product_d_right .product_desc"):
        title = node.select_one("strong")
        value = node.select_one("p")
        if title is not None and value is not None:
            properties[clean(title.get_text())] = clean(value.get_text())
    # Source sliders provide raw numbers but publish no categorical intensity scale.
    properties["flavorSliders"] = {clean(n.select_one(".product_flavor__title").get_text()):
                                  n.select_one(".product_flavor__line")["data-value"]
                                  for n in soup.select(".product_d_right .product_flavor__item")
                                  if n.select_one(".product_flavor__title") and n.select_one(".product_flavor__line[data-value]")}
    return properties, description


def collect_daloni(fetcher):
    host = "dalonicoffee.by"
    products, excluded, seen, coverage = [], [], set(), []
    for path, kind in (("/catalog/monosorta/", "roasted_beans"), ("/catalog/kupazhi/", "roasted_beans"),
                       ("/catalog/zelenyy-kofe/", "green_beans")):
        root = "https://" + host + path
        cards, pages = html_pages(fetcher, root, ".single_product")
        category_ids = set()
        for page_url, card in cards:
            link = card.select_one(".product_name a[href]")
            identity = card.select_one("[data-wishlist][data-id]")
            if link is None or identity is None or not identity["data-id"].isdigit():
                raise ParseError("Missing Daloni product identity")
            uid, name, url = identity["data-id"], clean(link.get_text()), urljoin(root, link["href"])
            if uid in category_ids:
                raise ParseError("Duplicate Daloni card across catalogue pages")
            category_ids.add(uid)
            if uid in seen:
                continue
            seen.add(uid)
            if NON_BEANS.search(name):
                excluded.append({"sourceProductId": uid, "sourceUrl": url, "name": name, "reason": "not_whole_beans"})
                continue
            form = card.select_one('form[data-action="addToCart"]')
            buy = form.select_one(".addto_cart_btn") if form else None
            if form is None or buy is None:
                if re.search(r"Ожидаем|Нет в наличии|Продано", text(str(card.select_one(".label_product"))), re.I):
                    excluded.append({"sourceProductId": uid, "sourceUrl": url, "name": name, "reason": "not_purchasable"})
                    continue
                raise ParseError(f"Unknown Daloni purchase state: {name}")
            grind = [clean(n.get("value")) for n in form.select('select[name="props[GRIND][VALUE]"] option')]
            if kind != "green_beans" and not any(re.search(r"не молоть|в з[её]рнах", g, re.I) for g in grind):
                raise ParseError(f"Daloni whole-bean option missing: {name}")
            detail = BeautifulSoup(fetcher.fetch(url), "html.parser")
            properties, description = daloni_details(detail)
            variants = []
            for n in form.select(".product_tp__item"):
                option = n.select_one('input[name="productId"]')
                if option is None or option.get("data-is-drip") not in ("Y", "N"):
                    raise ParseError("Unknown Daloni offer format")
                if option["data-is-drip"] == "Y":
                    continue
                if option["id"] != f"offer_{uid}_{option['value']}":
                    raise ParseError("Daloni offer/parent mismatch")
                price_node = n.select_one(".product_price")
                if price_node is None:
                    raise ParseError("Missing Daloni variant price")
                for old in price_node.select(".product_price--old"):
                    old.decompose()
                price = text(str(price_node))
                if not re.search("byn", price, re.I):
                    raise ParseError("Missing Daloni currency")
                variants.append(variant(option["value"], text(str(n.select_one(".product_tp__value"))),
                                        money(price), "BYN", not buy.has_attr("disabled"), {"isDrip": False}))
            if not variants:
                single = form.select_one('input[name="productId"][type="hidden"]')
                price = text(str(form.select_one(".current_price")))
                if single is None or single["value"] != uid or not re.search("byn", price, re.I):
                    raise ParseError(f"No Daloni weight variants: {name}")
                detail = BeautifulSoup(fetcher.fetch(url), "html.parser")
                detail_text = detail.get_text(" ", strip=True)
                weight = re.search(r"(?:объ[её]м кофе в [^:]{1,40}|вес(?: упаковки)?)\s*:\s*(\d+(?:[.,]\d+)?\s*(?:кг|гр\.?|г)\b)", detail_text, re.I)
                if weight:
                    variants.append(variant(uid, weight[1], money(price), "BYN", not buy.has_attr("disabled"),
                                            {"weightEvidence": weight[0]}))
                else:
                    variants.append({"externalVariantId": uid, "weightGrams": None, "price": positive_price(money(price)),
                                     "currency": "BYN", "availableForPurchase": not buy.has_attr("disabled"), "sourceAttributes": {}})
            item = product(host, uid, url, name, "Daloni Coffee", variants, kind=kind, blend=path == "/catalog/kupazhi/",
                           properties=properties, description=description,
                           images=[n["src"] for n in card.select(".primary_img img[src]")])
            item["grindOptions"] = grind
            if any(v["weightGrams"] is None for v in variants):
                item["reviewWarnings"].append("variant_weight_not_specified")
            retain(products, excluded, item)
        coverage.append({"productKind": kind, "catalogueUrl": root, "pages": pages,
                         "expectedProducts": None, "discoveredProducts": len(category_ids)})
    result = snapshot(fetcher, "https://" + host + "/catalog/", products, excluded, None, 0, discovered=len(seen))
    result["coverage"] = coverage
    return result


def mcoffee_variant(form):
    uid = form.select_one('input[name="product_id"]')
    option = form.select_one('input[data-name="variants_data[]"][checked]')
    price = form.select_one(".current-price")
    buy = form.select_one(".add_to_cart")
    if uid is None or option is None or price is None:
        raise ParseError("Missing Mcoffee selected variant/weight/price")
    grinds = form.select('input[name="options[]"][checked]')
    if not any(re.fullmatch(r"В з[её]рнах", text(str(form.select_one('label[for="' + n["id"] + '"]'))), re.I) for n in grinds):
        raise ParseError("Mcoffee selected grind is not whole beans")
    label = form.select_one('label[for="' + option["id"] + '"]')
    return variant(uid["value"], text(str(label)), money(text(str(price))), "BYN",
                   buy is not None and not buy.has_attr("disabled"), {"weightOptionId": option["value"]})


def mcoffee_details(soup):
    specs = soup.select_one(".product-specs")
    if specs is None:
        raise ParseError("Missing Manufactura product specifications")
    properties = {}
    for row in specs.select(".spec"):
        label, value = row.select_one(".spec__label"), row.select_one(".spec__value")
        if label is None or value is None:
            raise ParseError("Malformed Manufactura product specification")
        properties[clean(label.get_text())] = clean(value.get_text())
    description = "\n".join(text(str(n)) for n in soup.select(".product__header .product__description, .description__content"))
    properties.update({k: v for k, v in labels(description).items() if k not in properties})
    return properties, description


def collect_mcoffee(fetcher):
    root = "https://mcoffee.by/catalog/coffee/kofe-v-zernah"
    # The site uses a CSRF-protected read-only POST for option selection, separate from cart actions.
    if not fetcher.offline:
        fetcher.refresh = True
    cards, pages = html_pages(fetcher, root, ".products__grid form.product-card", "page")
    soup = BeautifulSoup(fetcher.fetch(root), "html.parser")
    csrf_node = soup.select_one('meta[name="csrf-token"]')
    if csrf_node is None:
        raise ParseError("Missing Mcoffee catalogue CSRF token")
    products, excluded, seen = [], [], {}
    for page_url, form in cards:
        uid = form.get("data-product-item")
        if not uid or not uid.isdigit():
            raise ParseError("Missing Mcoffee product ID")
        link = form.select_one("a.product-category__subTitle[href]")
        if link is None:
            link = form.select_one("a[href^='/products/']")
        if link is None:
            raise ParseError("Missing Mcoffee product link")
        name_node = form.select_one(".product-category__subTitle")
        name = text(str(name_node))
        url = urljoin(root, link["href"])
        if NON_BEANS.search(name):
            excluded.append({"sourceProductId": uid, "sourceUrl": url, "name": name, "reason": "not_whole_beans"})
            continue
        if uid in seen:
            if form.select_one(".add_to_cart") is not None and mcoffee_variant(form) not in seen[uid]["variants"]:
                raise ParseError("Inconsistent repeated Mcoffee parent/SKU card")
            continue
        detail = BeautifulSoup(fetcher.fetch(url), "html.parser")
        metadata = json_products(detail)
        if len(metadata) != 1 or metadata[0].get("offers", {}).get("priceCurrency") != "BYN":
            raise ParseError("Mcoffee store currency changed")
        if form.select_one(".add_to_cart") is None:
            if len(metadata) != 1 or metadata[0].get("offers", {}).get("availability", "").rsplit("/", 1)[-1] != "OutOfStock":
                raise ParseError(f"Mcoffee missing purchase button without OutOfStock evidence: {url}")
            excluded.append({"sourceProductId": uid, "sourceUrl": url, "name": name, "reason": "not_purchasable",
                             "sourceVariantId": form.select_one('input[name="product_id"]')["value"]})
            seen[uid] = {"variants": []}
            continue
        variants = []
        selected = mcoffee_variant(form)
        for option in form.select('input[data-name="variants_data[]"]'):
            label = form.select_one('label[for="' + option["id"] + '"]')
            grams = weight_grams(text(str(label)))
            if not grams:
                raise ParseError("Unsupported Mcoffee option")
            if option.has_attr("checked"):
                variants.append(selected)
                continue
            payload = {"product_id": int(uid), "product_variant_id": int(option["data-product_variant_id"]),
                       "attr_value_id": int(option["value"]), "view": "card"}
            data = json.loads(fetcher.fetch("https://mcoffee.by/products/get-variant", payload=payload, csrf=csrf_node["content"]))
            returned = BeautifulSoup(data.get("html", ""), "html.parser").select_one("form.product-card")
            if data.get("success") is not True or returned is None or returned.get("data-product-item") != uid:
                raise ParseError("Mcoffee option query changed product")
            v = mcoffee_variant(returned)
            if v["weightGrams"] != grams:
                raise ParseError("Mcoffee selected weight mismatch")
            variants.append(v)
        if not variants:
            raise ParseError("Missing Mcoffee variants")
        properties, description = mcoffee_details(detail)
        taste = form.select_one(".compound")
        processing = form.select_one(".processing")
        if taste is not None and not property_value(properties, "Во вкусе", "Вкусовой профиль", "Дескрипторы"):
            properties["Во вкусе"] = text(str(taste))
        if processing is not None and not property_value(properties, "Обработка", "Способ обработки"):
            properties["Обработка"] = text(str(processing))
        item = product("mcoffee.by", uid, url, name, "Manufactura", variants,
                       properties=properties, description=description,
                       images=[i["src"] for i in form.select("img[src]")][:1])
        retain(products, excluded, item)
        seen[uid] = item
    result = snapshot(fetcher, root, products, excluded, None, pages, discovered=len(cards))
    result["coverage"][0] |= {"countUnit": "source_cards_including_non_beans", "uniqueProducts": len(seen)}
    return result
