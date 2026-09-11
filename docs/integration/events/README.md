# Integration events

Every fact one module publishes for another, with its wire name, its JSON Schema and one example. The rules behind
this directory are [`../../architecture/conventions.md`](../../architecture/conventions.md) section 5.5 (naming and
versioning), [`../../adr/0008-transactional-outbox-and-workers.md`](../../adr/0008-transactional-outbox-and-workers.md)
(how an event is written and delivered) and
[`../../nfr/data-classification.md`](../../nfr/data-classification.md) (what a payload may carry). Where this file
and one of those disagree, they win and this one is corrected.

---

## 1. The catalogue

| Event | Payload | Example | Published by | Since |
| --- | --- | --- | --- | --- |
| `customers.consent-recorded.v1` | [schema](customers.consent-recorded.v1.schema.json) | [example](customers.consent-recorded.v1.example.json) | Customers, on a `Granted` or `Declined` answer | #26 |
| `customers.consent-withdrawn.v1` | [schema](customers.consent-withdrawn.v1.schema.json) | [example](customers.consent-withdrawn.v1.example.json) | Customers, on a withdrawal | #26 |
| `customers.preferences-changed.v1` | [schema](customers.preferences-changed.v1.schema.json) | [example](customers.preferences-changed.v1.example.json) | Customers, on a recorded or replaced communication preference | #26 |
| `customers.customer-merged.v1` | [schema](customers.customer-merged.v1.schema.json) | [example](customers.customer-merged.v1.example.json) | Customers, on an authorised merge of two records | #26 |
| `catalog.catalog-version-published.v1` | [schema](catalog.catalog-version-published.v1.schema.json) | [example](catalog.catalog-version-published.v1.example.json) | Catalog, when a draft becomes the active configuration | #29 |
| `catalog.catalog-version-retired.v1` | [schema](catalog.catalog-version-retired.v1.schema.json) | [example](catalog.catalog-version-retired.v1.example.json) | Catalog, on an explicit retirement | #29 |
| `customers.measurement-template-version-published.v1` | [schema](customers.measurement-template-version-published.v1.schema.json) | [example](customers.measurement-template-version-published.v1.example.json) | Customers, when a template version becomes the one measurements are captured against | #91 |
| `customers.measurement-template-version-retired.v1` | [schema](customers.measurement-template-version-retired.v1.schema.json) | [example](customers.measurement-template-version-retired.v1.example.json) | Customers, when a template version stops taking new captures | #91 |
| `customers.measurement-version-confirmed.v1` | [schema](customers.measurement-version-confirmed.v1.schema.json) | [example](customers.measurement-version-confirmed.v1.example.json) | Customers, when a customer's measurements are confirmed against a template version | #121 |
| `orders.estimate-issued.v1` | [schema](orders.estimate-issued.v1.schema.json) | [example](orders.estimate-issued.v1.example.json) | Orders, when a priced estimate is issued against an open draft | #32 |
| `orders.order-confirmed.v1` | [schema](orders.order-confirmed.v1.schema.json) | [example](orders.order-confirmed.v1.example.json) | Orders, when a draft becomes a confirmed order, and again with a higher `revisionNumber` where a revision republishes it | #32 |
| `orders.garment-job-created.v1` | [schema](orders.garment-job-created.v1.schema.json) | [example](orders.garment-job-created.v1.example.json) | Orders, once per garment of a confirmed order | #32 |
| `orders.order-revised.v1` | [schema](orders.order-revised.v1.schema.json) | [example](orders.order-revised.v1.example.json) | Orders, on a revision of a confirmed order | #32 |
| `orders.job-entered-production.v1` | [schema](orders.job-entered-production.v1.schema.json) | [example](orders.job-entered-production.v1.example.json) | Orders, when a garment starts being made and its workflow version is pinned | #32 |
| `orders.job-held.v1` | [schema](orders.job-held.v1.schema.json) | [example](orders.job-held.v1.example.json) | Orders, when work on a garment is suspended | #32 |
| `orders.job-resumed.v1` | [schema](orders.job-resumed.v1.schema.json) | [example](orders.job-resumed.v1.example.json) | Orders, when a held garment goes back to being made | #32 |
| `orders.job-rescheduled.v1` | [schema](orders.job-rescheduled.v1.schema.json) | [example](orders.job-rescheduled.v1.example.json) | Orders, when a garment's promised date moves | #32 |
| `orders.job-ready-for-delivery.v1` | [schema](orders.job-ready-for-delivery.v1.schema.json) | [example](orders.job-ready-for-delivery.v1.example.json) | Orders, when the ready-for-delivery gate opens on a garment | #32 |
| `orders.job-cancelled.v1` | [schema](orders.job-cancelled.v1.schema.json) | [example](orders.job-cancelled.v1.example.json) | Orders, on the cancellation of one garment | #32 |
| `orders.order-cancelled.v1` | [schema](orders.order-cancelled.v1.schema.json) | [example](orders.order-cancelled.v1.example.json) | Orders, on the cancellation of a whole order | #32 |

Each is declared as a record in the publishing module's `Contracts` project — the module's published surface — and
nowhere else. `Tailor360.Modules.Customers.Contracts.Events` holds the seven Customers events,
`Tailor360.Modules.Catalog.Contracts.Events` the two Catalog ones, and
`Tailor360.Modules.Orders.Contracts.Events` the eleven Orders ones.

## 2. Naming and versioning

`<module>.<event-name>.v<major>`, lower case, the event name hyphenated. The major version appears **twice**: in the
name and as `schemaVersion` in the payload, and the two must agree. `IntegrationEventTests` in the contract tier
fails the build when they do not, because a subscriber routes on the name while a producer might bump only the
property.

Within a major version only **additive** changes are permitted: a new optional field, never a removed or renamed or
re-typed one. A breaking change publishes the new major **alongside** the old one for the deprecation window, and
subscribers migrate within it. Two majors of the same event are two entries in the table above, two schemas and two
records.

Two schema rules follow from that, and the contract tier enforces both:

- **`additionalProperties` is `true`.** A closed schema would make every additive change breaking — a subscriber
  validating against the copy it fetched before the field existed would reject the first payload carrying it. The
  robustness a version policy assumes has to be written into the schema, not just into the policy.
- **`required` lists the fields present when the major was published**, and a field added within it is optional
  **forever**. A dead-lettered message replayed months later (`OutboxAdministration.ReplayAsync`) carries the
  payload as it was written, so requiring a field added after that would reject a message this system really does
  emit.

The test can prove the first rule and half of the second — it refuses a schema that requires a property the event
does not publish, and one that requires nothing at all — but it cannot know which fields existed a year ago. That
half is a reviewer reading the diff against this section.

## 3. What a payload may carry

Section 5.5 of `conventions.md` admits **identifiers, codes, statuses, timestamps, amounts and branch codes only**,
unless the event is classified personal *and* the subscriber is approved for it.

That is stricter than it first looks, and the twenty events here are the worked example. Consent records and
communication preferences are **Personal** under `data-classification.md` section 5.3, whose access row says of the
module that acts on them: *"Notifications reads it through `IConsentQuery` and never copies it."* An outbox row **is**
a copy — written to a table, read by every registered handler, outliving the moment it described — so:

- `customers.consent-recorded.v1` and `customers.consent-withdrawn.v1` carry the purpose, the outcome and the
  wording version, which section 5.3 already permits to be recorded outside the record itself ("the consent
  decision (purpose, outcome, wording version) is audited"), plus identifiers and timestamps. They do **not** carry
  `source` — free text a member of staff typed, which is not one of the admitted kinds. It is on the record, and
  `IConsentQuery` hands it to a consumer approved to read it.
- `customers.preferences-changed.v1` carries **no preference at all**. Not the channels, not the language, not the
  quiet window. It says the answer changed; the reader asks `ICommunicationPreferenceQuery` what it now is, which is
  what Notifications does before every send anyway, because a preference evaluated at send time is the only one
  that is current.
- `catalog.catalog-version-published.v1` carries the version, the organisation, the number a person reads and the version it
  superseded — and **not the hierarchy**. Copying the tree into an event would make every subscriber a second,
  stale catalogue; a consumer that needs it reads `ICatalogAvailabilityQuery`, which re-authorises and answers from
  whichever version is published at the moment it is asked.
- `catalog.catalog-version-retired.v1` is published only for an **explicit** retirement. A version retired because a
  successor superseded it is already announced by `catalog.catalog-version-published.v1` through its `supersededVersionId`,
  and publishing both would make one fact look like two — a subscriber that acted on each would act twice.
- `customers.customer-merged.v1` carries three identifiers, the organisation and the branch, and nothing else. Not
  the customer number, not the name, and **not the reason** — free text a member of staff typed about a named
  person, which is not one of the admitted kinds and which the erasure workflow has to be able to redact. It stays
  on the merge record. A subscriber showing a merge to somebody asks `ICustomerSnapshotQuery`, which re-authorises
  the read.
- The **eleven Orders events** withhold four kinds of thing between them, for four different reasons. **No amounts
  and no tax components**, and none of the catalogue, price-list or tax-configuration version identifiers that
  only mean anything beside them: a garment job's price snapshot is Confidential under section 2.1, and section 3
  of `data-classification.md` admits Confidential data to an integration event *"only to a subscriber approved for
  it"* — no such approval exists. It costs nothing, because #42 and #43 read the priced result through
  `IOrderSnapshotQuery` and recalculate. **No measurement of any kind** — not a value, not a field key, not the
  template, not the version, not a count of them — because section 5.4 forbids one in an integration event
  payload outright. **No customer name or contact**: an order holds only a `customerId`, and a consumer that must
  name the person asks `ICustomerSnapshotQuery`, which re-authorises and masks. And **no free text a person
  wrote** — no notes, no hold reason, no cancellation reason, no revision reason, no resume reason. Only the
  configured reason **code** travels, which section 5.5 admits as a code; the text stays in Orders, where the
  erasure workflow can reach it. None of the eleven names an **actor**, on the same line
  `customers.customer-merged.v1` takes: audit (ARCH-008) is the authority on who, and `orders.order-revised.v1`
  hands a consumer a `revisionId` to quote back instead.

A subscriber that genuinely needs content the payload withholds is a change to **who may access it** — the handling
rules of `data-classification.md` section 3 and the "Who may access" row of whichever of sections 5.2 to 5.20 owns
the field. That is a new major version and an approval — not a field added within this one.

## 4. Ordering, delivery and duplicates

| Property | What holds |
| --- | --- |
| Atomicity | The event and the change that produced it are one save on one connection, in the module's own schema (issue #77). A consumer never sees an event for work that rolled back |
| Ordering | Preserved **per aggregate**, over messages already committed — see section 4.1, which is the limit |
| Delivery | At least once. A consumer de-duplicates on `eventId`, and the dispatcher's inbox row makes the *effect* at most once for a handler that stages its writes |
| Encoding | `camelCase` property names; an enumeration goes out as its **name**, not its ordinal (`OutboxPayload.SerializerOptions`) |
| Instants | `date-time`, always UTC |

### 4.1 What per-aggregate ordering does and does not promise

**It promises** that no number of dispatchers reorders one aggregate's committed messages. The claim excludes any
message whose aggregate has an older unprocessed one, so at most one is ever in flight per aggregate and they go
out in `occurredAt`, then `id` order.

**It does not serialise the producers**, and that is the part to know before relying on it. Each writer stamps
`occurredAt` before it saves, and nothing locks the aggregate in between, so two counters answering for the same
customer at once can commit in the opposite order to their timestamps. The dispatcher then delivers the one that
committed first — it cannot rank a row it cannot yet see — and the earlier-stamped message follows it. The window
is the length of the slower transaction.

So a consumer:

- **de-duplicates on `eventId`**, because delivery is at least once anyway;
- **treats the read contract as the authority on current state** rather than reconstructing it from the order
  events arrived in. `data-classification.md` section 5.3 already requires this of the consumer that matters
  ("Notifications reads it through `IConsentQuery` and never copies it"), and it is the same reason these payloads
  are thin.

Closing the gap means serialising the writers per aggregate — taking a row lock on the customer for the length of
the write — which is a change to a module's concurrency model, not to an event. It is worth doing for an aggregate
whose consumers must apply events in order; none of the three here is one.

### 4.2 What a consumer of `customers.customer-merged.v1` has to do

It is the one event in this directory that asks a consumer to change data it already holds, so it is worth stating
what "re-point" means and what it does not.

| The consumer holds | It should |
| --- | --- |
| A live reference to a customer — an open order, a custody record, a subscription, a queued message | Re-point it to `aggregateId` |
| A **snapshot** taken at a decision point — the customer as an invoice, a job card or a sent notification recorded her | Leave it exactly as it is. INV-CUS-04 and G-8: a snapshot is a record of what was agreed, and a later merge never reaches back into settled work |
| Nothing about either record | Nothing. A consumer that has never heard of either identifier does not have to care |

Delivery is at least once, so re-pointing has to be safe on a second delivery: pointing a reference at a record it
already names is the ordinary case, not an error. There is no un-merge and no `customer-unmerged` event, so a
consumer never needs a compensating path.

**A chain does not resolve itself, and this is the part to get right.** A is merged into B, and B is later merged
into C. Those are two events with two *different* aggregates — B and C — so the per-aggregate ordering in section
4.1 does not order them against each other at all, and with concurrent dispatchers the B→C event can be handled
first. A consumer that only applied each event as it arrived would then re-point its B references to C, and
afterwards re-point its A references to B: a live reference to a record that no longer stands, arrived at by
following the contract exactly.

So the last step of handling this event is not "re-point to `aggregateId`" but **"re-point to whichever record
`aggregateId` resolves to now"**:

```
snapshot = ICustomerSnapshotQuery.GetAsync(aggregateId)
target   = snapshot.mergedIntoCustomerId ?? aggregateId
```

One lookup is always enough. A merge flattens every pointer that named the record it is folding in, inside the same
transaction, so a stored `mergedIntoCustomerId` never names a record that has itself been merged. This is the same
rule section 4.1 already states in general — the read contract is the authority on current state, not the order
events arrived in — and the merge is simply the case where ignoring it costs the most.

## 5. Adding an event

1. Declare the record in the publishing module's `Contracts` project, deriving from `IntegrationEvent`, with a
   `public const string Type` holding the wire name so subscribers bind to a constant rather than a literal.
2. Publish it **before** the save that commits the change, through the module's own publisher port
   (`ICustomersEventPublisher` and its equivalents) — never `IEventPublisher` directly, which is shared.
3. Add `<name>.schema.json` and `<name>.example.json` here, and a row to the table in section 1.
4. Run the contract tier. `IntegrationEventTests` checks the name's shape, that the name and `schemaVersion` agree,
   that the schema's properties match the record's serialised properties exactly, that the schema stays open and
   requires only fields the event publishes, that the example matches the schema, and that no field name in the
   payload looks like personal data.
