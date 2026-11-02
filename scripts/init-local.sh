#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
python3 - <<'PY'
from pathlib import Path
import hashlib
import os
import secrets

path = Path('.env')
existing = path.read_text() if path.exists() else ''
values = dict(line.split('=', 1) for line in existing.splitlines() if '=' in line and not line.lstrip().startswith('#'))
if values.get('PACT_PUBLISHER_TOKEN_SHA256'):
    print('Keeping the existing local publisher configuration.')
else:
    token = values.get('PACT_PUBLISHER_TOKEN') or secrets.token_hex(32)
    if len(token) < 32 or any(character.isspace() for character in token):
        raise SystemExit('The existing local token must contain at least 32 non-whitespace characters.')
    addition = '\n' if existing and not existing.endswith('\n') else ''
    if 'PACT_PUBLISHER_TOKEN' not in values:
        addition += f'PACT_PUBLISHER_TOKEN={token}\n'
    addition += f'PACT_PUBLISHER_TOKEN_SHA256={hashlib.sha256(token.encode()).hexdigest()}\n'
    with path.open('a') as output:
        output.write(addition)
    os.chmod(path, 0o600)
    print('Local publisher credentials are configured in the ignored .env file.')
PY
