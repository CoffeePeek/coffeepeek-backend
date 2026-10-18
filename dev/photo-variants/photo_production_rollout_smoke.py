#!/usr/bin/env python3
"""Fresh private upload acceptance on the full release; generated fixture only, no write retries."""
import datetime as dt
import json
import os
from pathlib import Path
import sys

import photo_production_activation as activation
import photo_production_canary as canary
import photo_production_rollout as rollout

OPERATIONS = {"rollout-smoke-create", "rollout-smoke-verify", "rollout-smoke-cleanup"}


def execute(operator, operation):
    operator.release()
    operator.runtime()
    probe = operator.operator
    probe.state_path = operator.root / "new-photo-smoke.json"
    probe.confirmation_path = operator.root / "new-photo-confirmation.json"
    probe.old_path = operator.root / "new-photo-public-observation.json"
    probe.client = operator.public
    if operation == "rollout-smoke-create":
        # prepare durably marks registration before POST; an existing file blocks a second attempt.
        canary.prepare(probe.state_path, probe.client)
        state = probe.confirm_observation(probe.state())
        state["activation"] = {"deploySha": operator.sha, "beforeUtc": dt.datetime.now(dt.timezone.utc).isoformat()}
        canary.private_write(probe.state_path, state)
        return canary.confirm_create(probe.state_path, probe.confirmation_path, probe.client)
    state = probe.state()
    canary.require(state.get("activation", {}).get("deploySha") == operator.sha)
    if operation == "rollout-smoke-cleanup":
        return canary.cleanup(probe.state_path, probe.client)
    canary.require(operation == "rollout-smoke-verify" and not state.get("deleted"))
    probe.old_photo(state)
    client = activation.CacheDisabledClient(probe.client)
    result = canary.verify(probe.state_path, probe.old_path, client, recipe=1)
    state = probe.state()
    home = client.api("GET", "/api/v1/check-ins/home/" + canary.identity(state["homeId"]), state["accessToken"])
    canary.validate_home(state, home)
    # The actual owner DTO must advertise all twelve protected descriptors; HEAD validates each.
    observed = canary.cleanup_observation(state, home, state["accessToken"], client)
    canary.require(len(observed["photos"]) == 2 and all(len(photo["variants"]) == 6 for photo in observed["photos"]))
    result["ownerDescriptorsVerified"] = 12
    canary.private_write(operator.root / "new-photo-smoke-report.json", result)
    return result


def main(argv=None, environment=None, commands=None, public_client=None):
    operation = None
    try:
        canary.require(not (sys.argv[1:] if argv is None else argv) and Path.cwd().resolve() == activation.ROOT)
        environment = dict(os.environ if environment is None else environment)
        operation = environment.get("PHOTO_OPERATION")
        canary.require(operation in OPERATIONS)
        with activation.private_lock():
            operator = rollout.Rollout(environment, commands=commands, public_client=public_client)
            result = execute(operator, operation)
        print(json.dumps({"success": True, "operation": operation, "result": result}, allow_nan=False))
        return 0
    except Exception:
        print(json.dumps({"success": False, "operation": operation if operation in OPERATIONS else None,
                          "requiresPrivateStateReview": True, "automaticWriteRetry": False}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
