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


def load_accepted(path: str | None, scope: str | None) -> list[dict]:
    """
    The entries of the register that apply to this scan, or an empty list when there is none.

    A scan is identified by `scope` — the CodeQL language, or the name of a Trivy report. Entries
    belonging to another scan are dropped here rather than carried through and reported as stale,
    because the stale check exists to catch an entry whose finding has changed, and an entry that was
    never going to appear in *this* report has not changed at all. Getting that wrong turns one
    register shared by two scans into a build that can never be green: the C# entries would match
    nothing on the JavaScript run and fail it, which is exactly what happened when this was first
    wired into a matrix job.
    """
    if not path:
        return []

    with open(path, encoding="utf-8-sig") as handle:
        document = json.load(handle)

    entries = document.get("accepted") or []
    for index, entry in enumerate(entries):
        for field in ("scope", "rule", "path", "message_contains", "reason", "reviewed"):
            if not str(entry.get(field) or "").strip():
                raise ValueError(
                    f"accepted[{index}] has no {field}. Every entry names the scan it belongs to, "
                    "the rule, the file, a fragment of the message, the reason it is not a defect, "
                    "and the date somebody decided that."
                )

    if scope is None:
        raise ValueError(
            "--accepted needs --scope, so that an entry is only applied to the scan it was written "
            "for. Without it a register shared by two scans fails whichever one it does not describe."
        )

    return [entry for entry in entries if entry["scope"] == scope]


def accepts(entry: dict, finding: dict) -> bool:
    """
    Whether one register entry covers one finding.

    Deliberately matched on rule, file and a fragment of the message rather than on a line number: a
    line moves with the next edit above it, and an entry that silently stopped matching would turn a
    recorded acceptance into an unrecorded one. Equally deliberately, it is not matched on the file
    alone — a different finding of the same rule in the same file is a different finding, and has to
    be read on its own.
    """
    location = finding["location"].rsplit(":", 1)[0] if ":" in finding["location"] else finding["location"]
    return (
        finding["rule"] == entry["rule"]
        and location.replace("\\", "/").endswith(entry["path"])
        and entry["message_contains"] in finding["message"]
    )


def partition_accepted(
    findings: list[dict], accepted: list[dict]
) -> tuple[list[dict], list[dict], list[dict]]:
    """Split the findings into those still gated, those accepted, and the entries that matched none."""
    gated: list[dict] = []
    covered: list[dict] = []
    matched: set[int] = set()

    for finding in findings:
        for index, entry in enumerate(accepted):
            if accepts(entry, finding):
                matched.add(index)
                covered.append({**finding, "accepted": entry})
                break
        else:
            gated.append(finding)

    stale = [entry for index, entry in enumerate(accepted) if index not in matched]
    return gated, covered, stale


def render(
    label: str,
    findings: list[dict],
    threshold: float,
    sources: list[str],
    accepted: list[dict] | None = None,
) -> str:
    """The markdown block written to the step summary."""
    accepted = accepted or []
    lines = [f"### {label}", ""]

    if not findings:
        lines += [
            f"No findings. Gate: anything at severity {threshold:g} or above fails the build.",
            "",
        ]
        lines += accepted_rows(accepted)
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

    lines += accepted_rows(accepted)
    lines += ["", f"Reports read: {', '.join(f'`{os.path.basename(s)}`' for s in sources)}", ""]
    return "\n".join(lines)


def accepted_rows(accepted: list[dict]) -> list[str]:
    """The accepted findings, listed so that a reviewer sees what the gate did not stop on."""
    if not accepted:
        return []

    lines = [
        "",
        f"{len(accepted)} finding(s) read and accepted as not defects. They do not gate the build; "
        "the reason for each is in `.github/sarif-accepted.json`.",
        "",
        "| Rule | Location | Why it is not a defect |",
        "| --- | --- | --- |",
    ]
    for finding in accepted:
        reason = finding["accepted"]["reason"].replace("|", "\\|")
        lines.append(
            f"| `{finding['rule']}` | `{finding['location'] or '-'}` | {reason} |"
        )
    return lines


def gate(
    paths: list[str],
    label: str,
    threshold: float,
    allow_empty: bool,
    accepted_path: str | None = None,
    scope: str | None = None,
) -> int:
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

    try:
        accepted = load_accepted(accepted_path, scope)
    except (OSError, ValueError) as error:
        print(f"{label}: {accepted_path} could not be read: {error}", file=sys.stderr)
        return 1

    findings, covered, stale = partition_accepted(findings, accepted)

    # Accepted findings stay in the summary. Removing them would make the register a way to stop
    # seeing something rather than a way to record having looked at it.
    summary = render(label, findings, threshold, reports, covered)
    print(summary)

    destination = os.environ.get("GITHUB_STEP_SUMMARY")
    if destination:
        with open(destination, "a", encoding="utf-8") as handle:
            handle.write(summary + "\n")

    if stale:
        # An entry that matches nothing is the dangerous state: the finding it described has moved or
        # changed, so the register is now accepting something nobody read. Same reasoning as the
        # unmatched floor in scripts/coverage-floor.py.
        print(
            f"::error title={label}::{len(stale)} accepted finding(s) matched nothing in this "
            f"report. Either the finding is fixed, in which case remove the entry from "
            f"{accepted_path}, or it changed and needs reading again.",
        )
        for entry in stale:
            print(f"  {entry['rule']} {entry['path']}: {entry['message_contains']}", file=sys.stderr)
        return 1

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

            failures.extend(self_test_accepted(directory, report))
        finally:
            if environment is not None:
                os.environ["GITHUB_STEP_SUMMARY"] = environment

    if failures:
        print("SELF-TEST FAILED:", file=sys.stderr)
        for failure in failures:
            print(f"  {failure}", file=sys.stderr)
        return 1

    print(
        "self-test passed: severities read, suppressed and passing results ignored, gate enforced, "
        "accepted findings honoured one at a time and a stale acceptance caught"
    )
    return 0


def self_test_accepted(directory: str, report: str) -> list[str]:
    """
    Prove the acceptance register accepts exactly what it names, and nothing else.

    The three failures worth guarding against are the three ways a register like this rots: it stops
    covering the finding it was written for, it starts covering a finding nobody read, or an entry is
    added with no reason and nobody notices.
    """
    failures: list[str] = []
    critical = next(iter(SELF_TEST_EXPECTED))

    def register(path: str, entries: list[dict]) -> str:
        with open(path, "w", encoding="utf-8") as handle:
            json.dump({"accepted": entries}, handle)
        return path

    findings, _ = read_findings(report)
    gated = next(f for f in findings if f["severity"] >= 9.0)
    entry = {
        "scope": "self-test-scan",
        "rule": gated["rule"],
        "path": gated["location"].rsplit(":", 1)[0],
        "message_contains": gated["message"][:20],
        "reason": "A self-test fixture, not a real finding.",
        "reviewed": "2026-09-07",
    }
    matching = register(os.path.join(directory, "accepted.json"), [entry])
    if gate([report], "self-test", 9.0, allow_empty=False, accepted_path=matching,
            scope="self-test-scan") != 0:
        failures.append("an accepted finding still fails the gate")

    # The regression this scope exists for: one register, two scans. An entry written for another
    # scan is neither applied here nor counted stale here, so the run it does not describe still
    # passes on its own findings.
    other = register(
        os.path.join(directory, "other-scan.json"),
        [{**entry, "scope": "a-different-scan"}],
    )
    if gate([report], "self-test", 9.9, allow_empty=False, accepted_path=other,
            scope="self-test-scan") != 0:
        failures.append("an entry for another scan is treated as stale in this one")
    if gate([report], "self-test", 9.0, allow_empty=False, accepted_path=other,
            scope="self-test-scan") != 1:
        failures.append("an entry for another scan is applied in this one")

    # Gated at 9.9 again, above every finding, so the missing --scope is the only thing that can
    # fail this run. At 9.0 it would fail whether or not the guard existed, because no entry would
    # be applied and the critical finding would still be gated — a pass for the wrong reason.
    if gate([report], "self-test", 9.9, allow_empty=False, accepted_path=matching, scope=None) != 1:
        failures.append("--accepted without --scope no longer refuses to run")

    # The same rule and the same file, a different message: a different finding, still gated.
    narrow = register(
        os.path.join(directory, "narrow.json"),
        [{**entry, "message_contains": "a message this finding does not carry"}],
    )
    if gate([report], "self-test", 9.0, allow_empty=False, accepted_path=narrow,
            scope="self-test-scan") != 1:
        failures.append("the register matches on more than the finding it names")

    # Nothing in the report matches: the entry is stale and the build has to say so.
    #
    # Gated at 9.9, above every finding in the fixture, so the run would pass on its findings alone —
    # which the threshold case above already asserts. The stale entry is therefore the only thing that
    # can fail it, and a stale check that stopped working would show up here as a green run rather
    # than being masked by a finding that was failing anyway.
    stale = register(
        os.path.join(directory, "stale.json"),
        [{**entry, "rule": "self-test/rule-that-fired-nowhere"}],
    )
    if gate([report], "self-test", 9.9, allow_empty=False, accepted_path=stale,
            scope="self-test-scan") != 1:
        failures.append("an acceptance that matches nothing no longer fails the build")

    for field in ("scope", "rule", "path", "message_contains", "reason", "reviewed"):
        incomplete = register(
            os.path.join(directory, f"no-{field}.json"), [{**entry, field: "  "}]
        )
        if gate([report], "self-test", 9.0, allow_empty=False, accepted_path=incomplete,
                scope="self-test-scan") != 1:
            failures.append(f"an acceptance with no {field} is accepted")

    if critical is None:  # pragma: no cover - keeps the fixture referenced and honest
        failures.append("the self-test fixture no longer holds a critical finding")

    return failures


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
    parser.add_argument(
        "--accepted",
        default=None,
        help="A register of findings already read and accepted as not defects.",
    )
    parser.add_argument(
        "--scope",
        default=None,
        help="Which scan this run is, so --accepted applies only the entries written for it.",
    )
    parser.add_argument("--self-test", action="store_true", help="Prove the detector still detects.")
    arguments = parser.parse_args()

    if arguments.self_test:
        return self_test()
    if not arguments.paths:
        parser.error("give at least one SARIF file or directory")

    return gate(
        arguments.paths,
        arguments.label,
        arguments.fail_on_severity,
        arguments.allow_empty,
        arguments.accepted,
        arguments.scope,
    )


if __name__ == "__main__":
    sys.exit(main())
