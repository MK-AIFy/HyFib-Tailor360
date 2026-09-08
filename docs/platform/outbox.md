# Transactional outbox

How a fact one module records reaches the modules that need to know about it.

## The problem it solves

A module that confirmed an order and then called a notification service directly would have two ways to
be wrong: the call could succeed and the transaction roll back, so a customer is told about an order
that does not exist; or the transaction could commit and the call fail, so the order exists and nobody
is told. Neither is recoverable after the fact, because nothing records what was supposed to happen.

The outbox removes the choice. Publishing writes a row in the same transaction as the change. If the
transaction rolls back, so does the event. Delivery happens afterwards, from that row, with retries.

## Publishing

```csharp
await publisher.PublishAsync(new OrderConfirmed(id, occurredAt, orderId, …), cancellationToken);
await context.SaveChangesAsync(cancellationToken);   // the event commits with the order
```

`IEventPublisher` writes to `platform.outbox_messages`. It never sends anything.

### A module cannot do this yet — issue #77

The snippet above holds only where `context` is `PlatformDbContext`, and that is not where a module's
change lives. [ADR-0008](../adr/0008-transactional-outbox-and-workers.md) decided an `outbox_messages`
table **in each module's own schema**, so that the module's own context writes both the aggregate and
the event and one `SaveChangesAsync` commits them together. What issue #21 built is a single shared
`platform.outbox_messages` on the platform context.

The mechanism below — claims, leases, per-aggregate ordering, retries, dead-lettering, replay — is
built and tested and is not what is wrong. What is missing is the atomicity, and it is missing exactly
for the callers the pattern exists for: a module publishing beside its own write has two contexts, two
connections and two transactions, so saving the change first can commit work whose event is lost, and
saving the event first can announce work that rolled back. `OutboxTests` publishes and rolls back on
the platform context alone, which is why the suite is green.

Until #77 closes, **do not publish an integration event alongside a module's own write**. A read
contract is the alternative that works today: a consumer that pulls asks the owner at the moment it
needs the answer, and nothing is lost in between. That is why the Customers module publishes
`IConsentQuery`, `ICommunicationPreferenceQuery` and `ICustomerSnapshotQuery` and publishes none of its
three integration events yet.

## Delivery

The worker runs one or more dispatcher instances. Each cycle:

1. **Claim.** A single statement selects eligible messages `FOR UPDATE SKIP LOCKED` and stamps a lease.
   Skipping locked rows is what lets two instances work at once without blocking each other.
2. **Handle.** Each registered `IOutboxMessageHandler` for the event type runs, and an inbox row is
   written with the handler's name in the same transaction as the handler's own writes.
3. **Complete.** The message is marked processed and its lease cleared.

### Ordering within an aggregate

Only the **oldest undelivered message of each aggregate** is ever eligible for a claim. A later message
therefore cannot be delivered before an earlier one for the same aggregate, whatever the number of
dispatchers. Ordering the claim query instead would make reordering merely unlikely; this makes it
impossible. Messages for *different* aggregates proceed in parallel, which is where the throughput
comes from.

### Leases

A claim stamps `lease_owner` and `lease_expires_at`. A dispatcher that crashes mid-delivery leaves the
lease behind; once it lapses the message becomes eligible again and another instance takes it. No
operator has to clear anything.

The lease is **renewed while the handlers run**, every third of its duration. Without renewal a handler
that outlives the lease — an external provider stalling is enough — would let a second dispatcher claim
the same message and run the same handler at the same time. The inbox row cannot prevent that, because
it is written only after the handler returns, so both invocations would find no inbox row and both
proceed. Renewal is what makes "one message per aggregate in flight" true rather than usual.

Completion is conditional on still owning the lease. A dispatcher that lost its lease anyway logs a
warning and leaves the message to whoever holds it now, rather than marking work done that another
instance has yet to do.

### Retries and dead letters

A failed delivery moves `available_at` forward with exponential backoff plus jitter. The jitter matters
when a downstream provider recovers: without it every message that failed during the outage would retry
in the same instant and knock it over again. After `MaximumAttempts` the message is dead-lettered:
delivery stops, the error is recorded, and the outbox health check reports degraded.

The stored error is the exception type and message only, never the payload. A failure diagnostic must
not become a second copy of personal data in a column nobody thinks of as personal data.

### Replay

```bash
dotnet run --project src/Tools/Tailor360.Cli -- replay-outbox --dead-letter --reason "Provider outage resolved"
dotnet run --project src/Tools/Tailor360.Cli -- replay-outbox --id <guid> --reason "Fixed the malformed address"
```

A replay demands a reason and writes an audit entry. The same operation is exposed over HTTP behind
step-up authorisation — `GET /api/v1/admin/outbox/dead-letters` lists the queue and
`POST /api/v1/admin/outbox/{messageId}/replay` puts one message back — and both paths call
`IOutboxAdministration`, so a console replay and an endpoint replay do the same thing to the same rows
and write the same entry. Draining the whole dead letter stays console-only: it is a decision made with
the logs open after an outage has been diagnosed, and one operator's "everything" is another's
duplicate-delivery storm.

## Duplicate delivery

Delivery is at-least-once. The inbox row keyed by (message, handler) is what turns that into
at-most-once *effect*: a redelivered message finds its inbox row and the handler is skipped. Handlers
must still be written to tolerate being called twice, because the inbox row and the handler's writes
commit together but a handler that reaches an external system cannot be transactional with it.

## Health

The outbox health check is tagged non-essential, so a backlog degrades rather than removing the
instance from rotation. It reports degraded when any message is dead-lettered, or when the oldest
undelivered message is older than `BacklogWarningAge`. A growing backlog is invisible in the user
interface until someone asks why a message never arrived, which is why it is alerted on rather than
merely logged.

## Configuration

| Setting | Default | Meaning |
| --- | --- | --- |
| `Outbox:BatchSize` | 20 | Messages claimed per cycle |
| `Outbox:IdlePollInterval` | 2s | Wait after a cycle that found nothing |
| `Outbox:LeaseDuration` | 2m | How long a claim is held |
| `Outbox:MaximumAttempts` | 8 | Attempts before dead-lettering |
| `Outbox:RetryBaseDelay` | 5s | Base of the exponential backoff |
| `Outbox:RetryMaximumDelay` | 10m | Cap on the backoff |
| `Outbox:BacklogWarningAge` | 2m | Age at which the health check degrades |
