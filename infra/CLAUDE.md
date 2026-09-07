# `infra` — infrastructure rules

Rules for working on the container definitions, the reverse proxy and the deployment stacks. Read
[`../CLAUDE.md`](../CLAUDE.md) first; this file carries only what is specific to this tree.
[`README.md`](README.md) describes what each file is and how to run the stacks — it is the operating manual, this
is the rule set. The authority behind both is Section 4.7 of
[`../docs/IMPLEMENTATION_PLAN.md`](../docs/IMPLEMENTATION_PLAN.md).

**Nothing in this directory holds a secret.** Every credential here is either an obviously fake development
placeholder in `compose/.env.example` or a file mounted at run time under `/run/secrets`. If you are about to
commit a value that would matter if it leaked, stop.

---

## 1. What is here

| Path | Purpose |
| --- | --- |
| `compose/docker-compose.yml` | Development **backing services** only: PostgreSQL, MinIO and its bucket creator, ClamAV, Mailpit, an OpenTelemetry collector |
| `compose/docker-compose.app.yml` | Overlay that additionally runs the built `web` and `worker` images behind Caddy |
| `compose/docker-compose.staging.yml` | The interim staging stack: no published database, object-storage or scanner ports |
| `compose/.env.example` | Every variable the three files read, with development-only values. Copy to `compose/.env` |
| `docker/Dockerfile.{web,worker,pwa}` | Multi-stage builds; the build context is the **repository root** |
| `caddy/Caddyfile` | Reverse proxy for the overlay and for staging |
| `observability/otel-collector-config.yaml` | Collector pipeline: OTLP in, `debug` out. Exports nothing off the machine by default |
| `dev-environment/` | The coding-environment image definition and its session-start hook |

During development the web host, the worker and the client are **not** containers: they run from the .NET SDK and
from Vite, because that is what gives hot reload, a debugger and readable stack traces. Compose provides only the
services they talk to.

## 2. Compose rules

1. **Publish nothing beyond the loopback in development.** Every published port binds `127.0.0.1`, never
   `0.0.0.0`. A development laptop is regularly on a shop, home or café network, and an unauthenticated database
   must be unreachable from it even when the machine is not.
2. **In staging, the data services publish no ports at all.** PostgreSQL, MinIO and ClamAV have no `ports:` key,
   and the worker's health port stays internal. Exactly two ports are published — the proxy and the Mailpit user
   interface — and both bind the gated address (`TAILOR360_BIND_ADDRESS`, default `127.0.0.1`). Those two are the
   whole external surface; adding a `ports:` entry to anything else is a security change, not a convenience.
3. **Pin images by digest in the staging file**, `name:tag@sha256:...`, keeping the tag beside the digest so a
   reader can still see which version it is. A tag is a mutable pointer: whoever can move it changes what staging
   runs without a commit. `.github/dependabot.yml` raises these digests weekly (`docker` ecosystem), so pinning
   does not mean running old images. The development files may pin by tag alone, because a laptop is not an
   environment anything is promoted from — but never `latest` anywhere: an unnoticed upgrade turns a local
   reproduction into a different system. Production promotes digests, not tags.
4. **No secret in an `environment:` block or a build argument.** Environment values are visible in
   `docker inspect`; build arguments are visible in the image history. Secrets are files, the file name is the
   configuration key and `__` is the section separator — `Database__ConnectionString` becomes
   `Database:ConnectionString`.
5. **Cap the logs on every container** (`json-file`, 50 MB × 5), so neither a laptop nor the VM can be filled by a
   log loop.
6. **Harden every application container**: non-root, `read_only: true`, `no-new-privileges:true`, `cap_drop: ALL`,
   and a small `tmpfs` for `/tmp`. The hosts write nothing to disk: logs go to stdout, and the Data Protection key
   ring is to live in the database — nothing persists it yet (#23), so with a read-only root filesystem each
   container holds an **ephemeral** ring today and a restart signs its users out. See
   [`../docs/dev/staging.md`](../docs/dev/staging.md) section 12 item 4.
7. **Every long-running service has a health check, and dependants wait on it.** Readiness, not liveness, because
   `depends_on: service_healthy` and `up --wait` must mean "can serve requests". A one-shot service has no health
   check; dependants use `condition: service_completed_successfully`.
8. **Migrations run before the new version serves.** Web and worker wait on the one-shot `migrate` service with
   `condition: service_completed_successfully`. The service is wired in `compose/docker-compose.staging.yml`; what
   is still owed is the `tailor360-cli` image it runs (`TAILOR360_CLI_IMAGE`), so until `docker/Dockerfile.cli` and
   the job that builds it exist the step cannot actually run — see "Arriving later" in [`README.md`](README.md) and
   [`../docs/dev/staging.md`](../docs/dev/staging.md) section 12 item 2. The development overlay still runs the
   migration by hand. The application never migrates itself — during a deployment two versions run at once and
   would race.
9. **Every published port has an override in `.env`**, for machines where something already listens there.

## 3. Secrets

Create the development secret files once; `compose/secrets/` is git-ignored:

```bash
mkdir -p infra/compose/secrets
printf 'Host=postgres;Port=5432;Database=tailor360;Username=tailor360;Password=tailor360_dev_only' \
  > infra/compose/secrets/Database__ConnectionString
printf 'tailor360-dev'              > infra/compose/secrets/ObjectStorage__AccessKey
printf 'tailor360-dev-not-a-secret' > infra/compose/secrets/ObjectStorage__SecretKey
chmod 0444 infra/compose/secrets/*
```

`printf`, not `echo`: a trailing newline becomes part of the configuration value. The mode matters too — Compose
bind-mounts these files with their host ownership and the containers run as the unprivileged `app` user (UID 1654)
of the .NET runtime image, so the file must be readable by that user. On the staging VM the deploy script places
them `0440`, owned so that only the container user can read them.
Policy and rotation: [`../docs/platform/secrets.md`](../docs/platform/secrets.md).

## 4. The reverse proxy

One `Caddyfile` serves the local overlay and the staging VM, so a proxy problem is found on a laptop rather than on
the server; everything that differs between them is an environment variable.

- **Security headers are set by the application, not by Caddy.** One implementation then covers every deployment,
  including SDK runs and integration tests, and the policy is version-controlled next to the code it protects. Do
  not add a CSP, HSTS or `Referrer-Policy` header here.
- **`/health` and `/health/*` are answered with 404 at the proxy** — the matcher names the bare path as well as
  the subtree, because `/health/*` alone would let `/health` through to the application. The probes are internal;
  an orchestrator, the compose watchdog and `./scripts/dev status` call them on the container port.
- **`X-Robots-Tag` stays on.** Everything is behind a login, and staging holds synthetic data that must never
  appear in a search result.
- **`trusted_proxies static private_ranges`**, and the application applies the same rule again, because
  address-partitioned rate limits and the audit trail must never trust a header a client could choose.
- The ACME DNS-01 block is a deliberate placeholder. Enabling it needs both the hostname and DNS-provider decision
  and a Caddy image built with that provider's module — the official image contains none, so `acme_dns` would fail
  at start-up. The token is then mounted as a file, never baked into the image.

## 5. Interim staging

The environment is gated **in front of** the stack, never by it: an IP allowlist for the shop networks plus an
identity-aware proxy or WireGuard/Tailscale enrolment for phones and tablets. The application login is the second
factor, never the only one, and until the authentication issue merges there is no public hostname at all.

Guard-rails that must stay true of `compose/docker-compose.staging.yml`:

- Synthetic data only, seeded by `seed-synthetic`. No production data, ever, and no restore of it except the
  rehearsal that the restore-drill issue defines.
- No production secrets and no provider credentials: mail goes to Mailpit, and the payment, SMS and messaging
  adapters stay on their fakes.
- Its own Data Protection discriminator (`DataProtection__ApplicationDiscriminator: tailor360-staging`). Read
  this one carefully, because it is declared and **not yet bound**: no `AddDataProtection()` call reads it and no
  migration creates `platform.data_protection_keys`, so the variable protects nothing today. What actually keeps a
  staging session cookie or protected payload away from production is that staging has its own database, plus the
  ephemeral per-container ring the read-only root filesystem forces. The discriminator becomes the second layer
  when #23 persists the ring — [`../docs/dev/staging.md`](../docs/dev/staging.md) section 12 item 4.
- `noindex` at the proxy (`X-Robots-Tag` in `caddy/Caddyfile`). Everything reaches the environment through the
  proxy, so nothing is indexable — but the application does not set the header itself yet, so the guard-rail is
  one hop deep rather than two ([`../docs/dev/staging.md`](../docs/dev/staging.md) section 12 item 5).
- `TAILOR360_WEB_IMAGE`, `TAILOR360_WORKER_IMAGE`, `TAILOR360_CLI_IMAGE` and `TAILOR360_STAGING_SECRETS_DIR` are
  required: the stack refuses to start rather than silently falling back to a local default.

## 6. Deployment

- **Build once, promote the identical digest** after verifying its signature and provenance. Nothing is built on
  the VM.
- **Deployment is pull-based**: `tailor360-deploy` runs on the VM, verifies the signature, runs `migrate`, then
  `docker compose up -d --wait --pull always`. There is no held SSH key from the build system into a server. The
  script itself does not exist yet — `scripts/` holds no `tailor360-deploy` — so this is the procedure an operator
  follows by hand until it does ([`../docs/dev/staging.md`](../docs/dev/staging.md) section 12 item 3).
- **Compose has no rolling update**, so a container swap costs a few seconds of proxy errors. That is accepted
  against the availability target and minimised with `--wait` and proxy retry; blue-green arrives with the
  orchestrated environments.
- **Rollback is redeploy of the previous tag**, which works because migrations are expand-and-contract and the
  start-up check tolerates a database that is one release ahead.
- If a deploy's `migrate` step fails, the environment is reset — `down -v`, `migrate`, `init-reference-data`,
  `seed-synthetic` — the run is labelled `staging-reset` and an issue is opened. A half-migrated staging database
  is worse than an empty one.

## 7. Before opening a pull request that changes these files

```bash
docker compose -f infra/compose/docker-compose.yml config --quiet
docker compose -f infra/compose/docker-compose.yml -f infra/compose/docker-compose.app.yml config --quiet
docker compose -f infra/compose/docker-compose.staging.yml config --quiet   # needs the staging variables set
docker build -f infra/docker/Dockerfile.pwa --target assets -t tailor360-pwa:check .
docker build -f infra/docker/Dockerfile.web --build-arg PWA_IMAGE=tailor360-pwa:check -t tailor360-web:check .
docker build -f infra/docker/Dockerfile.worker -t tailor360-worker:check .
caddy validate --config infra/caddy/Caddyfile --adapter caddyfile
```

Both `docker build` commands run from the repository root: central package management means a restore needs
`Directory.Packages.props`, `Directory.Build.props`, `global.json` and `.editorconfig`, so the context is the root
and `-f` points into this directory.

An environment without a Docker daemon cannot run these. Say so in the pull request and let the continuous
integration run provide the evidence — do not claim a check you did not perform.
