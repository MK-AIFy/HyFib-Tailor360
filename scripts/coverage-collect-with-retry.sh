#!/usr/bin/env bash
#
# HyFib Tailor360 — retry wrapper for `dotnet-coverage collect` (CI job ".NET build and tests").
#
# `dotnet-coverage collect`'s instrumented host has an intermittent native crash on the Linux
# runner: exit code 139 (SIGSEGV), observed after the wrapped `dotnet test` has already reported
# every test passed (runs 34763655620 and 34750465498 each lost a different tier this way — the
# Unit tier once, the Architecture tier the other time — so it is the coverage collector's exit,
# not a project's tests, that is flaky). A crash at that point loses the tier's coverage output,
# which is why an unrelated pull request can see the per-project coverage floor fail even though
# nothing it touched lost coverage.
#
# This script retries the wrapped command only on exit 139, and only a bounded number of times: a
# real test failure exits through the Microsoft.Testing.Platform runner with a different code and
# fails on the first attempt, same as before this script existed.
#
# Usage: scripts/coverage-collect-with-retry.sh <dotnet-coverage collect ... -- dotnet test ...>

set -uo pipefail

readonly max_attempts=3
attempt=1

while true; do
  "$@"
  status=$?

  if [ "$status" -eq 0 ]; then
    exit 0
  fi

  if [ "$status" -ne 139 ] || [ "$attempt" -ge "$max_attempts" ]; then
    exit "$status"
  fi

  echo "::warning::dotnet-coverage collect exited 139 (attempt $attempt of $max_attempts) — retrying; this is the known instrumented-host segfault, not a failing test." >&2
  attempt=$((attempt + 1))
done
