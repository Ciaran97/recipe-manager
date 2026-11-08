# Operator recovery through the running host

Enable the private listener with `Maintenance__SocketPath` (absolute path, at most 100 UTF-8 bytes),
`Maintenance__SecretFile` (32–1024-character random secret) and `Server__PublicAddress` (the app's
explicit HTTP/HTTPS bind address, for example https://localhost:7243). The socket directory must
already exist with mode 0700 and must not be a symlink. The host sets the socket to 0600 before
listening. It refuses an existing socket path: stop the previous host and verify it has exited before
removing a stale socket. Never remove a socket belonging to a running host.

For self-hosted Linux, keep the socket in the app's private runtime directory (for example
/run/recipe-manager/maintenance.sock) and mount the maintenance secret as a private file. The app
continues to expose its normal web/API listener. All maintenance paths are rejected on TCP before
body binding, regardless of Host/forwarded headers or possession of the secret. The Unix listener
rejects normal application paths. No public password-reset or deactivation endpoint is provided.

Run the packaged API executable with the same socket/secret environment settings:

```sh
dotnet RecipeManager.Api.dll maintenance reset-password user@example.com
dotnet RecipeManager.Api.dll maintenance rotate-stamp user@example.com
dotnet RecipeManager.Api.dll maintenance deactivate user@example.com
```

The password prompt hides input. Automation can append `--password-stdin` to reset-password and
supply the password through standard input; it is never a command-line argument. The CLI connects
only to the Unix socket, disables proxies/redirects, and sends the mounted secret in a header. It
starts no second server and never updates Identity or PostgreSQL independently.

Reset creates and consumes a short-lived standard Identity reset token inside the live host's common
revocation service. Password reset, deactivation and explicit stamp rotation use the same per-user
send fence, household transaction, stamp rotation and socket abort as logout/password changes/member
removal. A success message means the operation committed and affected sessions were revoked.
`last_administrator` leaves the sole active Administrator intact. `session_drain_pending` means a
transport has not drained; the operation has not reported success and must be retried after drain.

If the host cannot run, first stop it and restore its database/key/certificate configuration from a
verified backup. Then start it and use this live-host command. Version 1 does not ship a second-process
offline Identity writer. Stop/restore/restart avoids silently leaving live SignalR connections outside
the revocation fence. Recovery output contains status codes, never passwords, reset tokens or the
maintenance secret. Preserve shared household content when removing or deactivating accounts.
