# Ports

Every port the development and interim staging stacks use, what binds it, whether it is published
outside its network and on which interface. "Published" means reachable from the machine's own
network stack; a container port with no entry in `ports:` is reachable only from inside the compose
network.

The rule behind the tables: **nothing binds an address wider than it needs.** A developer laptop is
regularly on an untrusted shop, home or café network, and the interim staging VM sits behind a
network gate, so every published port defaults to the loopback address.

## Development — hosts run from the SDK

This is the normal working set: `./scripts/dev up` for the services, `./scripts/dev run` for the
three processes.

| Port | Bound by | Published | Interface | Notes |
| --- | --- | --- | --- | --- |
| 5173 | Vite dev server (`pnpm dev`) | yes | `127.0.0.1` | Pinned by `strictPort`; proxies `/api` and `/health` to 8080. Open **this** origin. |
| 8080 | `Tailor360.Web` (SDK run) | yes | `127.0.0.1` | `./scripts/dev run` sets `ASPNETCORE_URLS=http://127.0.0.1:8080` and keeps a value you set yourself. |
| 8081 | `Tailor360.Worker` probes | yes | `0.0.0.0` | The host calls `UseUrls("http://0.0.0.0:{Worker:HealthPort}")` so that a container orchestrator can probe it; in a container the port is never published. |
| 5432 | PostgreSQL container | yes | `127.0.0.1` | `POSTGRES_PUBLISHED_PORT` in `infra/compose/.env`. |
| 9000 | MinIO S3 API | yes | `127.0.0.1` | `MINIO_API_PUBLISHED_PORT`. |
| 9001 | MinIO console | yes | `127.0.0.1` | `MINIO_CONSOLE_PUBLISHED_PORT`. |
| 1025 | Mailpit SMTP | yes | `127.0.0.1` | `MAILPIT_SMTP_PUBLISHED_PORT`. |
| 8025 | Mailpit web interface | yes | `127.0.0.1` | `MAILPIT_UI_PUBLISHED_PORT`; where sent mail is read. |
| 3310 | ClamAV `clamd` | yes | `127.0.0.1` | Only with `--profile scanner`. `CLAMAV_PUBLISHED_PORT`. |
| 4317 | OpenTelemetry collector, OTLP gRPC | yes | `127.0.0.1` | Only with `--profile observability`. |
| 4318 | OpenTelemetry collector, OTLP HTTP | yes | `127.0.0.1` | Only with `--profile observability`. |
| 13133 | Collector `health_check` extension | yes | `127.0.0.1` | Probed from the host; the image contains no shell to probe itself. |

Device testing on a phone or tablet on the same network needs both ends opened deliberately:
export `ASPNETCORE_URLS=http://0.0.0.0:8080` before `./scripts/dev run` and start the client with
`pnpm --dir clients/pwa dev --host`. Do that on a trusted network only.

## Development — the built images (`docker-compose.app.yml` overlay)

Used to verify what will actually be deployed: same Dockerfiles, same non-root user, same read-only
filesystem, same reverse proxy as the interim staging environment.

| Port | Bound by | Published | Interface | Notes |
| --- | --- | --- | --- | --- |
| 8080 | Caddy | yes | `127.0.0.1` | `TAILOR360_HTTP_PUBLISHED_PORT`. The only port this overlay publishes. |
| 8080 | `web` container | no | compose network | Reached as `web:8080` by Caddy. |
| 8081 | `worker` container probes | no | compose network | Probed by the container health check on `127.0.0.1` inside the container. |

The backing services keep the loopback ports of the base file, so SDK runs and the integration tests
still work while the overlay is up.

## Interim staging (`docker-compose.staging.yml`)

A single VM behind a network gate: an IP allowlist for the shop networks plus an identity-aware
proxy or WireGuard/Tailscale enrolment. Until authentication ships (#23) there is no public
hostname at all, and the application login is the second factor, never the only one.

| Port | Bound by | Published | Interface | Notes |
| --- | --- | --- | --- | --- |
| 8080 | Caddy | yes | `${TAILOR360_BIND_ADDRESS:-127.0.0.1}:${TAILOR360_STAGING_HTTP_PORT:-8080}` | The bind address is the VM's loopback or its WireGuard address; TLS is terminated by the gate in front. |
| 8025 | Mailpit web interface | yes | `${TAILOR360_BIND_ADDRESS:-127.0.0.1}:8025` | Synthetic mail only; nothing leaves the VM. |
| 8080 | `web` container | no | compose network | |
| 8081 | `worker` container probes | no | compose network | `/health/*` and the worker health port stay on the internal network. |
| 5432 | PostgreSQL | no | compose network | Administrative access is over an SSH session on the VM. |
| 9000, 9001 | MinIO API and console | no | compose network | |
| 3310 | ClamAV | no | compose network | |

## Tests

| Port | Bound by | Notes |
| --- | --- | --- |
| ephemeral | Testcontainers (PostgreSQL, MinIO, ClamAV) | Random high ports on `127.0.0.1`, chosen per run; nothing to configure. |
| from the variable | `TAILOR360_TEST_DATABASE_URL` | An externally provided instance instead of a container — for example port 5433 when a system PostgreSQL already owns 5432. |
| from the variable | `TAILOR360_TEST_S3_ENDPOINT` | An externally provided S3-compatible endpoint. |
| ephemeral | `WebApplicationFactory` in the API tests | In-memory transport; no TCP port is opened. |

## Changing a port

Everything above is a default and every one of them can be moved, which is the answer to "address
already in use":

| Port | Change it in |
| --- | --- |
| 5173 | `TAILOR360_PWA_PORT` (scripts) and `server.port` in `clients/pwa/vite.config.ts` |
| 8080 | `TAILOR360_WEB_PORT` (scripts) and `ASPNETCORE_URLS` for the host itself |
| 8081 | `TAILOR360_WORKER_HEALTH_PORT` (scripts) and `Worker__HealthPort` for the host |
| 5432, 9000, 9001, 1025, 8025, 3310, 4317, 4318, 13133 | `infra/compose/.env` (`*_PUBLISHED_PORT`) |
| 8080 in the overlay | `TAILOR360_HTTP_PUBLISHED_PORT` in `infra/compose/.env` |

The Vite dev server and the web host must agree: the proxy target in `vite.config.ts` is
`http://localhost:8080`, so moving the host means moving that value too.

See `docs/dev/troubleshooting.md` for how to find what is already holding a port.
