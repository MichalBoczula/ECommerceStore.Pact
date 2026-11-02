"""Verify deployed readiness and scoped write authentication without storing records."""
import argparse
import json
import os
import re
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ValueError("Deployment verification does not follow redirects.")


def base_url(value, allow_local_http=False):
    parsed = urlsplit(value)
    local = parsed.scheme == "http" and parsed.hostname in {"localhost", "127.0.0.1", "::1"} and allow_local_http
    if (parsed.scheme != "https" and not local) or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment or parsed.path not in {"", "/"}:
        raise ValueError("Use a root HTTPS URL without credentials, query or fragment.")
    return value.rstrip("/")


def call(opener, url, payload=None, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    request = Request(url, data=None if payload is None else json.dumps(payload).encode(), headers=headers)
    try:
        response = opener.open(request, timeout=10)
    except HTTPError as error:
        response = error
    with response:
        raw = response.read(2 * 1024 * 1024 + 1)
        if len(raw) > 2 * 1024 * 1024:
            raise ValueError("Deployment response exceeded the limit.")
        return response.status, json.loads(raw)


def verify(url, application, token, *, allow_local_http=False, wait_seconds=180):
    url = base_url(url, allow_local_http)
    if len(application) > 100 or not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", application):
        raise ValueError("Use the publisher's explicit application ID.")
    if not token or not 32 <= len(token) <= 512 or any(ord(c) < 33 or ord(c) > 126 for c in token):
        raise ValueError("Set PACT_PUBLISHER_TOKEN to the configured publisher credential.")
    opener = build_opener(NoRedirect())
    deadline = time.monotonic() + wait_seconds
    while True:
        try:
            status, ready = call(opener, url + "/health/ready")
            if status == 200 and ready.get("checks", {}).get("mongodb") == "Healthy":
                break
        except (URLError, TimeoutError):
            pass
        if time.monotonic() >= deadline:
            raise ValueError("Deployed MongoDB readiness did not become healthy within the verification window.")
        time.sleep(2)
    status, live = call(opener, url + "/health/live")
    if status != 200 or live.get("status") != "Healthy":
        raise ValueError("Deployed liveness did not pass.")
    # The URL fails domain validation before any repository write. A valid scoped
    # credential reaches this specific 400; absent/wrong-scope credentials cannot.
    payload = {"commitSha": "0" * 40, "repositoryUrl": "intentionally-invalid-repository-url"}
    path = url + "/api/applications/" + application + "/versions"
    anonymous, _ = call(opener, path, payload)
    if anonymous != 401:
        raise ValueError("Anonymous publication was not rejected with 401.")
    authenticated, problem = call(opener, path, payload, token)
    if authenticated != 400 or "repositoryUrl" not in problem.get("errors", {}):
        raise ValueError("Publisher authentication/scope did not reach the expected non-writing validation response.")
    return {"ready": True, "live": True, "anonymousWritesRejected": True, "publisherScopeVerified": application, "recordsWritten": 0}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", required=True)
    parser.add_argument("--application", required=True)
    parser.add_argument("--allow-local-http", action="store_true", help="Only for loopback container testing.")
    args = parser.parse_args()
    try:
        print(json.dumps(verify(args.url, args.application, os.environ.get("PACT_PUBLISHER_TOKEN"), allow_local_http=args.allow_local_http)))
    except (ValueError, URLError, TimeoutError):
        parser.exit(1, "Deployment verification failed; check readiness, HTTPS/ingress and the publisher scope. No credentials were printed.\n")


if __name__ == "__main__":
    main()
