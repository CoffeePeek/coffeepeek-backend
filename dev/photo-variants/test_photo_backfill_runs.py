import unittest
from unittest.mock import Mock

import photo_backfill_runs as tool

RUN = "00000000-0000-0000-0000-000000000003"


class ControlTests(unittest.TestCase):
    def test_output_has_allowlisted_fields_only(self):
        output = tool.safe_run_view({"run": {"id": RUN, "paused": True, "storageKey": "private-key"},
                                    "jobs": {"Ready": 4, "sensitive-key": 99}, "generationDrained": True})
        self.assertEqual({"id": RUN, "paused": True, "jobs": {"Ready": 4}, "generationDrained": True}, output)

    def test_create_makes_one_request_with_fixed_cutoffs_and_never_resumes(self):
        args = tool.parser().parse_args(["create", "--media-url", "http://media", "--before", "2026-01-01T00:00:00Z"])
        client = Mock()
        client.call.return_value = {"id": RUN, "paused": True}
        self.assertTrue(tool.run_control(args, client)["paused"])
        client.call.assert_called_once()
        call = client.call.call_args
        self.assertEqual("/internal/check-in-photo-backfill/runs", call.args[1])
        self.assertEqual(10, call.args[2]["batchSize"])

    def test_advance_does_not_retry_or_loop_automatically(self):
        args = tool.parser().parse_args(["advance", "--media-url", "http://media", "--run-id", RUN])
        client = Mock()
        client.call.return_value = {"outcome": "rate_limited", "run": {"id": RUN}}
        result = tool.run_control(args, client)
        client.call.assert_called_once()
        self.assertEqual("rate_limited", result["outcome"])

    def test_status_is_read_only(self):
        args = tool.parser().parse_args(["status", "--media-url", "http://media", "--run-id", RUN])
        client = Mock()
        client.call.return_value = {"run": {"id": RUN}, "jobs": {}, "generationDrained": True}
        tool.run_control(args, client)
        self.assertEqual("GET", client.call.call_args.kwargs["method"])


if __name__ == "__main__":
    unittest.main()
