# Developer setup

First run on a new machine, for Windows 11 with WSL2, macOS and Linux. It takes about twenty
minutes on a fast connection, most of it downloading the .NET SDK and the npm packages.

When you are finished, `./scripts/dev doctor` reports the toolchain and `./scripts/dev status`
reports the five running components. Those two outputs are the evidence a pull request quotes.

## 1. What you are installing

| Tool | Version | Why this version |
| --- | --- | --- |
| .NET SDK | 10.0.100 or any later 10.0.1xx | `global.json` pins the feature band and rolls forward within it (`rollForward: latestFeature`); an 11.x SDK will refuse to build. |
| Node.js | 22 LTS | `clients/pwa/package.json` declares `engines.node >= 22`. |
| pnpm | 10.33.0 | `packageManager` in `clients/pwa/package.json`; Corepack installs exactly this version. |
| Git | 2.40 or newer | `git config core.autocrlf` behaviour on Windows (see step 2). |
| Docker | Engine 27 or Docker Desktop 4.3x | Optional but strongly recommended: it runs PostgreSQL, MinIO, Mailpit and the Testcontainers-based integration tests. |
| PostgreSQL client tools | 16 | Optional: `pg_isready` gives `./scripts/dev status` a real readiness answer instead of a plain port check, and `psql` is how you inspect the database. |
| Python | 3.9 or newer | Optional: `./scripts/dev docs` runs the documentation link check with it. The same check runs in CI, so a missing interpreter delays the failure rather than hiding it. Windows installs the interpreter as `python`, which the script accepts alongside `python3`. |
| PowerShell | 7.4 or newer | Only on Windows without WSL. `scripts/dev.ps1` also runs on Windows PowerShell 5.1. |

Everything else — the compilers, the analysers, the test runner, Vite — comes from `dotnet
restore` and `pnpm install`.

## 2. Windows 11 with WSL2 (recommended on Windows)

WSL2 is recommended because the repository is built and tested on Linux in continuous integration,
and because file watching, Docker and the shell scripts all behave there as they do on the build
server.

```bash
# In PowerShell, as administrator, once per machine:
wsl --install -d Ubuntu-24.04
```

Then, inside the Ubuntu shell:

```bash
# .NET 10 SDK. Ubuntu 24.04 carries it in noble-updates/main; the Microsoft feed
# (packages.microsoft.com) is the fallback on distributions that do not.
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0

# Node.js 22 LTS
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
sudo apt-get install -y nodejs

# pnpm, at the exact version the repository pins
corepack enable
corepack prepare pnpm@10.33.0 --activate

# PostgreSQL client tools (optional but useful)
sudo apt-get install -y postgresql-client-16
```

Install **Docker Desktop** on Windows and enable "Use the WSL 2 based engine" plus integration with
the Ubuntu distribution, so that `docker` works inside WSL.

Two Windows-specific rules:

- **Clone inside the Linux filesystem** (`~/src/HyFib-Tailor360`), never under `/mnt/c`. Builds and
  file watchers on `/mnt/c` are an order of magnitude slower and `dotnet watch` misses changes.
- **Do not let Git rewrite line endings.** `git config --global core.autocrlf false` before
  cloning: with the default `true`, Git checks the shell scripts out with CRLF endings and bash
  then fails on them with `$'\r': command not found`.

## 3. Windows 11 without WSL

Supported: use `scripts\dev.ps1`, which carries the same verbs and prints the same output as
`scripts/dev`. The `run` verb starts the three processes through `cmd.exe`, so it is Windows-only;
everything else works anywhere PowerShell does.

```powershell
winget install Microsoft.DotNet.SDK.10
winget install OpenJS.NodeJS.LTS          # Node 22 LTS
winget install Microsoft.PowerShell       # PowerShell 7, optional but recommended
corepack enable
corepack prepare pnpm@10.33.0 --activate
```

Install Docker Desktop if you want the backing services and the integration tests.

PowerShell refuses to run unsigned scripts by default. Allow it for the current session only:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\dev.ps1 doctor
```

## 4. macOS (14 Sonoma or newer, Intel or Apple silicon)

```bash
# .NET 10 SDK — the official installer script is the reliable route, because the Homebrew cask
# tracks the newest channel and may install an SDK that global.json rejects.
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.zshrc

# Node.js 22 and pnpm
brew install node@22
brew link --overwrite node@22
corepack enable
corepack prepare pnpm@10.33.0 --activate

# PostgreSQL client tools (optional)
brew install libpq && brew link --force libpq
```

Install Docker Desktop, OrbStack or Colima for the backing services. With Colima, start it with
enough memory for PostgreSQL and MinIO: `colima start --cpu 4 --memory 8`.

## 5. Linux (Ubuntu 24.04 and derivatives)

Same as the WSL2 instructions in step 2, plus Docker Engine:

```bash
sudo apt-get install -y docker.io docker-compose-v2
sudo usermod -aG docker "$USER"   # log out and back in for this to take effect
```

On Fedora and RHEL derivatives the SDK package is `dotnet-sdk-10.0` from the distribution
repository or from `packages.microsoft.com`.

## 6. Check the toolchain

```bash
./scripts/dev doctor
```

It prints what is installed and which test tiers can run here. A machine with Docker shows
`Integration RUNS`; a machine without shows `Integration SKIPPED` and the reason. Example output
from a cloud session with no Docker daemon:

```text
TOOL                         STATE        DETAIL
---------------------------- ------------ ----------------------------------
.NET SDK                     FOUND        10.0.111
Node.js                      FOUND        v22.22.2
pnpm                         FOUND        10.33.0
psql (PostgreSQL)            FOUND        16.13
curl                         FOUND        8.5.0
Docker daemon                UNAVAILABLE  CLI present, no daemon — Testcontainers cannot run
TAILOR360_TEST_DATABASE_URL  UNSET        Integration tier falls back to Testcontainers
TAILOR360_TEST_S3_ENDPOINT   UNSET        media tests use Testcontainers or skip
Playwright browsers          FOUND        /opt/pw-browsers

TEST TIER                    STATE        DETAIL
---------------------------- ------------ ----------------------------------
Unit                         RUNS         no external dependency
Architecture                 RUNS         no external dependency
Contract                     RUNS         no external dependency
Integration                  SKIPPED      no database and no Docker daemon
E2E (Playwright)             RUNS         browsers installed; tests/e2e arrives with issue #52
```

## 7. Start the backing services

```bash
./scripts/dev up
```

`up` restores first (`dotnet restore`, `pnpm install`), then starts PostgreSQL, MinIO, the bucket
initialiser and Mailpit from `infra/compose/docker-compose.yml` and waits until each reports
healthy. On the first run it copies `infra/compose/.env.example` to `infra/compose/.env`; every
value in it is a development placeholder and none of them is a secret.

ClamAV and the OpenTelemetry collector are opt-in profiles, because clamd needs roughly 1.5 GB of
memory and two minutes to become ready:

```bash
docker compose -f infra/compose/docker-compose.yml --profile scanner up -d --wait
docker compose -f infra/compose/docker-compose.yml --profile observability up -d
```

**No Docker on this machine?** `up` says so and points at the two environment variables that let
the tests use services you already have:

```bash
export TAILOR360_TEST_DATABASE_URL="Host=127.0.0.1;Port=5432;Database=tailor360;Username=postgres"
export TAILOR360_TEST_S3_ENDPOINT="http://127.0.0.1:9000"
```

Without the first one the `Integration` tier skips with a visible warning, and `CI=true` turns that
skip into a failure so that nothing merges unverified.

## 8. Run the application

```bash
./scripts/dev run
```

This starts the web host, the worker and the Vite dev server together and follows all three logs.

| What | Where |
| --- | --- |
| Progressive web application | <http://127.0.0.1:5173> — **open this one** |
| Web host (API and probes) | <http://127.0.0.1:8080> |
| Readiness probe | <http://127.0.0.1:8080/health/ready> |
| Build and environment | <http://127.0.0.1:8080/api/version> |
| OpenAPI document | <http://127.0.0.1:8080/openapi/v1.json> (Development only) |
| Worker probes | <http://127.0.0.1:8081/health/ready> |
| Mailpit (sent mail) | <http://127.0.0.1:8025> |
| MinIO console | <http://127.0.0.1:9001> |

Open the PWA origin rather than the host origin: the dev server proxies `/api` and `/health` to the
web host, so the browser sees one origin and session cookies, the anti-forgery header and the
content security policy behave as they do behind the reverse proxy in production.

In a second terminal:

```bash
./scripts/dev status
```

It prints the five components and exits non-zero when an essential one is down.

Because the environment is not `production`, the shell shows the persistent
**"TRAINING — not real data"** banner. That is deliberate: it is driven by
`GET /api/version.environment`.

## 9. Run the tests

| Tier | Command | Needs |
| --- | --- | --- |
| Everything | `./scripts/dev test` | Database for the integration tier, otherwise it skips |
| Unit | `./scripts/dev test unit` | Nothing |
| Architecture | `./scripts/dev test architecture` | Nothing |
| Contract | `./scripts/dev test contract` | Nothing |
| Integration | `./scripts/dev test integration` | `TAILOR360_TEST_DATABASE_URL` or a Docker daemon |
| Progressive web app | `./scripts/dev test pwa` | Nothing |
| End to end | `./scripts/dev test e2e` | Playwright browsers; the suite itself arrives with issue #52 |

The tiers are xUnit traits (`[Trait("Category", "Unit")]`), one per test project, so a tier can be
run on its own on any machine. `docs/dev/commands.md` lists the underlying command for each verb.

Before opening a pull request, run at least:

```bash
dotnet format --verify-no-changes            # formatting, as CI checks it
./scripts/dev test unit
./scripts/dev test architecture
./scripts/dev test contract
pnpm --dir clients/pwa lint
./scripts/dev test pwa
```

Integration tests are the part a machine without Docker cannot produce; the continuous integration
run carries that evidence, so read it before requesting review.

## 10. Reset the local data

```bash
./scripts/dev reset          # asks for confirmation; --yes skips it
```

It destroys the compose volumes (`down --volumes`), starts the services again and then runs
`migrate`, `init-reference-data` and `seed-synthetic` through `Tailor360.Cli`. `seed-synthetic`
refuses to run when `ASPNETCORE_ENVIRONMENT=Production`, unconditionally and with no override, and
`reset` refuses before it for the same reason.

## Next

- `docs/dev/commands.md` — every verb and the command it runs.
- `docs/dev/ports.md` — every port, what binds it and whether it is published.
- `docs/dev/troubleshooting.md` — the failures a new contributor actually hits.
- `infra/dev-environment/README.md` — running in a Claude Code session or on a CI runner.
