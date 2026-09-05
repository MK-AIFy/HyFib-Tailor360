<!--
  This template mirrors the Definition of Done in docs/IMPLEMENTATION_PLAN.md, Section 5.1.
  The PR policy check enforces the linked issue and the branch name; the rest is what a reviewer
  reads first. Delete a section only when it genuinely does not apply, and say why.
-->

## What and why

<!-- What changed, and the reason a reader who did not attend the discussion would need. Two or
     three sentences. Describe the behaviour, not the diff. -->

## Linked issue

<!-- Exactly one issue. Use `Refs #NN` when the issue stays open, or `Closes #NN` when merging
     completes it. `Fixes` and `Resolves` are not accepted, so the link reads the same everywhere. -->

Refs #NN

## How it was verified

<!-- The commands you actually ran and what they reported. Replace this list with your own. -->

- [ ] `./scripts/dev test` (or the tiers you ran: Unit, Architecture, Contract, Integration)
- [ ] `./scripts/dev status` — all five components healthy
- [ ] `pnpm test` and `pnpm build` in `clients/pwa` (UI changes)
- [ ] Tests run twice, to expose order dependence

## Evidence

**Evidence is required before review starts.** A reviewer will not begin on a pull request whose
evidence section is empty: paste the run output, screenshots and migration output here rather than
describing them.

<!-- Attach or paste:
     - test output or the CI run link (tier summary from the job summary is enough)
     - screenshots or a short recording for any UI change, at phone, tablet and desktop widths
     - migration output (`dotnet run --project src/Tools/Tailor360.Cli -- migrate`) for schema changes
     - security notes: secret-scan result, anything the threat model touches -->

## Risk and rollback

<!-- What could break, who notices first, and how to undo it. For a schema change, state the
     rollback or restore path from docs/dev/migrations.md: forward-only fix migration first;
     restore from backup only when data was mutated. -->

## Follow-ups

<!-- Anything deliberately left out, with the issue it is tracked in. "None" is a valid answer. -->

---

## Definition of Done

- [ ] 1. Linked to exactly one issue (or sub-issue); only scoped changes; branch named
      `feat/eXX-fYY-<slug>`.
- [ ] 2. Server-side authorisation, validation, idempotency (where the endpoint is retried) and
      audit events on every state-changing endpoint; every new or changed endpoint adds its
      role × own-branch/other-branch expectations (and field mask where applicable) to the
      authorisation-matrix fixtures.
- [ ] 3. Unit tests for domain rules, integration tests for persistence and API, architecture tests
      still green, E2E coverage for any new critical journey; synthetic data only.
- [ ] 4. Migrations forward-only and backward compatible with the previous release
      (expand/contract); rollback or restore note included above.
- [ ] 5. OpenAPI updated (or the endpoint marked internal); generated client regenerated; no
      undocumented breaking change.
- [ ] 6. No secrets, no personal data in logs; telemetry names and redaction reviewed; the threat
      model covering this flow is referenced and its mapped controls are closed.
- [ ] 7. UI changes only: accessibility check (axe and the screen-reader items in
      `docs/nfr/a11y-checklist.md` for a new journey), responsive check at the phone, tablet and
      desktop profiles including the overflow and obscured-focus helper, pseudo-locale story, CSP
      clean, Storybook stories for the loading, empty, error, offline and forbidden state of every
      new screen, and one network-failure step in the journey's E2E (request aborted → retry
      succeeds with exactly one effect).
- [ ] 8. Documentation updated: module README, an ADR if a decision changed, a runbook if
      operations changed, the metric dictionary if a report changed.
- [ ] 9. This evidence checklist is complete — the linked issue, the branch name and the checklist
      are verified by a required status check.
