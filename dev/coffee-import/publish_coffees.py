"""Store source images in Media and apply a snapshot through the private Shops API."""

import argparse
import hashlib
import json
import os
import socket
import time
import uuid
from datetime import datetime, timezone
from ipaddress import ip_address
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener

from roast_by import ParseError, normalize_image

MAX_IMAGE = 8 * 1024 * 1024
IMAGE_HOSTS = {"roast.by", "spiritus.by", "espressoclub.by", "theweldercatherine.ru",
               "s3.shop.tastycoffee.ru", "static.tildacdn.biz", "static.tildacdn.com",
               "xn--80ad9al.xn--90ais", "sorso.by", "singlestep.by", "sensecoffee.by", "marconi.by",
               "mcoffee.by", "kcr.by", "idealcoffee.by", "dalonicoffee.by", "www.blasercafe.by"}


class ImportApplyError(ParseError):
    stage = "apply"


def image_format(data):
    if data.startswith(b"\xff\xd8\xff"):
        return "image/jpeg", "jpg"
    if data.startswith(b"\x89PNG\r\n\x1a\n"):
        return "image/png", "png"
    if data[:6] in (b"GIF87a", b"GIF89a"):
        return "image/gif", "gif"
    if data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return "image/webp", "webp"
    if data[4:8] == b"ftyp" and data[8:12] in (b"avif", b"avis"):
        return "image/avif", "avif"
    raise ParseError("Source did not return a supported raster image")


def check_image_url(url):
    parsed = urlsplit(url)
    if parsed.scheme != "https" or parsed.netloc not in IMAGE_HOSTS:
        raise ParseError("Image hostname is outside the observed source/CDN allowlist")
    # Hostnames are fixed, not supplied as arbitrary destinations by catalogue descriptions.
    addresses = socket.getaddrinfo(parsed.hostname, 443, type=socket.SOCK_STREAM)
    if not addresses or any(not ip_address(a[4][0]).is_global for a in addresses):
        raise ParseError("Image hostname resolves to a non-public address")


class ImageRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        check_image_url(newurl)
        return super().redirect_request(req, fp, code, msg, headers, newurl)


class NoServiceRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ParseError("Private API redirects are forbidden")


class Publisher:
    def __init__(self, output, shops_url, media_url, token):
        if len(token) < 32:
            raise ParseError("COFFEE_IMPORT_TOKEN must contain at least 32 characters")
        for url in (shops_url, media_url):
            p = urlsplit(url)
            if p.scheme not in ("http", "https") or not p.hostname or p.username or p.password or p.query or p.fragment:
                raise ParseError("Configure the private Shops/Media base URLs")
        self.output = Path(output)
        self.shops_url, self.media_url, self.token = shops_url.rstrip("/"), media_url.rstrip("/"), token
        self.images = build_opener(ImageRedirects())
        self.services = build_opener(NoServiceRedirects())
        self.last_image_request = 0.0
        self.photo_cache = {}

    @classmethod
    def from_environment(cls, output):
        return cls(output, os.environ.get("COFFEE_IMPORT_SHOPS_URL", ""),
                   os.environ.get("COFFEE_IMPORT_MEDIA_URL", ""), os.environ.get("COFFEE_IMPORT_TOKEN", ""))

    def api(self, base, path, data, content_type):
        req = Request(base + path, data=data, headers={"Content-Type": content_type,
                      "X-Coffee-Import-Token": self.token, "User-Agent": "CoffeePeek-CoffeeImport/1.0"})
        for attempt in range(3):
            try:
                with self.services.open(req, timeout=120) as response:
                    body = response.read(1024 * 1024 + 1)
                    if len(body) > 1024 * 1024:
                        raise ParseError("Private API response is too large")
                    return json.loads(body)
            except HTTPError as exc:
                if exc.code not in (429, 500, 502, 503, 504) or attempt == 2:
                    detail = ""
                    try:
                        body = exc.read(8193)
                        error = json.loads(body) if len(body) <= 8192 else None
                        if isinstance(error, dict):
                            messages = [error[k] for k in ("message", "detail") if isinstance(error.get(k), str)]
                            if isinstance(error.get("errors"), dict):
                                for field, values in error["errors"].items():
                                    if isinstance(values, list):
                                        messages.extend(f"{field}: {value}" for value in values if isinstance(value, str))
                            detail = " ".join("; ".join(messages).split()).replace(self.token, "[redacted]")[:1200]
                    except (ValueError, OSError):
                        pass
                    finally:
                        exc.close()
                    suffix = f": {detail}" if detail else ""
                    raise ParseError(f"Private API {path} rejected the request (HTTP {exc.code}){suffix}") from exc
            except (URLError, TimeoutError, ConnectionError):
                if attempt == 2:
                    raise
            time.sleep(2 ** attempt)

    def image(self, url):
        key = hashlib.sha256(url.encode()).hexdigest()
        cache = self.output / ".images"
        cache.mkdir(parents=True, exist_ok=True)
        path, meta_path = cache / (key + ".bin"), cache / (key + ".json")
        try:
            meta = json.loads(meta_path.read_text(encoding="utf-8")) if meta_path.exists() and path.exists() else {}
        except (OSError, json.JSONDecodeError):
            meta = {}  # An interrupted cache write must not block later daily imports.
        headers = {"User-Agent": "CoffeePeek-CoffeeImport/1.0", "Accept": "image/avif,image/webp,image/png,image/jpeg,image/gif"}
        if meta.get("etag"):
            headers["If-None-Match"] = meta["etag"]
        if meta.get("modified"):
            headers["If-Modified-Since"] = meta["modified"]
        for attempt in range(3):
            check_image_url(url)
            time.sleep(max(0, 3 - (time.monotonic() - self.last_image_request)))
            self.last_image_request = time.monotonic()
            try:
                with self.images.open(Request(url, headers=headers), timeout=30) as response:
                    check_image_url(response.url)
                    data = response.read(MAX_IMAGE + 1)
                    if not data or len(data) > MAX_IMAGE:
                        raise ParseError("Image exceeds the 8 MiB limit or is empty")
                    image_format(data)
                    new_meta = {"url": url, "sha256": hashlib.sha256(data).hexdigest(),
                                "etag": response.headers.get("ETag"), "modified": response.headers.get("Last-Modified")}
                temporary = path.with_suffix(".tmp")
                temporary.write_bytes(data)
                temporary.replace(path)
                temporary_meta = meta_path.with_suffix(".tmp")
                temporary_meta.write_text(json.dumps(new_meta), encoding="utf-8")
                temporary_meta.replace(meta_path)
                return data
            except HTTPError as exc:
                if exc.code == 304 and meta.get("url") == url:
                    data = path.read_bytes()
                    if len(data) > MAX_IMAGE or hashlib.sha256(data).hexdigest() != meta.get("sha256"):
                        raise ParseError("Invalid cached image")
                    image_format(data)
                    return data
                if exc.code not in (429, 500, 502, 503, 504) or attempt == 2:
                    raise
            except (URLError, TimeoutError, ConnectionError):
                if attempt == 2:
                    raise
            time.sleep(2 ** attempt)

    def photo(self, url):
        if url in self.photo_cache:
            return {**self.photo_cache[url], "sourceUrl": url}
        data = self.image(url)
        content_type, extension = image_format(data)
        boundary = "coffeepeek-" + uuid.uuid4().hex
        head = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="coffee.{extension}"\r\n'
                f"Content-Type: {content_type}\r\n\r\n").encode()
        result = self.api(self.media_url, "/internal/coffee-photos", head + data + f"\r\n--{boundary}--\r\n".encode(),
                          "multipart/form-data; boundary=" + boundary)
        expected = "coffees/" + hashlib.sha256(data).hexdigest() + "." + extension
        if result.get("storageKey") != expected or result.get("contentType") != content_type or result.get("sizeBytes") != len(data):
            raise ParseError("Media returned unexpected image metadata")
        self.photo_cache[url] = result
        return {**result, "sourceUrl": url}

    def publish(self, snapshot):
        source = urlsplit(snapshot["source"]).hostname.encode("idna").decode("ascii")
        for product in snapshot["products"]:
            urls = list(dict.fromkeys(normalize_image(url, product["sourceUrl"]) for url in product["imageUrls"]))
            product["imageUrls"] = urls
            try:
                product["storedPhotos"] = [self.photo(url) for url in urls]
            except HTTPError as exc:
                if exc.code not in (404, 410):
                    raise
                exc.close()
                # ponytail: preserve the whole gallery; add partial gallery merging if needed.
                product["storedPhotos"] = []
                product["reviewWarnings"] = sorted(set(product.get("reviewWarnings", []) + [f"photo_unavailable:{exc.code}"]))
        payload = json.dumps(snapshot, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode()
        identity = hashlib.sha256(payload).hexdigest()
        request = {"sourceKey": source, "snapshotId": identity, "snapshot": snapshot}
        history = self.output / "history" / source
        history.mkdir(parents=True, exist_ok=True)
        (history / (identity + ".json")).write_text(json.dumps(request, ensure_ascii=False, indent=2), encoding="utf-8")
        try:
            result = self.api(self.shops_url, "/internal/coffee-import/snapshots", json.dumps(request, ensure_ascii=False).encode(), "application/json")
        except Exception as exc:
            raise ImportApplyError(str(exc)) from exc
        # ponytail: keep 30 days of replay documents; price history belongs in a separate feature if requested.
        for path in history.glob("*.json"):
            if time.time() - path.stat().st_mtime > 30 * 86400:
                path.unlink()
        return result

    def report_failure(self, source, stage, error):
        source = source.encode("idna").decode("ascii")
        timestamp = datetime.now(timezone.utc).isoformat()
        identity = hashlib.sha256((source + timestamp + stage).encode()).hexdigest()
        report = {"sourceKey": source, "snapshotId": identity, "collectedAtUtc": timestamp,
                  "stage": stage, "error": str(error)[:1980]}
        self.api(self.shops_url, "/internal/coffee-import/failures", json.dumps(report).encode(), "application/json")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("snapshot", type=Path, help="A collected coffees.json; preserves its original collection time")
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "imports")
    args = parser.parse_args()
    result = Publisher.from_environment(args.output).publish(json.loads(args.snapshot.read_text(encoding="utf-8")))
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
