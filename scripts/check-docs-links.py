#!/usr/bin/env python3
"""Fail when a relative link in the documentation points at something that is not there.

The first documentation pull request carried ten broken relative links through review unnoticed,
and two of them pointed at documents that a document map declared as deliverables and that nobody
had written. A reader following such a link learns nothing; a reviewer skimming the map sees a
complete set. This check makes that class of defect impossible to merge.

It deliberately checks only relative links. An external URL is somebody else's uptime and would
make the build fail for reasons no commit here can fix; a link to a heading inside a file is
checked for the file, not the anchor, because a heading may legitimately be added later in the
same series of changes.

Run it directly, or through `./scripts/dev docs`.
"""

from __future__ import annotations

import os
import re
import sys
from urllib.parse import unquote

# Inline links, reference definitions and bare <> autolinks. The negative lookbehind on "!" keeps
# image embeds in, since a missing image is as broken as a missing document.
INLINE = re.compile(r"\[[^\]]*\]\(\s*<?([^)>\s]+?)>?(?:\s+[\"'][^\"']*[\"'])?\s*\)")
REFERENCE = re.compile(r"^\s{0,3}\[[^\]]+\]:\s*<?([^\s>]+)>?", re.MULTILINE)

SKIPPED_SCHEMES = ("http://", "https://", "mailto:", "tel:", "ftp://", "data:")
SEARCH_ROOTS = ("docs", ".github")
EXTRA_FILES = ("README.md", "CONTRIBUTING.md", "SECURITY.md", "CLAUDE.md")

# Guides and operating manuals that live beside the code they describe rather than under docs/.
# They are hub documents — almost every line points somewhere else — so leaving them outside the
# gate would leave the largest concentration of relative links in the repository ungated. Listed
# one by one rather than by walking src/, clients/ and infra/, so that a stray markdown file in a
# build output directory cannot fail the check.
EXTRA_FILES += (
    os.path.join("src", "Modules", "CLAUDE.md"),
    os.path.join("clients", "pwa", "CLAUDE.md"),
    os.path.join("clients", "pwa", "README.md"),
    os.path.join("infra", "CLAUDE.md"),
    os.path.join("infra", "README.md"),
    os.path.join("infra", "dev-environment", "README.md"),
)

# A fenced code block may contain an illustrative link that is not meant to resolve, and so may
# an inline code span: `![x](diagram.png)` inside backticks renders as literal text, not a link.
FENCE = re.compile(r"^\s*(```|~~~)")
CODE_SPAN = re.compile(r"(?<!`)(`+)(?!`)(.+?)(?<!`)\1(?!`)", re.S)


def strip_fenced_blocks(text: str) -> str:
    kept, fence = [], None
    for line in text.splitlines():
        match = FENCE.match(line)
        if fence is None and match:
            fence = match.group(1)
            kept.append("")
            continue
        if fence is not None:
            kept.append("")
            if match and match.group(1) == fence:
                fence = None
            continue
        kept.append(line)
    return "\n".join(kept)


def markdown_files(repository: str) -> list[str]:
    found = []
    for root in SEARCH_ROOTS:
        for directory, _, names in os.walk(os.path.join(repository, root)):
            found.extend(
                os.path.join(directory, name) for name in names if name.endswith(".md")
            )
    found.extend(
        os.path.join(repository, name)
        for name in EXTRA_FILES
        if os.path.exists(os.path.join(repository, name))
    )
    return sorted(found)


def strip_code_spans(text: str) -> str:
    """Blank the contents of every inline code span, keeping the line and column count intact."""
    return CODE_SPAN.sub(lambda m: m.group(1) + (" " * len(m.group(2))) + m.group(1), text)


def targets(text: str):
    body = strip_code_spans(strip_fenced_blocks(text))
    for pattern in (INLINE, REFERENCE):
        for match in pattern.finditer(body):
            yield match.group(1).strip()


def find_broken(repository: str) -> tuple[list[tuple[str, str]], int]:
    """Return every broken relative link under *repository*, and how many files were read.

    This is the single detection path. `check` and `self_test` both go through it, so the
    self-test genuinely exercises what the gate runs — a self-test with its own copy of this
    loop would keep passing while the real check rotted.
    """
    broken: list[tuple[str, str]] = []
    files = markdown_files(repository)

    for path in files:
        with open(path, encoding="utf-8") as handle:
            text = handle.read()
        for target in targets(text):
            if target.startswith(SKIPPED_SCHEMES):
                continue
            # Keep the file part; an anchor is not checked, only the document it lives in. An
            # anchor-only link such as (#section) leaves nothing to check and is skipped here.
            # This guard is defensive rather than load-bearing: without it the empty path would
            # resolve to the containing directory, which exists, so nothing would be reported
            # either way. It is kept because relying on that coincidence would be a trap for
            # whoever changes the resolution below, and it is the one guard here with no
            # failing negative control — stated plainly rather than left to look tested.
            document = unquote(target.split("#", 1)[0])
            if not document:
                continue
            resolved = os.path.normpath(os.path.join(os.path.dirname(path), document))
            if not os.path.exists(resolved):
                broken.append((os.path.relpath(path, repository), target))

    return broken, len(files)


def check(repository: str) -> int:
    broken, file_count = find_broken(repository)

    print(f"checked {file_count} markdown files")

    if not broken:
        print("every relative link resolves")
        return 0

    print(f"\n{len(broken)} broken relative link(s):\n", file=sys.stderr)
    for source, target in broken:
        print(f"  {source} -> {target}", file=sys.stderr)
    print(
        "\nEither create the missing document, correct the path, or — if the target is a future "
        "deliverable —\nrefer to it as inline code rather than as a link, the way "
        "docs/prd/00-overview.md refers to\ndocuments issue #24 has yet to deliver.",
        file=sys.stderr,
    )
    return 1


SELF_TEST_DOCUMENT = """# Sample

A working link to [b](b.md), and an [external](https://example.com/missing.md) one.
An [anchor-only](#section) link, and one [with an anchor](b.md#part).
A [mailto](mailto:someone@example.com) link.
An image ![shot](missing-image.png) that is not there.

```markdown
This fenced block holds a [link](totally-missing.md) that must be ignored.
```

~~~markdown
A tilde-fenced block holds a [link](tilde-fenced-missing.md) that must be ignored too. Only
strip_fenced_blocks handles this style; the backtick code-span pass does not see it.
~~~

A [reference link][ref].

An inline code span holding `[a link](inside-code.md)` and `![an image](inside-code.png)`, both of
which render as literal text and must be ignored.

[ref]: c.md
"""

# What the document above must produce: exactly the two genuine breaks, and nothing else. A checker
# that stops detecting is indistinguishable from a repository with no broken links, so the detector
# is made to prove itself against known-bad input before it is trusted to pass known-good input.
SELF_TEST_EXPECTED = {("docs/a.md", "missing-image.png"), ("docs/a.md", "c.md")}


def self_test() -> int:
    """Prove the detector still detects, and still ignores what it is meant to ignore."""
    import tempfile

    with tempfile.TemporaryDirectory() as repository:
        documents = os.path.join(repository, "docs")
        os.makedirs(documents)
        with open(os.path.join(documents, "a.md"), "w", encoding="utf-8") as handle:
            handle.write(SELF_TEST_DOCUMENT)
        open(os.path.join(documents, "b.md"), "w", encoding="utf-8").close()

        found = set(find_broken(repository)[0])

    missed = SELF_TEST_EXPECTED - found
    spurious = found - SELF_TEST_EXPECTED

    if missed:
        print("SELF-TEST FAILED: the detector no longer catches:", file=sys.stderr)
        for source, target in sorted(missed):
            print(f"  {source} -> {target}", file=sys.stderr)
    if spurious:
        print("SELF-TEST FAILED: the detector now reports links it must ignore:", file=sys.stderr)
        for source, target in sorted(spurious):
            print(f"  {source} -> {target}", file=sys.stderr)

    if missed or spurious:
        return 1

    print("self-test passed: 2 genuine breaks found, external, anchor, mailto and fenced links ignored")
    return 0


if __name__ == "__main__":
    if "--self-test" in sys.argv[1:]:
        sys.exit(self_test())
    sys.exit(check(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
