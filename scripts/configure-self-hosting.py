#!/usr/bin/env python3
"""Create first-install configuration without printing or overwriting secrets."""
import argparse
import ipaddress
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import tempfile
from urllib.parse import urlsplit


def docker(*args):
    return subprocess.run(["docker", *args], check=True, capture_output=True, text=True).stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("origin", help="HTTPS origin reachable from household browsers and iOS")
    parser.add_argument("--directory", type=Path, default=Path(__file__).resolve().parents[1] / "deploy")
    parser.add_argument("--project", default="recipe-manager")
    args = parser.parse_args()
    origin = urlsplit(args.origin)
    if (origin.scheme != "https" or not origin.hostname or origin.username or origin.password
            or origin.path not in ("", "/") or origin.query or origin.fragment):
        parser.error("Use an HTTPS origin without credentials, a path, query or fragment.")
    try:
        _ = origin.port
    except ValueError:
        parser.error("Use a valid HTTPS port.")
    if not re.fullmatch(r"[a-z][a-z0-9_-]{0,39}", args.project):
        parser.error("Use a project name starting with a letter and at most 40 lowercase letters, digits, hyphens or underscores.")
    directory = args.directory.resolve()
    configuration = directory / ".env"
    protected = directory / "secrets" / "production"
    if configuration.exists() or protected.exists():
        parser.error("Configuration already exists. Keep the existing secrets; this command never rotates or overwrites them.")
    network_name = args.project + "_default"
    try:
        network = json.loads(docker("network", "inspect", network_name))[0]
    except subprocess.CalledProcessError:
        docker("network", "create", "--label", "com.docker.compose.project=" + args.project,
               "--label", "com.docker.compose.network=default", network_name)
        network = json.loads(docker("network", "inspect", network_name))[0]
    if network.get("Labels", {}).get("com.docker.compose.project") != args.project or network.get("Containers"):
        parser.error("The network is not an unused network owned by this project. Keep an existing installation's configuration.")
    addressing = next(item for item in network["IPAM"]["Config"] if ":" not in item["Subnet"])
    subnet = ipaddress.ip_network(addressing["Subnet"])
    proxy_ip = subnet.network_address + 10
    if proxy_ip >= subnet.broadcast_address:
        parser.error("The project network is too small for its optional TLS proxy.")
    os.umask(0o077)
    protected.mkdir(parents=True, mode=0o700)
    protected.parent.chmod(0o700)
    protected.chmod(0o700)
    for name in ("postgres-password", "setup-token", "data-protection-password", "maintenance-secret"):
        (protected / name).write_text(secrets.token_urlsafe(48) + "\n")
    with tempfile.TemporaryDirectory(prefix="recipe-manager-certificate-") as temporary:
        temporary = Path(temporary)
        subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:3072", "-sha256", "-nodes", "-days", "3650",
                        "-subj", "/CN=RecipeManagerDataProtection", "-keyout", str(temporary / "key.pem"),
                        "-out", str(temporary / "certificate.pem")], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        subprocess.run(["openssl", "pkcs12", "-export", "-inkey", str(temporary / "key.pem"), "-in", str(temporary / "certificate.pem"),
                        "-out", str(protected / "data-protection.pfx"), "-passout", "file:" + str(protected / "data-protection-password")],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    # The host-only parent is 0700; individually mounted read-only secrets must be readable by
    # the non-root application UID. Compose file-backed secrets retain source permissions.
    for path in protected.iterdir():
        path.chmod(0o444)
    values = {"COMPOSE_PROJECT_NAME": args.project, "RECIPE_MANAGER_SECRET_DIR": str(protected),
              "RECIPE_MANAGER_HOST": "[" + origin.hostname + "]" if ":" in origin.hostname else origin.hostname,
              "RECIPE_MANAGER_NETWORK": network_name, "RECIPE_MANAGER_PUBLIC_ORIGIN": args.origin.rstrip("/"),
              "RECIPE_MANAGER_TRUSTED_PROXY_IP": addressing["Gateway"], "RECIPE_MANAGER_TLS_PROXY_IP": str(proxy_ip)}
    def quoted(value):
        if "\n" in value or "\r" in value:
            raise ValueError("Configuration values must be single lines")
        return "'" + value.replace("'", "\\'") + "'"
    configuration.write_text("".join(key + "=" + quoted(value) + "\n" for key, value in values.items()))
    configuration.chmod(0o600)
    print("Created private configuration at " + str(configuration) + ". No secret values were printed.")
    print("Keep the secret directory and encryption certificate with the installation's protected backups.")


if __name__ == "__main__":
    try:
        main()
    except (OSError, subprocess.CalledProcessError, ValueError, KeyError, StopIteration) as error:
        raise SystemExit("Configuration failed safely; inspect the private output directory before retrying. " + type(error).__name__)
