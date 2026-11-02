"""Bind the tested Docker image to its source commit and publication receipt."""
import argparse
import json
from pathlib import Path
import re


def image_metadata(inspect, commit, expected_id=None):
    if not re.fullmatch(r"[a-f0-9]{40}|[a-f0-9]{64}", commit):
        raise ValueError("A full source commit is required.")
    item = inspect[0]
    image_id = item["Id"]
    if not re.fullmatch(r"sha256:[a-f0-9]{64}", image_id) or (expected_id is not None and image_id != expected_id):
        raise ValueError("The image ID differs from the built/tested image.")
    if item["Os"] != "linux" or item["Architecture"] != "amd64":
        raise ValueError("Only the tested linux/amd64 image may be published.")
    labels = item["Config"]["Labels"]
    if labels.get("org.opencontainers.image.revision") != commit:
        raise ValueError("The image revision does not match this workflow commit.")
    return {"schemaVersion": 1, "commitSha": commit, "imageId": image_id, "platform": "linux/amd64"}


def verify(metadata, inspect, commit):
    expected = image_metadata(inspect, commit, metadata.get("imageId"))
    if metadata != expected:
        raise ValueError("The archived image metadata does not match this workflow and loaded image.")
    return expected


def receipt(metadata, repository, digest):
    if not re.fullmatch(r"[a-z0-9]+(?:[_-][a-z0-9]+)*/[a-z0-9]+(?:[._-][a-z0-9]+)*", repository):
        raise ValueError("Use a Docker Hub namespace/repository.")
    if not re.fullmatch(r"sha256:[a-f0-9]{64}", digest):
        raise ValueError("A registry manifest digest is required.")
    return {**metadata, "repository": "docker.io/" + repository, "digest": digest,
            "imageReference": "docker.io/" + repository + "@" + digest}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["record", "verify", "receipt"])
    parser.add_argument("--inspect")
    parser.add_argument("--commit")
    parser.add_argument("--expected-id")
    parser.add_argument("--metadata")
    parser.add_argument("--repository")
    parser.add_argument("--digest")
    parser.add_argument("--out")
    args = parser.parse_args()
    try:
        if args.command == "record":
            result = image_metadata(json.loads(Path(args.inspect).read_text()), args.commit, args.expected_id)
        elif args.command == "verify":
            result = verify(json.loads(Path(args.metadata).read_text()), json.loads(Path(args.inspect).read_text()), args.commit)
        else:
            result = receipt(json.loads(Path(args.metadata).read_text()), args.repository, args.digest)
        if args.out:
            Path(args.out).write_text(json.dumps(result, indent=2) + "\n")
        else:
            print(json.dumps(result))
    except (ValueError, KeyError, IndexError, TypeError, OSError) as error:
        parser.exit(1, str(error) + "\n")


if __name__ == "__main__":
    main()
