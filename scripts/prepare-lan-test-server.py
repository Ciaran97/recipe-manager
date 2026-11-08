#!/usr/bin/env python3
"""Prepare a fresh LAN-only Recipe Manager test stack and a certificate-only bootstrap page."""
import argparse
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import plistlib
import re
import secrets
import socket
import ssl
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--hostname", required=True, help="This Mac's .local hostname or the supplied LAN IPv4 address")
    parser.add_argument("--address", required=True, help="This Mac's private LAN IPv4 address")
    parser.add_argument("--image", required=True, help="An existing, locally validated application image")
    parser.add_argument("--https-port", type=int, default=8443)
    parser.add_argument("--certificate-port", type=int, default=8889)
    parser.add_argument("--app-port", type=int, default=18080)
    args = parser.parse_args()
    hostname = args.hostname.lower()
    address = ipaddress.ip_address(args.address)
    if hostname != str(address) and not re.fullmatch(r"[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.local", hostname):
        parser.error("Use this Mac's single-label .local hostname or its supplied LAN IPv4 address.")
    if address.version != 4 or not any(address in ipaddress.ip_network(network) for network in ("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16")):
        parser.error("Use the Mac's private LAN IPv4 address.")
    if len({args.https_port, args.certificate_port, args.app_port}) != 3:
        parser.error("Choose distinct ports.")
    for host, port in [(str(address), args.https_port), (str(address), args.certificate_port), ("127.0.0.1", args.app_port)]:
        if not 1024 <= port <= 65535:
            parser.error("Use unprivileged valid ports.")
        with socket.socket() as probe:
            probe.bind((host, port))
    image = json.loads(subprocess.check_output(["docker", "image", "inspect", args.image]))[0]
    os.umask(0o077)
    directory = Path(tempfile.mkdtemp(prefix="recipe-manager-lan-"))
    project = "rm-device-" + secrets.token_hex(4)
    origin = f"https://{hostname}:{args.https_port}"
    bootstrap = f"http://{address}:{args.certificate_port}"
    public = directory / "public"
    public.mkdir(mode=0o755)
    ca_name = "Recipe Manager LAN Test " + project.removeprefix("rm-device-")
    root_name = ca_name + " Root"
    base = ["docker", "compose", "-p", project, "--env-file", str(directory / ".env"),
            "-f", str(ROOT / "deploy/compose.yaml"), "-f", str(ROOT / "deploy/compose.tls.yaml"),
            "-f", str(directory / "compose.lan.json")]
    log = directory / "preparation.log"
    try:
        with log.open("w") as output:
            subprocess.run([sys.executable, str(ROOT / "scripts/configure-self-hosting.py"), origin,
                            "--directory", str(directory), "--project", project], check=True, stdout=output, stderr=output)
        with (directory / ".env").open("a") as output:
            # Pin this owned test stack to the already inspected local image, never pull a tag.
            output.write(f"RECIPE_MANAGER_IMAGE='{image['Id']}'\n")
            output.write(f"RECIPE_MANAGER_TLS_BIND_ADDRESS='{address}'\n")
            output.write(f"RECIPE_MANAGER_TLS_HTTP_PORT='{args.certificate_port}'\n")
            output.write(f"RECIPE_MANAGER_TLS_HTTPS_PORT='{args.https_port}'\n")
            output.write(f"RECIPE_MANAGER_HTTP_PORT='{args.app_port}'\n")
        (directory / "Caddyfile").write_text(f'''{{
    admin off
    skip_install_trust
    default_sni {hostname}
    pki {{
        ca local {{
            name "{ca_name}"
            root_cn "{root_name}"
        }}
    }}
}}
https://{hostname} {{
    tls internal
    # Keep opaque API validators unchanged for conditional reads and writes.
    @compressible not path /api /api/*
    encode @compressible zstd gzip
    reverse_proxy app:8080
}}
http://:80 {{
    @profile path /recipe-manager-test-ca.mobileconfig
    handle @profile {{
        root * /bootstrap
        header Content-Type application/x-apple-aspen-config
        file_server
    }}
    @certificate path /recipe-manager-test-ca.crt
    handle @certificate {{
        root * /bootstrap
        header Content-Type application/x-x509-ca-cert
        file_server
    }}
    respond 404
}}
''')
        (directory / "Caddyfile").chmod(0o444)
        override = {"services": {
            "app": {"pull_policy": "never"}, "migrator": {"pull_policy": "never"},
            "proxy": {"volumes": [str(directory / "Caddyfile") + ":/etc/caddy/Caddyfile:ro",
                                   str(public) + ":/bootstrap:ro"]}}}
        (directory / "compose.lan.json").write_text(json.dumps(override, indent=2) + "\n")
        configuration = json.loads(subprocess.check_output(base + ["config", "--format", "json"]))
        assert not configuration["services"]["db"].get("ports")
        assert all(port["host_ip"] == str(address) for port in configuration["services"]["proxy"]["ports"])
        assert all(port["host_ip"] == "127.0.0.1" for port in configuration["services"]["app"]["ports"])
        import shlex
        (directory / "stop.sh").write_text("#!/usr/bin/env bash\nset -euo pipefail\n" + shlex.join(base + ["stop"]) + "\n")
        (directory / "stop.sh").chmod(0o700)
        metadata = {"project": project, "origin": origin, "bootstrap": bootstrap, "rootName": root_name,
                    "directory": str(directory), "imageId": image["Id"]}
        (directory / "info.json").write_text(json.dumps(metadata, indent=2) + "\n")
        Path("/private/tmp/recipe-manager-lan-current").write_text(str(directory) + "\n")
        with log.open("a") as output:
            subprocess.run(base + ["up", "-d", "--no-build", "--wait", "--wait-timeout", "150"],
                           check=True, stdout=output, stderr=output)
            subprocess.run(base + ["cp", "proxy:/data/caddy/pki/authorities/local/root.crt", str(directory / "root-ca.pem")],
                           check=True, stdout=output, stderr=output)
        der = subprocess.check_output(["openssl", "x509", "-in", str(directory / "root-ca.pem"), "-outform", "DER"])
        certificate = {"PayloadType": "com.apple.security.root", "PayloadVersion": 1,
                       "PayloadIdentifier": "local.recipemanager." + project + ".root",
                       "PayloadUUID": str(uuid.uuid4()).upper(), "PayloadDisplayName": root_name,
                       "PayloadCertificateFileName": "recipe-manager-test-ca.crt", "PayloadContent": der}
        profile = {"PayloadType": "Configuration", "PayloadVersion": 1,
                   "PayloadIdentifier": "local.recipemanager." + project,
                   "PayloadUUID": str(uuid.uuid4()).upper(), "PayloadDisplayName": ca_name,
                   "PayloadDescription": "Trust anchor for Recipe Manager LAN testing. Remove this profile after testing.",
                   "PayloadRemovalDisallowed": False, "PayloadContent": [certificate]}
        (public / "recipe-manager-test-ca.crt").write_bytes(der)
        (public / "recipe-manager-test-ca.mobileconfig").write_bytes(plistlib.dumps(profile))
        for path in public.iterdir():
            path.chmod(0o444)
        context = ssl.create_default_context(cafile=str(directory / "root-ca.pem"))
        deadline = time.monotonic() + 30
        while True:
            try:
                with urllib.request.urlopen(origin + "/api/v1/setup", context=context, timeout=5) as response:
                    assert json.load(response)["setupRequired"] is True
                break
            except (OSError, urllib.error.URLError):
                if time.monotonic() >= deadline:
                    raise
                time.sleep(0.5)
        with urllib.request.urlopen(bootstrap + "/recipe-manager-test-ca.crt", timeout=5) as response:
            assert response.read() == der
        for path in ["/", "/api/v1/setup", "/secrets/setup-token"]:
            try:
                urllib.request.urlopen(bootstrap + path, timeout=5)
                raise AssertionError("HTTP bootstrap must not serve application or secrets")
            except urllib.error.HTTPError as error:
                assert error.code == 404
        fingerprint = hashlib.sha256(der).hexdigest().upper()
        metadata["rootSha256"] = ":".join(fingerprint[i:i+2] for i in range(0, len(fingerprint), 2))
        (directory / "info.json").write_text(json.dumps(metadata, indent=2) + "\n")
        print("LAN HTTPS test server ready: " + origin)
        print("Certificate profile: " + bootstrap + "/recipe-manager-test-ca.mobileconfig")
        print("Certificate name: " + root_name)
        print("Root certificate SHA-256: " + metadata["rootSha256"])
        print("Private configuration and stop command: " + str(directory))
        print("No setup token, account credentials or private CA key were exposed.")
    except Exception as error:
        raise SystemExit("LAN preparation failed: " + type(error).__name__ + ". Retained private artifacts: " + str(directory)) from None


if __name__ == "__main__":
    main()
