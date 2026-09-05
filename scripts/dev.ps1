#!/usr/bin/env pwsh
<#
.SYNOPSIS
  HyFib Tailor360 developer entry point for Windows (issue #20).

.DESCRIPTION
  The PowerShell twin of scripts/dev. It carries the same verbs — up, restore, build, test, run,
  reset, status, doctor — and prints the same output shape, so documentation, pull-request evidence
  and continuous integration can quote either one.

  It targets Windows 11 without WSL and is written for Windows PowerShell 5.1 as well as
  PowerShell 7: no ternary operator, no null-coalescing and no $PSStyle, because 5.1 is what a new
  machine has before anything is installed.

  Everything is resolved from the location of this file, so every verb works from any working
  directory.

.EXAMPLE
  .\scripts\dev.ps1 doctor
  .\scripts\dev.ps1 test unit
  .\scripts\dev.ps1 reset --yes
#>

[CmdletBinding()]
param(
  [Parameter(Position = 0)]
  [string]$Verb = 'help',

  [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
  [string[]]$Arguments = @()
)

# Stop on the first error from a cmdlet; native commands are checked through Invoke-Step, which
# inspects $LASTEXITCODE (a failing exe does not raise a PowerShell error on its own).
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------
# Repository location
# ---------------------------------------------------------------------------------------------

$RepoRoot = Split-Path -Parent $PSScriptRoot

# Every command runs from the repository root. This is not cosmetic: `dotnet` resolves global.json
# from the working directory, and global.json is what selects the SDK version and the
# Microsoft.Testing.Platform test runner — from another directory `dotnet test` silently falls back
# to VSTest and rejects the runner's own options.
Set-Location -LiteralPath $RepoRoot

$Solution = Join-Path $RepoRoot 'HyFib.Tailor360.slnx'
$PwaDir = Join-Path $RepoRoot 'clients/pwa'
$WebProject = Join-Path $RepoRoot 'src/Hosts/Tailor360.Web/Tailor360.Web.csproj'
$WorkerProject = Join-Path $RepoRoot 'src/Hosts/Tailor360.Worker/Tailor360.Worker.csproj'
$CliProject = Join-Path $RepoRoot 'src/Tools/Tailor360.Cli/Tailor360.Cli.csproj'
$ComposeDir = Join-Path $RepoRoot 'infra/compose'
$ComposeFile = Join-Path $ComposeDir 'docker-compose.yml'
# artifacts/ is already git-ignored, so run logs never appear in `git status`.
$LogDir = Join-Path $RepoRoot 'artifacts/logs'

# ---------------------------------------------------------------------------------------------
# Configuration (every value can be overridden from the environment)
# ---------------------------------------------------------------------------------------------

function Get-EnvOrDefault {
  param([string]$Name, [string]$Default)
  $value = [Environment]::GetEnvironmentVariable($Name)
  if ([string]::IsNullOrWhiteSpace($value)) { return $Default }
  return $value
}

# The web host listens on 8080 in containers and the Vite dev server proxies /api and /health to
# that origin, so an SDK run must use the same port.
$WebPort = Get-EnvOrDefault 'TAILOR360_WEB_PORT' '8080'
# The worker serves only its own probes, on its own port (Worker:HealthPort, appsettings.json).
$WorkerHealthPort = Get-EnvOrDefault 'TAILOR360_WORKER_HEALTH_PORT' '8081'
# vite.config.ts pins the dev server with strictPort, so this is the only port it will use.
$PwaPort = Get-EnvOrDefault 'TAILOR360_PWA_PORT' '5173'
$MinioPort = Get-EnvOrDefault 'TAILOR360_MINIO_PORT' '9000'
# Probes are local, so a short timeout keeps `status` fast; a slow answer is itself a finding.
$ProbeTimeout = [int](Get-EnvOrDefault 'TAILOR360_PROBE_TIMEOUT' '5')
$DotnetConfiguration = Get-EnvOrDefault 'TAILOR360_CONFIGURATION' 'Debug'

# ---------------------------------------------------------------------------------------------
# Output helpers
# ---------------------------------------------------------------------------------------------

# Colour only when stdout is a real console: redirected output must stay free of escape sequences
# so that a pull request can quote it verbatim.
$script:UseColour = (-not [Console]::IsOutputRedirected) -and
  [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable('NO_COLOR'))

$Esc = [char]27
if ($script:UseColour) {
  $script:CReset = "$Esc[0m"; $script:CBold = "$Esc[1m"; $script:CDim = "$Esc[2m"
  $script:CRed = "$Esc[31m"; $script:CGreen = "$Esc[32m"; $script:CYellow = "$Esc[33m"
} else {
  $script:CReset = ''; $script:CBold = ''; $script:CDim = ''
  $script:CRed = ''; $script:CGreen = ''; $script:CYellow = ''
}

function Write-Heading { param([string]$Text) Write-Host "`n$($script:CBold)==> $Text$($script:CReset)" }
function Write-Info { param([string]$Text) Write-Host $Text }
function Write-Detail { param([string]$Text) Write-Host "$($script:CDim)$Text$($script:CReset)" }
function Write-Warn { param([string]$Text) Write-Host "$($script:CYellow)warning:$($script:CReset) $Text" }
function Write-Failure { param([string]$Text) Write-Host "$($script:CRed)error:$($script:CReset) $Text" }

# Echoes the command before running it, so that the transcript of a session is also a record of
# exactly what was executed — this is what pull-request evidence is quoted from.
function Invoke-Step {
  param(
    [Parameter(Mandatory = $true)][string]$Command,
    [string[]]$CommandArguments = @()
  )
  Write-Host "$($script:CDim)`$ $Command $($CommandArguments -join ' ')$($script:CReset)"
  & $Command @CommandArguments
  if ($LASTEXITCODE -ne 0) {
    throw "Command failed with exit code ${LASTEXITCODE}: $Command $($CommandArguments -join ' ')"
  }
}

function Test-Tool {
  param([string]$Name)
  return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Assert-Tool {
  param([string]$Name)
  if (-not (Test-Tool $Name)) {
    Write-Failure "required tool not found on PATH: $Name"
    Write-Failure "install it as described in docs/dev/setup.md, then run '.\scripts\dev.ps1 doctor'."
    exit 127
  }
}

function Show-Usage {
  @'
HyFib Tailor360 developer entry point.

Usage: .\scripts\dev.ps1 <verb> [arguments]

Verbs:
  up                 Restore, then start the compose backing services (PostgreSQL, MinIO,
                     Mailpit; ClamAV and the collector are opt-in profiles).
  restore            dotnet restore the solution and pnpm install the progressive web application.
  build              dotnet build the solution and build the progressive web application.
  test [tier]        Run the tests. Tier: all (default), unit, architecture, contract,
                     integration, pwa, e2e.
  run                Start the web host, the worker and the PWA dev server together.
  reset              Destroy the local data and re-create it: compose down -v, migrate,
                     init-reference-data, seed-synthetic. Never runs against Production.
  status             Print the state of the five components and exit non-zero when an essential
                     one is down. This output is the evidence for issue #20.
  doctor             Report the toolchain and which test tiers can run in this environment.
  migrate [--dry-run] Apply outstanding database migrations. Safe to re-run; --dry-run only reports.
  docs               Check that every relative link in the documentation resolves.

Common options:
  --yes              Skip the confirmation prompt of `reset` (also TAILOR360_ASSUME_YES=1).

Environment overrides:
  TAILOR360_WEB_PORT (8080), TAILOR360_WORKER_HEALTH_PORT (8081), TAILOR360_PWA_PORT (5173),
  TAILOR360_MINIO_PORT (9000), TAILOR360_PROBE_TIMEOUT (5), TAILOR360_CONFIGURATION (Debug),
  TAILOR360_TEST_DATABASE_URL, TAILOR360_TEST_S3_ENDPOINT.

Documentation: docs/dev/setup.md, docs/dev/commands.md, docs/dev/ports.md,
docs/dev/troubleshooting.md.
'@ | Write-Host
}

# ---------------------------------------------------------------------------------------------
# Docker and compose
# ---------------------------------------------------------------------------------------------

function Test-DockerDaemon {
  if (-not (Test-Tool 'docker')) { return $false }
  # `docker info` is the only reliable check: the CLI is present in plenty of environments that
  # have no daemon at all (Docker Desktop installed but not started is the common Windows case).
  & docker info 2>$null | Out-Null
  return ($LASTEXITCODE -eq 0)
}

function Invoke-Compose {
  param([string[]]$ComposeArguments)
  # --project-directory keeps infra/compose/.env as the environment file and the relative volume
  # paths inside the compose file resolvable, whatever the caller's working directory is.
  $all = @('compose', '--project-directory', $ComposeDir, '--file', $ComposeFile) + $ComposeArguments
  Invoke-Step -Command 'docker' -CommandArguments $all
}

function Show-NoDockerGuidance {
  @"
No Docker daemon is reachable, so the compose backing services cannot be started here.
This is expected in a Claude Code cloud session and on a machine without Docker Desktop.

Use the services the environment already provides instead:

  1. PostgreSQL — point the integration tests and the hosts at an instance you have:
       `$env:TAILOR360_TEST_DATABASE_URL = "Host=127.0.0.1;Port=5432;Database=tailor360;Username=postgres"
     Without that variable the Integration tier skips with a visible warning, and CI=true turns
     that skip into a failure, so nothing merges unverified.

  2. Object storage — when a MinIO or other S3-compatible endpoint is available:
       `$env:TAILOR360_TEST_S3_ENDPOINT = "http://127.0.0.1:$MinioPort"

  3. Check what can run here:
       .\scripts\dev.ps1 doctor

  4. If this machine should have Docker, see docs/dev/troubleshooting.md ("No Docker daemon").
"@ | Write-Host
}

# ---------------------------------------------------------------------------------------------
# Probe targets
# ---------------------------------------------------------------------------------------------

# Reads one key from infra/compose/.env without executing it: the file is developer-owned and must
# not be able to run code through this script.
function Get-ComposeEnvValue {
  param([string]$Key)
  $file = Join-Path $ComposeDir '.env'
  if (-not (Test-Path -LiteralPath $file)) { return '' }
  $match = Select-String -LiteralPath $file -Pattern "^\s*$([regex]::Escape($Key))=(.*)$" |
    Select-Object -Last 1
  if ($null -eq $match) { return '' }
  return $match.Matches[0].Groups[1].Value.Trim()
}

# Extracts one keyword from an ADO.NET connection string, case-insensitively. Only Host, Port,
# Database and Username are ever read — a password must never reach the output of this script.
function Get-ConnectionKeyword {
  param([string]$ConnectionString, [string]$Key)
  foreach ($part in $ConnectionString.Split(';')) {
    $equals = $part.IndexOf('=')
    if ($equals -lt 1) { continue }
    $name = $part.Substring(0, $equals).Trim()
    if ($name.ToLowerInvariant() -eq $Key.ToLowerInvariant()) {
      return $part.Substring($equals + 1).Trim()
    }
  }
  return ''
}

function Resolve-PostgresTarget {
  $target = @{
    Host = Get-EnvOrDefault 'TAILOR360_POSTGRES_HOST' '127.0.0.1'
    Port = Get-EnvOrDefault 'TAILOR360_POSTGRES_PORT' ''
    Database = Get-EnvOrDefault 'POSTGRES_DB' 'tailor360'
    User = Get-EnvOrDefault 'POSTGRES_USER' 'tailor360'
    Source = 'compose defaults'
  }

  $url = [Environment]::GetEnvironmentVariable('TAILOR360_TEST_DATABASE_URL')
  if (-not [string]::IsNullOrWhiteSpace($url)) {
    # An externally provided instance is the one the integration tests use, so it is the one
    # `status` must report — not a compose service that is not running.
    $target.Source = 'TAILOR360_TEST_DATABASE_URL'
    if ($url -match '^(postgres|postgresql)://') {
      $rest = $url -replace '^(postgres|postgresql)://', ''
      $at = $rest.LastIndexOf('@')
      if ($at -ge 0) { $rest = $rest.Substring($at + 1) }  # drop credentials before printing
      $slash = $rest.IndexOf('/')
      $hostPort = $rest
      if ($slash -ge 0) {
        $hostPort = $rest.Substring(0, $slash)
        $database = $rest.Substring($slash + 1).Split('?')[0]
        if (-not [string]::IsNullOrWhiteSpace($database)) { $target.Database = $database }
      }
      $colon = $hostPort.LastIndexOf(':')
      if ($colon -ge 0) {
        $target.Host = $hostPort.Substring(0, $colon)
        $target.Port = $hostPort.Substring($colon + 1)
      } else {
        $target.Host = $hostPort
      }
    } elseif ($url.Contains('=')) {
      $value = Get-ConnectionKeyword $url 'Host'; if ($value) { $target.Host = $value }
      $value = Get-ConnectionKeyword $url 'Port'; if ($value) { $target.Port = $value }
      $value = Get-ConnectionKeyword $url 'Database'; if ($value) { $target.Database = $value }
      $value = Get-ConnectionKeyword $url 'Username'; if ($value) { $target.User = $value }
    }
  } elseif ([string]::IsNullOrWhiteSpace($target.Port)) {
    $value = Get-ComposeEnvValue 'POSTGRES_PUBLISHED_PORT'; if ($value) { $target.Port = $value }
    $value = Get-ComposeEnvValue 'POSTGRES_DB'; if ($value) { $target.Database = $value }
    $value = Get-ComposeEnvValue 'POSTGRES_USER'; if ($value) { $target.User = $value }
  }

  if ([string]::IsNullOrWhiteSpace($target.Port)) { $target.Port = '5432' }
  return $target
}

function Get-ObjectStorageUrl {
  # The test endpoint wins for the same reason as the database: it is the instance actually in use.
  $endpoint = [Environment]::GetEnvironmentVariable('TAILOR360_TEST_S3_ENDPOINT')
  if (-not [string]::IsNullOrWhiteSpace($endpoint)) { return $endpoint.TrimEnd('/') }
  $port = Get-ComposeEnvValue 'MINIO_API_PUBLISHED_PORT'
  if ([string]::IsNullOrWhiteSpace($port)) { $port = $MinioPort }
  return "http://127.0.0.1:$port"
}

# ---------------------------------------------------------------------------------------------
# Probes. Each returns @{ State = 'OK'|'FAIL'|'SKIPPED'; Detail = '...' }.
# ---------------------------------------------------------------------------------------------

function Invoke-HttpProbe {
  param([string]$Url, [switch]$ReadHealthPayload)
  try {
    $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $ProbeTimeout -Method Get
    $detail = "HTTP $([int]$response.StatusCode)"
    if ($ReadHealthPayload) {
      # The health payload is {"Status":"Healthy","DurationMs":n,"Components":[...]} and the
      # top-level Status is the first match, so no JSON parser is needed.
      $match = [regex]::Match([string]$response.Content, '"Status"\s*:\s*"([A-Za-z]+)"')
      if ($match.Success) { $detail = "$detail, $($match.Groups[1].Value)" }
    }
    return @{ State = 'OK'; Detail = $detail }
  } catch {
    $response = $null
    if ($null -ne $_.Exception.PSObject.Properties['Response']) { $response = $_.Exception.Response }
    if ($null -ne $response) {
      # A 503 from a readiness probe is a meaningful answer, not a transport error.
      $code = [int]$response.StatusCode
      $detail = "HTTP $code"
      $body = ''
      if ($null -ne $_.ErrorDetails) { $body = [string]$_.ErrorDetails.Message }
      $match = [regex]::Match($body, '"Status"\s*:\s*"([A-Za-z]+)"')
      if ($ReadHealthPayload -and $match.Success) { $detail = "$detail, $($match.Groups[1].Value)" }
      return @{ State = 'FAIL'; Detail = $detail }
    }
    return @{ State = 'FAIL'; Detail = 'connection refused' }
  }
}

function Invoke-PostgresProbe {
  param([object]$Target)
  if (Test-Tool 'pg_isready') {
    # pg_isready only asks whether the server accepts connections; it performs no authentication,
    # so it needs no password and cannot fail because of one.
    & pg_isready --host=$($Target.Host) --port=$($Target.Port) --dbname=$($Target.Database) `
      --timeout=$ProbeTimeout --quiet 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { return @{ State = 'OK'; Detail = 'accepting connections' } }
    return @{ State = 'FAIL'; Detail = 'not accepting connections' }
  }
  # Fallback for a machine with no PostgreSQL client tools: a TCP connect is weaker evidence (it
  # proves the port is open, not that the cluster is ready) and the detail says so.
  $client = New-Object System.Net.Sockets.TcpClient
  try {
    $async = $client.BeginConnect($Target.Host, [int]$Target.Port, $null, $null)
    if ($async.AsyncWaitHandle.WaitOne([TimeSpan]::FromSeconds($ProbeTimeout)) -and $client.Connected) {
      $client.EndConnect($async)
      return @{ State = 'OK'; Detail = 'port open (no psql client, readiness unverified)' }
    }
    return @{ State = 'FAIL'; Detail = 'connection refused' }
  } catch {
    return @{ State = 'FAIL'; Detail = 'connection refused' }
  } finally {
    $client.Close()
  }
}

# ---------------------------------------------------------------------------------------------
# Verbs
# ---------------------------------------------------------------------------------------------

function Invoke-Restore {
  Assert-Tool 'dotnet'
  Assert-Tool 'pnpm'
  Write-Heading 'Restore'
  Invoke-Step -Command 'dotnet' -CommandArguments @('restore', $Solution)
  if ([Environment]::GetEnvironmentVariable('CI') -eq 'true') {
    # In continuous integration a lockfile that does not match package.json must fail the run
    # rather than be silently updated.
    Invoke-Step -Command 'pnpm' -CommandArguments @('--dir', $PwaDir, 'install', '--frozen-lockfile')
  } else {
    Invoke-Step -Command 'pnpm' -CommandArguments @('--dir', $PwaDir, 'install')
  }
}

function Invoke-Build {
  Assert-Tool 'dotnet'
  Assert-Tool 'pnpm'
  Write-Heading 'Build'
  Invoke-Step -Command 'dotnet' -CommandArguments @('build', $Solution, '--configuration', $DotnetConfiguration)
  # `pnpm build` is `tsc -b && vite build`, so this also type-checks the client.
  Invoke-Step -Command 'pnpm' -CommandArguments @('--dir', $PwaDir, 'build')
  Publish-Client
}

# Copies the built client into the web host's wwwroot, which is how the host serves it in a deployed
# environment. Doing it here as well means a local build exercises the same routing a container does,
# rather than only the dev server proxy, which is where an unmatched /api path behaves differently.
function Publish-Client {
  $target = Join-Path $RepoRoot 'src/Hosts/Tailor360.Web/wwwroot'
  $source = Join-Path $PwaDir 'dist'

  if (-not (Test-Path $source)) {
    Write-Warn "No client build found at $source; skipping the wwwroot copy."
    return
  }

  Write-Info "Publishing the client into $target"
  Get-ChildItem -Path $target -Force |
    Where-Object { $_.Name -ne '.gitkeep' } |
    Remove-Item -Recurse -Force
  Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
}

function Invoke-Test {
  param([string]$Tier = 'all')
  Assert-Tool 'dotnet'
  switch ($Tier) {
    'all' {
      Assert-Tool 'pnpm'
      Write-Heading 'Tests — all tiers'
      Invoke-Step -Command 'dotnet' -CommandArguments @('test', '--solution', $Solution)
      Invoke-Step -Command 'pnpm' -CommandArguments @('--dir', $PwaDir, 'test')
    }
    { $_ -in @('unit', 'architecture', 'contract', 'integration') } {
      # Each tier is one project, and the trait values are the ones the test classes carry, e.g.
      # [Trait("Category", "Unit")]. The project is named as well as the trait because a
      # solution-wide filtered run exits 8 ("zero tests ran") from the projects that hold no test
      # of that tier, which would report a green tier as a failure.
      $category = @{
        'unit' = 'Unit'; 'architecture' = 'Architecture'
        'contract' = 'Contract'; 'integration' = 'Integration'
      }[$Tier]
      $project = @{
        'unit' = 'Tailor360.UnitTests'; 'architecture' = 'Tailor360.ArchitectureTests'
        'contract' = 'Tailor360.ContractTests'; 'integration' = 'Tailor360.IntegrationTests'
      }[$Tier]
      Write-Heading "Tests — $category tier"
      if ($Tier -eq 'integration' -and
          [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('TAILOR360_TEST_DATABASE_URL')) -and
          -not (Test-DockerDaemon)) {
        Write-Warn 'No database and no Docker daemon: the Integration tier will skip. Set TAILOR360_TEST_DATABASE_URL.'
      }
      Invoke-Step -Command 'dotnet' -CommandArguments @(
        'test', '--project', (Join-Path $RepoRoot "tests/$project/$project.csproj"),
        '--', '--filter-trait', "Category=$category")
    }
    'pwa' {
      Assert-Tool 'pnpm'
      Write-Heading 'Tests — progressive web application'
      Invoke-Step -Command 'pnpm' -CommandArguments @('--dir', $PwaDir, 'test')
    }
    'e2e' {
      Write-Heading 'Tests — end to end'
      $e2e = Join-Path $RepoRoot 'tests/e2e'
      if (-not (Test-Path -LiteralPath $e2e)) {
        # tests/e2e and its Playwright configuration are owned by issue #52; saying so is more
        # useful than a missing-directory error.
        Write-Warn 'tests/e2e does not exist yet: the Playwright suite arrives with issue #52.'
        return
      }
      Assert-Tool 'pnpm'
      Invoke-Step -Command 'pnpm' -CommandArguments @('--dir', $e2e, 'test')
    }
    default {
      Write-Failure "unknown test tier: $Tier (expected all, unit, architecture, contract, integration, pwa or e2e)"
      exit 64
    }
  }
}

function Invoke-Up {
  # `up` restores first so that a clean clone reaches a working state with one command.
  Invoke-Restore

  Write-Heading 'Backing services'
  if (-not (Test-DockerDaemon)) {
    Show-NoDockerGuidance
    exit 1
  }

  $envFile = Join-Path $ComposeDir '.env'
  if (-not (Test-Path -LiteralPath $envFile)) {
    # Every value in .env.example is a development placeholder and none of them is a secret, so
    # copying it is safe and saves a confusing first run with mismatched ports.
    Copy-Item -LiteralPath (Join-Path $ComposeDir '.env.example') -Destination $envFile
    Write-Detail 'created infra/compose/.env from .env.example (development placeholders, no secrets)'
  }

  # --wait blocks until every service with a healthcheck reports healthy, so a green return here
  # means the services really are usable, not merely started.
  Invoke-Compose @('up', '--detach', '--wait')

  $target = Resolve-PostgresTarget
  $consolePort = Get-ComposeEnvValue 'MINIO_CONSOLE_PUBLISHED_PORT'
  if ([string]::IsNullOrWhiteSpace($consolePort)) { $consolePort = '9001' }
  $mailpitPort = Get-ComposeEnvValue 'MAILPIT_UI_PUBLISHED_PORT'
  if ([string]::IsNullOrWhiteSpace($mailpitPort)) { $mailpitPort = '8025' }

  Write-Info ''
  Write-Info "PostgreSQL      $($target.Host):$($target.Port)/$($target.Database)"
  Write-Info "MinIO API       $(Get-ObjectStorageUrl)"
  Write-Info "MinIO console   http://127.0.0.1:$consolePort"
  Write-Info "Mailpit         http://127.0.0.1:$mailpitPort"
  Write-Detail 'ClamAV and the OpenTelemetry collector are opt-in profiles; see docs/dev/commands.md.'
  Write-Info ''
  Write-Info 'Next: .\scripts\dev.ps1 run    (web host, worker and PWA dev server)'
}

function Start-Component {
  param([string]$Name, [string]$CommandLine)
  $log = Join-Path $LogDir "$Name.log"
  # cmd.exe performs the redirection so that stdout and stderr land interleaved in one file;
  # Start-Process cannot redirect both streams to the same file.
  $process = Start-Process -FilePath $env:ComSpec -ArgumentList @('/c', "$CommandLine > `"$log`" 2>&1") `
    -WorkingDirectory $RepoRoot -NoNewWindow -PassThru
  return [pscustomobject]@{ Name = $Name; Process = $process; Log = $log }
}

function Stop-Component {
  param([pscustomobject]$Component)
  if ($null -eq $Component -or $Component.Process.HasExited) { return }
  # /T kills the tree: cmd.exe started dotnet, which started the built application, and killing
  # only the launcher would leave port 8080 or 5173 held.
  & taskkill /PID $Component.Process.Id /T /F 2>$null | Out-Null
}

function Invoke-Run {
  if ([string]::IsNullOrWhiteSpace($env:ComSpec)) {
    # The three components are started through cmd.exe so that both output streams can be
    # redirected into one log file. On macOS and Linux use scripts/dev, which does the same thing
    # with a POSIX shell; every other verb in this file works on both.
    Write-Failure 'run is implemented for Windows; on macOS and Linux use ./scripts/dev run.'
    exit 1
  }
  Assert-Tool 'dotnet'
  Assert-Tool 'pnpm'
  New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

  $target = Resolve-PostgresTarget
  $postgres = Invoke-PostgresProbe $target
  if ($postgres.State -ne 'OK') {
    Write-Warn "PostgreSQL at $($target.Host):$($target.Port) is not answering ($($postgres.Detail))."
    Write-Warn "Start it with '.\scripts\dev.ps1 up', or set TAILOR360_TEST_DATABASE_URL to an instance you have."
  }

  Write-Heading 'Running web host, worker and PWA dev server'
  Write-Info "Web host        http://127.0.0.1:$WebPort"
  Write-Info "  health        http://127.0.0.1:$WebPort/health/ready"
  Write-Info "  version       http://127.0.0.1:$WebPort/api/version"
  Write-Info "  OpenAPI       http://127.0.0.1:$WebPort/openapi/v1.json  (Development only)"
  Write-Info "Worker health   http://127.0.0.1:$WorkerHealthPort/health/ready"
  Write-Info "PWA dev server  http://127.0.0.1:$PwaPort"
  Write-Detail 'The PWA proxies /api and /health to the web host, so open the PWA URL, not the host URL.'
  Write-Detail "Logs: $LogDir\{web,worker,pwa}.log — press Ctrl-C to stop all three."
  Write-Info ''

  # The children inherit this process's environment, which is how the ports reach them.
  if ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_ENVIRONMENT)) { $env:ASPNETCORE_ENVIRONMENT = 'Development' }
  # Loopback by default, for the same reason every compose port is bound to 127.0.0.1: a laptop is
  # regularly on an untrusted shop, home or cafe network. Set ASPNETCORE_URLS yourself
  # (http://0.0.0.0:8080) together with `pnpm dev --host` to test from a phone on the same LAN.
  if ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_URLS)) { $env:ASPNETCORE_URLS = "http://127.0.0.1:$WebPort" }
  $env:Worker__HealthPort = $WorkerHealthPort

  $components = @()
  $readers = @{}
  $lastSource = ''
  try {
    $components += Start-Component 'web' "dotnet run --project `"$WebProject`" --configuration $DotnetConfiguration"
    $components += Start-Component 'worker' "dotnet run --project `"$WorkerProject`" --configuration $DotnetConfiguration"
    $components += Start-Component 'pwa' "pnpm --dir `"$PwaDir`" dev --port $PwaPort"

    foreach ($component in $components) {
      # FileShare.ReadWrite so that following a log never blocks the process writing it.
      $stream = New-Object System.IO.FileStream($component.Log, [System.IO.FileMode]::OpenOrCreate,
        [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
      $readers[$component.Name] = New-Object System.IO.StreamReader($stream)
    }

    while ($true) {
      $idle = $true
      foreach ($component in $components) {
        $reader = $readers[$component.Name]
        while ($true) {
          $line = $reader.ReadLine()
          if ($null -eq $line) { break }
          $idle = $false
          if ($lastSource -ne $component.Name) {
            # The same header `tail -f` prints in scripts/dev, so both transcripts read alike.
            Write-Host "`n==> $($component.Log) <=="
            $lastSource = $component.Name
          }
          Write-Host $line
        }
      }
      if ($components | Where-Object { $_.Process.HasExited }) {
        Write-Warn 'A component exited; stopping the others.'
        break
      }
      if ($idle) { Start-Sleep -Milliseconds 300 }
    }
  } finally {
    foreach ($component in $components) { Stop-Component $component }
    foreach ($reader in $readers.Values) { $reader.Dispose() }
    Write-Host "`nStopped. Logs remain in $LogDir."
  }
}

function Invoke-Reset {
  param([string[]]$Options = @())
  $assumeYes = ([Environment]::GetEnvironmentVariable('TAILOR360_ASSUME_YES') -eq '1')
  foreach ($option in $Options) {
    switch ($option) {
      '--yes' { $assumeYes = $true }
      '-y' { $assumeYes = $true }
      '-Yes' { $assumeYes = $true }
      default { Write-Failure "unknown option for reset: $option"; exit 64 }
    }
  }

  # The CLI refuses synthetic data in production unconditionally; refusing here as well means the
  # volumes are never destroyed on the way to that refusal.
  if ($env:ASPNETCORE_ENVIRONMENT -eq 'Production') {
    Write-Failure 'refusing to reset: ASPNETCORE_ENVIRONMENT is Production.'
    exit 3
  }

  if (-not $assumeYes) {
    if ([Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
      $answer = Read-Host 'This destroys the local database, object storage and mail volumes. Continue? [y/N]'
      if ($answer -notmatch '^(y|Y|yes|Yes)$') { Write-Info 'Cancelled.'; return }
    } else {
      Write-Failure 'reset is destructive and the session is not interactive; pass --yes to confirm.'
      exit 64
    }
  }

  Assert-Tool 'dotnet'

  Write-Heading 'Reset — destroying local data'
  if (Test-DockerDaemon) {
    # --volumes is the point of the verb: it removes the PostgreSQL, MinIO and Mailpit volumes.
    Invoke-Compose @('down', '--volumes', '--remove-orphans')
    Invoke-Compose @('up', '--detach', '--wait')
  } else {
    Write-Warn "No Docker daemon: skipping 'compose down -v' and 'compose up'."
    Write-Warn 'The database named by TAILOR360_TEST_DATABASE_URL is re-initialised in place instead.'
  }

  Write-Heading 'Reset — re-creating data'
  # Order matters: schema first, then the reference data every installation needs, then the
  # synthetic dataset that development and tests use.
  foreach ($command in @('migrate', 'init-reference-data', 'seed-synthetic')) {
    Invoke-Step -Command 'dotnet' -CommandArguments @(
      'run', '--project', $CliProject, '--configuration', $DotnetConfiguration, '--', $command)
  }
  Write-Info ''
  Write-Info "Reset complete. Run '.\scripts\dev.ps1 status' to confirm."
}

# ---------------------------------------------------------------------------------------------
# status
# ---------------------------------------------------------------------------------------------

function Write-StatusRow {
  param([string]$Name, [string]$Endpoint, [string]$State, [string]$Note)
  $colour = ''
  switch ($State) {
    'OK' { $colour = $script:CGreen }
    'FAIL' { $colour = $script:CRed }
    'SKIPPED' { $colour = $script:CYellow }
  }
  # The state is padded before it is coloured: escape sequences have width in a format string but
  # not on screen, so colouring a padded field would misalign every following column.
  $padded = $State.PadRight(8)
  Write-Host ("{0} {1} {2}{3}{4} {5}" -f $Name.PadRight(16), $Endpoint.PadRight(44),
    $colour, $padded, $script:CReset, $Note)
}

function Invoke-Status {
  $target = Resolve-PostgresTarget
  $storageUrl = Get-ObjectStorageUrl

  Write-Host "$($script:CBold)HyFib Tailor360 — local environment status$($script:CReset)"
  Write-Host (Get-Date -Format 'yyyy-MM-dd HH:mm:ss K')
  Write-Host ''
  Write-Host ("{0} {1} {2} {3}" -f 'COMPONENT'.PadRight(16), 'ENDPOINT'.PadRight(44), 'STATE'.PadRight(8), 'DETAIL')
  Write-Host ("{0} {1} {2} {3}" -f ('-' * 16), ('-' * 44), ('-' * 8), ('-' * 34))

  $essentialFailures = 0
  $otherFailures = 0
  $skipped = 0

  # Essential: the two hosts and the database. Losing any of them means the application cannot
  # serve a request at all.
  $rows = @(
    @{ Name = 'Web host'; Endpoint = "http://127.0.0.1:$WebPort/health/ready"; Essential = $true; Kind = 'health' },
    @{ Name = 'Worker host'; Endpoint = "http://127.0.0.1:$WorkerHealthPort/health/ready"; Essential = $true; Kind = 'health' },
    @{ Name = 'PostgreSQL'; Endpoint = "$($target.Host):$($target.Port)/$($target.Database)"; Essential = $true; Kind = 'postgres' },
    # Not essential: Section 4.4 classifies object storage as a degraded dependency (media stops
    # working, the instance keeps serving), and the PWA dev server is a developer convenience that
    # is deliberately not running when the built shell is served by the web host.
    @{ Name = 'Object storage'; Endpoint = "$storageUrl/minio/health/ready"; Essential = $false; Kind = 'http' },
    @{ Name = 'PWA dev server'; Endpoint = "http://127.0.0.1:$PwaPort/"; Essential = $false; Kind = 'http' }
  )

  foreach ($row in $rows) {
    switch ($row.Kind) {
      'health' { $result = Invoke-HttpProbe -Url $row.Endpoint -ReadHealthPayload }
      'http' { $result = Invoke-HttpProbe -Url $row.Endpoint }
      'postgres' { $result = Invoke-PostgresProbe $target }
    }
    Write-StatusRow -Name $row.Name -Endpoint $row.Endpoint -State $result.State -Note $result.Detail
    if ($result.State -eq 'SKIPPED') { $skipped++ }
    elseif ($result.State -eq 'FAIL') {
      if ($row.Essential) { $essentialFailures++ } else { $otherFailures++ }
    }
  }

  Write-Host ''
  Write-Detail "PostgreSQL target from: $($target.Source)"
  if ($essentialFailures -eq 0 -and $otherFailures -eq 0 -and $skipped -eq 0) {
    Write-Host "$($script:CGreen)All five components are up.$($script:CReset)"
    return 0
  }

  Write-Host ("{0} essential component(s) down, {1} non-essential down, {2} skipped." -f
    $essentialFailures, $otherFailures, $skipped)
  if ($essentialFailures -gt 0) {
    Write-Info "Start the backing services with '.\scripts\dev.ps1 up' and the hosts with '.\scripts\dev.ps1 run'."
    Write-Info 'See docs/dev/troubleshooting.md when a component stays down.'
    return 1
  }
  Write-Info 'Every essential component is up.'
  return 0
}

# ---------------------------------------------------------------------------------------------
# doctor
# ---------------------------------------------------------------------------------------------

function Write-DoctorRow {
  param([string]$Name, [string]$State, [string]$Note)
  $colour = $script:CYellow
  switch ($State) {
    'OK' { $colour = $script:CGreen }
    'FOUND' { $colour = $script:CGreen }
    'AVAILABLE' { $colour = $script:CGreen }
    'RUNS' { $colour = $script:CGreen }
    'MISSING' { $colour = $script:CRed }
    'UNAVAILABLE' { $colour = $script:CRed }
  }
  Write-Host ("{0} {1}{2}{3} {4}" -f $Name.PadRight(28), $colour, $State.PadRight(12), $script:CReset, $Note)
}

function Get-ToolVersion {
  param([string]$Tool, [string[]]$VersionArguments)
  try {
    $output = (& $Tool @VersionArguments 2>$null | Select-Object -First 1)
  } catch {
    return ''
  }
  if ([string]::IsNullOrWhiteSpace($output)) { return '' }
  # Only the first version-looking token is kept: `psql --version` prints a paragraph, and a
  # wrapped detail column would make this table unreadable.
  $match = [regex]::Match([string]$output, '[vV]?\d+\.\d+[\w.\-]*')
  if ($match.Success) { return $match.Value }
  return [string]$output
}

function Get-PlaywrightBrowsersPath {
  $configured = [Environment]::GetEnvironmentVariable('PLAYWRIGHT_BROWSERS_PATH')
  if (-not [string]::IsNullOrWhiteSpace($configured)) { return $configured }
  if ($env:LOCALAPPDATA) { return (Join-Path $env:LOCALAPPDATA 'ms-playwright') }
  return (Join-Path $HOME '.cache/ms-playwright')
}

function Invoke-Doctor {
  Write-Host "$($script:CBold)HyFib Tailor360 — environment report$($script:CReset)"
  Write-Host ''
  Write-Host ("{0} {1} {2}" -f 'TOOL'.PadRight(28), 'STATE'.PadRight(12), 'DETAIL')
  Write-Host ("{0} {1} {2}" -f ('-' * 28), ('-' * 12), ('-' * 34))

  $missingRequired = 0
  $tools = @(
    @{ Label = '.NET SDK'; Tool = 'dotnet'; Args = @('--version'); Required = $true },
    @{ Label = 'Node.js'; Tool = 'node'; Args = @('--version'); Required = $true },
    @{ Label = 'pnpm'; Tool = 'pnpm'; Args = @('--version'); Required = $true },
    @{ Label = 'psql (PostgreSQL)'; Tool = 'psql'; Args = @('--version'); Required = $false },
    @{ Label = 'curl'; Tool = 'curl'; Args = @('--version'); Required = $false },
    # Optional: only '.\scripts\dev.ps1 docs' needs it, and that check also runs in CI.
    @{ Label = 'python3 or python'; Tool = 'python'; Args = @('--version'); Required = $false }
  )
  foreach ($tool in $tools) {
    if (Test-Tool $tool.Tool) {
      Write-DoctorRow $tool.Label 'FOUND' (Get-ToolVersion $tool.Tool $tool.Args)
    } elseif ($tool.Required) {
      $missingRequired++
      Write-DoctorRow $tool.Label 'MISSING' 'required — see docs/dev/setup.md'
    } else {
      Write-DoctorRow $tool.Label 'MISSING' 'optional'
    }
  }

  $dockerDaemon = $false
  if (Test-Tool 'docker') {
    if (Test-DockerDaemon) {
      $dockerDaemon = $true
      $serverVersion = (& docker version --format '{{.Server.Version}}' 2>$null | Select-Object -First 1)
      if ([string]::IsNullOrWhiteSpace($serverVersion)) { $serverVersion = 'reachable' }
      Write-DoctorRow 'Docker daemon' 'AVAILABLE' $serverVersion
    } else {
      Write-DoctorRow 'Docker daemon' 'UNAVAILABLE' 'CLI present, no daemon — Testcontainers cannot run'
    }
  } else {
    Write-DoctorRow 'Docker daemon' 'MISSING' 'optional — see docs/dev/troubleshooting.md'
  }

  $databaseUrlSet = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('TAILOR360_TEST_DATABASE_URL'))
  if ($databaseUrlSet) {
    $target = Resolve-PostgresTarget
    # The value itself is never printed: it normally carries a password.
    Write-DoctorRow 'TAILOR360_TEST_DATABASE_URL' 'SET' "$($target.Host):$($target.Port)/$($target.Database) (value not printed)"
  } else {
    Write-DoctorRow 'TAILOR360_TEST_DATABASE_URL' 'UNSET' 'Integration tier falls back to Testcontainers'
  }

  $storageUrlSet = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('TAILOR360_TEST_S3_ENDPOINT'))
  if ($storageUrlSet) {
    Write-DoctorRow 'TAILOR360_TEST_S3_ENDPOINT' 'SET' ([Environment]::GetEnvironmentVariable('TAILOR360_TEST_S3_ENDPOINT'))
  } else {
    Write-DoctorRow 'TAILOR360_TEST_S3_ENDPOINT' 'UNSET' 'media tests use Testcontainers or skip'
  }

  $browsersPath = Get-PlaywrightBrowsersPath
  $browsers = $false
  if (Test-Path -LiteralPath $browsersPath) {
    $chromium = Get-ChildItem -LiteralPath $browsersPath -Filter 'chromium*' -ErrorAction SilentlyContinue
    if ($chromium) { $browsers = $true }
  }
  if ($browsers) {
    Write-DoctorRow 'Playwright browsers' 'FOUND' $browsersPath
  } else {
    Write-DoctorRow 'Playwright browsers' 'MISSING' "$browsersPath (pnpm exec playwright install chromium)"
  }

  Write-Host ''
  Write-Host ("{0} {1} {2}" -f 'TEST TIER'.PadRight(28), 'STATE'.PadRight(12), 'DETAIL')
  Write-Host ("{0} {1} {2}" -f ('-' * 28), ('-' * 12), ('-' * 34))
  Write-DoctorRow 'Unit' 'RUNS' 'no external dependency'
  Write-DoctorRow 'Architecture' 'RUNS' 'no external dependency'
  Write-DoctorRow 'Contract' 'RUNS' 'no external dependency'

  if ($databaseUrlSet) {
    Write-DoctorRow 'Integration' 'RUNS' 'against TAILOR360_TEST_DATABASE_URL'
  } elseif ($dockerDaemon) {
    Write-DoctorRow 'Integration' 'RUNS' 'Testcontainers on the local Docker daemon'
  } else {
    Write-DoctorRow 'Integration' 'SKIPPED' 'no database and no Docker daemon'
  }

  $e2eSuite = 'tests/e2e arrives with issue #52'
  if (Test-Path -LiteralPath (Join-Path $RepoRoot 'tests/e2e')) { $e2eSuite = 'tests/e2e present' }
  if ($browsers) {
    Write-DoctorRow 'E2E (Playwright)' 'RUNS' "browsers installed; $e2eSuite"
  } else {
    Write-DoctorRow 'E2E (Playwright)' 'SKIPPED' "browsers not installed; $e2eSuite"
  }

  Write-Host ''
  if (-not $databaseUrlSet -and -not $dockerDaemon) {
    Write-Info 'Integration tests will skip with a visible warning here. Set TAILOR360_TEST_DATABASE_URL'
    Write-Info '(and TAILOR360_TEST_S3_ENDPOINT for media) to run them, or use a machine with Docker.'
    Write-Info 'CI=true turns that skip into a failure, so the tier is never silently lost on the way to main.'
  }
  if (-not $storageUrlSet -and -not $dockerDaemon) {
    Write-Info 'Object-storage tests have no endpoint here and will skip for the same reason.'
  }
  if (-not $browsers) {
    Write-Info 'Install the E2E browsers with: pnpm --dir clients/pwa exec playwright install chromium'
    Write-Info '(the Playwright CDN must be reachable — see infra/dev-environment/README.md).'
  }

  if ($missingRequired -gt 0) {
    Write-Failure "$missingRequired required tool(s) missing; see docs/dev/setup.md."
    return 1
  }
  return 0
}

# docs/dev/migrations.md documents this verb, and it did not exist: a developer following the
# document met "unknown verb" instead of a migration. It is a thin wrapper over the operator
# command line, which holds one advisory lock for the whole run, so two of these racing is safe.
function Invoke-Migrate {
  param([string[]]$Options = @())
  Assert-Tool 'dotnet'
  Write-Heading 'Migrate'
  Invoke-Step -Command 'dotnet' -CommandArguments (
    @('run', '--project', $CliProject, '--configuration', $DotnetConfiguration, '--', 'migrate') + $Options)
}

function Invoke-Docs {
  # Windows installs the interpreter as `python`; the Microsoft Store shim and most Unix-like
  # setups provide `python3`. Either satisfies the check, so accept whichever is on PATH rather
  # than making the verb unusable on a perfectly good machine.
  $python = @('python3', 'python') | Where-Object { Test-Tool $_ } | Select-Object -First 1
  if (-not $python) {
    Write-Failure 'required tool not found on PATH: python3 (or python)'
    Write-Failure "install it as described in docs/dev/setup.md, then run '.\scripts\dev.ps1 doctor'."
    exit 127
  }
  Write-Heading 'Documentation links'
  # The detector proves itself against known-bad input before it is trusted on the real tree.
  Invoke-Step $python @((Join-Path $RepoRoot 'scripts/check-docs-links.py'), '--self-test')
  Invoke-Step $python @((Join-Path $RepoRoot 'scripts/check-docs-links.py'))
}

# ---------------------------------------------------------------------------------------------
# Dispatch
# ---------------------------------------------------------------------------------------------

try {
  switch ($Verb.ToLowerInvariant()) {
    'up' { Invoke-Up; exit 0 }
    'restore' { Invoke-Restore; exit 0 }
    'build' { Invoke-Build; exit 0 }
    'test' {
      $tier = 'all'
      if ($Arguments.Count -ge 1) { $tier = $Arguments[0].ToLowerInvariant() }
      Invoke-Test -Tier $tier
      exit 0
    }
    'run' { Invoke-Run; exit 0 }
    'reset' { Invoke-Reset -Options $Arguments; exit 0 }
    'status' { exit (Invoke-Status) }
    'doctor' { exit (Invoke-Doctor) }
    'migrate' { Invoke-Migrate -Options $Arguments; exit 0 }
    'docs' { Invoke-Docs; exit 0 }
    { $_ -in @('help', '-h', '--help') } { Show-Usage; exit 0 }
    default {
      Write-Failure "unknown verb: $Verb"
      Show-Usage
      exit 64
    }
  }
} catch {
  Write-Failure $_.Exception.Message
  exit 1
}
