#!/usr/bin/env python3
"""Exercise business ownership through a local Gateway using a disposable roaster.

Requires BASE_URL, USER_TOKEN and ADMIN_TOKEN. The roaster is deleted afterwards;
claim and ownership audit records remain, as they do after ordinary deletion.
"""

import ipaddress
import json
import os
import sys
import uuid
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener


class SmokeFailure(Exception):
    pass


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def validate_base_url(value):
    try:
        parsed = urlsplit(value)
        local = parsed.hostname == "localhost" or ipaddress.ip_address(parsed.hostname).is_loopback
        port = parsed.port
    except (ValueError, TypeError):
        raise SmokeFailure("BASE_URL must be an explicit loopback HTTP(S) Gateway URL.") from None
    if (parsed.scheme not in ("http", "https") or not local
            or parsed.username is not None or parsed.password is not None
            or parsed.path not in ("", "/") or parsed.query or parsed.fragment
            or (port is not None and port == 0)):
        raise SmokeFailure("BASE_URL must be an explicit loopback HTTP(S) Gateway URL without credentials or a path.")
    return value.rstrip("/")


def roaster_update(profile, about):
    """Preserve all editable profile fields and retain the existing photo gallery."""
    location = profile.get("location") or {}
    contact = profile.get("contact") or {}
    return {
        "name": profile["name"], "about": about, "cityId": profile.get("cityId"),
        "address": location.get("address"), "latitude": location.get("latitude"),
        "longitude": location.get("longitude"), "instagramLink": contact.get("instagramLink"),
        "siteLink": contact.get("siteLink"), "photos": None,
    }


def request(opener, base_url, method, path, token=None, body=None, revision=None, expected=(200,)):
    headers = {"Accept": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    if revision is not None:
        headers["If-Match"] = '"' + str(revision) + '"'
    payload = None
    if body is not None:
        headers["Content-Type"] = "application/json"
        payload = json.dumps(body).encode("utf-8")
    req = Request(base_url + path, data=payload, headers=headers, method=method)
    try:
        response = opener.open(req, timeout=20)
    except HTTPError as error:
        response = error
    except (URLError, TimeoutError, OSError):
        raise SmokeFailure(f"Gateway connection failed during {method} {path}.") from None
    with response:
        status = response.code
        if status not in expected:
            # Do not echo a remote error body or request headers: either may contain credentials.
            raise SmokeFailure(f"{method} {path}: expected {expected}, received HTTP {status}.")
        if status >= 400:
            return None
        try:
            envelope = json.loads(response.read())
        except (ValueError, UnicodeDecodeError):
            raise SmokeFailure(f"{method} {path}: expected a JSON response.") from None
    if not isinstance(envelope, dict) or envelope.get("isSuccess") is not True:
        raise SmokeFailure(f"{method} {path}: response did not report success.")
    return envelope.get("data")


def run(base_url, user_token, admin_token):
    # Never forward bearer credentials through redirects or environment HTTP proxies.
    opener = build_opener(ProxyHandler({}), NoRedirect())
    request(opener, base_url, "GET", "/api/admin/ownership-claims", user_token, expected=(403,))
    request(opener, base_url, "GET", "/api/admin/ownership-claims", admin_token)
    slug = "ownership-smoke-" + uuid.uuid4().hex
    roaster_id = None
    failure = None
    try:
        created = request(opener, base_url, "POST", "/api/admin/roasters", admin_token,
                          {"name": slug, "about": "Disposable ownership smoke fixture"})
        roaster_id = str(uuid.UUID(created["id"]))
        owner_path = "/api/owner/roasters/" + roaster_id
        admin_path = "/api/admin/roasters/" + roaster_id

        # The existing ASCII slug generator preserves this unique name; verify its public profile.
        public = request(opener, base_url, "GET", "/api/roasters/" + slug)
        if public["name"] != slug:
            raise SmokeFailure("Disposable roaster public address did not resolve to its expected name.")
        request(opener, base_url, "GET", owner_path, user_token, expected=(404,))
        claim = request(opener, base_url, "POST", "/api/owner/ownership-claims", user_token,
                        {"businessType": "Roaster", "businessSlug": slug,
                         "contact": "ownership-smoke@example.invalid",
                         "evidence": "Local disposable fixture created by the ownership smoke script."},
                        expected=(201,))
        if claim["businessId"] != roaster_id or claim["status"] != "Pending" or claim["hasCurrentAccess"]:
            raise SmokeFailure("New claim did not describe the unowned disposable roaster.")
        claim_id = str(uuid.UUID(claim["id"]))
        decision_path = "/api/admin/ownership-claims/" + claim_id + "/approve"
        request(opener, base_url, "POST", decision_path, admin_token, {"reason": "Local smoke approval"})

        profile = request(opener, base_url, "GET", owner_path, user_token)
        revision = profile["profileRevision"]
        if not isinstance(revision, int) or revision < 0:
            raise SmokeFailure("Owner profile has no valid revision.")
        about = "Ownership smoke edit " + uuid.uuid4().hex
        update = roaster_update(profile, about)
        updated = request(opener, base_url, "PUT", owner_path, user_token, update, revision)
        if updated["profileRevision"] <= revision:
            raise SmokeFailure("Owner update did not advance the profile revision.")
        public = request(opener, base_url, "GET", "/api/roasters/" + slug)
        if public["about"] != about:
            raise SmokeFailure("Owner update was not immediately visible in the public profile.")

        request(opener, base_url, "PATCH", admin_path + "/owner", admin_token,
                {"ownerUserId": None, "reason": "Local smoke revocation"})
        request(opener, base_url, "GET", owner_path, user_token, expected=(404,))
        request(opener, base_url, "PUT", owner_path, user_token, update,
                updated["profileRevision"], expected=(404,))
        replay = request(opener, base_url, "POST", decision_path, admin_token, {"reason": "Replay after revoke"})
        if replay["status"] != "Approved" or replay["hasCurrentAccess"]:
            raise SmokeFailure("Replaying approval restored revoked ownership or changed the historical decision.")
        request(opener, base_url, "GET", owner_path, user_token, expected=(404,))
        own_claim = request(opener, base_url, "GET", "/api/owner/ownership-claims/" + claim_id, user_token)
        if own_claim["hasCurrentAccess"]:
            raise SmokeFailure("Own claim still reports access after revocation.")
        print("PASS: submit, approve, edit, public read, revoke and approval replay with the same user token.")
    except SmokeFailure as error:
        failure = error
    except (KeyError, TypeError, ValueError):
        failure = SmokeFailure("Gateway response did not match the documented ownership contract.")
    finally:
        if roaster_id is not None:
            try:
                request(opener, base_url, "DELETE", "/api/admin/roasters/" + roaster_id,
                        admin_token, expected=(200, 404))
                print("Disposable roaster removed; claim and audit history retained.")
            except SmokeFailure:
                print(f"Cleanup failed: remove disposable roaster {roaster_id} through the local admin API.", file=sys.stderr)
                if failure is None:
                    failure = SmokeFailure("Smoke assertions passed, but disposable-roaster cleanup failed.")
    if failure is not None:
        raise failure


def main():
    try:
        base_url = validate_base_url(os.environ.get("BASE_URL", ""))
        user_token = os.environ.get("USER_TOKEN", "").strip()
        admin_token = os.environ.get("ADMIN_TOKEN", "").strip()
        if not user_token or not admin_token or user_token == admin_token:
            raise SmokeFailure("Set distinct USER_TOKEN (ordinary User) and ADMIN_TOKEN environment variables.")
        if any(c in user_token + admin_token for c in "\r\n"):
            raise SmokeFailure("Tokens must contain a single line.")
        run(base_url, user_token, admin_token)
    except SmokeFailure as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
