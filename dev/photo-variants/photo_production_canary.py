#!/usr/bin/env python3
"""Explicit HTTPS photo canary. Private state contains credentials; stdout contains counts only.

prepare registers a new reserved-domain account. confirm-create consumes a private DB observation
{email,userId,confirmationToken}, confirms that account, uploads two synthetic PNGs and creates one
private home check-in. verify takes an operator-selected old PUBLIC photo observation
{checkInId,photoId,sourceVersion,recipeVersion,variants:[{purpose,format,sizeBytes,...}]}.
cleanup deletes ONLY the new private home check-in; the account is retained for operator review.
There are no automatic mutation retries. On an uncertain write, inspect private state and server
records before taking another action. Never put this state in a repository or CI artifact.
"""
import argparse
import base64
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import stat
import struct
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zlib

API = "https://api.coffeepeek.by"
UPLOAD = "https://media.coffeepeek.by"
MARKER = "coffeepeek-photo-production-canary-v1"
MAX_JSON = 1024 * 1024
PURPOSES = {"thumb": 320, "feed": 960, "detail": 1600}


class CanaryError(Exception):
    pass


def require(condition):
    if not condition:
        raise CanaryError("Canary validation failed; inspect private state and service health.")


def private_read(path):
    path = Path(path)
    require(not path.is_symlink())
    with path.open("rb") as source:
        info = os.fstat(source.fileno())
        require(stat.S_ISREG(info.st_mode) and (os.name == "nt" or info.st_mode & 0o077 == 0))
        data = source.read(MAX_JSON + 1)
    require(len(data) <= MAX_JSON)
    return json.loads(data)


def private_write(path, value):
    path = Path(path)
    require(not path.is_symlink())
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    temporary = None
    try:
        descriptor, name = tempfile.mkstemp(prefix=".photo-canary-", dir=path.parent)
        temporary = Path(name)
        with os.fdopen(descriptor, "w", encoding="utf-8") as output:
            os.chmod(name, 0o600)
            json.dump(value, output, allow_nan=False)
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, path)
    finally:
        if temporary and temporary.exists():
            temporary.unlink()


def identity(value):
    parsed = uuid.UUID(value)
    require(parsed.int != 0)
    return str(parsed)


def origin_url(value, origin, allow_query=False):
    parsed = urllib.parse.urlsplit(value)
    require(parsed.scheme == "https" and parsed.netloc == urllib.parse.urlsplit(origin).netloc
            and not parsed.username and not parsed.password and not parsed.fragment
            and (allow_query or not parsed.query))
    return value


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, url):
        return None


class Client:
    def __init__(self, timeout=30):
        self.timeout = timeout
        self.opener = urllib.request.build_opener(NoRedirect())

    def request(self, method, url, token=None, data=None, headers=None, expected=(200,), cap=MAX_JSON, upload=False):
        origin_url(url, UPLOAD if upload else API, allow_query=True)
        require(not upload or token is None)
        outgoing = {"User-Agent": "CoffeePeek-Photo-Canary/1", "Cache-Control": "no-store"}
        outgoing.update(headers or {})
        if token:
            outgoing["Authorization"] = "Bearer " + token
        if isinstance(data, (dict, list)):
            data = json.dumps(data, allow_nan=False).encode()
            outgoing["Content-Type"] = "application/json"
        request = urllib.request.Request(url, data=data, headers=outgoing, method=method)
        try:
            with self.opener.open(request, timeout=self.timeout) as response:
                status = response.status
                require(status in expected)
                body = response.read(cap + 1)
                require(len(body) <= cap)
                return status, {key.lower(): value for key, value in response.headers.items()}, body
        except urllib.error.HTTPError as error:
            status = error.code
            response_headers = {key.lower(): value for key, value in error.headers.items()}
            error.close()
            require(status in expected)
            return status, response_headers, b""
        except (urllib.error.URLError, TimeoutError, OSError, ValueError):
            raise CanaryError("Canary transport failed; no mutation was retried.") from None

    def api(self, method, path, token=None, data=None):
        require(path.startswith("/api/") and "\\" not in path and not path.startswith("//"))
        _, _, body = self.request(method, API + path, token, data)
        envelope = json.loads(body)
        require(envelope.get("isSuccess") is True)
        return envelope.get("data")


def png(alpha=False):
    width, height = 48, 64
    pixels = b"".join(b"\0" + b"".join(bytes((x * 5 % 256, y * 3 % 256, 120))
        + (bytes((0 if x < 8 else 128 if x < 24 else 255,)) if alpha else b"")
        for x in range(width)) for y in range(height))
    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6 if alpha else 2, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(pixels)) + chunk(b"IEND", b"")


def validate_state(state):
    require(state.get("marker") == MARKER and re.fullmatch(r"photo_canary_[0-9a-f]{16}@photo-canary\.invalid", state.get("email", "")))
    require(state.get("userName") == state["email"].split("@")[0] and len(state.get("password", "")) >= 24)


def prepare(path, client):
    require(not Path(path).exists())
    username = "photo_canary_" + secrets.token_hex(8)
    state = {"marker": MARKER, "email": username + "@photo-canary.invalid", "userName": username,
             "password": secrets.token_urlsafe(32), "registrationAttempted": True}
    private_write(path, state)  # Recorded before the write, including an uncertain registration outcome.
    client.api("POST", "/api/Users", data={key: state[key] for key in ("userName", "email", "password")})
    state["registered"] = True
    private_write(path, state)
    return {"registered": True, "next": "confirm-create"}


def login(state, path, client):
    token = client.api("POST", "/api/Tokens", data={"email": state["email"], "password": state["password"]})["accessToken"]
    require(isinstance(token, str) and len(token) < 16384)
    payload = token.split(".")[1]
    claims = json.loads(base64.urlsafe_b64decode(payload + "=" * (-len(payload) % 4)))
    # JWT parsing is only an extra identity check; the real HTTPS Gateway validates the token below.
    require(identity(claims["sub"]) == identity(state["userId"]) and claims["exp"] > time.time())
    profile = client.api("GET", "/api/Users/me", token)
    require(profile["email"].lower() == state["email"])
    state["accessToken"] = token
    private_write(path, state)
    return token


def confirm_create(path, confirmation_path, client):
    state = private_read(path)
    validate_state(state)
    require(state.get("registered") is True and not state.get("creationAttempted"))
    confirmation = private_read(confirmation_path)
    require(confirmation["email"] == state["email"] and isinstance(confirmation["confirmationToken"], str))
    state["userId"] = identity(confirmation["userId"])
    state["creationAttempted"] = True
    private_write(path, state)
    token_query = urllib.parse.urlencode({"token": confirmation["confirmationToken"]})
    client.api("PUT", "/api/Users/me/email-confirmation?" + token_query)
    state["confirmed"] = True
    private_write(path, state)
    token = login(state, path, client)
    images = [png(False), png(True)]
    requests = [{"fileName": "photo-canary-original.png" if index == 0 else "photo-canary-cutout.png",
                 "contentType": "image/png", "sizeBytes": len(body)} for index, body in enumerate(images)]
    grants = client.api("POST", "/api/Photos/check-in", token, requests)
    require(isinstance(grants, list) and len(grants) == 2)
    state["uploads"] = grants
    private_write(path, state)
    for index, (grant, body) in enumerate(zip(grants, images)):
        identity(grant["photoId"])
        origin_url(grant["uploadUrl"], UPLOAD, allow_query=True)
        require(isinstance(grant["storageKey"], str) and len(grant["storageKey"]) <= 255)
        client.request("PUT", grant["uploadUrl"], data=body, upload=True, expected=(200, 204),
                       headers={"Content-Type": "image/png", "x-amz-tagging": "is_permanent=False"})
        state.setdefault("uploaded", []).append(index)
        private_write(path, state)
    references = [{"fileName": requests[index]["fileName"], "contentType": "image/png", "size": len(images[index]),
                   "storageKey": grant["storageKey"]} for index, grant in enumerate(grants)]
    now = dt.datetime.now(dt.timezone.utc)
    body = {"visibility": "Private", "preparedAt": now.isoformat(), "preparedOn": now.date().isoformat(),
            "text": "Photo delivery private canary", "photos": [references[0]], "drinkPhoto": references[1]}
    home = client.api("POST", "/api/v1/check-ins/home", token, body)
    state["homeId"] = identity(home["id"])
    state["home"] = home
    private_write(path, state)
    validate_home(state, home)
    return {"privateHomeCreated": True, "uploadedPhotos": 2, "next": "verify"}


def validate_home(state, home):
    require(identity(home["id"]) == identity(state["homeId"]) and home["visibility"] in (0, "Private"))
    require(len(home["photos"]) == 1 and home.get("drinkPhoto") is not None)
    require(identity(home["photos"][0]["id"]) == identity(state["uploads"][0]["photoId"])
            and identity(home["drinkPhoto"]["id"]) == identity(state["uploads"][1]["photoId"]))


def base_path(check_in, photo):
    return "/api/v1/check-ins/" + identity(check_in) + "/photos/" + identity(photo)


def png_has_alpha(image):
    width, height = struct.unpack(">II", image[16:24])
    require(0 < width <= 48 and 0 < height <= 64 and image[24] == 8)
    color = image[25]
    require(color in (4, 6))
    channels = 4 if color == 6 else 2
    offset, compressed = 8, bytearray()
    while offset < len(image):
        length = struct.unpack(">I", image[offset:offset + 4])[0]
        require(offset + 12 + length <= len(image))
        if image[offset + 4:offset + 8] == b"IDAT":
            compressed.extend(image[offset + 8:offset + 8 + length])
        offset += length + 12
    maximum = (width * channels + 1) * height
    decoder = zlib.decompressobj()
    decoded = decoder.decompress(compressed, maximum + 1)
    require(len(decoded) == maximum and decoder.eof)
    previous = bytearray(width * channels)
    alphas = []
    for y in range(height):
        line = decoded[y * (len(previous) + 1):(y + 1) * (len(previous) + 1)]
        mode, row = line[0], bytearray(line[1:])
        require(mode <= 4)
        for x in range(len(row)):
            left, above = row[x - channels] if x >= channels else 0, previous[x]
            diagonal = previous[x - channels] if x >= channels else 0
            prediction = left + above - diagonal
            distances = (abs(prediction - left), abs(prediction - above), abs(prediction - diagonal))
            paeth = (left, above, diagonal)[distances.index(min(distances))]
            row[x] = (row[x] + (0, left, above, (left + above) // 2, paeth)[mode]) % 256
        alphas.extend(row[channels - 1::channels])
        previous = row
    return any(alpha < 255 for alpha in alphas) and any(alpha > 0 for alpha in alphas)


def check_cache_policy(headers, public, cache_enabled=False):
    policy = headers.get("cache-control", "").lower()
    require("private" in policy)
    if public:
        age = re.search(r"(?:^|,)\s*max-age=(\d+)(?:\s*,|\s*$)", policy)
        if cache_enabled:
            require("must-revalidate" in policy and age and int(age[1]) <= 20)
        else:
            require("no-store" in policy or "must-revalidate" in policy and age and int(age[1]) == 0)
    else:
        require("no-store" in policy)


def photo_checks(client, base, variants, token=None, public=False, original_digest=None, cache_enabled=False):
    _, original_headers, original = client.request("GET", API + base, token, cap=32 * 1024 * 1024)
    require("no-store" in original_headers.get("cache-control", ""))
    if original_digest:
        require(hashlib.sha256(original).hexdigest() == original_digest)
    checks = 1
    require(len(variants) == 6)
    seen = set()
    for variant in variants:
        purpose, fmt, source, recipe = variant["purpose"], variant["format"], variant["sourceVersion"], variant["recipeVersion"]
        require(purpose in PURPOSES and fmt in ("webp", "jpeg", "png") and re.fullmatch("[0-9a-f]{64}", source)
                and isinstance(recipe, int) and recipe > 0 and (purpose, fmt) not in seen)
        seen.add((purpose, fmt))
        url = API + base + "/variants/" + source + "/" + str(recipe) + "/" + purpose + "." + fmt
        _, headers, image = client.request("GET", url, token, cap=8 * 1024 * 1024)
        require(headers.get("content-type", "").split(";")[0] == "image/" + fmt and headers.get("etag"))
        require(headers["etag"] == '"' + source + "-v" + str(recipe) + "-" + hashlib.sha256(image).hexdigest() + '"')
        require(int(headers["content-length"]) == len(image) > 0 and headers.get("x-content-type-options") == "nosniff")
        check_cache_policy(headers, public, cache_enabled)
        if variant.get("sizeBytes") is not None:
            require(len(image) == variant["sizeBytes"])
        if fmt == "png":
            require(image.startswith(b"\x89PNG\r\n\x1a\n"))
            width, height = struct.unpack(">II", image[16:24])
            require(0 < width <= PURPOSES[purpose] and 0 < height <= PURPOSES[purpose])
            if variant.get("alpha"):
                require(png_has_alpha(image))
        elif fmt == "webp":
            require(image[:4] == b"RIFF" and image[8:12] == b"WEBP")
        else:
            require(image.startswith(b"\xff\xd8"))
        _, head, body = client.request("HEAD", url, token)
        check_cache_policy(head, public, cache_enabled)
        require(not body and head.get("etag") == headers["etag"] and head.get("content-length") == headers["content-length"]
                and head.get("content-type") == headers["content-type"])
        conditional, conditional_headers, conditional_body = client.request("GET", url, token,
            headers={"If-None-Match": headers["etag"]}, expected=(304,) if public and cache_enabled else (200, 304) if public else (200,), cap=8 * 1024 * 1024)
        require(conditional in ((200, 304) if public and not cache_enabled else (304,) if public else (200,)))
        check_cache_policy(conditional_headers, public, cache_enabled)
        if conditional == 304:
            require(not conditional_body and conditional_headers.get("etag") == headers["etag"])
        else:
            require(conditional_body == image)
            if not public:
                require("no-store" in conditional_headers.get("cache-control", ""))
        if not public:
            denied, _, _ = client.request("GET", url, expected=(401, 404))
            require(denied in (401, 404))
        checks += 3 + (not public)
    require(all((purpose, "webp") in seen and sum(p == purpose and f in ("png", "jpeg") for p, f in seen) == 1 for purpose in PURPOSES))
    return checks


def verify(path, old_path, client, recipe=1, public_cache_enabled=False):
    state = private_read(path)
    validate_state(state)
    require(state.get("confirmed") and not state.get("deleted"))
    token = login(state, path, client)
    home = client.api("GET", "/api/v1/check-ins/home/" + identity(state["homeId"]), token)
    validate_home(state, home)
    checks = 0
    for index, photo in enumerate([home["photos"][0], home["drinkPhoto"]]):
        photo_id = identity(photo["id"])
        source = hashlib.sha256(("checkin:" + uuid.UUID(photo_id).hex + ":" + photo["storageKey"]).encode()).hexdigest()
        variants = [{"purpose": purpose, "format": fmt, "sourceVersion": source, "recipeVersion": recipe, "alpha": index == 1}
                    for purpose in PURPOSES for fmt in ("webp", "png" if index == 1 else "jpeg")]
        base = base_path(state["homeId"], photo_id)
        _, _, _ = client.request("GET", API + base, expected=(401, 404))
        checks += photo_checks(client, base, variants, token, original_digest=hashlib.sha256(png(index == 1)).hexdigest()) + 1
    old = private_read(old_path)
    require(old.get("public") is True)
    old_base = base_path(old["checkInId"], old["photoId"])
    require(identity(old["checkInId"]) != identity(state["homeId"]))
    variants = [dict(variant, sourceVersion=old["sourceVersion"], recipeVersion=old["recipeVersion"]) for variant in old["variants"]]
    checks += photo_checks(client, old_base, variants, public=True, original_digest=old.get("originalSha256"), cache_enabled=public_cache_enabled)
    client.request("GET", API + old_base, token="invalid-canary-token", expected=(401,))
    state["verification"] = {"checks": checks + 1, "newPhotos": 2, "oldPhotos": 1, "publicCacheRequested": public_cache_enabled,
                             "verifiedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat()}
    private_write(path, state)
    return state["verification"]


def cleanup(path, client):
    state = private_read(path)
    validate_state(state)
    require(state.get("confirmed") and state.get("homeId") and not state.get("cleanupAttempted"))
    token = login(state, path, client)
    home = client.api("GET", "/api/v1/check-ins/home/" + identity(state["homeId"]), token)
    validate_home(state, home)
    state["cleanupAttempted"] = True
    private_write(path, state)
    client.api("DELETE", "/api/v1/check-ins/home/" + identity(state["homeId"]), token)
    state["deleted"] = True
    state.pop("accessToken", None)
    private_write(path, state)
    for photo in [home["photos"][0], home["drinkPhoto"]]:
        client.request("GET", API + base_path(state["homeId"], photo["id"]), token, expected=(401, 404))
    return {"privateHomeDeleted": True, "accountRetained": True}


def main(argv=None, client=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("prepare", "confirm-create", "verify", "cleanup"))
    parser.add_argument("--state", required=True)
    parser.add_argument("--confirmation-file")
    parser.add_argument("--old-photo-file")
    parser.add_argument("--recipe", type=int, default=1)
    parser.add_argument("--public-cache-enabled", action="store_true", help="Require public conditional 304 and private freshness <=20s; default accepts no-store/200 or zero-freshness revalidation.")
    args = parser.parse_args(argv)
    try:
        require(args.recipe > 0)
        client = client or Client()
        if args.command == "prepare":
            result = prepare(args.state, client)
        elif args.command == "confirm-create":
            require(args.confirmation_file and Path(args.confirmation_file).resolve() != Path(args.state).resolve())
            result = confirm_create(args.state, args.confirmation_file, client)
        elif args.command == "verify":
            require(args.old_photo_file and Path(args.old_photo_file).resolve() != Path(args.state).resolve())
            result = verify(args.state, args.old_photo_file, client, args.recipe, args.public_cache_enabled)
        else:
            result = cleanup(args.state, client)
        print(json.dumps({"command": args.command, "success": True, "result": result}))
        return 0
    except Exception:
        print("Photo canary failed; inspect private state and server records. No mutation was retried.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
