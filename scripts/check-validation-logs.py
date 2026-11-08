#!/usr/bin/env python3
"""Inspect only owned disposable deployment logs without printing secret values."""
import json
from pathlib import Path
import re
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
directory = Path(sys.argv[1]).resolve()
configuration = (directory / ".env").read_text()
if not re.search(r"^COMPOSE_PROJECT_NAME='rm-validation-[a-f0-9]{8}'$", configuration, re.M):
    raise SystemExit("Refuse a non-validation installation")
values = []
for filename in ("postgres-password", "setup-token", "data-protection-password", "maintenance-secret"):
    values.append((directory / "secrets" / "production" / filename).read_text().strip())
record = json.loads((directory / "verification.json").read_text())
values.append(record["password"])
values.extend(pair.split("=", 1)[1] for _, pair in record.get("cookies", []))
result = subprocess.run(["docker", "compose", "--env-file", str(directory / ".env"), "-f", str(root / "deploy/compose.yaml"),
                         "-f", str(root / "deploy/compose.tls.yaml"), "logs", "--no-color"], capture_output=True, check=True, text=True)
logs = result.stdout + result.stderr
if any(value and value in logs for value in values):
    raise SystemExit("A synthetic secret appeared in private validation logs; inspect locally without publishing the value.")
if re.search(r'(?i)(authorization: bearer|password=|X-XSRF-TOKEN:|Cookie: RecipeManager)', logs):
    raise SystemExit("Potential credential logging needs private inspection")
print("Verified mounted secrets, synthetic account password and saved session cookies are absent from deployment logs.")
