#!/usr/bin/env bash
#
# HyFib Tailor360 — turn a directory of TRX reports into a job-summary table.
#
# Extracted from the `.NET build and tests` job when the Integration tier was sharded across three
# jobs (issue #495). Three jobs now need this: `build-test-dotnet` for its three tiers, each
# `test-integration` shard for its own slice, and `coverage-floor`, which downloads every one of
# them and renders the single combined table a reader actually wants. Forty-five lines of shell
# maintained in three copies would have drifted apart by the second edit.
#
# The TRX counters are the run's own record, so the summary cannot disagree with it. `notExecuted`
# is how TRX records a skipped test — which matters here more than it looks, because a skipped
# Integration test is the failure mode `CI=true` exists to convert into a red build.
#
# Usage: scripts/summarise-test-tiers.sh <results-directory> [heading]
#
# Exits non-zero only on misuse. An empty directory is reported in the table rather than treated as
# an error: whether a missing report is a defect is a question for the caller, and `coverage-floor`
# answers it explicitly by asserting which reports it expected.

set -euo pipefail

if [ "$#" -lt 1 ]; then
  echo "usage: $0 <results-directory> [heading]" >&2
  exit 2
fi

readonly directory="$1"
readonly heading="${2:-.NET test tiers}"

# Without a summary file to write to — running this locally, for instance — the table goes to
# standard output, so the script is useful outside a runner rather than silently doing nothing.
summary="${GITHUB_STEP_SUMMARY:-/dev/stdout}"

{
  echo "## ${heading}"
  echo
  echo "| Tier | Total | Passed | Failed | Skipped |"
  echo "| --- | ---: | ---: | ---: | ---: |"
} >> "$summary"

shopt -s nullglob
reports=("$directory"/*.trx)

if [ ${#reports[@]} -eq 0 ]; then
  echo "| _no test report was produced_ | - | - | - | - |" >> "$summary"
  exit 0
fi

for report in "${reports[@]}"; do
  tier=$(basename "$report" .trx)
  counters=$(grep -o '<Counters[^>]*>' "$report" | head -n 1 || true)
  value() { printf '%s' "$counters" | sed -n "s/.*$1=\"\([0-9]*\)\".*/\1/p"; }
  printf '| %s | %s | %s | %s | %s |\n' \
    "$tier" "$(value total)" "$(value passed)" "$(value failed)" "$(value notExecuted)" \
    >> "$summary"
done

# Failing test names save a trip into the raw log. Printed to the job's own console output as well
# as the step summary: the summary is not reachable from the API a reviewing agent has, so a name
# that exists only there is, in practice, unrecoverable.
failures=$(grep -ho 'testName="[^"]*" outcome="Failed"' "${reports[@]}" \
  | sed 's/testName="//; s/" outcome="Failed"//' | sort -u || true)

if [ -n "$failures" ]; then
  {
    echo
    echo "### Failed tests"
    echo
    printf '%s\n' "$failures" | sed 's/^/- `/; s/$/`/'
  } >> "$summary"

  echo "Failed tests:"
  printf '%s\n' "$failures" | sed 's/^/  - /'
fi
