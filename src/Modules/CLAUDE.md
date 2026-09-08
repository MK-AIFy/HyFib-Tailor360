# `src/Modules` — module rules

Rules for working inside a module. Read [`../../CLAUDE.md`](../../CLAUDE.md) first; this file carries only what is
specific to this tree. The authorities behind it are
[`../../docs/architecture/module-ownership.md`](../../docs/architecture/module-ownership.md) (what each module
owns), [`../../docs/architecture/architecture-rules.md`](../../docs/architecture/architecture-rules.md) (the
`ARCH-…` assertions) and [`../../docs/architecture/conventions.md`](../../docs/architecture/conventions.md) (money,
time, identifiers, concurrency, versioning).

Eleven modules — Identity, Customers, Catalog, Media, Orders, Custody, Inventory, Billing, Reporting,
Notifications, Integration — all the same shape. A module is a vertical slice that owns exactly one PostgreSQL
schema and one object-storage prefix, and exposes them only through published contracts.

---

## 1. The five projects

`src/Modules/<Module>/Tailor360.Modules.<Module>.<Layer>`, one directory per module, five projects each.

| Layer | Holds | May reference |
| --- | --- | --- |
| `Domain` | Aggregates, value objects, invariants, domain events. No framework, no persistence, no HTTP | `Platform.Abstractions` and nothing else (ARCH-001, ARCH-002) |
| `Application` | Commands, queries, validators, authorisation requirements, ports, integration-event mappers | Own `Domain`, own `Contracts`, `Platform.*`, another module's `Contracts` (ARCH-003) |
| `Infrastructure` | The EF `DbContext`, entity configuration, repositories, adapters, outbox handlers, the registration extension | Own `Domain`/`Application`/`Contracts`, `Platform.*`, another module's `Contracts` |
| `Api` | Minimal API endpoint groups and every request and response payload type | Own `Application`/`Contracts`, `Platform.*` |
| `Contracts` | Integration events, read contracts and the types they carry — the module's published surface | `Platform.Abstractions` only |

Two consequences that catch people out:

- **`Contracts` never references another module's `Contracts`.** That is what keeps the project graph acyclic even
  where two modules consume each other — Orders calls `ICustodyStateQuery` while Custody calls
  `IOrderSnapshotQuery`, and neither project references the other.
- **A type that leaves the module lives in `Contracts`.** If an application service wants to hand a `Domain` entity
  to another module, the answer is a contract type, not a project reference.

## 2. The boundary

**Only `Contracts` and `Platform.*` cross a module boundary** (ARCH-004). Everything else is internal, including
the `DbContext`, repositories, entities and endpoint handlers.

**No module reads or writes another module's tables** — no `SELECT`, no join, no view over another schema's base
tables, no direct SQL, no shared entity type. A read across a boundary is a contract call, not a query; a write
across a boundary is a command or an event, not an `INSERT`.

Exactly five mechanisms cross the boundary, and there is no sixth:

| Mechanism | Shape | Use it for | Consistency |
| --- | --- | --- | --- |
| Read contract | An interface in the owner's `Contracts`, consumer pulls | A decision that must be made now against current state | Strong, within the request |
| Integration event | Versioned event through the transactional outbox, owner pushes | Reacting to a fact, projections, notifications, relay | Eventual, at least once |
| Confirmation-participant hook | Synchronous, inside the owner's transaction | Work that must be atomic with a confirmation | Strong, same transaction |
| Host composition | The web host merges several modules for one screen | Screens that need several modules, such as the customer timeline | Strong per source |
| Platform port | An interface in `Platform.Abstractions` | Shared infrastructure: audit, sequences, print queue, outbound HTTP | As the port defines |

Anything else — a new shared table, a cross-schema view over base tables, a reference to another module's
`Infrastructure` — needs an architecture decision record in [`../../docs/adr/`](../../docs/adr/) **before** it is
merged. Billing in particular never references Orders (ARCH-010): it learns order facts from integration-event
payloads deserialised into Billing-owned types and from the identifiers and priced lines in the command that asks
it to post an invoice.

## 3. Composition

A host never sees a module's internals. Each module publishes two extension methods and the host calls them
(ARCH-006, ARCH-012):

```csharp
// Infrastructure/<Module>ModuleServiceCollectionExtensions.cs
public const string SchemaName = "orders";
public static IServiceCollection AddOrdersModule(this IServiceCollection services, IConfiguration configuration)

// Api/<Module>Endpoints.cs
public const string GroupPrefix = "/api/v1/orders";
public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder endpoints)
```

Permissions are registered from the module's `Application` project as an `IPermissionSource`. The catalogue is
composed at start-up and rejects a key claimed by two modules, so every permission has exactly one owner.

## 4. Endpoints

Route shape is `/api/v1/{module}/{resource}`; commands are `POST` sub-resources (`/orders/{id}/confirm`). Every
endpoint declares what it demands, using the extensions in `Tailor360.Platform.Security.Endpoints`:

```csharp
group.MapPost("/{id}/confirm", ConfirmAsync)
     .RequirePermission("orders.confirm", BranchScope.CurrentBranch)   // ARCH-007
     .Audited("orders.confirm");                                       // ARCH-008
```

The permission key is a constant on the module's `IPermissionSource`, not a literal typed at the call site; the
scope is `CurrentBranch`, `AssignedBranches` or `Organisation`, and `CurrentBranch` is the default for anything
touching branch-owned data.

- An endpoint deliberately reachable without a session uses
  `AllowAnonymousWithJustification(justification, reviewedIn)` — never a bare `AllowAnonymous()`.
- **Payload types are declared in the `Api` project.** A `Domain` type is never accepted or returned (ARCH-013).
- Errors are RFC 9457 problem details with field errors: no stack trace, no raw exception message.
- Retryable endpoints take `Idempotency-Key` through `IIdempotencyStore`; a replay returns the first outcome, and
  "same key, different body" is a conflict rather than a second effect.
- Cursor pagination, `filter[...]`, `sort`, `fields`; `X-Correlation-Id` and `X-Client-Version` on every request.
- Within a major API version, changes are additive only.

## 5. Persistence

The module's `DbContext` derives from `ModuleDbContext` and passes its schema name. That base fixes the
conventions once, so a module cannot drift from them by omission:

| Convention | Set by the base |
| --- | --- |
| Schema isolation | `HasDefaultSchema(schema)`; mapping a table in another module's schema fails ARCH-005 |
| Timestamps | Every `DateTime`/`DateTimeOffset` column is `timestamptz`; instants are stored in UTC |
| Money | `decimal(18,4)`. **Never `double` or `float` for money**, anywhere |
| Concurrency | `UseRowVersion(builder)` maps PostgreSQL's `xmin` as the optimistic-concurrency token |
| Migration history | `__ef_migrations_history` inside the module's own schema |

Tables and columns are `snake_case`. Every table carries `id`, `organisation_id`, `branch_id` where it is scoped,
`created_at`, `created_by`, `updated_at`, `updated_by` and `xmin`. Append-only tables — audit events, ledger
entries, scan events, custody transfers, posted invoices, payments, receipts, QC results — are protected by
database triggers that reject `UPDATE` and `DELETE` from the application role. Business records are never
soft-deleted; deactivate, retire or cancel instead.

Publishing an integration event is meant to write a row in the same transaction as the change:

```csharp
await publisher.PublishAsync(new OrderConfirmed(…), cancellationToken);
await context.SaveChangesAsync(cancellationToken);   // the event commits with the order
```

**It does not, yet, and a module must not publish an event alongside its own write until issue #77
closes.** `IEventPublisher` writes to `platform.outbox_messages` on `PlatformDbContext`, while the
change is on the module's context — two connections and two transactions, so one ordering loses events
for work that committed and the other announces work that rolled back.
[ADR-0008](../../docs/adr/0008-transactional-outbox-and-workers.md) decided an `outbox_messages` table
per module schema, which is what makes the snippet true; #21 built one shared table instead.
[`../../docs/platform/outbox.md`](../../docs/platform/outbox.md) has the detail. Use a read contract
until then — a consumer that pulls loses nothing in between.

`IAuditWriter` is the same two-context split and is a different case: it is a recorded trade-off with a
fixed ordering — **save the change first, then record it** — because the trail may lag reality and must
never lead it. See [`../../docs/platform/outbox.md`](../../docs/platform/outbox.md) and
[`../../docs/platform/database.md`](../../docs/platform/database.md).

## 6. Migrations

Full rules in [`../../docs/dev/migrations.md`](../../docs/dev/migrations.md). The four that decide whether a change
is acceptable:

1. **A migration touches only its own module's schema.**
2. **Expand in release N, contract no earlier than N+2.** Add the column, write both, then read the new one, then
   stop writing the old one, then drop it. A rename is those three steps, never a single `RENAME` — a rename looks
   atomic and is the most common cause of a failed rollback.
3. **Migrations are applied by the command line, never by the application.** Two versions run at once during a
   deployment; if either could migrate, they would race.
4. **Every migration has a working `Down` that has been executed at least once.** An untested `Down` is not a
   rollback plan.

```bash
dotnet ef migrations add <Name> \
  --project src/Modules/<Module>/Tailor360.Modules.<Module>.Infrastructure \
  --startup-project src/Modules/<Module>/Tailor360.Modules.<Module>.Infrastructure \
  --context <Module>DbContext --output-dir Migrations

dotnet run --project src/Tools/Tailor360.Cli -- migrate --dry-run
dotnet run --project src/Tools/Tailor360.Cli -- migrate
```

Review the generated file before committing it; the scaffolder is a starting point, not an authority. Anything the
model cannot express — partitioning, triggers, functions, grants — goes in as `migrationBuilder.Sql(...)` with a
comment saying why. A new column holding personal data is classified in
[`../../docs/nfr/data-classification.md`](../../docs/nfr/data-classification.md) in the same pull request. Never
edit a migration that has been applied anywhere but a developer machine; add another one.

## 7. Rules the architecture tier enforces immediately

Each of these fails `tests/Tailor360.ArchitectureTests` on the first run, before anything is deployed or reviewed.
The last column says what the test reads, which is also what it can and cannot see: a **source scan** matches the
`.cs` text of every non-test project under `src/` (comments are skipped), while the **project graph** reads every
`*.csproj` on disk, so it catches a package reference nobody has called yet.

| Instead of | Use | Rule | Read from |
| --- | --- | --- | --- |
| `DateTime.UtcNow`, `DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` | `IClock.UtcNow`, `IClock.TodayIn(branchTimeZone)` | ARCH-014 | Source scan |
| `Guid.NewGuid()` | `IIdGenerator` (UUIDv7) | ARCH-015 | Source scan |
| `new HttpClient(...)` | `IOutboundHttp` | ARCH-016 | Source scan |
| A vendor SDK package anywhere but `Integration.Infrastructure` | An adapter behind a port | ARCH-009 | Project graph |

Business dates — financial year, promised delivery, cashier sessions — are evaluated in the branch timezone, never
in UTC. Identifiers in API paths, deep links and customer links are UUIDv7; sequential integers are never exposed,
and personal data is never a key, a barcode payload, an object-storage key or a filename.

## 8. Before opening a pull request

```bash
dotnet format --verify-no-changes
dotnet build HyFib.Tailor360.slnx
./scripts/dev test unit
./scripts/dev test architecture
./scripts/dev test contract
./scripts/dev test integration     # needs TAILOR360_TEST_DATABASE_URL or a Docker daemon
```

Warnings are errors (`Directory.Build.props`), so a build that is merely noisy does not exist here. If an
architecture rule fails and you believe the design is right, change the rule in
`docs/architecture/architecture-rules.md` in the same pull request with the rationale and an exception-register
entry — never by weakening the test.
