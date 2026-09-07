#!/usr/bin/env python3
"""Fail when a project's line coverage falls below the floor recorded for it.

The tests already run with coverage collection, and until now the reports were uploaded and nobody
looked at them. A number nobody enforces is not a gate: coverage can fall for a year and every build
stays green. This makes the number a build result, per project, so a change that stops covering code
has to say so.

The floors live in `.github/coverage-floors.json` rather than in this file, so that lowering one is
a change to `/.github/` — the path CODEOWNERS puts under review — and so that raising one is a data
edit anybody can make. Each floor was measured, not chosen: the file records what the project
actually achieved when the floor was set, and the floor sits a few points under it to absorb the
ordinary movement of a small change. The floors ratchet upward. When a project runs comfortably
above its floor this script says so, and the floor is meant to be raised to match; nothing here ever
lowers one on its own.

Two things are excluded from the measurement, both because including them would make the number
say something untrue:

* Generated code. Every module's API project carries a 216-line file emitted by the OpenAPI XML
  comment source generator. Counted, those files put eleven projects at 1.8% and the solution at
  54%; ignored, the same projects sit at 80% and the solution at 83%. The generator's output is not
  this repository's code and no test here can or should cover it.
* The test projects themselves. A test assembly's own "coverage" is a measure of how much of the
  test code ran, which is a restatement of which tests ran, not a quality signal.

The four tier reports are read together and a line counts as covered when any tier covered it,
because a single method is routinely exercised by a unit test and an integration test at once.

    scripts/coverage-floor.py artifacts/coverage

Run `--self-test` to prove the detector still detects.
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import os
import sys
import xml.etree.ElementTree as ElementTree

DEFAULT_REPORT_DIRECTORY = "artifacts/coverage"
DEFAULT_CONFIGURATION = os.path.join(".github", "coverage-floors.json")


def load_configuration(path: str) -> dict:
    """Read the floors file."""
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def cobertura_files(paths: list[str]) -> list[str]:
    """Every Cobertura report named directly or found in a named directory."""
    found: list[str] = []
    for path in paths:
        if os.path.isdir(path):
            for directory, _, names in os.walk(path):
                found.extend(
                    os.path.join(directory, name)
                    for name in names
                    if name.endswith(".cobertura.xml") or name.endswith(".xml")
                )
        elif os.path.exists(path):
            found.append(path)
    # A merged report alongside the tier reports would double nothing (the union is idempotent) but
    # would still be read twice, so duplicates are removed for speed, not correctness.
    return sorted(set(found))


def excluded_file(filename: str, patterns: list[str]) -> bool:
    """Whether a source file is one the floor deliberately ignores."""
    normalised = filename.replace("\\", "/")
    return any(fnmatch.fnmatch(normalised, pattern) for pattern in patterns)


def read_reports(reports: list[str], excluded_files: list[str]) -> dict[str, dict[tuple, int]]:
    """The union of every report, as hit counts per project per source line.

    A line counts once no matter how many tiers touched it, and counts as covered when any tier
    covered it. Summing hit counts instead would make a project look better for being tested twice.
    """
    projects: dict[str, dict[tuple, int]] = {}

    for report in reports:
        root = ElementTree.parse(report).getroot()
        for package in root.iter("package"):
            name = package.get("name") or "(unnamed)"
            lines = projects.setdefault(name, {})
            for klass in package.iter("class"):
                filename = klass.get("filename") or ""
                if excluded_file(filename, excluded_files):
                    continue
                container = klass.find("lines")
                if container is None:
                    continue
                for line in container:
                    number = line.get("number")
                    if number is None:
                        continue
                    key = (filename.replace("\\", "/"), int(number))
                    # An uncovered line still has to be recorded, or a project with no coverage at
                    # all would read as fully covered. The key is always written; only the hit
                    # count takes the maximum across tiers.
                    lines[key] = max(lines.get(key, 0), int(line.get("hits") or 0))

    return projects


def measure(projects: dict[str, dict[tuple, int]]) -> dict[str, tuple[int, int]]:
    """Coverable and covered line counts per project."""
    return {
        name: (len(lines), sum(1 for hits in lines.values() if hits > 0))
        for name, lines in projects.items()
    }


def percentage(covered: int, total: int) -> float:
    """Line coverage as a percentage; a project with no coverable lines counts as fully covered."""
    return 100.0 if total == 0 else 100.0 * covered / total


def evaluate(measured: dict[str, tuple[int, int]], configuration: dict) -> tuple[list[dict], dict]:
    """Apply the floors, and report the aggregate."""
    excluded_projects = configuration.get("exclude_projects", [])
    minimum_lines = int(configuration.get("minimum_lines", 50))
    default_floor = float(configuration.get("default_floor", 0.0))
    recorded = configuration.get("projects", {})

    rows: list[dict] = []
    aggregate_total = 0
    aggregate_covered = 0
    ungated = 0

    for name in sorted(measured):
        if any(fnmatch.fnmatch(name, pattern) for pattern in excluded_projects):
            continue

        total, covered = measured[name]
        aggregate_total += total
        aggregate_covered += covered

        entry = recorded.get(name)
        # A project small enough that one line moves it by tens of points is not gated unless the
        # floors file names it: a percentage over five lines is noise, not a measurement.
        if entry is None and total < minimum_lines:
            ungated += 1
            continue

        floor = float(entry["floor"]) if entry else default_floor
        actual = percentage(covered, total)
        rows.append(
            {
                "project": name,
                "total": total,
                "covered": covered,
                "actual": actual,
                "floor": floor,
                "listed": entry is not None,
                "note": (entry or {}).get("note", ""),
                "breach": actual + 1e-9 < floor,
            }
        )

    missing = [name for name in sorted(recorded) if name not in measured]

    aggregate = {
        "total": aggregate_total,
        "covered": aggregate_covered,
        "actual": percentage(aggregate_covered, aggregate_total),
        "floor": float(configuration.get("aggregate_floor", 0.0)),
        "ungated": ungated,
        "missing": missing,
    }
    aggregate["breach"] = aggregate["actual"] + 1e-9 < aggregate["floor"]

    return rows, aggregate


def render(rows: list[dict], aggregate: dict, configuration: dict, reports: list[str]) -> str:
    """The markdown block written to the step summary."""
    slack = float(configuration.get("ratchet_slack", 5.0))
    lines = [
        "## Coverage floor",
        "",
        f"Solution: **{aggregate['actual']:.1f}%** "
        f"({aggregate['covered']}/{aggregate['total']} lines), floor {aggregate['floor']:g}%. "
        f"Generated code and test projects excluded.",
        "",
        "| Project | Lines | Covered | Coverage | Floor | |",
        "| --- | ---: | ---: | ---: | ---: | --- |",
    ]

    for row in sorted(rows, key=lambda r: (r["breach"] is False, r["actual"])):
        if row["breach"]:
            state = "below the floor"
        elif row["actual"] >= row["floor"] + slack:
            state = "raise the floor"
        else:
            state = "ok"
        lines.append(
            f"| `{row['project']}` | {row['total']} | {row['covered']} "
            f"| {row['actual']:.1f}% | {row['floor']:g}% | {state} |"
        )

    if aggregate["ungated"]:
        lines += [
            "",
            f"{aggregate['ungated']} project(s) hold fewer than "
            f"{configuration.get('minimum_lines', 50)} coverable lines and are not gated; they are "
            "module placeholders whose only code is an assembly marker.",
        ]

    if aggregate["missing"]:
        lines += [
            "",
            "**Floors that stopped applying.** These projects are named in "
            "`.github/coverage-floors.json` but produced no coverage data, so their floor gated "
            "nothing on this run:",
            "",
        ]
        lines += [f"- `{name}`" for name in aggregate["missing"]]

    lines += [
        "",
        f"Reports read: {', '.join('`' + os.path.basename(r) + '`' for r in reports)}",
        "",
    ]
    return "\n".join(lines)


def check(paths: list[str], configuration_path: str) -> int:
    """Read the reports, write the summary and decide the build result."""
    configuration = load_configuration(configuration_path)
    reports = cobertura_files(paths)

    if not reports:
        print(
            f"No coverage report found in {', '.join(paths)}. The test tiers were expected to "
            "produce one, so this is treated as an unmeasured build rather than a covered one.",
            file=sys.stderr,
        )
        return 1

    projects = read_reports(reports, configuration.get("exclude_files", []))
    rows, aggregate = evaluate(measure(projects), configuration)

    summary = render(rows, aggregate, configuration, reports)
    print(summary)

    destination = os.environ.get("GITHUB_STEP_SUMMARY")
    if destination:
        with open(destination, "a", encoding="utf-8") as handle:
            handle.write(summary + "\n")

    failed = False

    for row in sorted(rows, key=lambda r: r["actual"]):
        if row["breach"]:
            failed = True
            print(
                f"::error title=Coverage below the floor::{row['project']} is at "
                f"{row['actual']:.1f}% against a floor of {row['floor']:g}%.",
            )

    if aggregate["breach"]:
        failed = True
        print(
            f"::error title=Coverage below the floor::The solution is at "
            f"{aggregate['actual']:.1f}% against a floor of {aggregate['floor']:g}%.",
        )

    # A floor that matches no project is a gate that quietly stopped running, which is the failure
    # this whole check exists to prevent, so it fails rather than warns.
    for name in aggregate["missing"]:
        failed = True
        print(
            f"::error title=Coverage floor not applied::{name} is named in {configuration_path} "
            "but produced no coverage data. Either the project was renamed or removed, in which "
            "case update that file, or its tests stopped running.",
        )

    slack = float(configuration.get("ratchet_slack", 5.0))
    for row in rows:
        if not row["breach"] and row["actual"] >= row["floor"] + slack:
            print(
                f"::notice title=Coverage floor can be raised::{row['project']} is at "
                f"{row['actual']:.1f}% against a floor of {row['floor']:g}%. Raise the floor in "
                f"{configuration_path}.",
            )

    if failed:
        return 1

    print(f"Coverage floor met by {len(rows)} gated project(s).")
    return 0


SELF_TEST_REPORTS = {
    # Two tiers covering different lines of the same file: the union must count both.
    "unit.cobertura.xml": """<?xml version="1.0"?>
<coverage><packages>
  <package name="Gated">
    <classes>
      <class filename="/repo/src/Gated/A.cs">
        <lines>
          <line number="1" hits="1" /><line number="2" hits="0" /><line number="3" hits="0" />
          <line number="4" hits="0" /><line number="5" hits="0" /><line number="6" hits="0" />
        </lines>
      </class>
      <class filename="/repo/src/Gated/obj/Release/G.generated.cs">
        <lines><line number="1" hits="0" /><line number="2" hits="0" /></lines>
      </class>
    </classes>
  </package>
  <package name="Tiny">
    <classes>
      <class filename="/repo/src/Tiny/A.cs"><lines><line number="1" hits="0" /></lines></class>
    </classes>
  </package>
  <package name="Something.Tests">
    <classes>
      <class filename="/repo/tests/T.cs">
        <lines><line number="1" hits="1" /><line number="2" hits="1" /><line number="3" hits="1" />
               <line number="4" hits="1" /><line number="5" hits="1" /><line number="6" hits="1" />
        </lines>
      </class>
    </classes>
  </package>
</packages></coverage>
""",
    "integration.cobertura.xml": """<?xml version="1.0"?>
<coverage><packages>
  <package name="Gated">
    <classes>
      <class filename="/repo/src/Gated/A.cs">
        <lines>
          <line number="1" hits="0" /><line number="2" hits="1" /><line number="3" hits="1" />
          <line number="4" hits="0" /><line number="5" hits="0" /><line number="6" hits="0" />
        </lines>
      </class>
    </classes>
  </package>
</packages></coverage>
""",
}

SELF_TEST_CONFIGURATION = {
    "minimum_lines": 5,
    "default_floor": 40.0,
    "aggregate_floor": 40.0,
    "ratchet_slack": 5.0,
    "exclude_projects": ["*.Tests"],
    "exclude_files": ["*/obj/*", "*.generated.cs", "*.g.cs"],
    "projects": {"Gone": {"floor": 10.0, "note": "removed from the solution"}},
}


def self_test() -> int:
    """Prove the detector still detects, and still ignores what it is meant to ignore."""
    import tempfile

    failures: list[str] = []

    with tempfile.TemporaryDirectory() as directory:
        reports = os.path.join(directory, "coverage")
        os.makedirs(reports)
        for name, body in SELF_TEST_REPORTS.items():
            with open(os.path.join(reports, name), "w", encoding="utf-8") as handle:
                handle.write(body)

        configuration = os.path.join(directory, "floors.json")
        with open(configuration, "w", encoding="utf-8") as handle:
            json.dump(SELF_TEST_CONFIGURATION, handle)

        projects = read_reports(
            cobertura_files([reports]), SELF_TEST_CONFIGURATION["exclude_files"]
        )
        measured = measure(projects)

        # Six coverable lines, three of them covered once the two tiers are unioned, and the
        # generated file ignored entirely.
        if measured.get("Gated") != (6, 3):
            failures.append(f"union or generated-code exclusion changed: {measured.get('Gated')}")

        rows, aggregate = evaluate(measured, SELF_TEST_CONFIGURATION)
        by_name = {row["project"]: row for row in rows}

        if "Something.Tests" in by_name:
            failures.append("a test project is being gated")
        if "Tiny" in by_name:
            failures.append("a project below minimum_lines is being gated")
        if "Gated" not in by_name:
            failures.append("the gated project vanished from the report")
        elif by_name["Gated"]["breach"]:
            failures.append("3 of 6 lines is no longer read as 50% against a floor of 40%")
        if aggregate["missing"] != ["Gone"]:
            failures.append(f"a floor with no project is no longer reported: {aggregate['missing']}")

        environment = os.environ.pop("GITHUB_STEP_SUMMARY", None)
        try:
            # Gated is at 50%, comfortably over the 40% floor, so the only failure must be the
            # floor that matches nothing. Removing it must make the whole check pass.
            if check([reports], configuration) != 1:
                failures.append("a floor that matches no project no longer fails the build")

            relaxed = dict(SELF_TEST_CONFIGURATION, projects={}, default_floor=90.0)
            with open(configuration, "w", encoding="utf-8") as handle:
                json.dump(relaxed, handle)
            if check([reports], configuration) != 1:
                failures.append("a project below its floor no longer fails the build")

            passing = dict(SELF_TEST_CONFIGURATION, projects={}, default_floor=40.0)
            with open(configuration, "w", encoding="utf-8") as handle:
                json.dump(passing, handle)
            if check([reports], configuration) != 0:
                failures.append("a project above its floor now fails the build")

            if check([os.path.join(directory, "absent")], configuration) != 1:
                failures.append("a missing report no longer counts as an unmeasured build")
        finally:
            if environment is not None:
                os.environ["GITHUB_STEP_SUMMARY"] = environment

    if failures:
        print("SELF-TEST FAILED:", file=sys.stderr)
        for failure in failures:
            print(f"  {failure}", file=sys.stderr)
        return 1

    print(
        "self-test passed: tiers unioned, generated code and test projects excluded, "
        "floors enforced, an unmatched floor reported"
    )
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "paths",
        nargs="*",
        default=[DEFAULT_REPORT_DIRECTORY],
        help=f"Cobertura reports, or directories holding them (default {DEFAULT_REPORT_DIRECTORY}).",
    )
    parser.add_argument(
        "--configuration",
        default=DEFAULT_CONFIGURATION,
        help=f"The floors file (default {DEFAULT_CONFIGURATION}).",
    )
    parser.add_argument("--self-test", action="store_true", help="Prove the detector still detects.")
    arguments = parser.parse_args()

    if arguments.self_test:
        return self_test()

    return check(arguments.paths or [DEFAULT_REPORT_DIRECTORY], arguments.configuration)


if __name__ == "__main__":
    sys.exit(main())
