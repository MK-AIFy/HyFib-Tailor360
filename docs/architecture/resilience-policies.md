# Resilience policies

#58's sixth implementation step names seven mechanisms — timeout, bounded retry/backoff, circuit breaker,
bulkhead, queue limits, cancellation and safe degradation — and no document said which operation gets
which. This one does. `tests/Tailor360.ArchitectureTests/ResiliencePolicyCatalogueTests.cs` asserts every
file this document cites exists, so a citation that stops resolving because a file moved fails the build
rather than going unnoticed.

---

## 1. Operation classes

One row per class of operation this system performs, cited from the file that actually implements each
policy — never restated as a number here that could drift from the code.

| Class | Semantics | Timeout | Retry | Breaker / bulkhead | Cancellation leaves behind | Degradation visible to the user |
| --- | --- | --- | --- | --- | --- | --- |
| Synchronous command on the web host | At-most-once per attempt; retried safely only with `Idempotency-Key` | `RequestTimeoutPolicies.Command`, 30 s (`src/Platform/Tailor360.Platform.Security/Endpoints/RequestTimeoutPolicies.cs`) | None at this layer — a client retries with the same idempotency key, and `IdempotencyEndpointFilter` (`src/Platform/Tailor360.Platform.Security/Endpoints/IdempotencyEndpointFilter.cs`) returns the first outcome rather than repeating the effect | Neither; the endpoint's own dependencies (the database, object storage) carry their own | Nothing written: cancellation propagates to every database call and a cancelled `SaveChanges` rolls back, per `RequestTimeoutPolicies`'s own remarks | A 504, and the idempotency claim is left standing so a retry finds the same outcome once it exists |
| Safe read on the web host | Read-only, always retryable | `RequestTimeoutPolicies.Read`, 10 s | A client may simply retry; nothing is mutated | Neither | Nothing — a read has no effect to half-apply | A 504 |
| Outbox delivery (worker) | At-least-once, de-duplicated by the inbox at the consumer | None — a delivery attempt runs to completion or fails | Bounded exponential backoff with jitter, up to `OutboxOptions.MaximumAttempts` (8), then dead-lettered (`src/Platform/Tailor360.Platform.Persistence/Outbox/OutboxOptions.cs`, `src/Platform/Tailor360.Platform.Persistence/Outbox/OutboxDispatcher.cs`) | Neither a breaker nor a bulkhead: `OutboxDispatcherService`'s own loop (`src/Hosts/Tailor360.Worker/Jobs/OutboxDispatcherService.cs`) already survives a failing cycle without help, and dead-lettering is itself the circuit-breaking behaviour for this class | A failed delivery's message stays claimed until its lease expires, then becomes eligible again — never lost, never delivered twice while a lease holds it | None directly; a growing backlog is what `OutboxBacklogHealthCheck` (`src/Platform/Tailor360.Platform.Persistence/PlatformServiceCollectionExtensions.cs`) reports as `Degraded` |
| Scheduled job (worker) | Single-runner per job name | Per job | None generic; a job's own body decides | Bulkhead: the database-row lease in `JobLeaseService` (`src/Platform/Tailor360.Platform.Persistence/Scheduling/JobLeaseService.cs`) admits exactly one runner, taking over automatically if the holder's lease expires | The lease outlives a crashed holder only until it expires, then a fresh run takes over | None generic; per job |
| Object-storage call (both hosts) | Idempotent per key (`PutAsync` overwrites, `OpenReadAsync`/`ExistsAsync` are pure reads) | `ObjectStorageOptions.CallTimeout`, 10 s default (`src/Modules/Integration/Tailor360.Modules.Integration.Infrastructure/Storage/ObjectStorageOptions.cs`) | None — a caller that wants one retries at its own layer, now that a fast, distinguishable failure tells it whether retrying is worthwhile | **Both, new in this document**: `ResilientObjectStorage` (`src/Modules/Integration/Tailor360.Modules.Integration.Infrastructure/Storage/ResilientObjectStorage.cs`) wraps a bulkhead (`SemaphoreSlim`, sheds rather than queues) and `ObjectStorageCircuitBreaker` (`src/Modules/Integration/Tailor360.Modules.Integration.Infrastructure/Storage/ObjectStorageCircuitBreaker.cs`) around whichever adapter is selected | Nothing: a rejected or failed call never reaches the adapter, or its own result is discarded and `ObjectStorageUnavailableException` is thrown instead | `ObjectStorageHealthCheck` (`src/Modules/Integration/Tailor360.Modules.Integration.Infrastructure/Storage/ObjectStorageHealthCheck.cs`) reports `Degraded` on `/health/detail`; `503 media.unavailable` is the eventual caller-facing answer, owed by #188 |
| Outbound provider call (future adapters) | Per provider | Per provider | Per provider | Per provider, through `IOutboundHttp` once it has an implementation | Per provider | Per provider |

The last row is deliberately unfilled beyond "per provider": `IOutboundHttp`
(`src/Platform/Tailor360.Platform.Abstractions/Ports/IOutboundHttp.cs`) has no implementation yet — `grep
-rl IOutboundHttp src/` finds only the port and `src/Modules/CLAUDE.md` — matching
[`../security/abuse-cases.md`](../security/abuse-cases.md) `ABF-09`'s own honest gap and `NFR-SE-10`'s
"proof scheduled (#54, #55)". This document owes those sub-issues a row in this table, not an
implementation.

---

## 2. The object-storage resilience layer

The one dependency this system calls today that is neither the database nor in-process: `IObjectStorage`,
implemented by `MinioObjectStorage` or, in Development without an endpoint configured,
`InMemoryObjectStorage`.

### 2.1 Bulkhead

Bounds concurrent calls per host so a burst does not exhaust connections or memory. Sourced from
`docs/nfr/capacity-and-performance.md` section 2.4, not invented:

| Host | `ObjectStorage:Bulkhead:MaxConcurrentCalls` | Source |
| --- | --- | --- |
| Worker | 2 | "Bounded further by the worker `MediaProcessing` bulkhead of 2 concurrent decodes" |
| Web | 6 | The concurrent-image-upload row's mean figure |

A call past the limit is refused immediately (`ObjectStorageUnavailableException`), never queued —
`docs/nfr/capacity-and-performance.md` section 4's memory envelope has no allowance for a queue holding
connections open behind it.

### 2.2 Circuit breaker

Three states — closed, open, half-open — implemented in `ObjectStorageCircuitBreaker` with no dependency
on `IObjectStorage` itself, so it is unit-testable as a pure state machine
(`tests/Tailor360.UnitTests/Integration/ObjectStorageCircuitBreakerTests.cs`). Opens after
`FailureThreshold` consecutive failures; stays open for `BreakDuration`; then admits exactly one trial
call, which closes the breaker on success or re-opens it immediately on failure, without waiting for the
threshold again.

**Neither number is sourced from an existing document — both are proposed, to be confirmed**, per
`docs/process/definition-of-ready.md` section 5 rule 3 and recorded as **OD-27** in
`docs/prd/assumptions-and-open-decisions.md`:

| Setting | Proposed value | Constraint it is checked against |
| --- | --- | --- |
| `ObjectStorage:Breaker:FailureThreshold` | 5 consecutive failures | None written; a judgement call, named as one |
| `ObjectStorage:Breaker:BreakDuration` | 30 seconds | `docs/nfr/slo.md` S6 requires the upload queue to drain inside 10 minutes; 30 s times the retry bound this document's own table gives (none, at this layer) stays well inside that. **S6 does not fix the value** — it is only a ceiling the proposed value must stay under |

### 2.3 Timeout

`ObjectStorage:CallTimeout`, 10 seconds, applies to every call regardless of host. A call that has not
finished by this point is a failure for the breaker's purposes whatever it eventually returns — the same
reasoning `RequestTimeoutPolicies`'s own remarks give for a command endpoint.

### 2.4 The liveness watchdog

Not an object-storage mechanism, but specified alongside it because both close the same gap: a dependency
or a process that has stopped answering must become visible without a human watching a dashboard.
`LivenessWatchdogService` (`src/Platform/Tailor360.Platform.Observability/Health/LivenessWatchdogService.cs`)
polls `HealthCheckTags.Live` on an interval, with its own timeout, and exits with code 70 after three
consecutive failures (`LivenessFailureCounter`,
`src/Platform/Tailor360.Platform.Observability/Health/LivenessFailureCounter.cs`), so the container's
`restart: unless-stopped` policy recovers it — `docs/architecture/container.md`. The worker composes its
own `[WorkerJob]`-carrying subclass, `WorkerLivenessWatchdogService`
(`src/Hosts/Tailor360.Worker/Jobs/WorkerLivenessWatchdogService.cs`), because architecture rule ARCH-021
requires every hosted service it runs to declare one, and that attribute lives in a `Platform.*` project
the shared watchdog class cannot reference (the flat layering among `Platform.*` projects forbids
`Tailor360.Platform.Observability` from referencing `Tailor360.Platform.Security`).

**Liveness performs no dependency call.** `HealthCheckTags.Live` is registered only against `"self"`
(a constant) and, on the worker, `OutboxDispatcherLivenessHealthCheck`
(`src/Hosts/Tailor360.Worker/Jobs/OutboxDispatcherLivenessHealthCheck.cs`), which asserts the dispatcher's
own loop is still iterating (`IOutboxDispatcherActivityMonitor`,
`src/Hosts/Tailor360.Worker/Jobs/IOutboxDispatcherActivityMonitor.cs`) without ever calling the database
itself. A host whose database is unreachable is therefore not restarted by the watchdog — restarting would
turn a shared-dependency outage into every instance restarting at once, which is exactly the failure this
design avoids (`docs/architecture/failure-modes.md` principle 2).

The dispatcher-liveness check's own staleness window is a generous multiple of the outbox's poll interval
— fifteen times `OutboxOptions.IdlePollInterval`, floored at 60 seconds — because the loop's activity stamp
is written before the cycle's own work runs and survives a failing or slow cycle (the loop's own `catch`
already keeps it iterating); only a genuine stall — thread-pool starvation, a deadlock — leaves it stale
for that long. Tested over a stub monitor, not a real dispatcher, in
`tests/Tailor360.UnitTests/Worker/OutboxDispatcherLivenessTests.cs`.

---

## 3. What is deliberately unchanged

Recorded so a later reader does not add a second mechanism for a problem already solved.

| Mechanism | Where it lives | Why it stays as it is |
| --- | --- | --- |
| `QueueLimit = 0` on every rate-limit policy | `src/Hosts/Tailor360.Web/Configuration/RateLimitPolicies.cs` | Shed immediately rather than queue: a queued request still holds a connection, which is exactly what a bulkhead exists to prevent |
| The single-runner job lease | `src/Platform/Tailor360.Platform.Persistence/Scheduling/JobLeaseService.cs` | Already a bulkhead of one, implemented as a database-row lease rather than an in-process semaphore, because a job may run on either host process |
| The outbox's bounded retry, backoff and dead letter | `src/Platform/Tailor360.Platform.Persistence/Outbox/OutboxOptions.cs`, `OutboxDispatcher.cs` | Already complete: exponential backoff with jitter, capped, dead-lettered at `MaximumAttempts` |
| The idempotency store and its in-flight lease | `src/Platform/Tailor360.Platform.Persistence/Idempotency/IdempotencyStore.cs`, `src/Platform/Tailor360.Platform.Security/Endpoints/IdempotencyEndpointFilter.cs` | Already what makes a client's retry of a synchronous command safe; this document's cancellation column for that row cites it rather than duplicating it |

---

## 4. Related documents

| Document | Why it matters here |
| --- | --- |
| [`failure-modes.md`](failure-modes.md) | The five principles this catalogue must not contradict, and the object-storage row (section 5) this document implements |
| [`container.md`](container.md) | The liveness watchdog's exact contract: three consecutive failures, exit code 70 |
| [`../nfr/capacity-and-performance.md`](../nfr/capacity-and-performance.md) | Section 2.4, the bulkhead figures; section 4, the memory envelope the shed-rather-than-queue rule protects |
| [`../nfr/slo.md`](../nfr/slo.md) | Section 3, which counts a declared `503 media.unavailable` against the error budget rather than excusing it |
| [`../nfr/traceability.md`](../nfr/traceability.md) | `NFR-AV-03` and `NFR-CP-02`, which this document advances |
| [`../prd/assumptions-and-open-decisions.md`](../prd/assumptions-and-open-decisions.md) | **OD-27**, the two proposed breaker numbers |
| [`../security/abuse-cases.md`](../security/abuse-cases.md) | `ABF-09` and `ABF-11`, whose control patterns this document's bulkhead and breaker rows answer |

Refs #58
