# Idempotency, concurrency and request timeouts

How a command survives being sent twice, how an edit survives two people making it at once, and what
happens to a request the server stops waiting for.

The three belong in one document because they answer three questions about the same request, in a fixed
order: *may you do this* (authorisation, [`../security/permission-matrix.md`](../security/permission-matrix.md)),
*have you already done this* (idempotency), *is this still the version you were looking at* (concurrency),
and *is this taking too long* (timeouts). The conventions they implement are
[`../architecture/conventions.md`](../architecture/conventions.md) sections 4 and 5 and plan Section 4.4;
this document is the operator-level description of the mechanism.

---

## 1. Idempotency

### 1.1 The case it exists for

A cashier takes a payment on a counter tablet. The shop's connection drops before the response arrives,
so the client retries. Without a record of the first attempt the customer is charged twice, and nobody
finds out until the card statement. Everything below follows from wanting that to be impossible even when
the process dies, the request times out, or two attempts arrive at the same moment.

### 1.2 The contract

| Element | Value |
| --- | --- |
| Request header | `Idempotency-Key`, a UUID the **client** generates for one attempt at one action |
| Response header on a replay | `Idempotency-Replayed: true` |
| Record key | `(principal id, method and route template, client key)` |
| Stored | request fingerprint, status, response status code and body, `created_at`, `in_flight_until`, `expires_at` |
| Retention | `max(2 × Idempotency:MaximumOfflineQueueAge, 7 days)` — seven days by default |

The key is a UUID and nothing else. A key a client assembles from its own state — a till number, an order
reference — collides with itself the next time that till takes a payment, so a non-UUID key is refused
rather than accepted and silently mis-scoped.

The **fingerprint** is a SHA-256 over the method, the resolved path, the query string and the body. The
resolved path matters: records are keyed by the route *template*, so `POST /api/v1/orders/{orderId}/confirm`
is one route for every order. Without the path in the fingerprint, confirming order 8 under the key already
used for order 7 would be answered with order 7's response and order 8 would never be confirmed. Only the
hash is stored — a body kept verbatim would be a week of measurements, names and phone numbers in a
platform table nobody classified.

### 1.3 What the caller gets

| Situation | Answer |
| --- | --- |
| No `Idempotency-Key` on an endpoint that requires one | `400 idempotency.key-required` |
| A key that is not a UUID | `400 idempotency.key-invalid` |
| A new key | The command runs |
| The same key, the same request, first attempt finished | The stored response, with `Idempotency-Replayed: true` |
| The same key, a **different** request | `422 idempotency.key-reused` |
| The same key, first attempt still running | Wait up to `Idempotency:Requests:DuplicateWaitBudget` (5 s); replay it if it finishes, otherwise `409 idempotency.in-progress` with `Retry-After` |
| The same key, first attempt's lease expired | This request takes the claim over and runs |

Authentication and authorisation run **before** the record is read, so a replay presented by a revoked or
unauthorised principal is refused rather than served from the store.

### 1.4 The two hard cases, decided

**The same key with a different body is `422`, not a replay and not a second execution.** Answering with
the first response would silently swallow a second, different payment; running the command would break the
promise the key made. Both are worse than telling the client it is wrong, which is what a reused key is.

**A duplicate arriving while the first attempt is in flight waits, then conflicts.** It never runs
concurrently. The wait exists because of the shape of the duplicate this mostly catches: somebody pressed
*Take payment* twice on a slow connection, and the first attempt is usually a few hundred milliseconds from
finishing. Waiting turns the second press into the same receipt instead of an error a cashier has to
interpret in front of a customer. It is bounded at five seconds because a waiting request holds a thread, a
database connection from a budget of thirty, and a person's attention; past that, "try again in a moment"
is more honest and cheaper. The wait is spent inside the duplicate's own request, so it must stay well
under the request timeout that request is running under — five seconds against thirty leaves ample room.

### 1.5 What binds a key, and what does not

| The command answered | The record |
| --- | --- |
| `2xx` or `3xx` | **Stored.** A retry replays it |
| `4xx` | **Released.** The key is free again |
| `5xx`, an unhandled exception, or a request timeout | **Held** until the lease expires |

A `4xx` changed nothing, and the client is expected to correct it and send it again *under the same key* —
which is exactly what the design system's conflict flow and the in-place re-authentication of plan Section
4.4 do. A stored refusal would replay itself forever and make both impossible.

A `5xx`, an exception and a timeout are different: nobody knows whether the command took effect. Releasing
the key would invite the client to run it again on a guess, which is the one thing that must not happen, so
the claim stands and the honest answer to an immediate retry is `409 idempotency.in-progress`.

### 1.6 What the record holds, and for how long

The **request** is only ever a hash. The **response** is stored verbatim, because replaying it is the whole
point — and that means a command whose receipt carries a customer's name or phone number has that value in
`platform.idempotency_keys` for the retention window. Three consequences, none of them optional:

1. A command's response body is subject to the same field minimisation as any other response
   ([`../nfr/data-classification.md`](../nfr/data-classification.md)); it is not a place to return more than
   the screen needs because "it is only a receipt".
2. `platform.idempotency_keys` is in scope for the erasure and pseudonymisation work of issue #57, alongside
   the tables the data classification already names.
3. Nothing else about the request is kept. There is no request body, no header set and no caller address in
   the record — only the principal identifier, the route, the key and the hash.

### 1.7 The lease

A claim says "I am running this now, and I expect to be finished by `in_flight_until`". Without it, a
process that dies mid-command holds the key for the whole seven-day retention: the customer is at the
counter, the payment did not happen, and every retry is refused for a week. The lease bounds that to
`Idempotency:InFlightLease` — 45 seconds — after which the next request takes the claim over in the same
atomic statement that would otherwise have inserted it.

The lease must be **longer than the request timeout of every endpoint that declares idempotency**, or a
claim could be taken over while its first holder was still working, which would run one command twice at
once — something the fencing in section 1.6 does not prevent, because both executions are real.

It is deliberately not longer than the *longest* timeout in the catalogue. The lease is also how long a
crashed process holds a key, and a customer at a counter whose payment died should not wait out an export's
two minutes to retry; the 45-second lease outlives the 30-second command timeout that a command-shaped
endpoint actually uses. The pairing is therefore bounded per endpoint rather than globally, by
`EndpointRequestSafetyTests.AnIdempotentEndpointFinishesInsideItsClaimLease` over the composed route table,
which fails an endpoint that declares idempotency alongside a timeout the lease does not outlive.

### 1.8 The window this does not close, and who closes it

The record is written after the command commits and **before** the response bytes reach the network, which
is what makes the dropped-connection case work. A process that dies in between — microseconds — leaves a key
that will be re-executed once its lease expires, and no filter outside the command's own transaction can
close that.

**So a command that moves money, stock or custody carries its own transactional guard as well**: a unique
index on the natural key of the effect, so a second execution fails on the constraint rather than producing
a second row. That is what the payment-intent design of plan Section 4.4 and issue #55 rely on — record the
intent and its outbox message in one transaction, call the provider with the intent identifier as *its*
idempotency key, and apply only a verified outcome. The endpoint filter and that guard are two layers of
the same promise, and neither replaces the other.

### 1.9 Declaring it

```csharp
endpoints.MapPost("/api/v1/billing/invoices/{invoiceId:guid}/payments", RecordPaymentAsync)
    .RequirePermission(BillingPermissions.PaymentsRecord)
    .Audited("billing.payment-recorded")
    .RequireIdempotency()
    .WithRequestTimeout(RequestTimeoutPolicies.Command);
```

Declare it on every command a client may reasonably send twice — confirm, scan, post, pay, callback,
webhook replay (plan Section 4.4) — which in practice is every command the progressive web application can
queue offline or retry after re-authenticating.

---

## 2. Concurrency

### 2.1 The contract

Editable aggregates use PostgreSQL's `xmin` as their concurrency token
([`../architecture/conventions.md`](../architecture/conventions.md) section 4.1). It travels to the client
as a strong `ETag` and comes back as `If-Match`.

| Situation | Answer |
| --- | --- |
| `GET` of an editable aggregate | `200` with `ETag: "48213"` |
| Update with a matching `If-Match` | The change is made; the response carries the new `ETag` |
| `If-Match` absent | `428 concurrency.if-match-required` (**COD-05**) |
| `If-Match` present but not a strong entity tag — a weak validator, an unquoted token, a list | `400 concurrency.if-match-invalid` |
| `If-Match: *` | Matches any version of a record that exists |
| `If-Match` stale | `409 <module>.version-conflict`, carrying `currentVersion` and `currentEtag` |

428 rather than 400 for the missing header is recorded decision **COD-05**: the client can tell "you forgot
the precondition", which it fixes by re-reading and resending, from "your payload is wrong", which it
cannot. A weak validator is refused rather than compared, because a weak tag means "semantically
equivalent" and that is not a basis on which to overwrite somebody else's edit.

### 2.2 The 409 carries the current version, always

```json
{
  "type": "urn:tailor360:problem:orders.version-conflict",
  "title": "This record changed since you opened it",
  "status": 409,
  "code": "orders.version-conflict",
  "detail": "The garment job was updated by another user. Reload to see the current values.",
  "correlationId": "5b2c1a9e-1f0d-4a7c-9e6f-2b8d3c4a5e6f",
  "currentVersion": "48219",
  "currentEtag": "\"48219\"",
  "retryable": false
}
```

Without the current version a client can only say "no". With it, it can show both values and let the person
choose, which is what the design system requires: **never discard what the person typed**. A retry after a
conflict reuses the *same* `Idempotency-Key`, which is why a `4xx` releases its claim (section 1.5).

### 2.3 Declaring it

```csharp
endpoints.MapPut("/api/v1/customers/{customerId:guid}", UpdateCustomerAsync)
    .RequirePermission(CustomersPermissions.Update)
    .Audited("customers.updated")
    .RequireIfMatch()
    .WithRequestTimeout(RequestTimeoutPolicies.Command);
```

`RequireIfMatch()` answers the two questions that can be answered before the aggregate is loaded — was the
header there, and is it a strong tag. Whether the version is *current* is a question only the handler can
answer, and it answers it with one call:

```csharp
var refusal = ConcurrencyResults.CheckIfMatch(
    http, db.EntityTagOf(customer), "customers.version-conflict", "…Reload to see the current values.");

if (refusal is not null)
{
    return refusal;
}
```

Append-only aggregates — scan events, ledger entries, payments, posted invoices, audit events — carry no
version and no `If-Match`. Their protection is the idempotency record and a unique index.

---

## 3. Request timeouts

### 3.1 The catalogue

| Policy | Budget | Protects |
| --- | --- | --- |
| `read` | 10 s | Read latency, SLO **S2**, p95 &lt; 400 ms |
| `command` | 30 s | Command latency, SLO **S3**, p95 &lt; 800 ms — and the default for an endpoint that declares nothing |
| `report` | 60 s | Report and read-model queries, p95 &lt; 1.5 s |
| `export` | 120 s | Export generation, p95 &lt; 60 s |

A timeout is not a latency target; it is the point past which the request is certainly broken rather than
merely slow. Each is set an order of magnitude above the objective it protects in
[`../nfr/slo.md`](../nfr/slo.md) section 5, so a timeout firing is a defect and never a busy afternoon. The
availability indicator is defined over these numbers — a *good* request is one answered below 500 **within
the request timeout** — so they are part of the service-level objective, not an implementation detail. They
are **proposed, to be confirmed** alongside the rate-limit numbers of issue #19.

A default policy is deliberate, and it is the one difference from the rate-limit catalogue, where every
endpoint must declare a policy because the wrong limit is a security decision. Here the wrong answer is an
unbounded request, and an endpoint nobody remembered to annotate is exactly the one that will hang. An
endpoint that genuinely needs longer — a media stream, a large import — declares its own policy built with
`RequestTimeoutPolicies.Create`, so that every timed-out request in the system still answers the same way.

### 3.2 A timed-out request leaves nothing behind

The timeout cancels `HttpContext.RequestAborted`; every database call is made with that token; a cancelled
`SaveChanges` rolls its transaction back. Nothing written by a request that ran out of time survives. The
caller gets `504 request.timeout` as problem details — never a bare status line, which a client cannot tell
apart from a reverse proxy giving up — and, because the outcome is unknown to the caller, the request's
idempotency claim stands until its lease expires (section 1.5).

---

## 4. Host wiring

Three registrations and two pipeline steps.

```csharp
builder.Services.AddTailor360Security();     // calls AddTailor360RequestSafety(): options, clock, timeouts

app.UseRouting();
app.UseTailor360IdempotencyKeys();           // after UseRouting, before the endpoints run
app.UseRequestTimeouts();
```

`UseTailor360IdempotencyKeys()` makes request bodies re-readable, and only for requests that carry an
`Idempotency-Key`. A minimal-API endpoint filter runs *after* its parameters are bound, so by the time the
idempotency filter is asked to fingerprint a request its body has already been read to the end. A host that
omits the step does not quietly lose the guarantee: every idempotent endpoint with a body refuses, and the
refusal is logged as the defect it is.

## 5. Configuration

| Setting | Default | Meaning |
| --- | --- | --- |
| `Idempotency:MaximumOfflineQueueAge` | 3 d | The longest a request may sit in a device's offline queue |
| `Idempotency:InFlightLease` | 45 s | How long a claim is good for; must exceed the longest request timeout |
| `Idempotency:Requests:DuplicateWaitBudget` | 5 s | How long a duplicate waits for the first attempt |
| `Idempotency:Requests:DuplicatePollInterval` | 100 ms | How often the waiting duplicate re-reads the record |

`Idempotency:Retention` is derived, not configured: `max(2 × MaximumOfflineQueueAge, 7 days)`. The two
cannot drift apart, because a record deleted before its replay could arrive would let the command run a
second time.

## 6. Where the tests are

| Behaviour | Test |
| --- | --- |
| Fingerprints, entity tags, and the two configuration relationships | `tests/Tailor360.UnitTests/Platform/RequestSafetyTests.cs` |
| Replay, key reuse, duplicates in flight, dropped connections, leases, per-caller scoping | `tests/Tailor360.IntegrationTests/Platform/IdempotentCommandTests.cs` |
| `ETag`, `If-Match`, the 409 body, recovery from a conflict | `tests/Tailor360.IntegrationTests/Platform/ConcurrencyContractTests.cs` |
| Rollback on timeout, and a timed-out command's claim | `tests/Tailor360.IntegrationTests/Platform/RequestTimeoutTests.cs` |
| The record store itself | `tests/Tailor360.IntegrationTests/Platform/PlatformServiceTests.cs` |

The integration tests run against a real PostgreSQL instance, over endpoints of the shape every later
command will have. No module publishes a payment, a scan or an editable aggregate yet; waiting for one would
have meant shipping the mechanism all of them depend on without anyone ever having watched a retried
payment fail to become a second payment.

## 7. Related documents

| Document | What it covers |
| --- | --- |
| [`../architecture/conventions.md`](../architecture/conventions.md) | The conventions this implements: sections 4 (concurrency) and 5 (API versioning) |
| [`../nfr/slo.md`](../nfr/slo.md) | The latency objectives the timeout budgets are derived from |
| [`outbox.md`](outbox.md) | The other half of "exactly once": how a committed fact reaches other modules |
| [`database.md`](database.md) | The connection budget a waiting request spends from |
