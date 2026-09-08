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

Each is declared as a record in the publishing module's `Contracts` project — the module's published surface — and
nowhere else. `Tailor360.Modules.Customers.Contracts.Events` holds all four.

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

That is stricter than it first looks, and the three events here are the worked example. Consent records and
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
- `customers.customer-merged.v1` carries three identifiers, the organisation and the branch, and nothing else. Not
  the customer number, not the name, and **not the reason** — free text a member of staff typed about a named
  person, which is not one of the admitted kinds and which the erasure workflow has to be able to redact. It stays
  on the merge record. A subscriber showing a merge to somebody asks `ICustomerSnapshotQuery`, which re-authorises
  the read.

A subscriber that genuinely needs content the payload withholds is a change to **who may access it** under section
5.3. That is a new major version and an approval — not a field added within this one.

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
already names is the ordinary case, not an error. And there is no un-merge and no `customer-unmerged` event, so a
consumer never needs a compensating path — but it may see a **chain**: A was merged into B, and B is later merged
into C. Customers flattens its own pointers so a record's `mergedIntoCustomerId` is always one hop from a record
that stands; a consumer that applies each event as it arrives ends in the same place, because re-pointing A→B and
then B→C leaves A naming C.

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
