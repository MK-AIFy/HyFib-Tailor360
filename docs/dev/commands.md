# Commands

Every verb of `./scripts/dev` (bash) and `.\scripts\dev.ps1` (PowerShell), and the command each one
runs. The two scripts are equivalent: same verbs, same options, same output, so a transcript from
either can be pasted into a pull request.

Both scripts resolve the repository from their own location and then work from the repository root,
so every verb behaves the same whatever directory you are in. That matters more than it looks:
`dotnet` reads `global.json` from the working directory, and `global.json` is what selects the SDK
feature band **and** the Microsoft.Testing.Platform test runner. Run `dotnet test` from elsewhere
and it silently falls back to VSTest, which rejects the runner's own options.

## Summary

| Verb | Does |
| --- | --- |
| `up` | `restore`, then start the compose backing services |
| `restore` | `dotnet restore` and `pnpm install` |
| `build` | `dotnet build` and the PWA production build |
| `test [tier]` | Run one or all test tiers |
| `run` | Web host, worker and PWA dev server together |
| `reset` | Destroy the local data and re-create it |
| `status` | State of the five components; non-zero exit when an essential one is down |
| `doctor` | Toolchain and which test tiers can run here |
| `migrate` | Apply outstanding database migrations; `--dry-run` reports without applying |
| `docs` | Check that every relative link in the documentation resolves |

## `restore`

```bash
dotnet restore HyFib.Tailor360.slnx
pnpm --dir clients/pwa install                     # local
pnpm --dir clients/pwa install --frozen-lockfile   # when CI=true
```

`--frozen-lockfile` under `CI=true` makes a lockfile that no longer matches `package.json` fail the
run instead of being silently updated.

## `build`

```bash
dotnet build HyFib.Tailor360.slnx --configuration Debug
pnpm --dir clients/pwa build
```

`TAILOR360_CONFIGURATION=Release ./scripts/dev build` builds Release instead. The PWA `build` script
is `tsc -b && vite build`, so this also type-checks the client.

## `test [tier]`

| Tier | Command |
| --- | --- |
| `all` (default) | `dotnet test --solution HyFib.Tailor360.slnx` then `pnpm --dir clients/pwa test` |
| `unit` | `dotnet test --project tests/Tailor360.UnitTests/Tailor360.UnitTests.csproj -- --filter-trait Category=Unit` |
| `architecture` | `dotnet test --project tests/Tailor360.ArchitectureTests/Tailor360.ArchitectureTests.csproj -- --filter-trait Category=Architecture` |
| `contract` | `dotnet test --project tests/Tailor360.ContractTests/Tailor360.ContractTests.csproj -- --filter-trait Category=Contract` |
| `integration` | `dotnet test --project tests/Tailor360.IntegrationTests/Tailor360.IntegrationTests.csproj -- --filter-trait Category=Integration` |
| `pwa` | `pnpm --dir clients/pwa test` (Vitest, run once) |
| `e2e` | `pnpm --dir tests/e2e test` — the suite itself arrives with issue #52 |

A tier names its project as well as its trait on purpose. Filtering the whole solution by trait
makes the projects that hold no test of that tier report "zero tests ran", which the test platform
returns as exit code 8 — a green tier would be reported as a failure.

`test integration` warns before it starts when there is neither `TAILOR360_TEST_DATABASE_URL` nor a
Docker daemon, because the tier will then skip.

## `up`

```bash
# 1. restore (above)
# 2. first run only:
cp infra/compose/.env.example infra/compose/.env
# 3.
docker compose --project-directory infra/compose \
  --file infra/compose/docker-compose.yml up --detach --wait
```

`--wait` blocks until every service with a health check reports healthy, so a successful return
means the services are usable and not merely started.

Opt-in profiles are not started by `up` and are run directly:

```bash
docker compose -f infra/compose/docker-compose.yml --profile scanner up -d --wait
docker compose -f infra/compose/docker-compose.yml --profile observability up -d
```

Without a Docker daemon `up` prints what to do instead — point `TAILOR360_TEST_DATABASE_URL` and
`TAILOR360_TEST_S3_ENDPOINT` at services you already have — and exits non-zero.

## `run`

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:8080 \
  dotnet run --project src/Hosts/Tailor360.Web/Tailor360.Web.csproj      > artifacts/logs/web.log    2>&1 &
ASPNETCORE_ENVIRONMENT=Development Worker__HealthPort=8081 \
  dotnet run --project src/Hosts/Tailor360.Worker/Tailor360.Worker.csproj > artifacts/logs/worker.log 2>&1 &
pnpm --dir clients/pwa dev --port 5173                                    > artifacts/logs/pwa.log    2>&1 &
tail -f artifacts/logs/{web,worker,pwa}.log
```

Ctrl-C stops all three. The logs stay in `artifacts/logs/`, which is git-ignored. `run` warns first
when PostgreSQL is not answering, because both hosts will then start and report unready.

To run only one component, use the underlying command; the ports above are the ones the rest of the
configuration expects (`docs/dev/ports.md`).

The web host binds the loopback address only, because a laptop is regularly on an untrusted
network. To open the stack to a phone on the same LAN for device testing, export
`ASPNETCORE_URLS=http://0.0.0.0:8080` before `run` (the script keeps a value you set) and start the
client with `pnpm --dir clients/pwa dev --host`.

## `reset`

```bash
docker compose --project-directory infra/compose --file infra/compose/docker-compose.yml \
  down --volumes --remove-orphans
docker compose --project-directory infra/compose --file infra/compose/docker-compose.yml \
  up --detach --wait
dotnet run --project src/Tools/Tailor360.Cli/Tailor360.Cli.csproj -- migrate
dotnet run --project src/Tools/Tailor360.Cli/Tailor360.Cli.csproj -- init-reference-data
dotnet run --project src/Tools/Tailor360.Cli/Tailor360.Cli.csproj -- seed-synthetic
```

`reset` is destructive, so it asks for confirmation; `--yes` (or `TAILOR360_ASSUME_YES=1`) skips the
prompt and a non-interactive session without `--yes` is refused. It also refuses outright when
`ASPNETCORE_ENVIRONMENT=Production`, before anything is destroyed — `seed-synthetic` would refuse in
production anyway, and the volumes must not be lost on the way to that refusal.

Order matters: schema, then the reference data every installation needs, then the synthetic dataset
that only development and tests may have.

## `status`

No single underlying command: it probes five components and prints one table.

| Row | Probe | Essential |
| --- | --- | --- |
| Web host | `GET http://127.0.0.1:8080/health/ready`, top-level `Status` from the payload | yes |
| Worker host | `GET http://127.0.0.1:8081/health/ready` | yes |
| PostgreSQL | `pg_isready`, or a TCP connect when no client tools are installed | yes |
| Object storage | `GET <endpoint>/minio/health/ready` | no |
| PWA dev server | `GET http://127.0.0.1:5173/` | no |

States are `OK`, `FAIL` and `SKIPPED` (the probe could not be attempted, for example `curl` is not
installed). The exit code is non-zero when an essential component is down. Object storage is not
essential because losing it degrades media handling rather than the instance, and the dev server is
a developer convenience that is deliberately absent when the built shell is served by the web host.

The database row follows `TAILOR360_TEST_DATABASE_URL` when it is set, because that is the instance
the tests actually use; otherwise it follows `infra/compose/.env` and then the compose defaults. The
variable's value is never printed — it normally carries a password.

## `doctor`

Detects `dotnet`, `node`, `pnpm`, `psql`, `curl`, a reachable Docker daemon,
`TAILOR360_TEST_DATABASE_URL`, `TAILOR360_TEST_S3_ENDPOINT` and the Playwright browser cache
(`PLAYWRIGHT_BROWSERS_PATH`, otherwise the per-platform default), then prints which test tiers can
run:

- `Unit`, `Architecture` and `Contract` always run.
- `Integration` runs against `TAILOR360_TEST_DATABASE_URL` when set, otherwise on Testcontainers
  when a Docker daemon is reachable, otherwise it is reported as skipped — and `CI=true` turns that
  skip into a failure.
- `E2E` runs when the Playwright browsers are present.

`doctor` exits non-zero only when a required tool (`dotnet`, `node`, `pnpm`) is missing. `psql`, `curl`,
Docker and Python are reported as optional: Python is needed only by `docs`, and that check also runs in CI.

## `migrate [--dry-run]`

Applies outstanding database migrations through the operator command line
(`dotnet run --project src/Tools/Tailor360.Cli -- migrate`). `--dry-run` reports what would be
applied and changes nothing.

`docs/dev/migrations.md` documented this verb before it existed, so a developer following that
document met `unknown verb: migrate` instead of a migration. It is a thin wrapper rather than a
second implementation: the command line holds one PostgreSQL advisory lock for the whole run, so
two of these racing is safe, and `reset` already used the same command internally.

It needs a connection string, which comes from configuration rather than from the test variable:
`Database:ConnectionString` in `src/Tools/Tailor360.Cli/appsettings.Development.json`, or the
`Database__ConnectionString` environment variable. Running it without one fails with a named
options-validation error rather than a stack trace about a missing service.

```
$ ./scripts/dev migrate --dry-run
platform: 3 migration(s) would be applied

$ ./scripts/dev migrate
platform: applied 3 migration(s)

$ ./scripts/dev migrate
platform: already up to date
```

## `docs`

Runs `scripts/check-docs-links.py` twice. First with `--self-test`, which builds a known-bad
document in a temporary directory and fails unless the detector finds exactly the two genuine
breaks in it and ignores the external, anchor, mailto and fenced-code links it is meant to ignore.
Then for real, walking every markdown file under `docs/` and `.github/` plus the repository's
top-level `README.md`, and failing when a **relative** link points at a file that is not there.

The self-test exists because a checker that has quietly stopped detecting looks exactly like a
repository with no broken links: both are silent and green. The `Documentation links` job in CI
runs the same two commands, so a failure here is the failure a reviewer would otherwise have seen
only after pushing.

What it checks and what it deliberately does not:

| Kind of link | Checked | Why |
| --- | --- | --- |
| Relative path, for example `../nfr/slo.md` | Yes | The repository owns whether the file exists |
| Relative path with an anchor, `slo.md#section` | The file only | A heading may arrive later in the same series of changes |
| Image embed, `![x](diagram.png)` | Yes | A missing image is as broken as a missing document |
| Reference definition, `[ref]: c.md` | Yes | Same failure, different syntax |
| `http://`, `https://`, `mailto:` | No | Somebody else's uptime; a build must not fail for a reason no commit here can fix |
| Anything inside a fenced code block | No | An illustrative link in an example is not meant to resolve |

When the target is a document a future issue will deliver, refer to it as inline code rather than
as a link — `docs/prd/00-overview.md` does this for the documents issue #24 has yet to deliver — so
that a reader is told the document is planned instead of following a link to nothing.

## Things the scripts deliberately do not wrap

| Task | Command |
| --- | --- |
| Format .NET code | `dotnet format` / `dotnet format --verify-no-changes` |
| Lint the client | `pnpm --dir clients/pwa lint` |
| Format the client | `pnpm --dir clients/pwa format` / `format:check` |
| Regenerate the client's API types | `pnpm --dir clients/pwa generate:api` (`generate:api:check` fails on drift) |
| Type-check the client only | `pnpm --dir clients/pwa typecheck` |
| Watch the client tests | `pnpm --dir clients/pwa test:watch` |
| Preview the built client | `pnpm --dir clients/pwa preview` |
| Follow one service's logs | `docker compose -f infra/compose/docker-compose.yml logs -f postgres` |
| Open a database shell | `psql "postgresql://tailor360@127.0.0.1:5432/tailor360"` |
| Build the container images | see `infra/compose/docker-compose.app.yml` |

They are one-liners already, and wrapping them would hide the tool that reports the error.

## Environment variables the scripts read

| Variable | Default | Effect |
| --- | --- | --- |
| `TAILOR360_WEB_PORT` | `8080` | Web host port for `run` and `status` |
| `TAILOR360_WORKER_HEALTH_PORT` | `8081` | Worker probe port |
| `TAILOR360_PWA_PORT` | `5173` | Vite dev server port |
| `TAILOR360_MINIO_PORT` | `9000` | Object-storage port when `infra/compose/.env` says nothing |
| `TAILOR360_PROBE_TIMEOUT` | `5` | Seconds a `status` probe waits |
| `TAILOR360_CONFIGURATION` | `Debug` | Build configuration for `build`, `run` and `reset` |
| `TAILOR360_ASSUME_YES` | unset | `1` skips the `reset` confirmation |
| `TAILOR360_TEST_DATABASE_URL` | unset | External PostgreSQL for the tests, `status` and `run` |
| `TAILOR360_TEST_S3_ENDPOINT` | unset | External S3-compatible endpoint |
| `ASPNETCORE_ENVIRONMENT` | `Development` | Passed to the hosts; `Production` makes `reset` refuse |
| `CI` | unset | `true` selects `--frozen-lockfile` and turns integration skips into failures |
| `NO_COLOR` | unset | Any value disables colour (colour is off automatically when stdout is not a terminal) |
