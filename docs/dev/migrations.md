# Database migrations

How schema changes are made, applied, verified and undone. Read this before writing a migration; the
pull-request template links here.

## Principles

1. **One module owns one schema.** A migration for the Orders module may only touch the `orders`
   schema. Architecture rule ARCH-005 fails the build if a context maps a table outside its schema.
2. **Migrations are applied by the command line, never by the application.** During a rolling
   deployment two versions of the application run at once; if either could migrate, they would race.
   `Tailor360.Cli migrate` runs once, before the new version is allowed to serve.
3. **The migrator is not the application.** Migrations run as a role that may change the schema. The
   application role may not, so a defect in the application cannot alter its own tables.
4. **Expand in release N, contract no earlier than N+2.** This is what makes a rollback survivable.

## Expand and contract

A change that removes or narrows anything is split across releases:

| Release | What ships | Why |
| --- | --- | --- |
| N | Add the new column or table. Write to both. Read from the old one. | The previous version still runs and still understands the old shape. |
| N+1 | Read from the new one. Keep writing both. | If N+1 is rolled back, N still works. |
| N+2 or later | Stop writing the old one, then drop it. | By now no supported version reads it. |

Renaming a column is the same three steps, never a single `RENAME`. A rename looks atomic and is the
most common cause of a failed rollback.

## Writing a migration

```bash
dotnet ef migrations add <Name> \
  --project src/Platform/Tailor360.Platform.Persistence \
  --startup-project src/Platform/Tailor360.Platform.Persistence \
  --context PlatformDbContext \
  --output-dir Migrations
```

Substitute the module's project and context for a module migration. Review the generated file before
committing it: the scaffolder is a starting point, not an authority. Add explicit SQL for anything the
model cannot express — partitioning, triggers, functions, grants — as `migrationBuilder.Sql(...)` with
a comment saying why it is there. `platform.audit_events` is built entirely this way, because its
partitioning, hash-chain trigger and append-only trigger are the point of the table.

Every migration needs a working `Down`. An untested `Down` is not a rollback plan.

## Applying migrations

```bash
./scripts/dev migrate          # or:
dotnet run --project src/Tools/Tailor360.Cli -- migrate --dry-run
dotnet run --project src/Tools/Tailor360.Cli -- migrate
```

The runner holds one PostgreSQL advisory lock for the whole run, so two instances starting together
cannot both migrate. Contexts are applied in a fixed order: `platform`, then `identity`, then the
remaining modules alphabetically. Each schema keeps its own `__ef_migrations_history` table.

## Startup validation

The startup health check compares this build's migrations against the database:

- **A migration this build knows is missing from the database** — the build refuses to serve. Serving
  against a schema that lacks a column this code writes would corrupt data.
- **The database has migrations this build does not know** — the build serves and reports *degraded*.
  This is the normal state during a rollback to release N while the database is still at N+1. Comparing
  the two sets for equality instead would turn every rollback into an outage, which is why the check is
  deliberately one-directional.

## Rolling back

1. **Prefer a forward fix.** A new migration that corrects the problem is almost always safer than
   reversing one, because it does not have to guess what the reversed migration did to the data.
2. **If the schema change did not touch data**, deploy the previous application version. Expand and
   contract means it still understands the current schema.
3. **If data was mutated and is wrong**, restore from backup to a point before the migration and replay
   what happened since. This is the case the restore drill in issue #60 rehearses.

Never edit a migration that has been applied anywhere but a developer machine. Add another one.

## Compatibility check against the previous release

`tests/fixtures/db-snapshots/` holds the schema snapshot of release N-1 only. The migration test
applies this build's migrations to an empty database and to that snapshot; both must succeed. Older
snapshots are deleted, and migrations older than two releases may be squashed.

## Register

Every migration is recorded here when it merges, with its phase and the evidence that it was applied.
Conventions section 6.4 requires this alongside the pull-request evidence, so that a reviewer looking at
a schema six months later can see what each change was for without reading the code.

| Migration | Schema | Phase | Issue | Applied and verified |
| --- | --- | --- | --- | --- |
| `20260904185922_InitialPlatformSchema` | `platform` | Expand — initial | #21 | Empty database; re-run reports up to date |
| `20260904185927_AuditTrailStructure` | `platform` | Expand — initial | #21 | Empty database; re-run reports up to date |
| `20260904195746_AuditPartitionSafety` | `platform` | Expand | #21 | Empty database; re-run reports up to date |
| `20260905155743_InitialIdentitySchema` | `identity` | Expand — initial | #23 | Applied to an empty PostgreSQL 16 database; re-run reports `identity: already up to date`; `Down` reverted to an empty schema and the migration re-applied cleanly |
| `20260905162158_DataProtectionKeyRing` | `platform` | Expand | #23 | Applied to an empty PostgreSQL 16 database (`tailor360_dp_check`); re-run reports `platform: already up to date`; `Down` dropped `platform.data_protection_keys` and the migration re-applied cleanly |
| `20260905170018_AddRecoveryTokens` | `identity` | Expand | #23 | Applied to an empty PostgreSQL 16 database (`tailor360_mig_check`); both check constraints present afterwards — `ck_recovery_tokens_token_hash_is_digest` and `ck_recovery_tokens_lifetime`, the second of which is the domain's one-hour cap restated in the schema; `Down` dropped the table and the migration re-applied cleanly |
| `20260905193715_AddSessionPendingStep` | `identity` | Expand | #23 | Applied to `tailor360_mig_check`; `identity.sessions.pending_step` is `character varying(30) NOT NULL DEFAULT 'None'` — the default is `None` rather than the scaffolder's empty string, because rows written by the previous build were issued only after the first factor and an empty string parses as no enum member at all; `Down` dropped the column and the migration re-applied cleanly |
| `20260906121513_AddRolesBranchesAndAssignments` | `identity` | Expand | #24 | Applied to an empty PostgreSQL 16 database (`tailor360_mig26`) by `./scripts/dev migrate`; re-run reports `identity: already up to date`. Recorded here retrospectively in #26: the row was missed when the migration merged |
| `20260906170048_IdempotencyInFlightLease` | `platform` | Expand | #53 | Applied to `tailor360_mig26`; re-run reports `platform: already up to date`. Recorded here retrospectively in #26 for the same reason |
| `20260907044327_AddMfaConcurrencyTokens` | `identity` | Expand | #25 | Applied to `tailor360_mig26`; re-run reports `identity: already up to date`. Recorded here retrospectively in #26 for the same reason |
| `20260907090340_AddBranchMasterData` | `identity` | Expand | #25 | Applied to `tailor360_mig26`; re-run reports `identity: already up to date`. Recorded here retrospectively in #26 for the same reason |
| `20260907104054_AddRoleConcurrencyToken` | `identity` | Expand | #25 | Applied to `tailor360_mig26`; re-run reports `identity: already up to date`. Recorded here retrospectively in #26 for the same reason |
| `20260907160822_InitialCustomersSchema` | `customers` | Expand — initial | #26 | Applied to an empty PostgreSQL 16 database (`tailor360_mig26`): `platform: applied 5`, `identity: applied 7`, `customers: applied 1`. Creates `customers.customers`, `customers.customer_aliases` and `customers.customer_branch_visibility`, the three check constraints `ck_customers_phone_is_e164`, `ck_customers_alternate_phone_is_e164` and `ck_customers_deactivated_at_matches_status`, and — in explicit SQL, because EF Core's model cannot express an operator class — `CREATE EXTENSION IF NOT EXISTS pg_trgm` and the three GIN trigram indexes the counter search reads through. Re-run reports `customers: already up to date`; `Down` emptied the schema to the history table alone and deliberately left the extension in place, because dropping a shared extension would break anything else in the database using it; re-applying was clean, and `dotnet ef migrations has-pending-model-changes` reports none. **Deployment note:** `CREATE EXTENSION` needs a role permitted to create extensions, so the migration fails loudly on a database whose user cannot, rather than quietly leaving the search without its indexes |
| `20260908003058_AddConsentAndCommunicationPreferences` | `customers` | Expand | #26 | Applied to an empty PostgreSQL 16 database (`tailor360_mig26b`) after the initial customers schema; re-run reports `customers: already up to date`. Adds `consent_purposes`, `consent_wordings`, `consent_records` and `communication_preferences`. Three things the model cannot express are explicit SQL: the `consent_records_no_update` trigger that makes the consent trail append-only in the database and not only in the domain type (the pattern `platform.audit_events` already uses), and the check constraints `ck_consent_records_decision_is_known` and `ck_communication_preferences_quiet_hours_are_whole`. **All four were proved to bite against the live database**: `UPDATE` and `DELETE` on a consent record are refused with "consent_records is append-only", a decision of `Maybe` is refused, a quiet-hours start without an end is refused, and a whole window is accepted — with the consent record still reading `Granted` afterwards. `allowed_channels` is `character varying(20)[]` holding the channel *names*, so reordering the enumeration cannot re-point a stored preference. `Down` dropped the trigger, the four tables and the function, leaving the schema at the initial migration; re-applying was clean and `dotnet ef migrations has-pending-model-changes` reports none |
| `20260908022906_ModuleOutbox` | `identity` | Expand | #77 | Applied to an empty PostgreSQL 16 database (`tailor360_mig77`): `platform: applied 5`, `identity: applied 8`, `customers: applied 3`; re-run reports `already up to date` for all three. Adds `identity.outbox_messages` and `identity.inbox_messages` with the partial index `ix_outbox_messages_pending` the dispatcher's claim reads through. **The tables are mapped by `ModuleDbContext`, not by this module**, so every module has them by construction and a module added later gets them without anybody remembering — which is the point: a module's write and its event are then on one context and commit together. `Down` dropped both tables and re-applying was clean; `dotnet ef migrations has-pending-model-changes` reports none |
| `20260908022913_ModuleOutbox` | `customers` | Expand | #77 | The same pair in `customers`, from the same `ModuleDbContext` mapping, applied and reverted in the same run on `tailor360_mig77`. **Additive and nothing is moved:** `platform.outbox_messages` and `platform.inbox_messages` keep their shape and their rows — the platform reports `already up to date` because its model did not change, only where the mapping lives — and they are now the platform module's own pair rather than every module's. Both tables were empty in every environment beforehand, because nothing published, which is what made the change a pure expand rather than a data move |
| `20260911141153_InitialOrdersSchema` | `orders` | Expand — initial | #133 | **BLOCKED ON EVIDENCE — do not merge on this row.** There is no Docker daemon and no PostgreSQL on the machine this was written on, so the apply / `Down` / re-apply evidence this register requires must come from CI or from a machine with a database. What was produced locally: the solution builds warning-free in Release, `dotnet format --verify-no-changes` is clean, `dotnet ef migrations has-pending-model-changes` reports none, the unit, architecture and contract tiers are green (1673 / 34 / 151), and both directions were generated as SQL and read through — `dotnet ef migrations script` and `dotnet ef migrations script 20260911141153_InitialOrdersSchema 0` — which proves the DDL is emitted and ordered, and proves nothing about whether PostgreSQL accepts it. `migrate --dry-run` cannot stand in for that here: it compares what the build knows against `__ef_migrations_history`, so it opens a connection and fails with `Failed to connect to 127.0.0.1:5432`. Creates the twelve `orders` tables — `order_drafts`, `order_draft_garments`, `order_draft_garment_dependencies`, `estimates`, `orders`, `order_revisions`, `garment_jobs`, `job_dependencies`, `measurement_snapshots`, `design_snapshots`, plus the module's own `outbox_messages` and `inbox_messages` from `ModuleDbContext`. Five things the model cannot express are explicit SQL, each with a comment saying why: `job_snapshots_are_immutable` over the two snapshot tables and `garment_job_price_is_immutable` over the `price_…` columns of `garment_jobs` (INV-JOB-01, and both deliberately permit the replacement `Order.Revise` makes while every job is still `Confirmed`); `order_revisions_append_only` and `job_dependencies_append_only`; `garment_jobs_ready_is_gate_only` (INV-JOB-07); and `order_number_is_immutable`, `estimate_number_is_immutable` and `garment_job_number_is_immutable`, one per numbered table (INV-ORD-03). **The scaffolder's output needed no hand correction to its generated part, and the five `HAND-EDITED` sites this row used to list are gone with the `job_ready_state` fragment.** `GarmentJob` is no longer split across two tables — Entity Framework Core 10.0.11 cannot read an entity that is both split and holds a complex property, and the frozen price copy is a complex property, so `OrderStore.FindAsync` and `IOrderSnapshotQuery.GetPricedAsync` both threw `InvalidOperationException: Sequence contains more than one element` at query-compilation time. The gate's outcome is `is_ready_for_delivery`, `ready_state_computed_at` and `ready_state_blocks` on `garment_jobs`; `docs/architecture/module-ownership.md` section 5.5 is amended in the same pull request, as section 9 of that document requires. With the fragment gone the scaffolder names the primary key once and resolves every foreign key to `garment_jobs` on its own, and the two ready-state check constraints and the delivery-queue index are ordinary model declarations rather than hand-written SQL — they belong on `garment_jobs` now, which is where Entity Framework puts them anyway. The delivery-queue index changed shape with the table: `ix_job_ready_state_ready` over the fragment's key, with branch scoping left to a join, became `ix_garment_jobs_ready` over `branch_id` filtered on `is_ready_for_delivery`, and there is no join left to make. `garment_jobs_ready_is_gate_only` keeps the insert arm of the old fragment trigger verbatim and drops its delete arm, because on `garment_jobs` the same rule would say a garment job can never be deleted at all and would refuse the cascade from `orders`. `garment_job_price_is_immutable` reads `OLD.status` **and** `NEW.status`: reading `OLD` alone would have let the `StartProduction` update carry a rewritten price out of `Confirmed` with it, which INV-JOB-01 forbids and which no legitimate path does — `GarmentJob.CheckRevision` refuses any status but `Confirmed` and `Revise` never moves it, so both sides read `Confirmed` on the only path that re-prices. `Tailor360.Cli`'s `CliHost` gained `AddOrdersModule`, without which this migration compiles and is unreachable; `AddCatalogModule` and `AddMediaModule` went in beside it, because they were missing too and the resulting unresolvable `ICatalogAvailabilityQuery` made `migrate` throw while building its container, before it reached a database at all. **Three corrections from the second review, all in the trigger bodies and none in the scaffolded part.** *(a)* The `DELETE` arms of `job_snapshots_are_immutable`, `order_revisions_append_only` and `job_dependencies_append_only` refused unconditionally, which made every `ON DELETE CASCADE` in the schema unexecutable and made two of the messages say the opposite of what the trigger did: PostgreSQL deletes the parent row first and then fires the child's `BEFORE DELETE` trigger, so deleting an order or a garment job aborted on a `restrict_violation` whose own text said that case was allowed. Each now refuses only while the parent row still exists — `NOT EXISTS (SELECT 1 FROM …)` against a parent already gone from this transaction's view — and returns `OLD` otherwise. *(b)* `garment_job_price_is_immutable` compared two whole-row `to_jsonb()` documents on every `UPDATE` of the most-written table in the schema; it is an explicit `IS DISTINCT FROM` chain over the twenty-two `price_…` columns, which keeps the property the column list was chosen for — a column added later has to be considered here — without serialising the row. *(c)* the one generic `display_number_is_immutable` read its column through `to_jsonb(NEW) ->> …`, serialising the row twice to read one text column, and is three three-line functions instead. Two model changes came with them: `estimates` gained `updated_at`/`updated_by`, because it carries `xmin` and is rewritten by `Supersede`, `Convert` and `RecordArtefact` — `src/Modules/CLAUDE.md` section 5 asks for the pair on an editable row, and without it the row could not say when it last changed; and `ck_garment_jobs_delivered_is_consistent` was weakened from `(delivered_at IS NOT NULL) = (status IN ('Delivered', 'Closed'))` to `status <> 'Delivered' OR delivered_at IS NOT NULL`, because the converse is the naive shape the `orders` table deliberately omits one aggregate level up and would have to be contracted out at N+2 when issue #34's post-delivery alteration lands. **To verify on CI:** that deleting an order cascades through its garment jobs, revisions, dependencies and both snapshot tables without a `restrict_violation` — note specifically that `job_dependencies` holds two foreign keys into `garment_jobs`, `garment_job_id` cascading and `prerequisite_garment_job_id` restricting, so a garment job that is another garment's prerequisite may still abort the cascade depending on the order PostgreSQL resolves the two in, and the `job_dependencies_append_only` trigger then has to agree with whichever way that resolves; this is the one part of the delete path a review could not settle without a database, and it is reachable only from a hard delete, which no business path performs — an order is cancelled, never deleted, and that deleting a snapshot row, a revision or a dependency on its own is still refused; that the two immutability triggers accept `Order.Revise` on a confirmed order and refuse it afterwards, that an insert into `garment_jobs` carrying `is_ready_for_delivery = true` is refused, that the two ready-state check constraints refuse a ready row carrying blocking reasons and a ready row with no recorded evaluation, that a display number cannot be updated on any of the three tables, that a garment job with `status = 'Delivered'` and no `delivered_at` is refused while one carrying `delivered_at` in another status is accepted, and that `Down` empties the schema to the history table alone and the migration re-applies cleanly |

## Checklist

- [ ] The migration touches only its own module's schema.
- [ ] It is expand-only, or the thing it contracts was expanded at least two releases ago.
- [ ] `Down` exists and was executed at least once.
- [ ] Explicit SQL carries a comment explaining why the model could not express it.
- [ ] It applies to an empty database and to the N-1 snapshot.
- [ ] Any new column holding personal data is classified in `docs/nfr/data-classification.md`.
