# Home-server access through Cloudflare Tunnel

This guide uses a named, locally managed tunnel on a Linux Docker host. It preserves the same public HTTPS URL for Angular, SwiftUI and household invitations. No inbound router port forwarding is needed; the connector needs outbound connectivity to Cloudflare.

The route is:

```text
Browser / iPhone → Cloudflare HTTPS → encrypted tunnel → cloudflared on your server
                                                        ↓ verified HTTPS
                                                   bundled Caddy
                                                        ↓ private Docker network
                                                   Recipe Manager
```

The application and database remain on your server. Cloudflare handles the public HTTPS route, so requests pass through its network. Use Recipe Manager’s accounts and invitations. An additional interactive Cloudflare Access login or browser challenge is not supported by the native client.

These are operator instructions, not a claim that an owner’s live Cloudflare zone has been configured or tested. Local TLS/proxy deployment is validated separately; verify the external route using the checks at the end.

## 1. Start a loopback-only HTTPS origin

For a **new installation**, choose the public hostname you will keep, then run from the repository root:

```sh
python3 scripts/configure-self-hosting.py https://recipes.example.com
cat >> deploy/.env <<'CONFIG'
RECIPE_MANAGER_TLS_BIND_ADDRESS='127.0.0.1'
RECIPE_MANAGER_TLS_HTTP_PORT='8081'
RECIPE_MANAGER_TLS_HTTPS_PORT='8443'
CONFIG

dc() {
  docker compose --env-file deploy/.env \
    -f deploy/compose.yaml -f deploy/compose.tls.yaml "$@"
}

dc config --quiet
dc up -d --build --wait
dc exec -T proxy cat /data/caddy/pki/authorities/local/root.crt \
  > recipe-manager-local-root.crt
```

Caddy is available on server loopback port 8443. Its certificate is for `recipes.example.com`, not the loopback IP. Verify it without changing DNS:

```sh
curl --fail --cacert recipe-manager-local-root.crt \
  --resolve recipes.example.com:8443:127.0.0.1 \
  https://recipes.example.com:8443/health/ready
```

An existing installation must retain its secrets, project/network identity and volumes. Change its origin deliberately as described in [self-hosting](self-hosting.md); do not rerun the initializer over it. When Caddy already terminates your existing HTTPS route, you may use that verified proxy instead of starting another one.

## 2. Install cloudflared and create the tunnel

Install `cloudflared` on the **host**, following the [official downloads](https://developers.cloudflare.com/tunnel/downloads/). The loopback upstream in this guide is not reachable as `127.0.0.1` from a separate connector container.

Authenticate to the Cloudflare account that manages your domain, create a named tunnel and add its DNS route:

```sh
cloudflared tunnel login
cloudflared tunnel create recipe-manager
cloudflared tunnel route dns recipe-manager recipes.example.com
```

Record the UUID returned by the create command. It also creates a UUID-named credential JSON in your user’s `.cloudflared` directory. That tunnel credential is private; the separate login `cert.pem` is an account-management credential and should not be copied into the runtime service directory.

Cloudflare’s [local tunnel instructions](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/do-more-with-tunnels/local-management/create-local-tunnel/) explain the account authorization and DNS steps. Use a named tunnel for this installation rather than a temporary Quick Tunnel address.

## 3. Store credentials and configuration privately

Use a dedicated service account, or an existing appropriate account. On a Debian/Ubuntu-style host, create it if absent:

```sh
id cloudflared >/dev/null 2>&1 || \
  sudo useradd --system --user-group --home-dir /nonexistent \
    --shell /usr/sbin/nologin cloudflared
sudo install -d -m 0750 -o root -g cloudflared /etc/cloudflared
```

Replace `YOUR-TUNNEL-UUID` below with the returned UUID:

```sh
sudo install -m 0640 -o root -g cloudflared \
  "$HOME/.cloudflared/YOUR-TUNNEL-UUID.json" \
  /etc/cloudflared/recipe-manager.json
sudo install -m 0644 -o root -g root recipe-manager-local-root.crt \
  /etc/cloudflared/recipe-manager-origin-ca.pem
sudoedit /etc/cloudflared/recipe-manager.yml
```

Put this in that YAML file, replacing the UUID and hostname:

```yaml
tunnel: YOUR-TUNNEL-UUID
credentials-file: /etc/cloudflared/recipe-manager.json

ingress:
  - hostname: recipes.example.com
    service: https://127.0.0.1:8443
    originRequest:
      originServerName: recipes.example.com
      httpHostHeader: recipes.example.com
      caPool: /etc/cloudflared/recipe-manager-origin-ca.pem
  - service: http_status:404
```

Then restrict its permissions and validate the ingress rules:

```sh
sudo chown root:cloudflared /etc/cloudflared/recipe-manager.yml
sudo chmod 0640 /etc/cloudflared/recipe-manager.yml
sudo -u cloudflared cloudflared tunnel \
  --config /etc/cloudflared/recipe-manager.yml ingress validate
```

`originServerName` matches Caddy’s certificate, `httpHostHeader` preserves the public hostname, and `caPool` supplies its trusted public CA. Keep certificate verification enabled. These parameters are documented in [Cloudflare’s origin reference](https://developers.cloudflare.com/tunnel/reference/origin-parameters/).

The bundled Caddy then supplies a consistent single forwarded-header hop to the application. Connecting cloudflared directly to the base HTTP app without normalizing all forwarded headers is not the configuration described here.

## 4. Configure the public route

In Cloudflare, configure rules for this hostname:

- **Bypass caching** for `/api`, `/api/*`, `/hubs` and `/hubs/*`. Do not cache household API responses, images behind API authentication, session endpoints or SignalR traffic.
- **Disable response compression/transformation** for those same paths. Leave API ETags and conditional request headers untouched. Cloudflare can weaken or remove strong ETags when transforming responses; that can correctly cause conditional edits to fail with 412.
- Keep WebSocket support enabled. Interactive browser-only challenges on API/hub paths prevent SwiftUI from using them; the application’s own authentication still applies.

Example rule expression, with your hostname substituted:

```text
(http.host eq "recipes.example.com" and
 (http.request.uri.path eq "/api" or starts_with(http.request.uri.path, "/api/") or
  http.request.uri.path eq "/hubs" or starts_with(http.request.uri.path, "/hubs/")))
```

Use this match for a **Cache Rule → Bypass cache** and a **Compression Rule → Disable compression**. Do not make API responses cacheable just to enable an ETag cache setting. See [Compression Rules](https://developers.cloudflare.com/rules/compression-rules/) and [Cloudflare ETag behavior](https://developers.cloudflare.com/cache/reference/etag-headers/).

## 5. Start a dedicated Linux service

First check `command -v cloudflared`. Substitute its actual absolute path in `ExecStart` if it differs from `/usr/bin/cloudflared`. Create `/etc/systemd/system/recipe-manager-tunnel.service`:

```ini
[Unit]
Description=Recipe Manager Cloudflare Tunnel
Wants=network-online.target
After=network-online.target docker.service

[Service]
Type=simple
User=cloudflared
Group=cloudflared
ExecStart=/usr/bin/cloudflared tunnel --no-autoupdate --config /etc/cloudflared/recipe-manager.yml run
Restart=on-failure
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict

[Install]
WantedBy=multi-user.target
```

This uses a dedicated service name so it does not replace a connector serving other applications.

```sh
sudo systemctl daemon-reload
sudo systemctl enable --now recipe-manager-tunnel.service
sudo systemctl status recipe-manager-tunnel.service
```

Keep cloudflared updated through your package-management process. If the connector cannot reach Cloudflare, consult the [documented outbound firewall requirements](https://developers.cloudflare.com/tunnel/configuration/#firewall-rules); opening inbound router ports is not the solution.

## 6. Verify browser and native operation

Open `https://recipes.example.com/health/ready` from outside your LAN, then complete [household setup](../README.md#create-your-household). Use that same URL in the iOS app. Clients see Cloudflare’s public HTTPS certificate; they do not need your private Caddy root for this public route.

Verify login, invitation links, a recipe edit, an image upload and shopping-list updates between two signed-in devices. An API read followed by an edit must keep its original strong ETag; do not remove `W/` or encoding suffixes in clients to work around an altered validator.

| Symptom | Check |
|---|---|
| Tunnel is healthy, but the page returns 502 | Caddy health, the loopback upstream port, CA file permissions and `originServerName` |
| Certificate validation fails | The exported **public** Caddy CA and certificate hostname; keep TLS verification enabled |
| Login works on web but not iPhone | Browser-only Access/challenge rules, public origin and API routing |
| Edits return unexpected 412 | ETag transformation/compression and conditional-header preservation; real concurrent edits can also legitimately return 412 |
| Changes need a refresh | WebSocket routing through `/hubs/*`, caching rules and foreground/reconnection behavior |

## Backups and recovery

Include the tunnel configuration and credential JSON in protected server backups, alongside the application database, image/key volumes, secrets and Caddy TLS data. The CA certificate installed in cloudflared must match the restored Caddy CA. Do not regenerate the working installation’s encryption or TLS keys as a troubleshooting step.

See [backup/restore](backup-restore.md), [account recovery](recovery.md) and [self-hosting](self-hosting.md) for the complete operational procedures.

## Documentation verification

The configuration examples were checked on 2026-11-05 using the pinned deployment Caddy image, nginx (`sha256:66b50d24288dba67f3d000b31223daefc4868c58d29aaacb6a4ff9771597b5eb`) and cloudflared 2026.10.7 (`sha256:4955524aeaa379e154023d5a027c250f7b35041a2e7e950cf13f144bc9b9c5ba`). Base/TLS/tunnel Compose rendering (including loopback-only bindings), Caddy adaptation/validation, nginx configuration testing, cloudflared ingress validation and service-command flag parsing pass with networking disabled and synthetic certificates. No live tunnel, DNS route or Cloudflare account was created by these checks.
