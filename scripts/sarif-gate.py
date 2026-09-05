#!/usr/bin/env python3
"""Turn a SARIF report into a visible job summary and a build result.

Static analysers normally publish their findings to the code-scanning alerts page. That page needs
GitHub Advanced Security, which a private repository owned by a personal account cannot have, so on
this repository the SARIF file is often all there is. A SARIF file nobody reads is the same thing as
a security check nobody ran, and both look exactly like a green build.

This script closes that gap. It reads every finding out of one or more SARIF files, writes them into
the step summary where a reviewer sees them without opening the raw log, and exits non-zero when a
finding is at or above the severity the caller decided to gate on. It works the same whether or not
the alerts page is available, so the scan gates the build either way.

Severity is read from `security-severity` on the rule, which CodeQL and Trivy both emit on the
CVSS-like 0-10 scale. When a rule carries no such property the SARIF level is mapped onto the same
scale, so a report from a tool that omits it is still gated rather than silently passing.

    scripts/sarif-gate.py artifacts/security/trivy-config.sarif --label "Trivy: infrastructure" \
        --fail-on-severity 9.0

Run `--self-test` to prove the detector still detects.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

# SARIF only records four levels. A rule that carries no `security-severity` is mapped onto the same
# 0-10 scale the property uses, so one threshold covers both kinds of report. `error` maps to 8.0
# (high) rather than to 10.0 because a tool that reports an error without a severity has not claimed
# the finding is critical, and reading one into it would make the gate stricter than the tool meant.
LEVEL_SEVERITY = {"error": 8.0, "warning": 5.5, "note": 2.0, "none": 0.0}

# The bands GitHub uses for `security-severity`, reused so that the summary reads the same way the
# alerts page would have.
SEVERITY_BANDS = ((9.0, "critical"), (7.0, "high"), (4.0, "medium"), (0.1, "low"))

# A long report is truncated in the summary; the SARIF artefact carries the rest.
MAX_DETAIL_ROWS = 25


def band(severity: float) -> str:
    """The name of the severity band a score falls in."""
    for threshold, name in SEVERITY_BANDS:
        if severity >= threshold:
            return name
    return "none"


def sarif_files(paths: list[str]) -> list[str]:
    """Every SARIF file named directly or found in a named directory."""
    found: list[str] = []
    for path in paths:
        if os.path.isdir(path):
            for directory, _, names in os.walk(path):
                found.extend(
                    os.path.join(directory, name)
                    for name in names
                    if name.endswith((".sarif", ".sarif.json"))
                )
        elif os.path.exists(path):
            found.append(path)
    return sorted(set(found))


def rule_index(run: dict) -> dict[str, dict]:
    """Every rule the run declares, by identifier.

    Rules live on the driver and, for CodeQL, also on the extensions that contributed the query
    packs. Missing an extension's rules would lose the severity of every finding it raised.
    """
    tool = run.get("tool") or {}
    components = [tool.get("driver") or {}, *(tool.get("extensions") or [])]
    rules: dict[str, dict] = {}
    for component in components:
        for rule in component.get("rules") or []:
            identifier = rule.get("id")
            if identifier:
                rules.setdefault(identifier, rule)
    return rules


def positional_rules(run: dict) -> list[dict]:
    """The driver's rules in declaration order, for results that carry only a `ruleIndex`."""
    return list(((run.get("tool") or {}).get("driver") or {}).get("rules") or [])


def result_severity(result: dict, rule: dict) -> tuple[float, str]:
    """The severity of one finding, and the SARIF level it was reported at."""
    level = result.get("level")
    if not level:
        level = ((rule.get("defaultConfiguration") or {}).get("level")) or "warning"

    raw = (rule.get("properties") or {}).get("security-severity")
    if raw is not None:
        try:
            return float(raw), level
        except (TypeError, ValueError):
            pass

    return LEVEL_SEVERITY.get(level, 5.5), level


def first_location(result: dict) -> str:
    """Where the finding is, as `path:line`, or an empty string when the tool did not say."""
    for location in result.get("locations") or []:
        physical = location.get("physicalLocation") or {}
        uri = (physical.get("artifactLocation") or {}).get("uri")
        if not uri:
            continue
        line = (physical.get("region") or {}).get("startLine")
        return f"{uri}:{line}" if line else uri
    return ""


def message(result: dict) -> str:
    """The finding's message, flattened to one line."""
    text = (result.get("message") or {}).get("text") or ""
    return " ".join(text.split())


def read_findings(path: str) -> tuple[list[dict], list[str]]:
    """Every finding in one SARIF file, and the names of the tools that produced them."""
    with open(path, encoding="utf-8-sig") as handle:
        document = json.load(handle)

    findings: list[dict] = []
    tools: list[str] = []

    for run in document.get("runs") or []:
        driver = (run.get("tool") or {}).get("driver") or {}
        name = driver.get("name") or "unknown tool"
        if name not in tools:
            tools.append(name)

        by_id = rule_index(run)
        by_position = positional_rules(run)

        for result in run.get("results") or []:
            # A `pass`, `informational` or `notApplicable` result is the tool reporting that it
            # looked and found nothing; only a `fail` is a finding. `kind` defaults to `fail`.
            if result.get("kind", "fail") != "fail":
                continue
            # A suppression is a decision somebody already recorded. Counting it again would make
            # the waiver process pointless.
            if result.get("suppressions"):
                continue

            identifier = result.get("ruleId") or (result.get("rule") or {}).get("id")
            rule = by_id.get(identifier or "", {})
            if not rule:
                index = result.get("ruleIndex")
                if isinstance(index, int) and 0 <= index < len(by_position):
                    rule = by_position[index]
                    identifier = identifier or rule.get("id")

            severity, level = result_severity(result, rule)
            findings.append(
                {
                    "tool": name,
                    "rule": identifier or "(no rule id)",
                    "severity": severity,
                    "level": level,
                    "location": first_location(result),
                    "message": message(result),
                }
            )

    return findings, tools


def render(label: str, findings: list[dict], threshold: float, sources: list[str]) -> str:
    """The markdown block written to the step summary."""
    lines = [f"### {label}", ""]

    if not findings:
        lines += [
            f"No findings. Gate: anything at severity {threshold:g} or above fails the build.",
            "",
        ]
        return "\n".join(lines)

    counts: dict[str, int] = {}
    for finding in findings:
        name = band(finding["severity"])
        counts[name] = counts.get(name, 0) + 1
    tally = ", ".join(
        f"{counts[name]} {name}" for _, name in SEVERITY_BANDS if counts.get(name)
    )

    lines += [
        f"{len(findings)} finding(s): {tally}.",
        f"Gate: anything at severity {threshold:g} or above fails the build.",
        "",
        "| Severity | Rule | Location | Finding |",
        "| --- | --- | --- | --- |",
    ]

    ordered = sorted(findings, key=lambda f: (-f["severity"], f["rule"], f["location"]))
    for finding in ordered[:MAX_DETAIL_ROWS]:
        # A pipe inside a message would end the table cell early.
        text = (finding["message"][:160] or "-").replace("|", "\\|")
        location = finding["location"] or "-"
        lines.append(
            f"| {band(finding['severity'])} ({finding['severity']:g}) "
            f"| `{finding['rule']}` "
            f"| `{location}` "
            f"| {text} |"
        )

    if len(ordered) > MAX_DETAIL_ROWS:
        lines.append(
            f"| … | _{len(ordered) - MAX_DETAIL_ROWS} more_ | | _see the uploaded SARIF artefact_ |"
        )

    lines += ["", f"Reports read: {', '.join(f'`{os.path.basename(s)}`' for s in sources)}", ""]
    return "\n".join(lines)


def gate(paths: list[str], label: str, threshold: float, allow_empty: bool) -> int:
    """Read the reports, write the summary and decide the build result."""
    reports = sarif_files(paths)

    if not reports:
        if allow_empty:
            print(f"{label}: no SARIF report found; nothing to gate.")
            return 0
        print(
            f"{label}: no SARIF report found in {', '.join(paths)}. The scan was expected to "
            "produce one, so this is treated as a failed scan rather than a clean one.",
            file=sys.stderr,
        )
        return 1

    findings: list[dict] = []
    for report in reports:
        try:
            found, _ = read_findings(report)
        except (OSError, ValueError) as error:
            print(f"{label}: {report} could not be read as SARIF: {error}", file=sys.stderr)
            return 1
        findings.extend(found)

    summary = render(label, findings, threshold, reports)
    print(summary)

    destination = os.environ.get("GITHUB_STEP_SUMMARY")
    if destination:
        with open(destination, "a", encoding="utf-8") as handle:
            handle.write(summary + "\n")

    breaching = [f for f in findings if f["severity"] >= threshold]
    if breaching:
        print(
            f"::error title={label}::{len(breaching)} finding(s) at severity {threshold:g} or "
            "above. Fix them, or record a waiver under docs/process/waivers.md.",
        )
        for finding in sorted(breaching, key=lambda f: -f["severity"])[:MAX_DETAIL_ROWS]:
            print(f"  {finding['rule']} {finding['location']}: {finding['message']}", file=sys.stderr)
        return 1

    print(f"{label}: no finding at severity {threshold:g} or above.")
    return 0


SELF_TEST_REPORT = {
    "version": "2.1.0",
    "runs": [
        {
            "tool": {
                "driver": {
                    "name": "self-test",
                    "rules": [
                        {"id": "CRIT-1", "properties": {"security-severity": "9.8"}},
                        {"id": "LOW-1", "properties": {"security-severity": "2.0"}},
                        {"id": "NO-SEVERITY", "defaultConfiguration": {"level": "error"}},
                        {"id": "SUPPRESSED", "properties": {"security-severity": "9.9"}},
                        {"id": "PASSING", "properties": {"security-severity": "9.9"}},
                    ],
                }
            },
            "results": [
                {
                    "ruleId": "CRIT-1",
                    "message": {"text": "critical finding"},
                    "locations": [
                        {
                            "physicalLocation": {
                                "artifactLocation": {"uri": "a.cs"},
                                "region": {"startLine": 7},
                            }
                        }
                    ],
                },
                {"ruleId": "LOW-1", "message": {"text": "low finding"}},
                # No security-severity: the level must still be mapped onto the scale and gated.
                {"ruleId": "NO-SEVERITY", "message": {"text": "error without a severity"}},
                # Already waived, so it must not be counted twice.
                {
                    "ruleId": "SUPPRESSED",
                    "message": {"text": "waived"},
                    "suppressions": [{"kind": "external"}],
                },
                # The tool reporting that it looked and found nothing.
                {"ruleId": "PASSING", "kind": "pass", "message": {"text": "clean"}},
                # Only a rule index, which is how some tools reference their rules.
                {"ruleIndex": 1, "message": {"text": "another low finding"}},
            ],
        }
    ],
}

SELF_TEST_EXPECTED = {
    ("CRIT-1", 9.8, "a.cs:7"),
    ("LOW-1", 2.0, ""),
    ("NO-SEVERITY", 8.0, ""),
    ("LOW-1", 2.0, ""),
}


def self_test() -> int:
    """Prove the reader still reads, and the gate still gates."""
    import tempfile

    failures: list[str] = []

    with tempfile.TemporaryDirectory() as directory:
        report = os.path.join(directory, "report.sarif")
        with open(report, "w", encoding="utf-8") as handle:
            json.dump(SELF_TEST_REPORT, handle)

        findings, tools = read_findings(report)
        seen = {(f["rule"], f["severity"], f["location"]) for f in findings}

        if len(findings) != 4:
            failures.append(f"expected 4 findings, read {len(findings)}")
        if seen != SELF_TEST_EXPECTED:
            failures.append(f"findings changed: {sorted(seen)}")
        if tools != ["self-test"]:
            failures.append(f"tool name lost: {tools}")

        environment = os.environ.pop("GITHUB_STEP_SUMMARY", None)
        try:
            if gate([report], "self-test", 9.0, allow_empty=False) != 1:
                failures.append("a critical finding no longer fails the gate")
            if gate([report], "self-test", 9.9, allow_empty=False) != 0:
                failures.append("a finding below the threshold now fails the gate")
            if gate([os.path.join(directory, "absent")], "self-test", 9.0, allow_empty=False) != 1:
                failures.append("a missing report no longer counts as a failed scan")
            if gate([os.path.join(directory, "absent")], "self-test", 9.0, allow_empty=True) != 0:
                failures.append("--allow-empty no longer tolerates a missing report")
        finally:
            if environment is not None:
                os.environ["GITHUB_STEP_SUMMARY"] = environment

    if failures:
        print("SELF-TEST FAILED:", file=sys.stderr)
        for failure in failures:
            print(f"  {failure}", file=sys.stderr)
        return 1

    print("self-test passed: severities read, suppressed and passing results ignored, gate enforced")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("paths", nargs="*", help="SARIF files, or directories holding them.")
    parser.add_argument("--label", default="Static analysis", help="Heading for the summary.")
    parser.add_argument(
        "--fail-on-severity",
        type=float,
        default=7.0,
        help="Fail at or above this severity on the 0-10 scale (default 7.0, high).",
    )
    parser.add_argument(
        "--allow-empty",
        action="store_true",
        help="Treat a missing report as nothing to gate rather than as a failed scan.",
    )
    parser.add_argument("--self-test", action="store_true", help="Prove the detector still detects.")
    arguments = parser.parse_args()

    if arguments.self_test:
        return self_test()
    if not arguments.paths:
        parser.error("give at least one SARIF file or directory")

    return gate(arguments.paths, arguments.label, arguments.fail_on_severity, arguments.allow_empty)


if __name__ == "__main__":
    sys.exit(main())
