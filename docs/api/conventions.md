# API conventions

How to design an endpoint on this API: the boundary it sits behind, the shape a resource and a command
take, what a collection response looks like, and which limits every request runs under.

It is the surface guide. The rules that are not specific to HTTP live elsewhere and **those documents
win** — this one links to them rather than restating them, because a convention written down twice is a
convention that will disagree with itself:

| For | Read |
| --- | --- |
| Money, time, identifiers, concurrency, versioning, deprecation | [`../architecture/conventions.md`](../architecture/conventions.md) |
| The problem-details envelope, retries, `ETag`/`If-Match` in practice | [`../platform/idempotency-and-concurrency.md`](../platform/idempotency-and-concurrency.md) |
| How the published document is generated and gated | [`openapi-gates.md`](openapi-gates.md) |
| Routes that publish no contract | [`internal-endpoints.md`](internal-endpoints.md) |
| The request pipeline these conventions run inside | [`../architecture/components.md`](../architecture/components.md) section 5 |

---

## 1. The boundary

The API is a **backend-for-frontend**: one ASP.NET Core host serves both the progressive web application
and `/api/v1/**`, on one origin ([ADR-0006](../adr/0006-bff-cookie-session.md)). That is not a
deployment detail; it is what makes the rest of the security model possible.

- The session is an `HttpOnly; Secure; SameSite=Lax` cookie, `__Host-t360.session`, which script cannot
  read and therefore cannot leak. There is no token in a response body, in `localStorage`, or in a URL.
- Because there is one origin, no CORS grant exists to be widened later.
- Every non-safe request carries the anti-forgery request token in `X-CSRF-Token`, and its `Origin` and
  `Sec-Fetch-Site` are checked before anything else runs.

The consequence for design: **this API is shaped for one client, and that is deliberate**. An endpoint
may answer exactly what a screen needs, in one round trip, rather than making the client assemble it from
three generic resources. A third-party surface will be `/api/ext/v1/**` and will be designed separately;
do not compromise a screen's endpoint to make it a better public API it is not.

## 2. Resources and commands

Two shapes, and the choice between them is not stylistic.

**A resource** is a noun with state a client reads and edits.

```
GET    /api/v1/customers/{customerId}
PATCH  /api/v1/customers/{customerId}
GET    /api/v1/customers
POST   /api/v1/customers
```

**A command** is a verb: something the system *does*, whose effect is not "these fields now hold these
values". It is a `POST` to a sub-path named after the action.

```
POST /api/v1/orders/{orderId}/confirm
POST /api/v1/invoices/{invoiceId}/payments
POST /api/v1/garments/{garmentId}/scan
```

Choose a command whenever the operation has a precondition the client cannot evaluate, produces effects
beyond the row, or must be audited as an action rather than as an edit. Confirming an order allocates
barcodes and writes an outbox message; expressing that as `PATCH { "status": "confirmed" }` invites a
client to set any status it likes and makes the audit trail read as a field change.

Never accept a state transition as a client-supplied value on an edit. Business state changes through
commands, which check the transition server-side.

### 2.1 Methods and their statuses

| Method | Means | Answers |
| --- | --- | --- |
| `GET` | Read. Safe, and never changes state — not even a counter | `200`, or `404` |
| `POST` | Create, or run a command | `201` with `Location` for a create; `200` with the outcome, or `202` when a worker will finish the work |
| `PUT` | Replace the whole representation. Rare here | `200` or `204` |
| `PATCH` | Merge the named fields. `application/json` with only the members being changed | `200` or `204` |
| `DELETE` | Remove, or revoke | `204` |

`POST` is the only method that may have effects a retry would duplicate, which is why it is the method
that takes `Idempotency-Key`.

## 3. Requests

### 3.1 Headers every request carries

| Header | Sent by | For |
| --- | --- | --- |
| `X-Correlation-Id` | The client, on every request | Ties a user action to every log line, outbox message and downstream effect. Echoed on every response, including a failure |
| `X-Client-Version` | The client, on every request | The client-version handshake — [`../architecture/conventions.md`](../architecture/conventions.md) section 5.4 |
| `X-CSRF-Token` | The client, on every non-safe request | The request half of the anti-forgery pair |
| `Idempotency-Key` | The client, on a retryable command | A client-generated UUID. The **caller** holds it across retries; a fresh key on a retry is a second payment |
| `If-Match` | The client, on an edit to a versioned aggregate | The `ETag` the client last read. A missing one is refused, never treated as "no opinion" |

### 3.2 Validation

Validation runs server-side, before any idempotency record is touched, so a malformed request is refused
without consuming a key. The answer is `400` with problem details and per-field `errors`; a client never
sees an exception message, a stack trace or a database constraint name.

A field the client did not send and a field it sent as `null` are different things on a `PATCH`: the
first is "leave it alone", the second is "clear it". An endpoint that cannot express that distinction
takes an explicit member rather than guessing.

### 3.3 Limits

| Limit | Where it is set |
| --- | --- |
| Rate limit | Exactly one policy per endpoint from the catalogue (**ARCH-017**), declared with `.RequireRateLimiting(...)`. A `429` carries `Retry-After` and problem details |
| Request timeout | A policy per endpoint from `RequestTimeoutPolicies`. A request past its deadline is cancelled, and the cancellation reaches the database |
| Request size | The host's body-size limit. Media is streamed by its own endpoints, never posted as a JSON member |
| Cancellation | Every handler takes a `CancellationToken` and passes it down. A client that navigates away must not leave a query running |

## 4. Responses

### 4.1 A single resource

The payload type is declared in the module's `Api` project (**ARCH-013**) and published on the endpoint
with `.Produces<T>(StatusCodes.Status200OK)`, which is what puts its schema in the document. A `Domain`
type is never returned.

An aggregate a client may edit carries an `ETag`; the client sends it back in `If-Match`. The rules are
[`../architecture/conventions.md`](../architecture/conventions.md) section 4.

### 4.2 A collection

Cursor pagination, never offset. An offset page shifts under a shop floor that is inserting rows while
somebody reads, and the reader silently skips a job.

```json
{
  "items": [],
  "nextCursor": "b3JkZXJzOjAxOTli…",
  "hasMore": true
}
```

- `nextCursor` is opaque. A client stores it and sends it back; it never parses it, and its contents are
  not part of the contract.
- `hasMore` is present so an empty last page is not required to discover the end.
- No `totalCount` unless a screen genuinely needs one. It costs a second query on every page, and on a
  filtered list over a large table it is the most expensive part of the request.

Filtering, sorting and sparse fields, when an endpoint supports them:

| Query | Shape | Notes |
| --- | --- | --- |
| Filter | `filter[status]=ready&filter[branchId]=…` | Only fields the endpoint declares. An unknown field is `400`, never ignored — silently ignoring a filter shows somebody more than they asked for |
| Sort | `sort=-dueAt,createdAt` | Leading `-` for descending. Only declared fields |
| Sparse fields | `fields=id,displayNumber,status` | Narrows the payload. It never widens it, and it never bypasses field-level masking |
| Page size | `limit=50` | Bounded by the endpoint. A request above the bound is clamped, not refused |

Sparse fields and filters are **not** an authorisation surface. What a caller may see is decided by
permission, branch scope and field masking; `fields` only chooses among what they were already allowed.

### 4.3 Errors

RFC 9457 problem details, `application/problem+json`, with a `urn:tailor360:problem:` type, a stable
dotted `code` the client branches on, the correlation identifier, and `retryable`. The envelope and the
code registry are [`../platform/idempotency-and-concurrency.md`](../platform/idempotency-and-concurrency.md).

Two rules that are easy to breach in a hurry:

- **A refusal never explains how to pass.** No header names, no expected values, no "the token should
  have been…". The `403` for a missing permission and the `404` for a row in another branch are
  deliberately indistinguishable from outside.
- **`404` is the answer for "no such thing" and for "none you may see".** Telling them apart lets an
  identifier space be probed.

### 4.4 Caching

Every authenticated response is either personal data or a security fact about the moment it was asked
for, so the client sends `cache: no-store` and endpoints returning either set `Cache-Control: no-store`
explicitly. Media is streamed by an endpoint that re-authorises each request; it never gets a URL a cache
or a log could hold.

## 5. Versioning, deprecation and the client handshake

All three are [`../architecture/conventions.md`](../architecture/conventions.md) section 5, which is
authoritative. In short: the major version is in the path and nothing else negotiates it; inside a major
version only additive changes are permitted, and the diff gate refuses a breaking one that is not
approved and recorded; a removal waits out the deprecation window behind `Deprecation` and `Sunset`
headers; and `GET /api/version` is the handshake, which is why it is the one path outside `/api/v1/`.

`GET /api/version` answers:

| Member | Is |
| --- | --- |
| `api` | The major surface this server serves, `v1` |
| `minimumClient` | The oldest client build it answers. Empty means it answers every build |
| `current` | The build it is serving. A different value in a running client means an update is available |
| `environment` | Lower case. Anything but `production` shows the training banner |
| `schemaVersion` | The newest migration timestamp the build carries. It changes only when the database shape changes |
| `commit` | The short revision — **Development only**, and absent rather than empty elsewhere |

A client below `minimumClient` is refused with `426` on every other endpoint, carrying `minimumClient`
and `current` so the update prompt can be written from the problem body.

## 6. Before an endpoint is merged

- It declares a permission or a justified anonymous exposure (**ARCH-007**), and never both (**ARCH-022**).
- It is audited if it changes state (**ARCH-008**).
- It declares exactly one rate-limit policy (**ARCH-017**) and one authentication scheme (**ARCH-019**).
- Its payload types live in an `Api` project, and it publishes them with `.Produces<T>(...)` (**ARCH-013**).
- It appears in [`openapi.v1.json`](openapi.v1.json), or is declared in
  [`internal-endpoints.md`](internal-endpoints.md).
- It carries a request example if it takes a body, and a summary and a tag whatever it takes.
- Its negative cases are tested: the other branch, the replayed key, the stale `If-Match`, the caller
  without the permission.
