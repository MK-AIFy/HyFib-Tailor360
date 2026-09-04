# Ephemeral development environments

What a Claude Code session, a cloud container or a fresh CI runner needs before it can build and
test this repository, and the SessionStart hook that arranges it.

The everyday developer path is `docs/dev/setup.md`. This directory is about environments that are
created empty every time.

## Contents

| File | Purpose |
| --- | --- |
| `session-start.sh` | Idempotent setup hook: .NET SDK, NuGet restore, pnpm workspace, Playwright browsers, `TAILOR360_TEST_DATABASE_URL` when a database is reachable, closing `./scripts/dev doctor` report. |
| `README.md` | This file: the network allowlist, the verified facts about the Claude Code network, and the self-hosted alternative. |

## Network allowlist

An ephemeral environment downloads everything at start-up, so its egress policy decides what can be
built. These are the hosts this repository needs, with what was actually observed from a Claude
Code session on 2026-09-04.

| Host | Needed for | Observed |
| --- | --- | --- |
| `archive.ubuntu.com`, `security.ubuntu.com` | `dotnet-sdk-10.0` on Ubuntu 24.04, `postgresql-client-16`, browser libraries | reachable |
| `packages.microsoft.com` | The Microsoft apt feed — the SDK on distributions Canonical does not package, and other Microsoft packages | reachable |
| `api.nuget.org` | `dotnet restore` (service index and package content) | reachable |
| `registry.npmjs.org` | `pnpm install` | reachable |
| `cdn.playwright.dev` | Playwright browser binaries | **blocked** (proxy answered 403 to CONNECT) |
| `builds.dotnet.microsoft.com` | Payloads of `dotnet-install.sh` | **blocked** (403) |
| `aka.ms`, `dot.net` | Redirectors used by `dotnet-install.sh` and by Microsoft documentation links | **blocked** — `https://dot.net/v1/dotnet-install.sh` redirects to `builds.dotnet.microsoft.com`, which is refused |
| `deb.nodesource.com` | Node.js 22 when the image does not ship it | **blocked** — the image here already has Node 22 |
| `github.com` | Git operations | Served by the session's own git proxy |

### The verified fact that matters

**In this environment the .NET 10 SDK installs from the apt repositories, not from
`dotnet-install.sh`.** `packages.microsoft.com` is reachable and configured on the image
(`/etc/apt/sources.list.d/microsoft-prod.list`), and on Ubuntu 24.04 the `dotnet-sdk-10.0` package
itself is served by Canonical's `noble-updates/main` (version 10.0.111 here); Microsoft's `noble`
feed carries no `dotnet-sdk` package for this release. Meanwhile `builds.dotnet.microsoft.com` and
`aka.ms` are blocked, so the `dotnet-install.sh --channel 10.0` route named in Section 12 of the
implementation plan **cannot be used here at all** — the script cannot even be downloaded.

`session-start.sh` therefore tries apt first and falls back to `dotnet-install.sh` only when
`builds.dotnet.microsoft.com` answers, which is the right order for both networks.

Two consequences follow for the allowlist:

- Add `cdn.playwright.dev` if end-to-end tests are to run in the session. Otherwise bake the
  browsers into the image and set `PLAYWRIGHT_BROWSERS_PATH` (this image does exactly that:
  Chromium is at `/opt/pw-browsers`, which is why `./scripts/dev doctor` reports the E2E tier as
  able to run).
- Adding `builds.dotnet.microsoft.com` and `aka.ms` is only needed if you prefer the installer
  script to apt — for example to pin an exact SDK patch that the distribution does not carry.

## The SessionStart hook

`session-start.sh` does five things, each of them skipped when it is already done:

1. **.NET 10 SDK** — skip when `dotnet --list-sdks` shows a 10.x entry; otherwise `apt-get install
   dotnet-sdk-10.0`; otherwise `dotnet-install.sh --channel 10.0` when its host is reachable.
2. **NuGet packages** — `dotnet restore HyFib.Tailor360.slnx`. The container image is cached after
   the hook completes, so this download happens once instead of on the first build of every session.
3. **Client workspace** — Corepack activates the pinned pnpm, then `pnpm install --frozen-lockfile`
   in `clients/pwa`. Frozen on purpose: an ephemeral environment must reproduce the committed
   dependency set exactly, and a stale lockfile is a defect to report rather than to paper over.
4. **Playwright Chromium** — skipped when browsers are already present, when Playwright is not yet a
   dependency (`tests/e2e` arrives with issue #52) or when the CDN is blocked.
5. **A database for the integration tier** — looks for a password-less PostgreSQL on 127.0.0.1:5432
   and :5433, and when it finds one writes `TAILOR360_TEST_DATABASE_URL` to `$CLAUDE_ENV_FILE` so
   that the whole session has it. This is what turns `Integration SKIPPED` into `Integration RUNS`.

It then prints `./scripts/dev doctor`, so the first thing in the session transcript is a statement
of which test tiers this environment can produce evidence for.

**It always exits 0.** A hook that fails would block the session over an optional dependency; the
warnings and the doctor report say what is missing and what that costs.

### Registering it

In `.claude/settings.json` at the repository root:

```json
{
  "hooks": {
    "SessionStart": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "$CLAUDE_PROJECT_DIR/infra/dev-environment/session-start.sh"
          }
        ]
      }
    ]
  }
}
```

Notes:

- It runs synchronously by default, which is what we want: the session must not start a build while
  the SDK is still installing. If start-up latency becomes a problem, the hook can print
  `{"async": true, "asyncTimeout": 300000}` as its first line instead, accepting the race.
- `$CLAUDE_PROJECT_DIR` is the repository root; the hook also resolves the root from its own path,
  so it can be run by hand.
- Run it by hand exactly as a session would:

  ```bash
  CLAUDE_ENV_FILE=/tmp/session-env ./infra/dev-environment/session-start.sh
  ```

- Once the settings file is on the default branch, every future session uses the hook.

## What the hook cannot fix

| Missing | Consequence | What to do |
| --- | --- | --- |
| Docker daemon | Testcontainers cannot start PostgreSQL, MinIO or ClamAV | Provide a database through `TAILOR360_TEST_DATABASE_URL` (the hook does this automatically when one is running), or use a runner with Docker |
| Object storage | Media integration tests skip | Set `TAILOR360_TEST_S3_ENDPOINT` to a MinIO or S3-compatible endpoint |
| Playwright CDN | End-to-end tests cannot run | Allowlist `cdn.playwright.dev`, or bake browsers in and set `PLAYWRIGHT_BROWSERS_PATH` |
| `clamd` | The real malware-scanner contract test cannot run | It runs in its own CI job with a pinned image; `FakeMalwareScanner` is the default everywhere else |

None of these silently reduces the evidence: `CI=true` turns an integration skip into a failure, so
a tier that could not run never passes for the wrong reason on the way to `main`.

## The alternative: a runner with Docker

A cloud session is right for most work. It is the wrong tool for anything that needs real
containers: the Testcontainers-based integration tests, the `clamd` contract test, image builds and
the Trivy image scan, and rehearsals of the compose deployment.

Two ways to get one, in the order of increasing effort:

1. **A self-hosted GitHub Actions runner** on a small VM (2 vCPU, 8 GB, Docker Engine 27+),
   labelled `docker`, so that the jobs which need a daemon target it while everything else stays on
   the hosted runners. Keep the runner ephemeral (`--ephemeral`), never expose it to fork pull
   requests, and give it no deployment secrets — those live in protected GitHub Environments.
2. **A dev container or a developer VM** with Docker for the same work done interactively:
   `./scripts/dev up` then `./scripts/dev test integration` behaves exactly as it does on a laptop.

Which of these the project adopts is Section 11 item 1 of the implementation plan, and it is worth
deciding before the persistence work of issue #21 lands, because that is where integration coverage
starts to matter.

## Verifying an environment

```bash
./scripts/dev doctor
```

Read the two tables: the first says what is installed, the second says which tiers can run. Paste
that output into the pull request when the environment is part of what changed — it is the same
evidence format as `./scripts/dev status`.
