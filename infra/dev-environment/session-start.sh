#!/usr/bin/env bash
#
# HyFib Tailor360 — SessionStart hook for Claude Code and other ephemeral environments (issue #20).
#
# It brings a fresh container to the point where the fast checks run: a .NET 10 SDK, restored NuGet
# packages, an installed pnpm workspace, Playwright browsers when they can be fetched, and
# TAILOR360_TEST_DATABASE_URL exported when a PostgreSQL instance is actually reachable.
#
# Register it in .claude/settings.json (see README.md in this directory):
#
#   { "hooks": { "SessionStart": [ { "hooks": [ { "type": "command",
#       "command": "$CLAUDE_PROJECT_DIR/infra/dev-environment/session-start.sh" } ] } ] } }
#
# Two rules govern this file:
#   1. It is idempotent. Every step checks first and skips when the work is already done, because
#      it runs again on every resume and on every cleared session.
#   2. It never blocks a session. An optional step that fails prints a warning and the script still
#      exits 0; the closing `./scripts/dev doctor` report says what is missing and what that costs.

set -euo pipefail

# ---------------------------------------------------------------------------------------------
# Location and helpers
# ---------------------------------------------------------------------------------------------

if [ -n "${CLAUDE_PROJECT_DIR:-}" ] && [ -d "$CLAUDE_PROJECT_DIR" ]; then
  REPO_ROOT=$CLAUDE_PROJECT_DIR
else
  script_source=${BASH_SOURCE[0]}
  while [ -L "$script_source" ]; do
    script_dir=$(cd -P "$(dirname "$script_source")" && pwd)
    script_source=$(readlink "$script_source")
    case $script_source in
      /*) ;;
      *) script_source="$script_dir/$script_source" ;;
    esac
  done
  REPO_ROOT=$(cd -P "$(dirname "$script_source")/../.." && pwd)
fi

PWA_DIR="$REPO_ROOT/clients/pwa"
WARNINGS=0

log() { printf '[tailor360 setup] %s\n' "$1"; }
warn() { printf '[tailor360 setup] warning: %s\n' "$1" >&2; WARNINGS=$((WARNINGS + 1)); }
have() { command -v "$1" >/dev/null 2>&1; }

# apt needs root. In a Claude Code container the session is root already; elsewhere sudo may exist.
if [ "$(id -u)" -eq 0 ]; then
  SUDO=""
elif have sudo; then
  SUDO="sudo"
else
  SUDO="unavailable"
fi

# Persists a variable for the whole session. Outside Claude Code the file does not exist, and the
# variable is only exported for this script's own children.
persist_env() {
  local assignment=$1
  if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
    # Idempotent: the hook runs again on every resume, and duplicate lines accumulate otherwise.
    if [ ! -f "$CLAUDE_ENV_FILE" ] || ! grep -qxF "export $assignment" "$CLAUDE_ENV_FILE"; then
      printf 'export %s\n' "$assignment" >> "$CLAUDE_ENV_FILE"
    fi
  fi
}

reachable() {
  # A HEAD request through whatever proxy is configured; used to decide between installation
  # routes rather than to fail on a blocked host.
  have curl || return 1
  curl --silent --show-error --output /dev/null --max-time 10 --head "$1" >/dev/null 2>&1
}

# ---------------------------------------------------------------------------------------------
# 1. .NET 10 SDK
# ---------------------------------------------------------------------------------------------

install_dotnet() {
  if have dotnet && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    log ".NET SDK $(dotnet --version) is present."
    return 0
  fi

  # apt first. On Ubuntu 24.04 dotnet-sdk-10.0 comes from the distribution archive, and
  # packages.microsoft.com serves the same package on distributions Canonical does not cover.
  # dotnet-install.sh is only a fallback here because builds.dotnet.microsoft.com and aka.ms are
  # blocked in the Claude Code network (see README.md in this directory).
  if have apt-get && [ "$SUDO" != "unavailable" ]; then
    log "Installing dotnet-sdk-10.0 from apt."
    if $SUDO apt-get update -qq && $SUDO DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-10.0; then
      log ".NET SDK $(dotnet --version) installed."
      return 0
    fi
    warn "apt could not install dotnet-sdk-10.0; trying the official installer script."
  fi

  if reachable https://builds.dotnet.microsoft.com/; then
    log "Installing the .NET SDK with dotnet-install.sh (channel 10.0)."
    if curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh &&
       bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"; then
      export PATH="$HOME/.dotnet:$PATH"
      persist_env "PATH=\$HOME/.dotnet:\$PATH"
      log ".NET SDK $(dotnet --version) installed into $HOME/.dotnet."
      return 0
    fi
    warn "dotnet-install.sh failed."
  else
    warn "builds.dotnet.microsoft.com is not reachable, so dotnet-install.sh cannot be used."
  fi

  warn "No .NET 10 SDK: nothing in this repository will build. See infra/dev-environment/README.md."
  return 1
}

# ---------------------------------------------------------------------------------------------
# 2. NuGet packages
# ---------------------------------------------------------------------------------------------

restore_dotnet() {
  have dotnet || return 0
  log "Restoring NuGet packages."
  # The container image is cached after the hook completes, so this download happens once rather
  # than at the start of the first build of every session.
  if (cd "$REPO_ROOT" && dotnet restore HyFib.Tailor360.slnx); then
    log "NuGet restore complete."
  else
    warn "dotnet restore failed; api.nuget.org may be blocked."
  fi
}

# ---------------------------------------------------------------------------------------------
# 3. pnpm and the client workspace
# ---------------------------------------------------------------------------------------------

install_pnpm_workspace() {
  if ! have node; then
    warn "Node.js is not installed; the progressive web application cannot be built or tested."
    return 1
  fi

  if ! have pnpm; then
    # The version comes from the packageManager field, so Corepack installs exactly what the
    # lockfile was written with.
    if have corepack; then
      log "Enabling pnpm through Corepack."
      corepack enable >/dev/null 2>&1 || warn "corepack enable failed; continuing."
    fi
  fi

  if ! have pnpm; then
    warn "pnpm is not available; skipping the client install."
    return 1
  fi

  log "Installing the client workspace (pnpm --frozen-lockfile)."
  # --frozen-lockfile: an ephemeral environment must reproduce the committed dependency set exactly,
  # and a lockfile that no longer matches package.json is a defect to report, not to paper over.
  if pnpm --dir "$PWA_DIR" install --frozen-lockfile; then
    log "Client workspace ready."
  else
    warn "pnpm install --frozen-lockfile failed; registry.npmjs.org may be blocked, or the lockfile is out of date."
    return 1
  fi
}

# ---------------------------------------------------------------------------------------------
# 4. Playwright browsers
# ---------------------------------------------------------------------------------------------

browsers_present() {
  local path=${PLAYWRIGHT_BROWSERS_PATH:-$HOME/.cache/ms-playwright}
  [ -d "$path" ] && ls "$path" 2>/dev/null | grep -q '^chromium'
}

install_playwright_chromium() {
  if browsers_present; then
    log "Playwright Chromium is already present (${PLAYWRIGHT_BROWSERS_PATH:-$HOME/.cache/ms-playwright})."
    return 0
  fi

  have pnpm || return 0
  # Playwright is not a dependency of the client workspace yet: tests/e2e and its configuration
  # arrive with issue #52. Attempting the install before then only produces a confusing error.
  if ! pnpm --dir "$PWA_DIR" exec playwright --version >/dev/null 2>&1; then
    log "Playwright is not a dependency yet (tests/e2e arrives with issue #52); skipping the browser install."
    return 0
  fi

  if ! reachable https://cdn.playwright.dev/; then
    warn "The Playwright CDN is not reachable; end-to-end tests cannot run in this environment."
    warn "Allowlist cdn.playwright.dev, or bake the browsers into the image and set PLAYWRIGHT_BROWSERS_PATH."
    return 0
  fi

  log "Installing Playwright Chromium."
  if pnpm --dir "$PWA_DIR" exec playwright install chromium; then
    log "Playwright Chromium installed."
  else
    warn "playwright install chromium failed."
  fi
}

# ---------------------------------------------------------------------------------------------
# 5. PostgreSQL for the integration tier
# ---------------------------------------------------------------------------------------------

detect_postgres() {
  if [ -n "${TAILOR360_TEST_DATABASE_URL:-}" ]; then
    log "TAILOR360_TEST_DATABASE_URL is already set; leaving it alone."
    return 0
  fi
  if ! have pg_isready || ! have psql; then
    log "No PostgreSQL client tools; not looking for a local instance."
    return 0
  fi

  local port user database
  # 5432 is the default; 5433 is the usual second instance in an image that already ships one.
  for port in ${TAILOR360_PG_CANDIDATE_PORTS:-5432 5433}; do
    pg_isready --host=127.0.0.1 --port="$port" --quiet || continue
    for user in postgres tailor360; do
      # `select 1` proves the connection can actually be opened without a password (trust or peer
      # authentication); anything else is not something a hook may guess at.
      if psql --host=127.0.0.1 --port="$port" --username="$user" --dbname=postgres \
          --no-password --tuples-only --quiet --command='select 1' >/dev/null 2>&1; then
        database=postgres
        if [ "$(psql --host=127.0.0.1 --port="$port" --username="$user" --dbname=postgres \
            --no-password --tuples-only --no-align \
            --command="select count(*) from pg_database where datname = 'tailor360'" 2>/dev/null)" = "1" ]; then
          database=tailor360
        fi
        local connection="Host=127.0.0.1;Port=$port;Database=$database;Username=$user"
        export TAILOR360_TEST_DATABASE_URL="$connection"
        persist_env "TAILOR360_TEST_DATABASE_URL=\"$connection\""
        log "Integration tier will use PostgreSQL at 127.0.0.1:$port/$database as $user."
        return 0
      fi
    done
  done

  log "No password-less PostgreSQL found; the Integration tier will skip unless a Docker daemon is available."
}

# ---------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------

log "Repository: $REPO_ROOT"

install_dotnet || true
restore_dotnet || true
install_pnpm_workspace || true
install_playwright_chromium || true
detect_postgres || true

if [ "$WARNINGS" -gt 0 ]; then
  log "Setup finished with $WARNINGS warning(s). The report below says what that costs."
else
  log "Setup complete."
fi

# The environment report is the point of the exercise: it states which test tiers can run here, so
# the session knows immediately what evidence it can and cannot produce.
if [ -x "$REPO_ROOT/scripts/dev" ]; then
  "$REPO_ROOT/scripts/dev" doctor || true
fi

# Always successful: a hook that fails would block a session over an optional dependency.
exit 0
