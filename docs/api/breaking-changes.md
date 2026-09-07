# Approved breaking changes to the version 1 API

Inside a major version only additive changes are permitted
([`../architecture/conventions.md`](../architecture/conventions.md) section 5.2). The gate that enforces it
is `scripts/openapi-diff.py`, run by the `api-contract` job on every pull request, and it refuses a breaking
change unless **both** of these are true:

1. a CODEOWNER of `docs/api/` has applied the `api-breaking-approved` label to the pull request, and
2. the change is recorded in the register below, naming that pull request as `#NN`.

Either alone is deliberately not enough. A label leaves no record of what was approved or why; a record
nobody approved is a note to self. The script looks for `#NN` in this file, so the pull-request number in a
row is what actually opens the gate — which is also why a row is written when the change is approved, not
afterwards.

**A row here is a promise to somebody.** Every entry names what breaks, who it breaks for, what they must do,
and when the old shape stops being served. Removal happens only in the next major version, served alongside
`v1` for the deprecation window in
[`../architecture/conventions.md`](../architecture/conventions.md) section 5.3.

---

## How to write a row

| Column | What goes in it |
| --- | --- |
| Date | The date the approval was given, `YYYY-MM-DD` |
| Pull request | `#NN`. The gate matches on this |
| Change | The operation, field or status, and what happened to it — in the words the diff reported |
| Why it could not be additive | The alternative that was considered and why it was worse. "It was simpler" is not a reason |
| Who it breaks | Which clients, and which versions of them |
| Migration | What a client does, and by when |
| Deprecation entry | Where the deprecated surface is marked and when it is served until |
| Approved by | The CODEOWNER who applied the label |

---

## The register

| Date | Pull request | Change | Why it could not be additive | Who it breaks | Migration | Deprecation entry | Approved by |
| --- | --- | --- | --- | --- | --- | --- | --- |
| — | — | _No breaking change has been approved. The version 1 contract has not broken since it was first published._ | — | — | — | — | — |

---

## What counts as breaking

The full table is [`../architecture/conventions.md`](../architecture/conventions.md) section 5.2. The
classifier implements it, and `scripts/openapi-diff.py --self-test` proves it still does. In short:

- **Additive**: a new endpoint, a new optional request field, a new response field, a new optional query
  parameter, a newly documented status, relaxed validation, a new value in a request enum.
- **Breaking**: removing or renaming anything; changing a type, format, nullability or unit; making an
  optional field required or tightening validation; removing a documented status; a new value in a
  response enum; changing an operation identifier; demanding a credential that was not demanded before.

A nullability change is a type change, and an operation identifier is a generated client's method name.
Both surprise people, so both are called out here as well as in the classifier.
