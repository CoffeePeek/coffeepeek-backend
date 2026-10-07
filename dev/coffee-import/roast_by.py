"""Export the current Roast.by whole-bean catalogue without changing CoffeePeek's DB."""

from __future__ import annotations

import argparse
import csv
import gzip
import hashlib
import json
import re
import sys
import time
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation
from io import BytesIO
from http.cookiejar import CookieJar
from http.client import HTTPException
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import parse_qs, quote, urljoin, urlsplit, urlunsplit
from urllib.request import HTTPCookieProcessor, HTTPRedirectHandler, Request, build_opener

from bs4 import BeautifulSoup


BASE_URL = "https://roast.by"
CATALOGUES = {
    "roasted_beans": BASE_URL + "/catalog/kofe/svezheobzharennyy_kofe/",
    "green_beans": BASE_URL + "/catalog/kofe/zelenyy_kofe/",
}
DEFAULT_OUTPUT = Path(__file__).resolve().parents[1] / "imports" / "roast-by"
ADAPTER_VERSION = "1.0"
COUNTRIES = {
    "CO": ("Колумбия", r"колумби[яиюе]|колумбийск[а-я]+|colombia"),
    "ET": ("Эфиопия", r"эфиопи[яиюе]|ethiopia"),
    "BR": ("Бразилия", r"бразили[яиюе]|бразильск[а-я]+|brazil"),
    "KE": ("Кения", r"кени[яиюе]|kenya"),
    "RW": ("Руанда", r"руанд[аыуе]|rwanda"),
    "CR": ("Коста-Рика", r"коста[\s-]+рик[аиеу]|costa\s+rica"),
    "PE": ("Перу", r"перу|peru"),
    "UG": ("Уганда", r"уганд[аыуе]|uganda"),
    "GT": ("Гватемала", r"гватемал[аыуе]|guatemala"),
    "ID": ("Индонезия", r"индонези[яиюе]|indonesia"),
    "HN": ("Гондурас", r"гондурас[аеу]?|honduras"),
    "BI": ("Бурунди", r"бурунди|burundi"),
    "TZ": ("Танзания", r"танзани[яиюе]|tanzania"),
    "BO": ("Боливия", r"боливи[яиюе]|bolivia"),
    "NI": ("Никарагуа", r"никарагуа|nicaragua"),
    "CN": ("Китай", r"китай|кита[яею]|china"),
    "MX": ("Мексика", r"мексик[аиеу]|mexico"),
    "VN": ("Вьетнам", r"вь?етнам[аеу]?|vietnam"),
    "IN": ("Индия", r"инди[яиюе]|india"),
    "VE": ("Венесуэла", r"венесуэл[аыеу]|venezuela"),
    "ZW": ("Зимбабве", r"зимбабве|zimbabwe"),
    "YE": ("Йемен", r"йемен[аеу]?|yemen"),
    "PG": ("Папуа-Новая Гвинея", r"папуа[\s-]+новая[\s-]+гвинея|papua\s+new\s+guinea"),
    "ZM": ("Замбия", r"замби[яиюе]|zambia"),
    "CM": ("Камерун", r"камерун[аеу]?|cameroon"),
    "LA": ("Лаос", r"лаос[аеу]?|laos"),
    "MW": ("Малави", r"малави|malawi"),
    "PA": ("Панама", r"панам[аыеу]|panama"),
    "SV": ("Сальвадор", r"(?:эль[\s-]+)?саль?вадор[аеу]?|el\s+salvador"),
    "MY": ("Малайзия", r"малайзи[яиюе]|malaysia"),
    "DO": ("Доминиканская Республика", r"доминиканск(?:ая|ой|ую)\s+республик(?:а|и|е|у)|dominican\s+republic"),
}


class ParseError(ValueError):
    pass


def clean(value: object) -> str:
    return re.sub(r"\s+", " ", str("" if value is None else value)).strip()


def decode_unicode(value):
    """The site's HTML attributes contain JSON with doubly escaped Cyrillic."""
    if isinstance(value, str):
        return re.sub(r"\\u([0-9a-fA-F]{4})", lambda m: chr(int(m[1], 16)), value)
    if isinstance(value, list):
        return [decode_unicode(v) for v in value]
    if isinstance(value, dict):
        return {k: decode_unicode(v) for k, v in value.items()}
    return value


def json_products(soup):
    products = []
    for script in soup.select('script[type="application/ld+json"]'):
        try:
            data = json.loads(script.get_text())
        except json.JSONDecodeError as exc:
            raise ParseError("Invalid product JSON-LD") from exc
        entries = data if isinstance(data, list) else data.get("@graph", [data])
        products.extend(x for x in entries if x.get("@type") == "Product")
    return products


def product_card_arguments(node):
    """Read only the two embedded JSON objects; never evaluate website JavaScript."""
    expression = node.get("x-data", "")
    match = re.match(r"productCard\(\$el,\s*(\d+),\s*(\d+),\s*", expression)
    if not match:
        raise ParseError("Missing supported productCard arguments")
    decoder = json.JSONDecoder()
    try:
        quantity, end = decoder.raw_decode(expression, match.end())
        separator = re.match(r"\s*,\s*", expression[end:])
        if separator is None:
            raise ParseError("Missing productCard offer separator")
        offers, _ = decoder.raw_decode(expression, end + separator.end())
    except json.JSONDecodeError as exc:
        raise ParseError("Invalid productCard JSON") from exc
    return match[1], decode_unicode(quantity), decode_unicode(offers)


def money(value) -> str | None:
    if value is None:
        return None
    match = re.search(r"\d[\d\s\u00a0]*(?:[.,]\d+)?", str(value))
    if not match:
        return None
    try:
        amount = Decimal(re.sub(r"\s", "", match[0]).replace(",", "."))
        return str(amount.quantize(Decimal("0.01")))
    except InvalidOperation:
        return None


def weight_grams(value) -> int | None:
    if value is None:
        return None
    text = clean(value).lower().replace(",", ".")
    match = re.fullmatch(r"(\d+(?:\.\d+)?)\s*(кг|kg|гр\.?|г\.?|g)?", text)
    if not match:
        return None
    grams = Decimal(match[1]) * (1000 if match[2] in ("кг", "kg") else 1)
    return int(grams) if grams == grams.to_integral_value() else None


def variants(node, currency="BYN", *, single_offer=None, description=""):
    selected_id, _, payload = product_card_arguments(node)
    if payload is False:
        ecommerce = decode_unicode(json.loads(node.get("data-ecommerce", "{}")))
        source_product = next((x for x in ecommerce.get("products", []) if str(x.get("id")) == selected_id), {})
        offer = single_offer or {}
        availability = offer.get("availability", "").rsplit("/", 1)[-1]
        available = {"InStock": True, "PreOrder": True, "BackOrder": True,
                     "OutOfStock": False, "SoldOut": False, "Discontinued": False}.get(availability)
        weight = re.search(r"(?<!\d)(\d+(?:[.,]\d+)?)\s*(кг|kg|грамм(?:а|ов)?|г)\b", description, re.I)
        attributes = {"MASSA": weight[0]} if weight else {}
        grams = weight_grams(re.sub(r"грамм(?:а|ов)?", "г", weight[0], flags=re.I)) if weight else None
        return selected_id, [{"externalVariantId": selected_id, "weightGrams": grams,
                              "price": money(offer.get("price", source_product.get("price"))),
                              "currency": currency, "availableForPurchase": available,
                              "sourceAttributes": attributes}]
    if not isinstance(payload, dict):
        raise ParseError("Unsupported productCard offer payload")
    combinations = payload.get("combines", {})
    additional = payload.get("additionalData", {})
    props = payload.get("props", {})
    result = []
    for variant_id in sorted(set(combinations) | set(additional), key=int):
        combination = combinations.get(variant_id, {})
        attributes = {}
        for code, xml_id in combination.items():
            attributes[code] = props.get(code, {}).get("VALUES", {}).get(xml_id, {}).get("VALUE")
        data = additional.get(variant_id, {})
        price = money(data.get("price", {}).get("value"))
        can_buy = data.get("canBuy")
        available = None if can_buy is None else str(can_buy).lower() in ("1", "true", "y")
        result.append({
            "externalVariantId": str(variant_id),
            "weightGrams": weight_grams(attributes.get("MASSA")),
            "price": price,
            "currency": currency,
            "availableForPurchase": available,
            "sourceAttributes": attributes,
        })
    if not result or selected_id not in {x["externalVariantId"] for x in result}:
        raise ParseError("Product has no complete offer group")
    return selected_id, result


def catalogue_page(html, url):
    soup = BeautifulSoup(html, "html.parser")
    grid = soup.select_one(".catalog-product-list")
    if grid is None:
        raise ParseError(f"Missing catalogue grid: {url}")
    items = []
    for card in grid.select(".product-block[data-element-id]"):
        link = card.select_one(".product-block__title[href]")
        if link is None:
            raise ParseError(f"Catalogue product is missing its URL: {url}")
        selected, offers = variants(card)
        items.append({"name": clean(link.get_text()), "url": urljoin(url, link["href"]),
                      "selectedId": selected, "variants": offers})
    if not items:
        raise ParseError(f"Empty catalogue: {url}")
    pagination = set()
    for link in soup.select("a[href]"):
        target = urljoin(url, link["href"])
        parsed = urlsplit(target)
        query = parse_qs(parsed.query)
        if parsed.netloc == "roast.by" and parsed.path == urlsplit(url).path and set(query) == {"PAGEN_1"}:
            if len(query["PAGEN_1"]) == 1 and query["PAGEN_1"][0].isdigit():
                pagination.add(target)
    expected = None
    for product in json_products(soup):
        offers = product.get("offers", {})
        if offers.get("@type") == "AggregateOffer":
            count = offers.get("offercount", offers.get("offerCount"))
            if count is not None:
                expected = int(count)
    return items, sorted(pagination), expected


def unique_groups(items):
    """Use the source's actual offer groups, never product name, to remove duplicates."""
    result = []
    seen_ids = set()
    for item in items:
        ids = {v["externalVariantId"] for v in item["variants"]}
        overlap = ids & seen_ids
        if overlap:
            previous = next(x for x in result if overlap & {v["externalVariantId"] for v in x["variants"]})
            if ids != {v["externalVariantId"] for v in previous["variants"]}:
                raise ParseError("Overlapping, inconsistent source offer groups")
            continue
        result.append(item)
        seen_ids.update(ids)
    return result


def country_matches(text):
    found = []
    for code, (name, pattern) in COUNTRIES.items():
        match = re.search(r"(?<!\w)(?:" + pattern + r")(?!\w)", text, re.IGNORECASE)
        if match:
            found.append((match.start(), code, name))
    return [(code, name) for _, code, name in sorted(found)]


def origins(properties, name, description, is_blend):
    raw = properties.get("Страна произрастания", "")
    evidence = "properties.Страна произрастания"
    text = raw
    warnings = []
    if not text:
        # A blend's countries must come from an explicit composition statement,
        # rather than general educational paragraphs elsewhere on the page.
        sentences = re.split(r"(?<=[.!?;])\s+|\n+", description)
        marker = r"в состав|состав[,:]|составляющ|состоит|входят|з[её]рна из|кофе из"
        if is_blend:
            marker += r"|балансирует вкус|добавляет смеси|смес[ьи].*(?:из|и)"
        composition = []
        for index, sentence in enumerate(sentences):
            if re.search(marker, sentence, re.I) and not re.search(r"\b(?:попробуйте|например|бывает)\b", sentence, re.I):
                composition.append(sentence)
                # A composition introduction ending in ':' is followed by the
                # HTML list as the next block, with all of its ingredients.
                if sentence.rstrip().endswith(":") and index + 1 < len(sentences):
                    composition.append(sentences[index + 1])
        text = " ".join(composition)
        evidence = "description.composition"
        if not country_matches(text):
            text = name
            evidence = "name"
    matches = country_matches(text)
    if re.search(r"конго|congo", text, re.I):
        # The seed's CG choice is not evidence for ambiguous external descriptions.
        warnings.append("ambiguous_congo_country")
    if not matches:
        warnings.append("origin_not_specified")
    elif evidence != "properties.Страна произрастания":
        warnings.append("origin_extracted_from_" + evidence)
    if is_blend and evidence == "name":
        warnings.append("blend_composition_may_be_incomplete")
    if is_blend and re.search(r"тр[её]х континент|плантаций.*континент", description, re.I):
        warnings.append("blend_other_origins_not_specified")
    return [{"countryCode": code, "countryNameRu": label, "percentage": None,
             "evidenceField": evidence} for code, label in matches], warnings


def property_value(properties, *names):
    """Match explicit source labels without rewriting their raw values."""
    values = {clean(k).rstrip(":").casefold(): clean(v) for k, v in properties.items()
              if isinstance(v, (str, int, float)) and v is not None}
    return next((values[clean(n).casefold()] for n in names if values.get(clean(n).casefold())), None)


def property_labels(html):
    soup = BeautifulSoup(html or "", "html.parser")
    for node in soup.find_all("br"):
        node.replace_with("\n")
    for node in soup.find_all(["p", "div", "li", "tr"]):
        node.append("\n")
    result = {}
    for line in soup.get_text().splitlines():
        match = re.match(r"^\s*([^:\n]{1,80}):\s*(.+)$", line)
        if not match:
            match = re.match(r"^\s*(Страна(?: происхождения)?|Страны|Степень обжарки|Обжарка|Способ обработки|Обработка|Оценка Q(?:-грейдера)?|Quality|SCA|Q|Кислотность|Плотность|Тело|Дескрипторы|Во вкусе|Вкус|Вкусовой профиль|Метод приготовления)\s+[-–—]\s*(.+)$", line, re.I)
        if not match:
            match = re.match(r"^\s*(Кислотность|Плотность|Тело)\s+(\d+(?:[.,]\d+)?\s*/\s*\d+)\s*$", line, re.I)
        if match:
            result.setdefault(clean(match[1]), clean(match[2]))
    return result


def split_values(value):
    return [clean(x) for x in str(value or "").split(",") if clean(x)]


def parse_product(html, url, kind):
    soup = BeautifulSoup(html, "html.parser")
    top = soup.select_one(".product-detail-js")
    title = soup.select_one("h1")
    if top is None or title is None:
        raise ParseError(f"Missing detail card: {url}")
    structured = [x for x in json_products(soup) if x.get("offers", {}).get("@type") == "Offer"]
    if len(structured) != 1:
        raise ParseError(f"Expected one detailed JSON-LD Product: {url}")
    source = structured[0]
    name = clean(title.get_text())
    offer = source["offers"]
    currency = offer.get("priceCurrency")
    if currency != "BYN":
        raise ParseError(f"Unexpected currency: {currency}")
    properties = {clean(x["name"]): clean(x.get("value")) for x in source.get("additionalProperty", [])}
    description_node = next((n for n in soup.select(".tabs-container-v2__item")
                             if "!== 'text'" in n.get(":class", "")), None)
    paragraphs = description_node.find_all(["p", "ul", "ol"]) if description_node else []
    description = "\n".join(clean(p.get_text(" ", strip=True)) for p in paragraphs)
    if description_node and not description:
        description = clean(description_node.get_text(" ", strip=True))
    selected, offers = variants(top, currency, single_offer=offer, description=description)
    features = split_values(properties.get("Особенности кофе"))
    is_blend = "Смесь" in features or bool(re.search(r"смесь|бленд", name, re.I))
    product_origins, warnings = origins(properties, name, description, is_blend)
    for variant in offers:
        if variant["weightGrams"] is None:
            warnings.append("variant_weight_not_specified:" + variant["externalVariantId"])
        if variant["price"] is None:
            raise ParseError(f"Missing price for variant {variant['externalVariantId']}: {url}")
        if variant["availableForPurchase"] is None:
            raise ParseError(f"Missing availability for variant {variant['externalVariantId']}: {url}")
    ecommerce = decode_unicode(json.loads(top.get("data-ecommerce", "{}")))
    brand = next((x.get("brand") for x in ecommerce.get("products", []) if x.get("brand")), None)
    if not brand:
        brand_link = soup.select_one(".product-brand[href]")
        if brand_link and brand_link["href"] == "/brands/roast_by/":
            brand = "Roast.by"
    images = source.get("image", [])
    if isinstance(images, str):
        images = [images]
    grinds = []
    for dropdown in top.select(".select"):
        if any(re.search(r"\bpomol\s*=", n.get("@click", "")) for n in dropdown.select("span")):
            candidates = dropdown.select(".select__body-list li .text")
            grinds = list(dict.fromkeys(clean(n.get_text()) for n in candidates if clean(n.get_text())))
    if kind == "green_beans" or "Не молоть" in grinds:
        product_form = "whole_beans"
    elif grinds:
        product_form = "ground_only"
    else:
        product_form = "unknown"
        warnings.append("product_form_not_specified")
    return {
        "sourceKey": "roast.by",
        "sourceProductId": selected,
        "sourceUrl": url,
        "name": name,
        "description": clean(description) or None,
        "roasterName": brand,
        "productKind": kind,
        "productForm": product_form,
        "availableForPurchase": any(v["availableForPurchase"] for v in offers),
        "compositionKind": "blend" if is_blend else "unknown",
        "origins": product_origins,
        "tasteDescriptors": split_values(properties.get("Вкусовые дескрипторы")),
        "processing": properties.get("Способ обработки"),
        "roastLevel": properties.get("Степень обжарки"),
        "qGraderScoreRaw": property_value(properties, "Оценка Q-грейдеров", "Q", "Quality", "SCA", "Оценка Q", "Оценка SCA"),
        "acidity": properties.get("Кислотность"),
        "body": properties.get("Плотность"),
        "features": features,
        "brewRecommendations": split_values(properties.get("Рекомендации по приготовлению")),
        "grindOptions": grinds,
        "imageUrls": [urljoin(url, x) for x in images],
        "variants": offers,
        "sourceProperties": properties,
        "reviewWarnings": sorted(set(warnings)),
    }


class Fetcher:
    def __init__(self, cache: Path, *, offline=False, refresh=False, delay=3.0, hosts=("roast.by",)):
        self.cache = cache
        self.offline = offline
        self.refresh = refresh
        self.delay = max(3.0, delay)
        self.last_request = 0.0
        self.sources = {}
        self.hosts = set(hosts)
        fetcher = self

        class SourceRedirects(HTTPRedirectHandler):
            def redirect_request(self, request, fp, code, msg, headers, newurl):
                fetcher.check_url(newurl)
                return super().redirect_request(request, fp, code, msg, headers, newurl)

        self.opener = build_opener(SourceRedirects(), HTTPCookieProcessor(CookieJar()))

    def check_url(self, url):
        parsed = urlsplit(url)
        if parsed.scheme != "https" or parsed.netloc not in self.hosts:
            raise ParseError(f"Unexpected source URL: {url}")

    def fetch(self, url, *, payload=None, csrf=None):
        self.check_url(url)
        body = None if payload is None else json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()
        if body is not None and url != "https://mcoffee.by/products/get-variant":
            raise ParseError("Only the observed read-only Mcoffee variant query permits POST")
        identity = url if body is None else url + "#query=" + body.decode()
        key = hashlib.sha256(identity.encode()).hexdigest()
        content_path = self.cache / (key + ".html")
        metadata_path = self.cache / (key + ".json")
        if content_path.exists() and metadata_path.exists() and not self.refresh:
            data = content_path.read_bytes()
            metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
            if metadata["url"] != url or metadata["sha256"] != hashlib.sha256(data).hexdigest():
                raise ParseError(f"Invalid cached source: {url}")
            if body is not None and (metadata.get("method") != "POST" or metadata.get("query") != payload):
                raise ParseError("Cached variant query does not match its request")
        else:
            if self.offline:
                raise ParseError(f"Page is not cached: {url}")
            data = None
            for attempt in range(3):
                time.sleep(max(0, self.delay - (time.monotonic() - self.last_request)))
                self.last_request = time.monotonic()
                try:
                    request_headers = {"User-Agent": "CoffeePeekCatalogImport/" + ADAPTER_VERSION, "Accept-Encoding": "gzip"}
                    if body is not None:
                        request_headers |= {"Content-Type": "application/json", "Accept": "application/json",
                                            "X-CSRF-TOKEN": csrf or ""}
                    request = Request(url, data=body, headers=request_headers)
                    with self.opener.open(request, timeout=30) as response:
                        self.check_url(response.url)
                        headers = {key: response.headers[key] for key in ("X-WP-Total", "X-WP-TotalPages")
                                   if response.headers.get(key) is not None}
                        data = response.read(4 * 1024 * 1024 + 1)
                        if len(data) > 4 * 1024 * 1024:
                            raise ParseError("Source page exceeds 4 MiB")
                        if data[:2] == b"\x1f\x8b":
                            with gzip.GzipFile(fileobj=BytesIO(data)) as compressed:
                                data = compressed.read(4 * 1024 * 1024 + 1)
                        if len(data) > 4 * 1024 * 1024:
                            raise ParseError("Source page exceeds 4 MiB")
                    break
                except HTTPError as exc:
                    if exc.code not in (429, 500, 502, 503, 504) or attempt == 2:
                        raise
                    time.sleep(3 * (attempt + 1))
                except (URLError, HTTPException, TimeoutError, ConnectionError):
                    if attempt == 2:
                        raise
                    time.sleep(3 * (attempt + 1))
            assert data is not None
            metadata = {"url": url, "fetchedAtUtc": datetime.now(timezone.utc).isoformat(),
                        "sha256": hashlib.sha256(data).hexdigest(), "paginationHeaders": headers}
            if body is not None:
                metadata |= {"method": "POST", "query": payload}
            self.cache.mkdir(parents=True, exist_ok=True)
            content_path.write_bytes(data)
            metadata_path.write_text(json.dumps(metadata, indent=2), encoding="utf-8")
        self.sources[identity] = metadata
        return data.decode("utf-8-sig")


def collect(fetcher, categories=CATALOGUES):
    products = []
    coverage = []
    excluded = []
    for kind, root_url in categories.items():
        queue = [root_url]
        visited = set()
        discovered = []
        expected = None
        while queue:
            url = queue.pop(0)
            if url in visited:
                continue
            if len(visited) >= 100:
                raise ParseError("Unexpected catalogue pagination exceeds 100 pages")
            items, pages, count = catalogue_page(fetcher.fetch(url), url)
            visited.add(url)
            discovered.extend(items)
            queue.extend(p for p in pages if p not in visited)
            if count is not None:
                if expected is not None and count != expected:
                    raise ParseError("Catalogue count changed during crawl; refresh the snapshot")
                expected = count
        grouped = unique_groups(discovered)
        if expected is None or len(grouped) != expected:
            raise ParseError(f"Incomplete {kind} catalogue: discovered {len(grouped)}, expected {expected}")
        coverage.append({"productKind": kind, "catalogueUrl": root_url, "pages": len(visited),
                         "expectedProducts": expected, "discoveredProducts": len(grouped)})
        for index, item in enumerate(grouped, 1):
            product = parse_product(fetcher.fetch(item["url"]), item["url"], kind)
            listing_ids = {v["externalVariantId"] for v in item["variants"]}
            detail_ids = {v["externalVariantId"] for v in product["variants"]}
            if listing_ids != detail_ids:
                raise ParseError(f"Offer group changed between catalogue and detail: {item['url']}")
            if product["availableForPurchase"]:
                products.append(product)
            else:
                excluded.append({"sourceUrl": item["url"], "name": product["name"], "reason": "not_purchasable",
                                 "sourceProductId": product["sourceProductId"], "variants": product["variants"]})
            print(f"{kind}: {index}/{len(grouped)} {product['name']}", flush=True)
    variant_ids = [v["externalVariantId"] for p in products for v in p["variants"]]
    if len(variant_ids) != len(set(variant_ids)):
        raise ParseError("Duplicate variants across exported products")
    return {"schemaVersion": 1, "adapterVersion": ADAPTER_VERSION, "source": BASE_URL,
            "collectedAtUtc": datetime.now(timezone.utc).isoformat(), "complete": True,
            "coverage": coverage, "products": products, "excluded": excluded,
            "sourceSnapshots": sorted(fetcher.sources.values(), key=lambda s: s["url"])}


def markdown_cell(value):
    return clean(value).replace("|", "\\|")


def variant_label(variant):
    label = f"{variant['weightGrams']} г" if variant["weightGrams"] is not None else "Вес не указан"
    purpose = variant.get("sourceAttributes", {}).get("roastPurpose")
    return label + (" / " + purpose if purpose else "")


def normalize_image(url, source_url):
    parsed = urlsplit(urljoin(source_url, url))
    if parsed.scheme not in ("http", "https") or parsed.username or parsed.password or parsed.port not in (None, 80, 443):
        raise ParseError("Unsupported image URL")
    host = (parsed.hostname or "").encode("idna").decode("ascii")
    return urlunsplit(("https", host, quote(parsed.path, safe="/%"), quote(parsed.query, safe="=&%/:?+"), ""))


def write_exports(snapshot, output):
    output.mkdir(parents=True, exist_ok=True)
    products = snapshot["products"]
    # Cached replays retain the date of the actual HTTP observations.
    if snapshot.get("sourceSnapshots"):
        snapshot["collectedAtUtc"] = max(datetime.fromisoformat(s["fetchedAtUtc"]) for s in snapshot["sourceSnapshots"]).isoformat()
    for product in products:
        product["imageUrls"] = list(dict.fromkeys(normalize_image(u, product["sourceUrl"]) for u in product["imageUrls"]))
    columns = ["sourceProductId", "name", "productKind", "productForm", "countryCodes", "countryNames", "roasterName",
               "processing", "roastLevel", "qGraderScoreRaw", "tasteDescriptors", "variants", "reviewWarnings", "sourceUrl"]
    with (output / "coffees.csv").open("w", encoding="utf-8-sig", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=columns)
        writer.writeheader()
        for product in products:
            row = {k: product.get(k) for k in columns}
            row["countryCodes"] = ", ".join(x["countryCode"] for x in product["origins"])
            row["countryNames"] = ", ".join(x["countryNameRu"] for x in product["origins"])
            row["tasteDescriptors"] = ", ".join(product["tasteDescriptors"])
            row["variants"] = "; ".join(f"{variant_label(v)}: {v['price']} {v['currency']} ({'доступен' if v['availableForPurchase'] else 'недоступен'})" for v in product["variants"])
            row["reviewWarnings"] = "; ".join(product["reviewWarnings"])
            writer.writerow(row)
    source_name = urlsplit(snapshot["source"]).netloc
    lines = [f"# Кофе {source_name} в продаже", "", "Снимок (UTC): " + snapshot["collectedAtUtc"], "",
             f"Источник: [{source_name}]({snapshot['source']}). Собраны публичные данные текущей витрины.", "",
             "Валюта указана рядом с ценой из карточки на момент сбора. Доступность означает возможность заказа; остатки склада не выводятся из этого признака.", "",
             "Страны со знаком * извлечены из текстового описания или названия и требуют проверки; основание сохранено в JSON. Неизвестные доли смеси не вычисляются.", ""]
    sections = [
        ("Обжаренное зерно", lambda p: p["productKind"] == "roasted_beans" and p["productForm"] == "whole_beans"),
        ("Зелёное зерно", lambda p: p["productKind"] == "green_beans"),
        ("Продаётся только молотым", lambda p: p["productForm"] == "ground_only"),
        ("Форма требует уточнения", lambda p: p["productForm"] == "unknown"),
    ]
    for title, predicate in sections:
        subset = [p for p in products if predicate(p)]
        if not subset:
            continue
        lines += [f"## {title}: {len(subset)}", "", "| Кофе | Происхождение | Упаковка и цена | Обработка |", "| --- | --- | --- | --- |"]
        for product in subset:
            countries = ", ".join(x["countryNameRu"] + " (" + x["countryCode"] + ")" for x in product["origins"]) or "Не указано"
            if any(o["evidenceField"] != "properties.Страна произрастания" for o in product["origins"]):
                countries += " *"
            if "blend_other_origins_not_specified" in product["reviewWarnings"]:
                countries += "; остальные страны не указаны"
            prices = "; ".join(f"{variant_label(v)} — {v['price']} {v['currency']}" for v in product["variants"] if v["availableForPurchase"])
            lines.append(f"| [{markdown_cell(product['name'])}]({product['sourceUrl']}) | {markdown_cell(countries)} | {markdown_cell(prices)} | {markdown_cell(product['processing'] or 'Не указана')} |")
        lines.append("")
    lines += ["## Полнота сбора", ""]
    for item in snapshot["coverage"]:
        units = {"source_rows_including_drips": "строк витрины, включая дрипы",
                 "source_cards_including_non_beans": "карточек витрины, включая остальные категории"}.get(item.get("countUnit"), "карточек")
        count = (f"{item['discoveredProducts']} из {item['expectedProducts']} {units} по счётчику источника"
                 if item['expectedProducts'] is not None else
                 f"{item['discoveredProducts']} {units}; независимый счётчик отсутствует, пройдены ссылки пагинации")
        lines.append(f"- {item['productKind']}: {item['pages']} страниц, {count}.")
    lines += [f"- Вариантов упаковки: {sum(len(p['variants']) for p in products)}.",
              f"- Карточек с замечаниями для проверки: {sum(bool(p['reviewWarnings']) for p in products)}.",
              f"- Недоступных карточек исключено: {len(snapshot['excluded'])}.", "",
              "Дрип-пакеты, капсулы, чай и оборудование относятся к другим категориям. Данные подготовлены для импорта; в БД CoffeePeek этот скрипт не пишет.", ""]
    (output / "coffees.md").write_text("\n".join(lines), encoding="utf-8")
    # JSON is the import boundary: consumers see either the old or the complete new snapshot.
    temporary = output / "coffees.json.tmp"
    temporary.write_text(json.dumps(snapshot, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(output / "coffees.json")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--cache", type=Path)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--offline", action="store_true", help="Parse previously captured pages without network access")
    mode.add_argument("--refresh", action="store_true", help="Fetch every catalogue and detail page again")
    parser.add_argument("--delay", type=float, default=3, help="Minimum seconds between requests (at least 3)")
    args = parser.parse_args()
    fetcher = Fetcher(args.cache or args.output / ".cache", offline=args.offline, refresh=args.refresh, delay=args.delay)
    try:
        snapshot = collect(fetcher)
    except (ParseError, HTTPError, URLError, OSError) as exc:
        print(f"Import failed; previous exports were not replaced: {exc}", file=sys.stderr)
        return 1
    try:
        write_exports(snapshot, args.output)
    except OSError as exc:
        print(f"Could not save exports: {exc}", file=sys.stderr)
        return 1
    print(f"Exported {len(snapshot['products'])} products to {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
