import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch
import json

spec = importlib.util.spec_from_file_location("deployment_probe", Path(__file__).resolve().parents[2] / "scripts/verify-deployment.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class DeploymentProbeTests(unittest.TestCase):
    def test_public_url_requires_https_and_root_path(self):
        for url in ["http://example.com", "https://user:secret@example.com", "https://example.com?token=secret", "https://example.com/other", "https://example.com#fragment"]:
            with self.subTest(url=url), self.assertRaises(ValueError):
                module.base_url(url)
        self.assertEqual("https://example.com", module.base_url("https://example.com/"))

    def test_http_exception_only_allows_literal_loopback(self):
        self.assertEqual("http://127.0.0.1:5080", module.base_url("http://127.0.0.1:5080", True))
        with self.assertRaises(ValueError):
            module.base_url("http://localhost.attacker.invalid", True)

    def test_redirects_are_rejected_before_forwarding_credentials(self):
        with self.assertRaises(ValueError):
            module.NoRedirect().redirect_request(None, None, 302, "", {}, "https://other.invalid")

    def test_transient_non_json_readiness_retries_before_authentication(self):
        replies = [json.JSONDecodeError("warming", "<html>", 0),
                   (200, {"checks": {"mongodb": "Healthy"}}), (200, {"status": "Healthy"}),
                   (401, {}), (400, {"errors": {"repositoryUrl": ["Invalid"]}})]
        with patch.object(module, "call", side_effect=replies), patch.object(module.time, "sleep") as sleep:
            result = module.verify("https://example.com", "payments", "synthetic-test-token-" + "a" * 32)
        self.assertEqual(0, result["recordsWritten"])
        sleep.assert_called_once()
