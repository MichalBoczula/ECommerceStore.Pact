import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("image_metadata", Path(__file__).resolve().parents[2] / "scripts/image-metadata.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ImageMetadataTests(unittest.TestCase):
    def setUp(self):
        self.commit = "a" * 40
        self.inspect = [{"Id": "sha256:" + "b" * 64, "Os": "linux", "Architecture": "amd64",
                         "Config": {"Labels": {"org.opencontainers.image.revision": self.commit}}}]
        self.metadata = module.image_metadata(self.inspect, self.commit)

    def test_loaded_image_matches_tested_archive(self):
        self.assertEqual(self.metadata, module.verify(self.metadata, self.inspect, self.commit))

    def test_rebuilt_image_cannot_replace_tested_image(self):
        other = copy.deepcopy(self.inspect)
        other[0]["Id"] = "sha256:" + "c" * 64
        with self.assertRaises(ValueError):
            module.verify(self.metadata, other, self.commit)

    def test_another_workflow_commit_is_rejected(self):
        with self.assertRaises(ValueError):
            module.verify(self.metadata, self.inspect, "d" * 40)

    def test_another_platform_is_rejected(self):
        self.inspect[0]["Architecture"] = "arm64"
        with self.assertRaises(ValueError):
            module.verify(self.metadata, self.inspect, self.commit)

    def test_missing_revision_label_is_rejected(self):
        self.inspect[0]["Config"]["Labels"] = {}
        with self.assertRaises(ValueError):
            module.image_metadata(self.inspect, self.commit)

    def test_immutable_reference_uses_registry_digest_not_local_image_id(self):
        result = module.receipt(self.metadata, "mb0101/ecommerce-store-pact-api", "sha256:" + "e" * 64)
        self.assertEqual("docker.io/mb0101/ecommerce-store-pact-api@sha256:" + "e" * 64, result["imageReference"])
        self.assertNotEqual(result["digest"], result["imageId"])

    def test_bad_repository_and_digest_are_rejected(self):
        for repository, digest in [("--malicious", "sha256:" + "e" * 64), ("mb0101/pact", "latest")]:
            with self.subTest(repository=repository, digest=digest), self.assertRaises(ValueError):
                module.receipt(self.metadata, repository, digest)
