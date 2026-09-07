#!/usr/bin/env python3
"""Classify the difference between two OpenAPI documents and fail on an unapproved breaking change.

The taxonomy is the one fixed by docs/architecture/conventions.md section 5.2: inside a major version
only additive changes are permitted, and everything else is breaking. This script is the mechanical
enforcement of that table on every pull request, comparing the committed document on the base branch
with the one in the branch under review.

Why this and not oasdiff, which the plan names? Two reasons, both about the gate rather than about the
tool. The first is that a gate that needs a Go toolchain and a package proxy is a gate that is skipped
the first time either is unavailable, and a skipped contract gate is how a breaking change reaches a
client. The second is that the rules here are *this repository's* rules: `oasdiff` cannot know that a
new value in a response enum is breaking for us, that an operation identifier is a client's method name,
or that an approved break needs a row in docs/api/breaking-changes.md. Running oasdiff as well would add
a second opinion with no authority to it. What is borrowed from oasdiff is its shape: compare two
documents, classify, report, exit non-zero.

  scripts/openapi-diff.py --base <old.json> --head <new.json> [--approved] [--register FILE] [--pr N]
  scripts/openapi-diff.py --self-test

`--self-test` runs the classifier over documents whose answers are known, so that a detector that has
quietly stopped detecting fails loudly rather than reporting a clean diff. It is the same reasoning as
scripts/coverage-floor.py --self-test.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

REQUEST = "request"
RESPONSE = "response"

# How deep a schema comparison recurses before it stops. A payload nested more deeply than this is a
# design problem in its own right; the limit is here so that a mutually recursive pair of schemas
# cannot hang the gate.
MAX_DEPTH = 24


class Change:
    """One difference between the two documents."""

    def __init__(self, breaking: bool, rule: str, where: str, detail: str) -> None:
        self.breaking = breaking
        self.rule = rule
        self.where = where
        self.detail = detail

    def __str__(self) -> str:
        mark = "BREAKING" if self.breaking else "additive"
        return f"  [{mark}] {self.rule} at {self.where}: {self.detail}"


# --------------------------------------------------------------------------------------------------
# Reading


def resolve(document: dict[str, Any], node: Any) -> Any:
    """Follow a local $ref to the component it names."""
    seen = 0
    while isinstance(node, dict) and "$ref" in node:
        seen += 1
        if seen > MAX_DEPTH:
            return {}
        pointer = node["$ref"]
        if not pointer.startswith("#/"):
            return {}
        target: Any = document
        for segment in pointer[2:].split("/"):
            segment = segment.replace("~1", "/").replace("~0", "~")
            if not isinstance(target, dict) or segment not in target:
                return {}
            target = target[segment]
        node = target
    return node if node is not None else {}


def operations(document: dict[str, Any]) -> dict[str, dict[str, Any]]:
    """Every operation, keyed 'METHOD path'."""
    methods = ("get", "put", "post", "delete", "options", "head", "patch", "trace")
    found: dict[str, dict[str, Any]] = {}
    for path, item in (document.get("paths") or {}).items():
        if not isinstance(item, dict):
            continue
        for method, operation in item.items():
            if method.lower() in methods and isinstance(operation, dict):
                found[f"{method.upper()} {path}"] = operation
    return found


def as_set(value: Any) -> set[str]:
    if value is None:
        return set()
    if isinstance(value, list):
        return {str(item) for item in value}
    return {str(value)}


# --------------------------------------------------------------------------------------------------
# Comparing


def compare(base: dict[str, Any], head: dict[str, Any]) -> list[Change]:
    """Classify every difference between two documents."""
    changes: list[Change] = []

    before = operations(base)
    after = operations(head)

    for key in sorted(set(before) - set(after)):
        changes.append(Change(True, "operation-removed", key, "the operation is gone"))

    for key in sorted(set(after) - set(before)):
        changes.append(Change(False, "operation-added", key, "a new operation"))

    for key in sorted(set(before) & set(after)):
        changes.extend(compare_operation(base, head, key, before[key], after[key]))

    return changes


def compare_operation(
    base_doc: dict[str, Any],
    head_doc: dict[str, Any],
    where: str,
    before: dict[str, Any],
    after: dict[str, Any],
) -> list[Change]:
    changes: list[Change] = []

    if before.get("operationId") != after.get("operationId"):
        changes.append(
            Change(
                True,
                "operation-id-changed",
                where,
                f"{before.get('operationId')!r} became {after.get('operationId')!r}; a generated "
                "client names its method after it",
            )
        )

    if after.get("deprecated") and not before.get("deprecated"):
        changes.append(Change(False, "operation-deprecated", where, "marked deprecated"))

    changes.extend(compare_security(where, before, after))
    changes.extend(compare_parameters(base_doc, head_doc, where, before, after))
    changes.extend(compare_request_body(base_doc, head_doc, where, before, after))
    changes.extend(compare_responses(base_doc, head_doc, where, before, after))

    return changes


def security_shape(operation: dict[str, Any]) -> set[str]:
    shape: set[str] = set()
    for requirement in operation.get("security") or []:
        if isinstance(requirement, dict):
            shape.add("+".join(sorted(requirement)))
    return shape


def compare_security(where: str, before: dict[str, Any], after: dict[str, Any]) -> list[Change]:
    was, now = security_shape(before), security_shape(after)
    changes: list[Change] = []

    for added in sorted(now - was):
        changes.append(
            Change(
                True,
                "security-added",
                where,
                f"the operation now also accepts or demands {added or 'nothing'}; a client that was "
                "working may no longer be",
            )
        )

    for removed in sorted(was - now):
        changes.append(
            Change(False, "security-removed", where, f"no longer requires {removed or 'nothing'}")
        )

    return changes


def parameters(document: dict[str, Any], operation: dict[str, Any]) -> dict[str, dict[str, Any]]:
    found: dict[str, dict[str, Any]] = {}
    for parameter in operation.get("parameters") or []:
        parameter = resolve(document, parameter)
        if isinstance(parameter, dict) and "name" in parameter:
            found[f"{parameter.get('in', '?')}:{parameter['name']}"] = parameter
    return found


def compare_parameters(
    base_doc: dict[str, Any],
    head_doc: dict[str, Any],
    where: str,
    before: dict[str, Any],
    after: dict[str, Any],
) -> list[Change]:
    was = parameters(base_doc, before)
    now = parameters(head_doc, after)
    changes: list[Change] = []

    for name in sorted(set(was) - set(now)):
        changes.append(Change(True, "parameter-removed", f"{where} {name}", "the parameter is gone"))

    for name in sorted(set(now) - set(was)):
        required = bool(now[name].get("required"))
        changes.append(
            Change(
                required,
                "parameter-added",
                f"{where} {name}",
                "a new required parameter" if required else "a new optional parameter",
            )
        )

    for name in sorted(set(was) & set(now)):
        if bool(now[name].get("required")) and not bool(was[name].get("required")):
            changes.append(
                Change(True, "parameter-now-required", f"{where} {name}", "was optional")
            )
        changes.extend(
            compare_schema(
                base_doc,
                head_doc,
                f"{where} {name}",
                was[name].get("schema"),
                now[name].get("schema"),
                REQUEST,
                0,
            )
        )

    return changes


def content(document: dict[str, Any], holder: Any) -> dict[str, Any]:
    holder = resolve(document, holder)
    return holder.get("content") or {} if isinstance(holder, dict) else {}


def compare_request_body(
    base_doc: dict[str, Any],
    head_doc: dict[str, Any],
    where: str,
    before: dict[str, Any],
    after: dict[str, Any],
) -> list[Change]:
    was = content(base_doc, before.get("requestBody"))
    now = content(head_doc, after.get("requestBody"))
    changes: list[Change] = []

    for media in sorted(set(was) - set(now)):
        changes.append(
            Change(True, "request-media-removed", f"{where} {media}", "the media type is no longer accepted")
        )

    was_required = bool(resolve(base_doc, before.get("requestBody") or {}).get("required"))
    now_required = bool(resolve(head_doc, after.get("requestBody") or {}).get("required"))
    if now_required and not was_required and was:
        changes.append(Change(True, "request-body-now-required", where, "the body was optional"))

    for media in sorted(set(was) & set(now)):
        changes.extend(
            compare_schema(
                base_doc,
                head_doc,
                f"{where} {media}",
                was[media].get("schema"),
                now[media].get("schema"),
                REQUEST,
                0,
            )
        )

    return changes


def compare_responses(
    base_doc: dict[str, Any],
    head_doc: dict[str, Any],
    where: str,
    before: dict[str, Any],
    after: dict[str, Any],
) -> list[Change]:
    was = before.get("responses") or {}
    now = after.get("responses") or {}
    changes: list[Change] = []

    for status in sorted(set(was) - set(now)):
        changes.append(
            Change(True, "response-removed", f"{where} {status}", "the status is no longer documented")
        )

    for status in sorted(set(now) - set(was)):
        changes.append(Change(False, "response-added", f"{where} {status}", "a newly documented status"))

    for status in sorted(set(was) & set(now)):
        was_content = content(base_doc, was[status])
        now_content = content(head_doc, now[status])

        for media in sorted(set(was_content) - set(now_content)):
            changes.append(
                Change(
                    True,
                    "response-media-removed",
                    f"{where} {status} {media}",
                    "the media type is no longer returned",
                )
            )

        for media in sorted(set(was_content) & set(now_content)):
            changes.extend(
                compare_schema(
                    base_doc,
                    head_doc,
                    f"{where} {status} {media}",
                    was_content[media].get("schema"),
                    now_content[media].get("schema"),
                    RESPONSE,
                    0,
                )
            )

    return changes


def compare_schema(
    base_doc: dict[str, Any],
    head_doc: dict[str, Any],
    where: str,
    before: Any,
    after: Any,
    context: str,
    depth: int,
) -> list[Change]:
    """Compare two schemas, applying the asymmetry between what a caller sends and what it receives."""
    if depth > MAX_DEPTH:
        return []

    was = resolve(base_doc, before)
    now = resolve(head_doc, after)

    if not isinstance(was, dict) or not isinstance(now, dict):
        return []

    changes: list[Change] = []

    # A type or a format change is breaking in both directions: the caller sends the wrong thing, or
    # receives something it cannot parse.
    if as_set(was.get("type")) != as_set(now.get("type")):
        changes.append(
            Change(
                True,
                "type-changed",
                where,
                f"{sorted(as_set(was.get('type')))} became {sorted(as_set(now.get('type')))} "
                "(a nullability change is a type change)",
            )
        )

    if was.get("format") != now.get("format"):
        changes.append(
            Change(True, "format-changed", where, f"{was.get('format')!r} became {now.get('format')!r}")
        )

    changes.extend(compare_enum(where, was, now, context))
    changes.extend(compare_required(where, was, now, context))
    changes.extend(compare_constraints(where, was, now, context))

    was_properties = was.get("properties") or {}
    now_properties = now.get("properties") or {}
    now_required = as_set(now.get("required"))

    for name in sorted(set(was_properties) - set(now_properties)):
        changes.append(Change(True, "property-removed", f"{where}.{name}", "the field is gone"))

    for name in sorted(set(now_properties) - set(was_properties)):
        breaking = context == REQUEST and name in now_required
        changes.append(
            Change(
                breaking,
                "property-added",
                f"{where}.{name}",
                "a new required request field" if breaking else "a new field",
            )
        )

    for name in sorted(set(was_properties) & set(now_properties)):
        changes.extend(
            compare_schema(
                base_doc,
                head_doc,
                f"{where}.{name}",
                was_properties[name],
                now_properties[name],
                context,
                depth + 1,
            )
        )

    if was.get("items") is not None or now.get("items") is not None:
        changes.extend(
            compare_schema(
                base_doc, head_doc, f"{where}[]", was.get("items"), now.get("items"), context, depth + 1
            )
        )

    return changes


def compare_enum(where: str, was: dict[str, Any], now: dict[str, Any], context: str) -> list[Change]:
    was_values = as_set(was.get("enum")) if "enum" in was else None
    now_values = as_set(now.get("enum")) if "enum" in now else None

    if was_values is None and now_values is None:
        return []

    if was_values is None:
        return [Change(True, "enum-introduced", where, "the field was open and is now closed")]

    if now_values is None:
        return [Change(False, "enum-removed", where, "the field is open again")]

    changes: list[Change] = []

    for value in sorted(was_values - now_values):
        changes.append(Change(True, "enum-value-removed", where, f"{value!r} is no longer accepted"))

    for value in sorted(now_values - was_values):
        # In a response, a new value is a value the client has never seen and may not handle: the
        # conventions call it breaking unless the field is documented as open-ended.
        breaking = context == RESPONSE
        changes.append(
            Change(
                breaking,
                "enum-value-added",
                where,
                f"{value!r} is new; a client switching on this field has no branch for it"
                if breaking
                else f"{value!r} is newly accepted",
            )
        )

    return changes


def compare_required(where: str, was: dict[str, Any], now: dict[str, Any], context: str) -> list[Change]:
    was_required = as_set(was.get("required"))
    now_required = as_set(now.get("required"))
    changes: list[Change] = []

    for name in sorted(now_required - was_required):
        if context == REQUEST:
            changes.append(
                Change(True, "field-now-required", f"{where}.{name}", "the field was optional")
            )
        else:
            changes.append(
                Change(False, "field-now-guaranteed", f"{where}.{name}", "always present now")
            )

    for name in sorted(was_required - now_required):
        if context == RESPONSE:
            changes.append(
                Change(
                    True,
                    "field-no-longer-guaranteed",
                    f"{where}.{name}",
                    "the response no longer promises the field",
                )
            )
        else:
            changes.append(Change(False, "field-now-optional", f"{where}.{name}", "relaxed"))

    return changes


def compare_constraints(
    where: str, was: dict[str, Any], now: dict[str, Any], context: str
) -> list[Change]:
    """Validation tightened in a request is breaking; loosened is not. Responses are not validated."""
    if context != REQUEST:
        return []

    changes: list[Change] = []

    def number(value: Any) -> float | None:
        try:
            return float(value)
        except (TypeError, ValueError):
            return None

    tighter = (
        ("maxLength", lambda old, new: old is not None and new is not None and new < old),
        ("maximum", lambda old, new: old is not None and new is not None and new < old),
        ("minLength", lambda old, new: old is not None and new is not None and new > old),
        ("minimum", lambda old, new: old is not None and new is not None and new > old),
    )

    for keyword, is_tighter in tighter:
        old, new = number(was.get(keyword)), number(now.get(keyword))
        if (old is None and new is not None) or is_tighter(old, new):
            changes.append(
                Change(True, "validation-tightened", where, f"{keyword} {old!r} became {new!r}")
            )

    if was.get("pattern") != now.get("pattern") and now.get("pattern") is not None:
        changes.append(
            Change(True, "validation-tightened", where, f"pattern is now {now['pattern']!r}")
        )

    if was.get("additionalProperties") is not False and now.get("additionalProperties") is False:
        changes.append(
            Change(True, "validation-tightened", where, "additional properties are no longer accepted")
        )

    return changes


# --------------------------------------------------------------------------------------------------
# The gate


def approved(register: Path | None, pull_request: str | None) -> tuple[bool, str]:
    """True when the register records this pull request as an approved breaking change."""
    if register is None or pull_request is None:
        return False, "no breaking-change register or pull-request number was supplied"

    if not register.exists():
        return False, f"{register} does not exist"

    text = register.read_text(encoding="utf-8")
    if re.search(rf"#{re.escape(str(pull_request))}\b", text):
        return True, f"{register} records #{pull_request}"

    return False, f"{register} has no entry naming #{pull_request}"


def report(changes: list[Change]) -> None:
    breaking = [change for change in changes if change.breaking]
    additive = [change for change in changes if not change.breaking]

    print(f"{len(changes)} change(s): {len(breaking)} breaking, {len(additive)} additive")

    if breaking:
        print("\nBreaking:")
        for change in breaking:
            print(change)

    if additive:
        print("\nAdditive:")
        for change in additive:
            print(change)


def self_test() -> int:
    """Run the classifier over documents whose answers are known."""

    def document(**operation: Any) -> dict[str, Any]:
        base: dict[str, Any] = {
            "openapi": "3.1.1",
            "paths": {
                "/api/v1/things": {
                    "post": {
                        "operationId": "CreateThing",
                        "security": [{"sessionCookie": []}],
                        "requestBody": {
                            "required": True,
                            "content": {
                                "application/json": {
                                    "schema": {"$ref": "#/components/schemas/Thing"}
                                }
                            },
                        },
                        "responses": {
                            "200": {
                                "content": {
                                    "application/json": {
                                        "schema": {"$ref": "#/components/schemas/ThingView"}
                                    }
                                }
                            },
                            "400": {"content": {"application/problem+json": {"schema": {}}}},
                        },
                    }
                }
            },
            "components": {
                "schemas": {
                    "Thing": {
                        "type": "object",
                        "required": ["name"],
                        "properties": {
                            "name": {"type": "string", "maxLength": 200},
                            "note": {"type": ["string", "null"]},
                        },
                    },
                    "ThingView": {
                        "type": "object",
                        "required": ["id", "status"],
                        "properties": {
                            "id": {"type": "string", "format": "uuid"},
                            "status": {"type": "string", "enum": ["draft", "confirmed"]},
                        },
                    },
                }
            },
        }
        base["paths"]["/api/v1/things"]["post"].update(operation)
        return base

    def mutate(change: Any) -> dict[str, Any]:
        head = json.loads(json.dumps(document()))
        change(head)
        return head

    def verdict(head: dict[str, Any]) -> tuple[bool, set[str]]:
        changes = compare(document(), head)
        return any(change.breaking for change in changes), {change.rule for change in changes}

    cases: list[tuple[str, Any, bool, str]] = [
        (
            "an unchanged document",
            lambda head: None,
            False,
            "",
        ),
        (
            "removing an operation",
            lambda head: head["paths"]["/api/v1/things"].pop("post"),
            True,
            "operation-removed",
        ),
        (
            "adding an operation",
            lambda head: head["paths"].__setitem__("/api/v1/others", {"get": {"operationId": "X"}}),
            False,
            "operation-added",
        ),
        (
            "renaming an operation",
            lambda head: head["paths"]["/api/v1/things"]["post"].update({"operationId": "MakeThing"}),
            True,
            "operation-id-changed",
        ),
        (
            "removing a request field",
            lambda head: head["components"]["schemas"]["Thing"]["properties"].pop("note"),
            True,
            "property-removed",
        ),
        (
            "adding an optional request field",
            lambda head: head["components"]["schemas"]["Thing"]["properties"].update(
                {"colour": {"type": "string"}}
            ),
            False,
            "property-added",
        ),
        (
            "adding a required request field",
            lambda head: (
                head["components"]["schemas"]["Thing"]["properties"].update(
                    {"colour": {"type": "string"}}
                ),
                head["components"]["schemas"]["Thing"]["required"].append("colour"),
            ),
            True,
            "property-added",
        ),
        (
            "making an optional request field required",
            lambda head: head["components"]["schemas"]["Thing"]["required"].append("note"),
            True,
            "field-now-required",
        ),
        (
            "adding a response field",
            lambda head: head["components"]["schemas"]["ThingView"]["properties"].update(
                {"total": {"type": "integer"}}
            ),
            False,
            "property-added",
        ),
        (
            "removing a response field",
            lambda head: head["components"]["schemas"]["ThingView"]["properties"].pop("status"),
            True,
            "property-removed",
        ),
        (
            "dropping a response guarantee",
            lambda head: head["components"]["schemas"]["ThingView"]["required"].remove("status"),
            True,
            "field-no-longer-guaranteed",
        ),
        (
            "changing a type",
            lambda head: head["components"]["schemas"]["ThingView"]["properties"]["id"].update(
                {"type": "integer"}
            ),
            True,
            "type-changed",
        ),
        (
            "making a response field nullable",
            lambda head: head["components"]["schemas"]["ThingView"]["properties"]["id"].update(
                {"type": ["string", "null"]}
            ),
            True,
            "type-changed",
        ),
        (
            "changing a format",
            lambda head: head["components"]["schemas"]["ThingView"]["properties"]["id"].update(
                {"format": "uri"}
            ),
            True,
            "format-changed",
        ),
        (
            "adding a value to a response enum",
            lambda head: head["components"]["schemas"]["ThingView"]["properties"]["status"][
                "enum"
            ].append("cancelled"),
            True,
            "enum-value-added",
        ),
        (
            "removing a value from a response enum",
            lambda head: head["components"]["schemas"]["ThingView"]["properties"]["status"][
                "enum"
            ].remove("draft"),
            True,
            "enum-value-removed",
        ),
        (
            "tightening request validation",
            lambda head: head["components"]["schemas"]["Thing"]["properties"]["name"].update(
                {"maxLength": 40}
            ),
            True,
            "validation-tightened",
        ),
        (
            "relaxing request validation",
            lambda head: head["components"]["schemas"]["Thing"]["properties"]["name"].update(
                {"maxLength": 400}
            ),
            False,
            "",
        ),
        (
            "removing a documented status",
            lambda head: head["paths"]["/api/v1/things"]["post"]["responses"].pop("400"),
            True,
            "response-removed",
        ),
        (
            "documenting a new status",
            lambda head: head["paths"]["/api/v1/things"]["post"]["responses"].update(
                {"429": {"content": {"application/problem+json": {"schema": {}}}}}
            ),
            False,
            "response-added",
        ),
        (
            "demanding a new credential",
            lambda head: head["paths"]["/api/v1/things"]["post"]["security"].append(
                {"sessionCookie": [], "antiForgeryToken": []}
            ),
            True,
            "security-added",
        ),
        (
            "adding a required parameter",
            lambda head: head["paths"]["/api/v1/things"]["post"].update(
                {"parameters": [{"name": "branch", "in": "query", "required": True}]}
            ),
            True,
            "parameter-added",
        ),
        (
            "adding an optional parameter",
            lambda head: head["paths"]["/api/v1/things"]["post"].update(
                {"parameters": [{"name": "branch", "in": "query"}]}
            ),
            False,
            "parameter-added",
        ),
    ]

    failures = 0
    for description, change, expected_breaking, expected_rule in cases:
        actual_breaking, rules = verdict(mutate(change))
        if actual_breaking != expected_breaking:
            failures += 1
            print(
                f"SELF-TEST FAILED: {description} should be "
                f"{'breaking' if expected_breaking else 'additive'}, was not. Rules: {sorted(rules)}"
            )
        elif expected_rule and expected_rule not in rules:
            failures += 1
            print(
                f"SELF-TEST FAILED: {description} did not report {expected_rule}. "
                f"Rules: {sorted(rules)}"
            )
        else:
            print(f"  ok  {description}")

    if failures:
        print(f"\n{failures} self-test case(s) failed: the diff gate is not detecting.")
        return 1

    print(f"\nAll {len(cases)} self-test cases passed.")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--base", type=Path, help="the document on the base branch")
    parser.add_argument("--head", type=Path, help="the document in this branch")
    parser.add_argument(
        "--approved",
        action="store_true",
        help="the pull request carries the api-breaking-approved label",
    )
    parser.add_argument("--register", type=Path, help="the approved breaking-change register")
    parser.add_argument("--pr", help="the pull-request number the register must name")
    parser.add_argument("--self-test", action="store_true", help="prove the classifier still detects")
    arguments = parser.parse_args(argv)

    if arguments.self_test:
        return self_test()

    if not arguments.base or not arguments.head:
        parser.error("--base and --head are required unless --self-test is given")

    base_text = arguments.base.read_text(encoding="utf-8").strip() if arguments.base.exists() else ""
    if not base_text:
        # The base branch has no document: this pull request introduces one, and everything in it is
        # new rather than changed.
        print(f"No document on the base branch at {arguments.base}: every operation is new.")
        base: dict[str, Any] = {}
    else:
        base = json.loads(base_text)

    head = json.loads(arguments.head.read_text(encoding="utf-8"))

    changes = compare(base, head)
    report(changes)

    breaking = [change for change in changes if change.breaking]
    if not breaking:
        print("\nThe API contract changed additively, or not at all.")
        return 0

    is_approved, reason = approved(arguments.register, arguments.pr)

    if arguments.approved and is_approved:
        print(f"\nBreaking changes are approved for this pull request ({reason}).")
        return 0

    print(
        "\nThe API contract changed in a way that breaks a client, and the change is not approved.\n"
        "Inside a major version only additive changes are permitted "
        "(docs/architecture/conventions.md section 5.2). Either:\n"
        "  - make the change additive: add rather than rename, keep the old field, widen rather than\n"
        "    narrow, and deprecate what is being replaced; or\n"
        "  - publish it under a new major path; or\n"
        "  - have it approved: a CODEOWNER of docs/api/ applies the api-breaking-approved label, and\n"
        "    the change is recorded in docs/api/breaking-changes.md naming this pull request."
    )
    if arguments.approved:
        print(f"\nThe label is present but the register does not back it: {reason}.")

    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
