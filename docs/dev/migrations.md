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

## Checklist

- [ ] The migration touches only its own module's schema.
- [ ] It is expand-only, or the thing it contracts was expanded at least two releases ago.
- [ ] `Down` exists and was executed at least once.
- [ ] Explicit SQL carries a comment explaining why the model could not express it.
- [ ] It applies to an empty database and to the N-1 snapshot.
- [ ] Any new column holding personal data is classified in `docs/nfr/data-classification.md`.
