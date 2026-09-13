# Work-item specifications

One file per unit of the breakdown in [`../work-breakdown.md`](../work-breakdown.md). Each is the **as-filed source
text of a GitHub sub-issue body**: what is already built and must not be rebuilt, the scope, what is deliberately out
of scope, acceptance criteria with a negative and an exception case, the data classification of anything new, the
permission keys and the roles that hold them, the modules and contracts crossed, the migration and rollback shape, the
open decisions in force with the default each one runs under, the test tiers, and the evidence the pull request must
carry.

## Which copy wins

**The GitHub issue wins.** These files exist so the breakdown survives independently of the tracker — they were written
before the issues were filed, and they are what the issues were filed from. Once a unit has an issue number, that issue
is authoritative for its scope: it is what a reviewer reads, what a pull request links, and what gets amended when the
work turns out differently. A file here that disagrees with its issue is stale, and the fix is to correct the file or
delete it, never to work from it in preference to the issue.

They can be pruned once every unit carries its body and the **Issue** column of
[`../work-breakdown.md`](../work-breakdown.md) has no dashes left. Until then they are the only copy of about 194,000
lines of planned work.

## One difference from the filed text

The relative links in these files are written to resolve from this directory, so that
`python3 scripts/check-docs-links.py` can gate them like every other document. The filed issue bodies carry the same
paths written from the repository root. Nothing else differs.

## Naming

`<epic>-<feature>-<unit>.md`, lower case — `e06-f02-1.md` is the first unit of `[E06-F02]`, issue #33. A letter suffix
(`-5b`, `-8a`) marks a unit that a review pass split out of its neighbour because one session could not finish both.
The branch each unit takes is named in its own body and follows
[`../../../CLAUDE.md`](../../../CLAUDE.md) section 6.

## What these are not

They are not a plan, a schedule or an index — [`../work-breakdown.md`](../work-breakdown.md) is all three, and it also
carries the contended-file register that says which of these units may not be worked at the same time. They are not
documentation of the system: they describe work to be done, and a unit's file stops being true the moment its pull
request merges. Read them with [`../definition-of-ready.md`](../definition-of-ready.md), which is the gate each one
must pass before its branch is cut.
