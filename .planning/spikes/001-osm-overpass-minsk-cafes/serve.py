#!/usr/bin/env python3
"""Serve the spike review queue on http://127.0.0.1:8765/"""

from __future__ import annotations

import json
import re
import urllib.error
import urllib.parse
import urllib.request
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

DIR = Path(__file__).resolve().parent
PORT = 8765
CACHE_PATH = DIR / "preview-cache.json"
YANDEX_CACHE_PATH = DIR / "yandex-cache.json"
GOOGLE_CACHE_PATH = DIR / "google-cache.json"
SECRETS_PATH = DIR / "secrets.local.json"
OG_IMAGE = re.compile(r'<meta[^>]+property=["\']og:image["\'][^>]+content=["\']([^"\']+)["\']', re.I)
OG_IMAGE_REV = re.compile(r'<meta[^>]+content=["\']([^"\']+)["\'][^>]+property=["\']og:image["\']', re.I)
OG_TITLE = re.compile(r'<meta[^>]+property=["\']og:title["\'][^>]+content=["\']([^"\']+)["\']', re.I)
OG_DESC = re.compile(r'<meta[^>]+property=["\']og:description["\'][^>]+content=["\']([^"\']+)["\']', re.I)
TITLE = re.compile(r"<title[^>]*>([^<]+)</title>", re.I)

_cache: dict[str, dict] | None = None


def load_cache() -> dict[str, dict]:
    global _cache
    if _cache is None:
        if CACHE_PATH.exists():
            _cache = json.loads(CACHE_PATH.read_text(encoding="utf-8"))
        else:
            _cache = {}
    return _cache


def save_cache() -> None:
    CACHE_PATH.write_text(json.dumps(load_cache(), ensure_ascii=False, indent=2), encoding="utf-8")


def first_match(patterns: list[re.Pattern], html: str) -> str | None:
    for pat in patterns:
        m = pat.search(html)
        if m:
            return m.group(1).strip()
    return None


def fetch_preview(url: str) -> dict:
    cache = load_cache()
    if url in cache:
        return cache[url]
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme not in {"http", "https"} or not parsed.netloc:
        return {"ok": False, "error": "bad url"}
    host = parsed.netloc.lower()
    if any(x in host for x in ("instagram.com", "yandex.", "facebook.com", "vk.com")):
        result = {"ok": False, "error": "no-embed"}
        cache[url] = result
        save_cache()
        return result
    req = urllib.request.Request(
        url,
        headers={"User-Agent": "CoffeePeekSpike/001 (local research preview)"},
        method="GET",
    )
    try:
        with urllib.request.urlopen(req, timeout=8) as resp:
            raw = resp.read(200_000)
            final = resp.geturl()
    except (urllib.error.URLError, TimeoutError, ValueError) as exc:
        result = {"ok": False, "error": str(exc)}
        cache[url] = result
        save_cache()
        return result
    html = raw.decode("utf-8", errors="ignore")
    image = first_match([OG_IMAGE, OG_IMAGE_REV], html)
    if image:
        image = urllib.parse.urljoin(final, image)
    result = {
        "ok": True,
        "image": image,
        "title": first_match([OG_TITLE, TITLE], html),
        "description": first_match([OG_DESC], html),
        "finalUrl": final,
    }
    cache[url] = result
    save_cache()
    return result


def secrets() -> dict:
    if not SECRETS_PATH.exists():
        return {}
    return json.loads(SECRETS_PATH.read_text(encoding="utf-8"))


def places_key() -> str:
    return secrets().get("yandexPlacesApiKey") or ""


def google_key() -> str:
    return secrets().get("googlePlacesApiKey") or ""


def load_yandex_cache() -> dict:
    if YANDEX_CACHE_PATH.exists():
        return json.loads(YANDEX_CACHE_PATH.read_text(encoding="utf-8"))
    return {}


def save_yandex_cache(cache: dict) -> None:
    YANDEX_CACHE_PATH.write_text(json.dumps(cache, ensure_ascii=False, indent=2), encoding="utf-8")


def haversine_m(lat1: float, lon1: float, lat2: float, lon2: float) -> float:
    from math import atan2, cos, radians, sin, sqrt

    r = 6371000
    p1, p2 = radians(lat1), radians(lat2)
    dphi = radians(lat2 - lat1)
    dl = radians(lon2 - lon1)
    a = sin(dphi / 2) ** 2 + cos(p1) * cos(p2) * sin(dl / 2) ** 2
    return 2 * r * atan2(sqrt(a), sqrt(1 - a))


def summarize_place(feature: dict, lat: float | None, lon: float | None) -> dict:
    props = feature.get("properties") or {}
    meta = props.get("CompanyMetaData") or {}
    hours = meta.get("Hours") or {}
    geom = (feature.get("geometry") or {}).get("coordinates") or [None, None]
    plat, plon = (geom[1], geom[0]) if len(geom) == 2 else (None, None)
    dist = None
    if lat is not None and lon is not None and plat is not None and plon is not None:
        dist = round(haversine_m(lat, lon, float(plat), float(plon)))
    hours_text = hours.get("text") or ""
    state = "open"
    lowered = hours_text.lower()
    if any(x in lowered for x in ("закрыто навсегда", "closed permanently", "больше не работает")):
        state = "closed"
    elif hours.get("Availabilities") == [] or "временно закрыт" in lowered:
        state = "maybe_closed"
    categories = [c.get("name") for c in (meta.get("Categories") or []) if c.get("name")]
    return {
        "name": props.get("name") or meta.get("name"),
        "address": meta.get("address") or props.get("description"),
        "hours": hours_text or None,
        "url": meta.get("url"),
        "phones": [p.get("formatted") for p in (meta.get("Phones") or []) if p.get("formatted")],
        "categories": categories,
        "distanceM": dist,
        "state": state,
    }


def yandex_status(name: str, lat: float | None, lon: float | None) -> dict:
    key = places_key()
    if not key:
        return {"ok": False, "error": "no-key", "hint": "Put the key in secrets.local.json"}
    cache_key = f"{name}|{lat}|{lon}"
    cache = load_yandex_cache()
    if cache_key in cache:
        return cache[cache_key]
    params = {
        "apikey": key,
        "text": name,
        "lang": "ru_RU",
        "type": "biz",
        "results": "5",
    }
    if lat is not None and lon is not None:
        params["ll"] = f"{lon},{lat}"
        params["spn"] = "0.02,0.02"
        params["rspn"] = "1"
    url = "https://search-maps.yandex.ru/v1/?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"User-Agent": "CoffeePeekSpike/001"})
    try:
        with urllib.request.urlopen(req, timeout=12) as resp:
            data = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        return {
            "ok": False,
            "error": f"http-{exc.code}",
            "hint": "Places key accepted, but the plan is blocked / quota is 0. In the dashboard, the \"Organization search\" tile must not be locked." if exc.code == 403 else body[:200],
        }
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
        return {"ok": False, "error": str(exc)}

    features = data.get("features") or []
    matches = [summarize_place(f, lat, lon) for f in features]
    best = matches[0] if matches else None
    if best and best.get("distanceM") is not None and best["distanceM"] > 250:
        best = None
    result = {
        "ok": True,
        "found": bool(matches),
        "best": best,
        "matches": matches[:3],
        "verdict": (
            "closed" if best and best["state"] == "closed"
            else "not_found" if not matches
            else "far" if matches and not best
            else best["state"] if best else "unknown"
        ),
    }
    cache[cache_key] = result
    save_yandex_cache(cache)
    return result


def google_status(name: str, lat: float | None, lon: float | None) -> dict:
    key = google_key()
    if not key:
        return {"ok": False, "error": "no-key", "hint": "No googlePlacesApiKey in secrets.local.json"}
    cache_key = f"{name}|{lat}|{lon}"
    cache = {}
    if GOOGLE_CACHE_PATH.exists():
        cache = json.loads(GOOGLE_CACHE_PATH.read_text(encoding="utf-8"))
    if cache_key in cache:
        return cache[cache_key]
    payload = {
        "textQuery": name,
        "languageCode": "ru",
        "regionCode": "BY",
        "maxResultCount": 5,
    }
    if lat is not None and lon is not None:
        payload["locationBias"] = {
            "circle": {
                "center": {"latitude": lat, "longitude": lon},
                "radius": 400.0,
            }
        }
    req = urllib.request.Request(
        "https://places.googleapis.com/v1/places:searchText",
        data=json.dumps(payload).encode("utf-8"),
        method="POST",
        headers={
            "Content-Type": "application/json",
            "X-Goog-Api-Key": key,
            "X-Goog-FieldMask": (
                "places.displayName,places.formattedAddress,places.businessStatus,"
                "places.location,places.googleMapsUri,places.websiteUri,places.types"
            ),
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=12) as resp:
            data = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        return {"ok": False, "error": f"http-{exc.code}", "hint": body[:240]}
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
        return {"ok": False, "error": str(exc)}

    matches = []
    for place in data.get("places") or []:
        loc = place.get("location") or {}
        plat, plon = loc.get("latitude"), loc.get("longitude")
        dist = None
        if lat is not None and lon is not None and plat is not None and plon is not None:
            dist = round(haversine_m(lat, lon, float(plat), float(plon)))
        status = place.get("businessStatus") or "UNKNOWN"
        name_text = ((place.get("displayName") or {}).get("text")) or name
        matches.append(
            {
                "name": name_text,
                "address": place.get("formattedAddress"),
                "status": status,
                "mapsUrl": place.get("googleMapsUri"),
                "website": place.get("websiteUri"),
                "types": place.get("types") or [],
                "distanceM": dist,
            }
        )
    best = matches[0] if matches else None
    if best and best.get("distanceM") is not None and best["distanceM"] > 250:
        best = None
    status = (best or {}).get("status")
    verdict = (
        "closed" if status == "CLOSED_PERMANENTLY"
        else "temp_closed" if status == "CLOSED_TEMPORARILY"
        else "open" if status == "OPERATIONAL"
        else "not_found" if not matches
        else "far" if matches and not best
        else "unknown"
    )
    result = {"ok": True, "found": bool(matches), "best": best, "matches": matches[:3], "verdict": verdict}
    cache[cache_key] = result
    GOOGLE_CACHE_PATH.write_text(json.dumps(cache, ensure_ascii=False, indent=2), encoding="utf-8")
    return result


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(DIR), **kwargs)

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path == "/preview":
            qs = urllib.parse.parse_qs(parsed.query)
            url = (qs.get("url") or [""])[0]
            payload = fetch_preview(url)
        elif parsed.path == "/google-status":
            qs = urllib.parse.parse_qs(parsed.query)
            name = (qs.get("name") or [""])[0]
            lat = qs.get("lat", [None])[0]
            lon = qs.get("lon", [None])[0]
            payload = google_status(
                name,
                float(lat) if lat else None,
                float(lon) if lon else None,
            )
        elif parsed.path == "/yandex-status":
            qs = urllib.parse.parse_qs(parsed.query)
            name = (qs.get("name") or [""])[0]
            lat = qs.get("lat", [None])[0]
            lon = qs.get("lon", [None])[0]
            payload = yandex_status(
                name,
                float(lat) if lat else None,
                float(lon) if lon else None,
            )
        else:
            return super().do_GET()
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)
        return


if __name__ == "__main__":
    httpd = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    print(f"Review queue: http://127.0.0.1:{PORT}/", flush=True)
    httpd.serve_forever()
