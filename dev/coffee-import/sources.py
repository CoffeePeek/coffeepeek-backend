"""Public Spiritus catalogue and River Roasters retail storefront adapters."""

import hashlib
import json
import re
from datetime import datetime, timezone
from html import unescape
from urllib.parse import urlencode, urlsplit

from bs4 import BeautifulSoup

from roast_by import ParseError, clean, money, origins, split_values, weight_grams


SPIRITUS_URL = "https://spiritus.by/catalog"
RIVER_URL = "https://espressoclub.by/storefront"
RIVER_DATA = "https://raw.githubusercontent.com/thisorient-dev/tilda-price-updater/main/data.json"
TILDA_ENDPOINTS = ("store.tildaapi.com", "store.tildaapi.biz")


def text(html):
    soup = BeautifulSoup(html or "", "html.parser")
    for br in soup.find_all("br"):
        br.replace_with("\n")
    return "\n".join(clean(line) for line in soup.get_text().splitlines() if clean(line))


def positive_price(value):
    price = money(value)
    if not re.fullmatch(r"\d+(?:[.,]\d+)?", str(value)) or price in (None, "0.00"):
        raise ParseError(f"Invalid positive price: {value!r}")
    return price


def quantity(value, *, unlimited=False):
    if value == "" and unlimited:
        return None  # Tilda disables inventory tracking with an empty quantity.
    if not re.fullmatch(r"\d+", str(value)):
        raise ParseError(f"Invalid stock quantity: {value!r}")
    return int(value)


def snapshot(fetcher, source, products, excluded, expected, pages, *, discovered=None):
    ids = [v["externalVariantId"] for p in products for v in p["variants"]]
    if len(ids) != len(set(ids)):
        raise ParseError("Duplicate variants across exported products")
    return {"schemaVersion": 1, "adapterVersion": "1.0", "source": source,
            "collectedAtUtc": datetime.now(timezone.utc).isoformat(), "complete": True,
            "coverage": [{"productKind": "roasted_beans", "catalogueUrl": source,
                          "pages": pages, "expectedProducts": expected,
                          "discoveredProducts": expected if discovered is None else discovered}],
            "products": products, "excluded": excluded,
            "sourceSnapshots": sorted(fetcher.sources.values(), key=lambda s: s["url"])}


def spiritus_product(item):
    name = clean(unescape(item["title"]))
    url = item["url"]
    if not name or urlsplit(url).scheme != "https" or urlsplit(url).netloc != "spiritus.by":
        raise ParseError("Invalid Spiritus product name or URL")
    editions = item.get("editions", [])
    grain = [e for e in editions if e.get("Тип") == "Зерно"]
    if not grain:
        return None
    variants = []
    for edition in grain:
        uid = str(edition["uid"])
        grams = weight_grams(edition.get("Вес"))
        stock = quantity(edition["quantity"], unlimited=True)
        if not uid.isdigit() or not grams or grams <= 0:
            raise ParseError(f"Missing Spiritus variant ID or weight: {name}")
        variants.append({"externalVariantId": uid, "weightGrams": grams,
                         "price": positive_price(edition["price"]), "currency": "BYN",
                         "availableForPurchase": stock is None or stock > 0,
                         "sourceAttributes": {k: edition[k] for k in
                                              ("Вес", "Тип", "Помол", "sku", "externalid", "quantity") if k in edition}})
    description = text(item.get("text", "")) + "\n" + text(item.get("descr", ""))
    properties = {clean(c["title"]): clean(c.get("value")) for c in item.get("characteristics", [])}
    for label in ("Во вкусе", "Обработка", "Разновидность", "Q", "Плотность", "Кислотность", "Страна"):
        match = re.search(re.escape(label) + r"\s*:\s*([^\n]+)", description, re.I)
        if match:
            properties.setdefault(label, clean(match[1]))
    if properties.get("Страна"):
        properties["Страна произрастания"] = properties["Страна"]
    blend = bool(re.search(r"смесь|бленд", name, re.I))
    countries, warnings = origins(properties, name, description, blend)
    mark = clean(item.get("mark", ""))
    brews = [label for flag, label in (("espresso", "Эспрессо"), ("filter", "Фильтр")) if flag in mark]
    gallery = json.loads(item.get("gallery") or "[]")
    return {"sourceKey": "spiritus.by", "sourceProductId": str(item["uid"]), "sourceUrl": url,
            "name": name, "roasterName": "Spiritus Coffee", "productKind": "roasted_beans",
            "productForm": "whole_beans", "availableForPurchase": any(v["availableForPurchase"] for v in variants),
            "compositionKind": "blend" if blend else "unknown", "origins": countries,
            "tasteDescriptors": split_values(properties.get("Во вкусе")), "processing": properties.get("Обработка"),
            "roastLevel": None, "qGraderScoreRaw": properties.get("Q"), "acidity": properties.get("Кислотность"),
            "body": properties.get("Плотность"), "features": split_values(mark), "brewRecommendations": brews,
            "grindOptions": sorted({e["Помол"] for e in editions if e.get("Помол") not in (None, "", "-")}),
            "imageUrls": [g["img"] for g in gallery if g.get("img")], "variants": variants,
            "sourceProperties": properties | {"mark": mark, "partuids": json.loads(item["partuids"]),
                                               "externalid": item.get("externalid")},
            "reviewWarnings": warnings}


def collect_tilda(fetcher, url=SPIRITUS_URL, product_parser=spiritus_product, *, require_root=True):
    html = fetcher.fetch(url)
    options = re.search(r"options\s*=\s*\{recid:'(\d+)',storepart:'(\d+)'", html)
    if not options or not (re.search(r"currency(?:Txt|Code):'BYN'", html)
                           or 'data-project-currency-code="BYN"' in html):
        raise ParseError("Tilda catalogue block or BYN currency not found")
    recid, part = options.groups()
    endpoint = TILDA_ENDPOINTS[0]
    products, excluded, seen, slices = [], [], set(), set()
    cursor = None
    expected = None
    while True:
        params = {"storepartuid": part, "recid": recid, "getparts": "true", "getoptions": "true",
                  "size": 36, "flag_root": "withroot"}
        if cursor is not None:
            params["slice"] = cursor
        data = json.loads(fetcher.fetch("https://" + endpoint + "/api/getproductslist/?" + urlencode(params)))
        if "redirectto" in data:
            redirected = "store.tildaapi." + str(data["redirectto"])
            if redirected not in TILDA_ENDPOINTS or redirected == endpoint:
                raise ParseError("Unexpected Tilda endpoint redirect")
            endpoint = redirected
            continue
        if data.get("partuid") != int(part) or not isinstance(data.get("products"), list):
            raise ParseError("Unexpected Tilda catalogue response")
        if require_root and not any(p.get("root") and p.get("uid") == int(part) for p in data.get("parts", [])):
            raise ParseError("Tilda block is not the complete root catalogue")
        total = data.get("total")
        if type(total) is not int or total <= 0 or (expected is not None and total != expected):
            raise ParseError("Tilda product count is invalid or changed during crawl")
        expected = total
        for item in data["products"]:
            uid = str(item["uid"])
            if not uid.isdigit() or uid in seen:
                raise ParseError("Duplicate or invalid Tilda product UID")
            seen.add(uid)
            product = product_parser(item)
            if product and product["availableForPurchase"]:
                products.append(product)
            else:
                excluded.append({"sourceUrl": item["url"], "name": clean(unescape(item["title"])),
                                 "reason": "not_purchasable" if product else "not_whole_beans",
                                 "sourceProductId": uid})
                if product:
                    excluded[-1]["variants"] = product["variants"]
        slices.add(cursor)
        cursor = data.get("nextslice")
        if not cursor:
            break
        if not isinstance(cursor, (str, int)) or cursor in slices or len(slices) >= 100:
            raise ParseError("Invalid Tilda pagination")
    if len(seen) != expected:
        raise ParseError(f"Incomplete Tilda catalogue: {len(seen)} of {expected}")
    result = snapshot(fetcher, url, products, excluded, expected, len(slices), discovered=len(seen))
    result["coverage"][0]["countUnit"] = "source_cards_including_non_beans"
    return result


collect_spiritus = collect_tilda


def river_products(data):
    rows = data.get("tableData")
    if not isinstance(rows, list) or not rows or not data.get("lastUpdateTime"):
        raise ParseError("Missing River storefront rows or update timestamp")
    products, excluded, identities = {}, [], set()
    for row in rows:
        name = clean(row["name"])
        stock = quantity(row["count"])
        # The storefront itself classifies beans by these prefixes; drips are a separate category.
        brew = next((prefix for prefix in ("Фильтр", "Эспрессо", "Омнирост") if name.startswith(prefix + " ")), None)
        if not brew:
            excluded.append({"sourceUrl": RIVER_URL, "name": name, "reason": "not_whole_beans"})
            continue
        grams = weight_grams(row["weight"])
        if not grams or grams <= 0:
            raise ParseError(f"Invalid River bean weight: {row['weight']!r}")
        # ponytail: the source has no IDs; renaming needs manual reconciliation, never fuzzy matching.
        product_id = "name:" + hashlib.sha256(name.encode("utf-8")).hexdigest()
        variant_id = product_id + ":" + str(grams)
        if variant_id in identities:
            raise ParseError("Duplicate River name/weight rows")
        identities.add(variant_id)
        if product_id not in products:
            blend = bool(re.search(r"смесь|бленд", name, re.I))
            countries, warnings = origins({}, name, "", blend)
            products[product_id] = {
                "sourceKey": "espressoclub.by", "sourceProductId": product_id, "sourceUrl": RIVER_URL,
                "name": name, "roasterName": "River Roasters", "productKind": "roasted_beans",
                "productForm": "whole_beans", "availableForPurchase": False, "availabilityScope": "in_store",
                "compositionKind": "blend" if blend else "unknown", "origins": countries,
                "tasteDescriptors": [], "processing": None, "roastLevel": None, "qGraderScoreRaw": None,
                "acidity": None, "body": None, "features": [], "brewRecommendations": [brew],
                "grindOptions": [], "imageUrls": [], "variants": [],
                "sourceProperties": {"lastUpdateTime": data["lastUpdateTime"], "identityKind": "name_derived"},
                "reviewWarnings": sorted(set(warnings + ["source_ids_not_provided", "purchase_in_store_only"]))}
        product = products[product_id]
        product["variants"].append({"externalVariantId": variant_id, "weightGrams": grams,
                                    "price": positive_price(row["price"]), "currency": "BYN",
                                    "availableForPurchase": stock > 0,
                                    "sourceAttributes": {"name": name, "weight": row["weight"], "count": row["count"]}})
        product["availableForPurchase"] |= stock > 0
    available = []
    for product in products.values():
        if product["availableForPurchase"]:
            available.append(product)
        else:
            excluded.append({"sourceUrl": RIVER_URL, "name": product["name"], "reason": "not_purchasable",
                             "sourceProductId": product["sourceProductId"], "variants": product["variants"]})
    return available, excluded


def collect_river(fetcher):
    html = fetcher.fetch(RIVER_URL)
    urls = re.findall(r"var DATA_URL\s*=\s*'([^']+)'", html)
    if urls != [RIVER_DATA] or "Цена, BYN" not in html or "Зерно в наличии" not in html:
        raise ParseError("River storefront feed, currency or bean availability scope changed")
    data = json.loads(fetcher.fetch(urls[0]))
    products, excluded = river_products(data)
    result = snapshot(fetcher, RIVER_URL, products, excluded, len(data["tableData"]), 1)
    result["coverage"][0]["countUnit"] = "source_rows_including_drips"
    result["sourceUpdatedAtRaw"] = data["lastUpdateTime"]
    return result
