# Troubleshooting

The failures a new contributor actually hits, in the order they usually hit them. Each entry gives
the symptom as it appears, why it happens and what to do.

Start with `./scripts/dev doctor` (toolchain and test tiers) and `./scripts/dev status` (the five
components). Between them they explain most of what follows.

## No Docker daemon

**Symptom**

```text
==> Backing services
No Docker daemon is reachable, so the compose backing services cannot be started here.
```

or, from `docker` directly:

```text
Cannot connect to the Docker daemon at unix:///var/run/docker.sock. Is the docker daemon running?
```

**Why** The Docker CLI is installed but nothing is serving it: Docker Desktop is not started, the
WSL integration is off, your user is not in the `docker` group, or the environment simply has no
daemon (a Claude Code cloud session is exactly that).

**Fix, in order**

1. Start Docker Desktop, or `sudo systemctl start docker` on Linux.
2. On Windows: Docker Desktop → Settings → Resources → WSL integration → enable your distribution.
3. On Linux: `sudo usermod -aG docker "$USER"`, then log out and back in — the group is applied at
   login, so `newgrp docker` or a new session is required.
4. If there is genuinely no daemon, do not fight it. Point the tests at services you have:

   ```bash
   export TAILOR360_TEST_DATABASE_URL="Host=127.0.0.1;Port=5432;Database=tailor360;Username=postgres"
   export TAILOR360_TEST_S3_ENDPOINT="http://127.0.0.1:9000"
   ./scripts/dev doctor
   ```

   The unit, architecture and contract tiers need nothing at all and run everywhere.

## A port is already in use

**Symptom**

```text
Failed to bind to address http://127.0.0.1:8080: address already in use.
Error response from daemon: driver failed programming external connectivity ... bind: address already in use
Port 5173 is already in use
```

**Why** Another process owns the port: a previous `./scripts/dev run` that did not exit cleanly, a
system PostgreSQL on 5432, IIS or another development stack on 8080.

**Find the owner**

```bash
# macOS and Linux
lsof -nP -iTCP:8080 -sTCP:LISTEN
ss -ltnp 'sport = :8080'          # Linux alternative
```

```powershell
# Windows
Get-NetTCPConnection -LocalPort 8080 -State Listen |
  Select-Object LocalAddress, LocalPort, OwningProcess
Get-Process -Id (Get-NetTCPConnection -LocalPort 8080 -State Listen).OwningProcess
```

**Fix** Stop the other process, or move ours — every port is a variable
(`docs/dev/ports.md` → "Changing a port"):

```bash
TAILOR360_WEB_PORT=8090 ./scripts/dev run
```

For the compose services, edit `infra/compose/.env` (`POSTGRES_PUBLISHED_PORT=5433` is the common
one on a machine with a system PostgreSQL) and run `./scripts/dev up` again.

If a previous `run` left processes behind, they are the `dotnet` and `node` children of that
terminal:

```bash
pkill -f 'Tailor360.Web' ; pkill -f 'Tailor360.Worker' ; pkill -f 'vite'
```

```powershell
Get-Process dotnet, node | Stop-Process   # Windows: check the list before running this
```

## PostgreSQL authentication and connection failures

**Symptoms**

```text
Npgsql.PostgresException (0x80004005): 28P01: password authentication failed for user "tailor360"
Npgsql.PostgresException: 3D000: database "tailor360" does not exist
Npgsql.NpgsqlException: Failed to connect to 127.0.0.1:5432
```

**Why and fix**

- **Wrong instance.** A system PostgreSQL is answering on 5432 instead of the container. Check
  which one you reached: `psql "postgresql://tailor360@127.0.0.1:5432/tailor360" -c 'select
  version()'`. Move the container to another port in `infra/compose/.env`, or stop the system
  service.
- **Wrong password.** The development password lives in `infra/compose/.env`
  (`POSTGRES_PASSWORD`); the hosts read the connection string from the secret file
  `infra/compose/secrets/Database__ConnectionString` (git-ignored, created as described in
  `infra/README.md`). If you changed one, change the other. The container only reads
  `POSTGRES_PASSWORD` when the data volume is created, so after changing it you must recreate the
  volume: `./scripts/dev reset`.
- **Database not created.** The volume was created before `POSTGRES_DB` was set to `tailor360`.
  Same fix: `./scripts/dev reset`.
- **The wrong connection-string form.** `TAILOR360_TEST_DATABASE_URL` is handed straight to Npgsql,
  which parses the keyword form. Use
  `Host=127.0.0.1;Port=5432;Database=tailor360;Username=postgres;Password=...`, not a
  `postgres://` URL.
- **`status` says "port open (no psql client, readiness unverified)".** Not a failure: the
  PostgreSQL client tools are not installed, so the probe fell back to a TCP connect. Install
  `postgresql-client-16` for a real readiness answer.

## Stale `node_modules` or an out-of-date lockfile

**Symptoms**

```text
ERR_PNPM_OUTDATED_LOCKFILE  Cannot install with "frozen-lockfile" because pnpm-lock.yaml is not up to date
Error: Cannot find module '@vitejs/plugin-react'
Failed to resolve import "react-intl"
```

**Why** A branch changed `package.json` (or you switched branches) and the installed tree no longer
matches, or a partial install left a broken store link.

**Fix**

```bash
./scripts/dev restore                     # the usual fix: pnpm install
```

If that does not settle it:

```bash
rm -rf clients/pwa/node_modules
pnpm --dir clients/pwa install
pnpm store prune                          # only when the store itself looks damaged
```

Never hand-edit `pnpm-lock.yaml`: change `package.json` and let `pnpm install` rewrite it, then
commit both. `CI=true` installs with `--frozen-lockfile`, so a lockfile that does not match fails
the build rather than being silently updated.

## A migration fails

**Symptoms**

```text
The database is not in a state the running build can serve: N migration(s) are unapplied.
relation "..." already exists
Npgsql.PostgresException: 42501: permission denied for schema ...
```

**Why and fix**

- **Unapplied migrations.** The host refuses to serve when a migration in the running assembly has
  not been applied. Run `./scripts/dev reset`, or apply them in place:
  `dotnet run --project src/Tools/Tailor360.Cli/Tailor360.Cli.csproj -- migrate`.
  The reverse case is fine by design: a database at N+1 served by release N is tolerated, because
  releases are expand-only (`docs/dev/migrations.md`, issue #21).
- **A migration half-applied on a development database.** Do not repair it by hand.
  `./scripts/dev reset` destroys the volumes and rebuilds from scratch; that is what the verb is
  for. On a shared environment, follow the rollback procedure in `docs/dev/migrations.md`
  (issue #21): forward-only fix migration first; restore from backup only when data was mutated.
- **Permission denied.** Migrations run as the migrator role, never as the application role, and
  the CLI refuses to run with the application role. Check which role your connection string names.
- **"already exists" after switching branches.** Your database carries another branch's migrations.
  `./scripts/dev reset`.

## Integration tests are skipping

**Symptom**

```text
No PostgreSQL instance is reachable. Set TAILOR360_TEST_DATABASE_URL or start the compose stack.
Set CI=true to turn this skip into a failure.
```

**Why** By design. The integration tier uses `TAILOR360_TEST_DATABASE_URL` when it is set, else
Testcontainers when a Docker daemon is reachable, else it skips with a visible warning — and
`CI=true` turns that skip into a failure so nothing merges unverified.

**Fix**

```bash
./scripts/dev up                       # start the compose services, then
./scripts/dev test integration
```

or point it at an instance you already have:

```bash
export TAILOR360_TEST_DATABASE_URL="Host=127.0.0.1;Port=5432;Database=tailor360;Username=postgres;Password=..."
export TAILOR360_TEST_S3_ENDPOINT="http://127.0.0.1:9000"
./scripts/dev test integration
```

Confirm what the environment can do with `./scripts/dev doctor`. If you cannot run the tier
locally, say so in the pull request and use the continuous integration run as the evidence — that
is what it is there for.

## Playwright browsers are missing

**Symptom**

```text
browserType.launch: Executable doesn't exist at /home/you/.cache/ms-playwright/chromium-.../chrome
Looks like Playwright Test or Playwright was just installed or updated.
Please run the following command to download new browsers: npx playwright install
```

**Why** The browser binaries are not part of the npm packages; they are downloaded separately and
cached outside the repository.

**Fix**

```bash
pnpm --dir clients/pwa exec playwright install chromium
```

- Firefox and WebKit are only needed for the nightly cross-browser run: `playwright install`
  without an argument downloads all three and takes considerably longer.
- On Linux the browsers also need system libraries: `pnpm exec playwright install-deps chromium`
  (or `sudo apt-get install -y libnss3 libatk1.0-0 libatk-bridge2.0-0 libcups2 libdrm2 libxkbcommon0
  libxcomposite1 libxdamage1 libxfixes3 libxrandr2 libgbm1 libpango-1.0-0 libcairo2 libasound2t64`).
- In a locked-down environment the Playwright CDN may be blocked; the download then hangs or fails
  with a 403. Either add the CDN to the allowlist or bake the browsers into the image and point
  `PLAYWRIGHT_BROWSERS_PATH` at them — see `infra/dev-environment/README.md`.
- `./scripts/dev doctor` reports where it looked and whether it found them.

## `dotnet test` rejects `--filter-trait`

**Symptom**

```text
MSBUILD : error MSB1001: Unknown switch.
... --property:VSTestCLIRunSettings="--filter-trait;Category=Unit" ...
```

**Why** `dotnet` reads `global.json` from the **working directory**, and `global.json` is what opts
this repository into Microsoft.Testing.Platform. Run `dotnet test` from outside the repository and
it falls back to VSTest, which does not understand the runner's options.

**Fix** Run it from inside the repository — or use `./scripts/dev test unit`, which changes to the
repository root first for exactly this reason.

## The SDK version is refused

**Symptom**

```text
A compatible .NET SDK was not found.
Requested SDK version: 10.0.100
```

**Why** `global.json` pins the 10.0.1xx feature band (`rollForward: latestFeature`). An 8.x or 11.x
SDK will not do.

**Fix** Install a 10.0.1xx SDK (`docs/dev/setup.md`) and check with `dotnet --list-sdks`. If several
are installed, the pin selects the right one automatically.

## PowerShell will not run `dev.ps1`

**Symptom**

```text
File ...\scripts\dev.ps1 cannot be loaded because running scripts is disabled on this system.
```

**Fix** Allow it for the current session only:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\dev.ps1 doctor
```

## `./scripts/dev` fails with `$'\r': command not found`

**Symptom**

```text
./scripts/dev: line 2: $'\r': command not found
```

**Why** Git checked the file out with Windows line endings (`core.autocrlf=true`).

**Fix**

```bash
git config --global core.autocrlf false
git rm --cached -r . && git reset --hard      # re-checkout with the stored endings
```

Inside WSL, clone into the Linux filesystem (`~/src/...`), not `/mnt/c`.

## `docker compose up --wait` times out

**Symptom** `up` sits on `waiting for services to be healthy` and eventually fails.

**Why and fix**

- **ClamAV.** `clamd` loads the whole signature database and can take two minutes; its health check
  allows for that (`start_period: 180s`). It is behind the `scanner` profile precisely because it is
  too heavy to start by default — do not add it to the default set.
- **MinIO or PostgreSQL.** Read the reason: `docker compose -f infra/compose/docker-compose.yml logs
  postgres`. A corrupt volume after an abrupt shutdown is the usual cause; `./scripts/dev reset`
  clears it.
- **Not enough memory.** Docker Desktop and Colima both default to a small allocation. Give the VM
  at least 8 GB when the scanner profile is running.

## Still stuck

- `docker compose -f infra/compose/docker-compose.yml ps` and `... logs <service>` for the services.
- `artifacts/logs/{web,worker,pwa}.log` for the last `./scripts/dev run`.
- `./scripts/dev status` for a one-screen summary — paste it into the issue or pull request; it is
  the evidence format the project uses.
