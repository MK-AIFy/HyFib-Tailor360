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
EXTRA_FILES = ("README.md", "CONTRIBUTING.md", "SECURITY.md")

# A fenced code block may contain an illustrative link that is not meant to resolve.
FENCE = re.compile(r"^\s*(```|~~~)")


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


def targets(text: str):
    body = strip_fenced_blocks(text)
    for pattern in (INLINE, REFERENCE):
        for match in pattern.finditer(body):
            yield match.group(1).strip()


def check(repository: str) -> int:
    broken: list[tuple[str, str]] = []
    files = markdown_files(repository)

    for path in files:
        with open(path, encoding="utf-8") as handle:
            text = handle.read()
        for target in targets(text):
            if target.startswith(SKIPPED_SCHEMES) or target.startswith("#"):
                continue
            # Keep the file part; an anchor is not checked, only the document it lives in.
            document = unquote(target.split("#", 1)[0])
            if not document:
                continue
            resolved = os.path.normpath(os.path.join(os.path.dirname(path), document))
            if not os.path.exists(resolved):
                broken.append((os.path.relpath(path, repository), target))

    print(f"checked {len(files)} markdown files")

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

A [reference link][ref].

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

        found = set()
        for path in markdown_files(repository):
            with open(path, encoding="utf-8") as handle:
                text = handle.read()
            for target in targets(text):
                if target.startswith(SKIPPED_SCHEMES) or target.startswith("#"):
                    continue
                document = unquote(target.split("#", 1)[0])
                if not document:
                    continue
                resolved = os.path.normpath(os.path.join(os.path.dirname(path), document))
                if not os.path.exists(resolved):
                    found.add((os.path.relpath(path, repository), target))

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
