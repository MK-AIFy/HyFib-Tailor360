# Infrastructure

Container definitions for the local development environment and for the interim staging environment
of HyFib Tailor360. Deployment topology is Section 4.7 of `docs/IMPLEMENTATION_PLAN.md`; the
guard-rails for staging are the #22 blueprint.

Nothing in this directory holds a secret. Every credential is either an obviously fake development
placeholder in `compose/.env.example` or a file mounted at run time under `/run/secrets`.

## What each file is

| File | Purpose |
| --- | --- |
| `compose/docker-compose.yml` | Development **backing services** only: PostgreSQL, MinIO (plus a one-shot bucket creator), ClamAV, Mailpit and an OpenTelemetry collector. The application itself runs from the SDK. |
| `compose/docker-compose.app.yml` | Overlay that additionally runs the built `web` and `worker` images behind Caddy, exactly as they are deployed. |
| `compose/docker-compose.staging.yml` | The interim staging stack: Caddy, web, worker and their backing services, with no published database, object-storage or scanner ports. |
| `compose/.env.example` | Every variable the three compose files read, with development-only values. Copy to `compose/.env`. |
| `docker/Dockerfile.web` | Multi-stage build of the web host; copies the built client into `wwwroot`. |
| `docker/Dockerfile.worker` | Multi-stage build of the background worker. |
| `docker/Dockerfile.pwa` | Builds `clients/pwa` and emits the bundle as an assets-only image the web build copies from. |
| `observability/otel-collector-config.yaml` | Collector pipeline: OTLP in, `debug` out. Exports nothing off the machine by default. |
| `caddy/Caddyfile` | Reverse proxy for the overlay and for staging: compression, `X-Robots-Tag`, internal health probes, ACME DNS-01 placeholder. |

## Development: the application runs from the SDK

The web host, the worker and the client are **not** containers during development. They run from the
.NET SDK and from Vite, because that is what gives hot reload, a debugger and readable stack traces.
`compose/docker-compose.yml` provides only the services they talk to.

```bash
cp infra/compose/.env.example infra/compose/.env

# Backing services
docker compose -f infra/compose/docker-compose.yml up -d --wait

# Optional: the malware scanner (heavy — roughly 1.5 GB of resident memory, ~2 min to become ready)
docker compose -f infra/compose/docker-compose.yml --profile scanner up -d --wait

# Optional: the OpenTelemetry collector
docker compose -f infra/compose/docker-compose.yml --profile observability up -d

# Stop, keeping the data
docker compose -f infra/compose/docker-compose.yml down

# Stop and discard every volume (a clean database, empty buckets, no mail)
docker compose -f infra/compose/docker-compose.yml down --volumes
```

`--wait` blocks until the long-running services report healthy, which is what makes the command
usable in `./scripts/dev up` and in CI. The one-shot `createbuckets` service exits as soon as the
buckets exist; services that need it wait with `condition: service_completed_successfully`.

## Running the built images locally

The web image copies the built client out of an assets-only image, so the client is built first.

```bash
docker build -f infra/docker/Dockerfile.pwa --target assets -t tailor360-pwa:local .
docker compose -f infra/compose/docker-compose.yml -f infra/compose/docker-compose.app.yml build
docker compose -f infra/compose/docker-compose.yml -f infra/compose/docker-compose.app.yml up -d --wait
# The application is then on http://127.0.0.1:8080 through Caddy.
```

Both `docker build` commands run from the repository root: central package management means a
restore needs `Directory.Packages.props`, `Directory.Build.props` and `global.json`, so the build
context is the root and the `-f` path points into this directory.

The repository has no `.dockerignore` yet; until it does, the .NET images delete any `bin/` and
`obj/` directory they copied from the working tree, and the client image deletes `node_modules/` and
`dist/`, so an image never depends on who built it.

### Secret files for the overlay

The hosts read infrastructure secrets as files (Section 4.4), never from environment variables: the
file name is the configuration key and `__` is the section separator. Create them once —
`infra/compose/secrets/` is git-ignored:

```bash
mkdir -p infra/compose/secrets
printf 'Host=postgres;Port=5432;Database=tailor360;Username=tailor360;Password=tailor360_dev_only' \
  > infra/compose/secrets/Database__ConnectionString
printf 'tailor360-dev'                > infra/compose/secrets/ObjectStorage__AccessKey
printf 'tailor360-dev-not-a-secret'   > infra/compose/secrets/ObjectStorage__SecretKey
chmod 0444 infra/compose/secrets/*
```

`printf` rather than `echo`, because a trailing newline becomes part of the configuration value.
The mode matters: Compose bind-mounts these files with their host ownership, and the containers run
as the unprivileged `app` user (UID 1654) of the .NET runtime image, so the files must be readable
by that user. These are development values; on the staging VM the deploy script places the files
`0440` and owned so that only the container user can read them.

## Interim staging

```bash
# On the VM, with the promoted image digests and the secret directory in the environment
docker compose -f infra/compose/docker-compose.staging.yml up -d --wait --pull always
```

Access is gated **in front of** the stack: an IP allowlist for the shop networks plus an
identity-aware proxy or WireGuard/Tailscale enrolment for phones and tablets. The application login
is the second factor, never the only one, and until issue #23 merges there is no public hostname at
all — which is why `TAILOR360_BIND_ADDRESS` defaults to `127.0.0.1` and the proxy is reached through
the tunnel. The environment holds synthetic data only, has no provider credentials (mail goes to
Mailpit) and uses its own Data Protection ring, so a token minted there can never be decrypted by
production.

`TAILOR360_WEB_IMAGE`, `TAILOR360_WORKER_IMAGE` and `TAILOR360_STAGING_SECRETS_DIR` are required:
the stack refuses to start rather than silently falling back to a local default.

## Published ports, and why the data services stay on the loopback

A development laptop is regularly on a shop, home or café network. Everything in the development
stack is therefore published to `127.0.0.1` only — never `0.0.0.0` — so an unauthenticated database
or object store is unreachable from that network even when the machine is. The same rule applies on
the staging VM: the data services publish nothing at all, and only the proxy is bound, to the gated
address.

| Host address | Service | Stack | Why it is published |
| --- | --- | --- | --- |
| `127.0.0.1:5432` | PostgreSQL | development | The SDK-run hosts, `dotnet ef`, the CLI and integration tests connect to it. |
| `127.0.0.1:9000` | MinIO S3 API | development | Object storage for the SDK-run hosts and integration tests. |
| `127.0.0.1:9001` | MinIO console | development | Inspecting objects while building the media pipeline. |
| `127.0.0.1:3310` | ClamAV (`scanner` profile) | development | The SDK-run worker and the clamd contract test. |
| `127.0.0.1:1025` | Mailpit SMTP | development, staging | Where notification e-mail is sent. |
| `127.0.0.1:8025` | Mailpit interface | development, staging | Where it is read. On staging, bound to the gated address. |
| `127.0.0.1:4317` / `:4318` / `:13133` | Collector OTLP gRPC / HTTP / health (`observability` profile) | development | SDK-run hosts export to it and `scripts/dev status` probes it. |
| `127.0.0.1:8080` | Caddy | app overlay | The only port the overlay publishes. |
| `<gate>:8080` | Caddy | staging | The only entry point, behind the network gate. |

Inside the compose network the containers use their service names and container ports: `postgres`
`5432`, `minio` `9000`, `clamav` `3310`, `mailpit` `1025`, `web` `8080`, `worker` `8081` (health
probes only), `otel-collector` `4317`.

Every published port has an override in `.env` (for example `POSTGRES_PUBLISHED_PORT`) for machines
where something already listens there.

## Health checks

Every long-running service has one, and dependants wait on it:

- **PostgreSQL** — `pg_isready` for the configured user and database.
- **MinIO** — `mc ready local`, which performs the same readiness query as `GET
  /minio/health/ready`; the image ships neither curl nor wget, so the URL cannot be called from
  inside the container.
- **ClamAV** — `clamdcheck.sh`, because the TCP port opens long before the signature database is
  loaded.
- **Mailpit** — `mailpit readyz`.
- **web / worker** — `GET /health/ready` on 8080 and 8081. Readiness rather than liveness, because
  `depends_on: service_healthy` and `up --wait` must mean "can serve requests".
- **Caddy** — fetches `/api/version` through itself, so the check covers proxy, routing and host.
- **createbuckets** has none (a one-shot has no steady state; dependants use
  `service_completed_successfully`), and **otel-collector** has none because its image is built
  `FROM scratch` and contains no shell or HTTP client — its `health_check` extension on 13133 is
  probed from the host instead.

## Conventions and constraints

- **Images are pinned by tag**, never `latest`, so an unnoticed upgrade cannot change a local
  reproduction. The release pipeline (#22) records the resolved digests, and production promotes
  digests rather than tags (Section 4.7).
- **Logs are capped** on every container (`json-file`, 50 MB × 5), so neither a laptop nor the VM
  can be filled by a log loop.
- **Application containers are hardened**: non-root (`app`, UID 1654), read-only root filesystem,
  `no-new-privileges`, all capabilities dropped, a small `tmpfs` for `/tmp`.
- **No secret in a build argument or an environment block.** Build arguments are visible in the
  image history; environment values are visible in `docker inspect`.
- Neither compose network is `internal: true`, because freshclam refreshes the ClamAV signatures and
  the collector ships telemetry outward. Isolation comes from publishing nothing beyond the loopback
  and from keeping the proxy off the staging backend network.

## Arriving later

- **#21** adds the one-shot `migrate` service and `docker/Dockerfile.cli`; `web` and `worker` then
  depend on it with `condition: service_completed_successfully`, and the per-role grants of
  Section 4.4 (`t360_migrator`, `t360_app`, `t360_reporting`, `t360_retention`, `t360_backup`)
  replace the bootstrap owner the development stack connects as today.
- **#31** adds the quarantine bucket to `createbuckets` and binds the `ObjectStorage__*` settings
  that the compose files already declare.
- **#59** replaces this with IaC-managed staging and production, adds pgBackRest and signature and
  provenance verification, and may move the runtime images to the chiselled variants once probing is
  done by the orchestrator rather than by `HEALTHCHECK`.

## Before opening a pull request that changes these files

```bash
docker compose -f infra/compose/docker-compose.yml config --quiet
docker compose -f infra/compose/docker-compose.yml -f infra/compose/docker-compose.app.yml config --quiet
docker compose -f infra/compose/docker-compose.staging.yml config --quiet   # needs the staging variables set
docker build -f infra/docker/Dockerfile.pwa --target assets -t tailor360-pwa:check .
docker build -f infra/docker/Dockerfile.web --build-arg PWA_IMAGE=tailor360-pwa:check -t tailor360-web:check .
docker build -f infra/docker/Dockerfile.worker -t tailor360-worker:check .
caddy validate --config infra/caddy/Caddyfile --adapter caddyfile
```

A Claude Code session without a Docker daemon cannot run these; say so in the pull request and let
CI (#22) provide the evidence.
