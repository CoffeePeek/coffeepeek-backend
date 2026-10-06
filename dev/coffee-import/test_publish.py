import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch
from urllib.error import HTTPError

from bs4 import BeautifulSoup
from catalogues import card_images
from publish_coffees import Publisher, check_image_url, image_format, normalize_image
from roast_by import ParseError


class PublisherCheck(unittest.TestCase):
    def test_missing_photo_preserves_gallery_and_does_not_block_other_products(self):
        good, missing, other = ("https://roast.by/" + name + ".png" for name in ("good", "missing", "other"))
        for code in (404, 410, 500):
            with self.subTest(code=code), tempfile.TemporaryDirectory(dir=Path(__file__).parent) as folder:
                publisher = Publisher(folder, "http://localhost:5243", "http://localhost:5025", "a" * 32)
                snapshot = {"source": "https://roast.by/", "products": [
                    {"sourceUrl": "https://roast.by/one/", "imageUrls": [good, missing]},
                    {"sourceUrl": "https://roast.by/two/", "imageUrls": [other]}]}
                photos = [{"sourceUrl": good}, HTTPError(missing, code, "Unavailable", None, None), {"sourceUrl": other}]
                self.addCleanup(photos[1].close)
                with patch.object(publisher, "photo", side_effect=photos), patch.object(publisher, "api", return_value={"status": "Applied"}) as api:
                    if code == 500:
                        with self.assertRaises(HTTPError):
                            publisher.publish(snapshot)
                        api.assert_not_called()
                        continue
                    self.assertEqual("Applied", publisher.publish(snapshot)["status"])
                    products = json.loads(api.call_args.args[2])["snapshot"]["products"]
                    self.assertEqual([], products[0]["storedPhotos"])
                    self.assertEqual([good, missing], products[0]["imageUrls"])
                    self.assertIn(f"photo_unavailable:{code}", products[0]["reviewWarnings"])
                    self.assertEqual([{"sourceUrl": other}], products[1]["storedPhotos"])

    def test_photos_are_uploaded_before_catalogue_and_keep_source_provenance(self):
        data = b"\x89PNG\r\n\x1a\nphoto"
        url = "https://roast.by/photo.png"
        expected = "coffees/" + hashlib.sha256(data).hexdigest() + ".png"
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as folder:
            publisher = Publisher(folder, "http://localhost:5243", "http://localhost:5025", "a" * 32)
            snapshot = {"source": "https://roast.by/", "products": [{"sourceUrl": "https://roast.by/coffee/",
                "imageUrls": ["/photo.png", "/photo.png"]}]}
            def api(base, path, body, content_type):
                if path == "/internal/coffee-photos":
                    self.assertIn(data, body)
                    return {"id": "00000000-0000-0000-0000-000000000001", "storageKey": expected,
                            "fileName": "photo.png", "contentType": "image/png", "sizeBytes": len(data)}
                request = json.loads(body)
                self.assertEqual(url, request["snapshot"]["products"][0]["storedPhotos"][0]["sourceUrl"])
                self.assertEqual(expected, request["snapshot"]["products"][0]["storedPhotos"][0]["storageKey"])
                return {"status": "Applied"}
            with patch.object(publisher, "image", return_value=data) as image, patch.object(publisher, "api", side_effect=api) as calls:
                self.assertEqual("Applied", publisher.publish(snapshot)["status"])
                self.assertEqual(2, calls.call_count)
                image.assert_called_once_with(url)
            self.assertEqual(1, len(list((Path(folder) / "history" / "roast.by").glob("*.json"))))
            cache = Path(folder) / ".images"
            cache.mkdir()
            key = hashlib.sha256(url.encode()).hexdigest()
            (cache / (key + ".bin")).write_bytes(data)
            (cache / (key + ".json")).write_text("{", encoding="utf-8")
            response = MagicMock(url=url, headers={})
            response.read.return_value = data
            response.__enter__.return_value = response
            with patch("publish_coffees.check_image_url"), patch.object(publisher.images, "open", return_value=response):
                self.assertEqual(data, publisher.image(url))
            self.assertEqual(url, json.loads((cache / (key + ".json")).read_text())["url"])
        self.assertEqual(["/real.png"], card_images(BeautifulSoup('<img src="data:image/gif;base64,x" data-src="/real.png">', "html.parser")))
        self.assertEqual(url, normalize_image("/photo.png", "https://roast.by/coffee/"))
        unicode_url = normalize_image("/кофе 1.jpg", "https://kcr.by/")
        self.assertEqual("https://kcr.by/%D0%BA%D0%BE%D1%84%D0%B5%201.jpg", unicode_url)
        self.assertEqual(unicode_url, normalize_image(unicode_url, "https://kcr.by/"))
        with self.assertRaises(ParseError): image_format(b"<svg/>")
        with self.assertRaises(ParseError): check_image_url("https://127.0.0.1/private.png")


if __name__ == "__main__":
    unittest.main()
