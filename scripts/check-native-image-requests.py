#!/usr/bin/env python3
"""Catch an image-view task restarting itself in the owned two-photo UI fixture."""
import argparse
import json
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("api_log", type=Path)
    arguments = parser.parse_args()
    count = 0
    for line in arguments.api_log.read_text().splitlines():
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        if (event.get("Category") == "Microsoft.AspNetCore.Routing.EndpointMiddleware"
                and event.get("EventId") == 0
                and event.get("State", {}).get("EndpointName") == "HTTP: GET /api/v1/images/{id:guid}"):
            count += 1
    # Two selected uploads can appear in editor, detail and library views. Allow lifecycle reloads
    # while rejecting the previously observed thousands of requests from a self-restarting task.
    if not 1 <= count <= 32:
        raise SystemExit(f"Native photo fixture image requests out of bounds: {count}; expected 1–32.")
    print(f"Native photo fixture image requests: {count} (bounded; no self-restarting fetch loop).")


if __name__ == "__main__":
    main()
