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

Each is declared as a record in the publishing module's `Contracts` project — the module's published surface — and
nowhere else. `Tailor360.Modules.Customers.Contracts.Events` holds all three.

## 2. Naming and versioning

`<module>.<event-name>.v<major>`, lower case, the event name hyphenated. The major version appears **twice**: in the
name and as `schemaVersion` in the payload, and the two must agree. `IntegrationEventTests` in the contract tier
fails the build when they do not, because a subscriber routes on the name while a producer might bump only the
property.

Within a major version only **additive** changes are permitted: a new optional field, never a removed or renamed or
re-typed one. A breaking change publishes the new major **alongside** the old one for the deprecation window, and
subscribers migrate within it. Two majors of the same event are two entries in the table above, two schemas and two
records.

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

A subscriber that genuinely needs content the payload withholds is a change to **who may access it** under section
5.3. That is a new major version and an approval — not a field added within this one.

## 4. Ordering, delivery and duplicates

| Property | What holds |
| --- | --- |
| Atomicity | The event and the change that produced it are one save on one connection, in the module's own schema (issue #77). A consumer never sees an event for work that rolled back |
| Ordering | Preserved **per aggregate**. The three events here use the **customer** as the aggregate, so a grant cannot reach a consumer after the withdrawal that revoked it |
| Delivery | At least once. A consumer de-duplicates on `eventId`, and the dispatcher's inbox row makes the *effect* at most once for a handler that stages its writes |
| Encoding | `camelCase` property names; an enumeration goes out as its **name**, not its ordinal (`OutboxPayload.SerializerOptions`) |
| Instants | `date-time`, always UTC |

## 5. Adding an event

1. Declare the record in the publishing module's `Contracts` project, deriving from `IntegrationEvent`, with a
   `public const string Type` holding the wire name so subscribers bind to a constant rather than a literal.
2. Publish it **before** the save that commits the change, through the module's own publisher port
   (`ICustomersEventPublisher` and its equivalents) — never `IEventPublisher` directly, which is shared.
3. Add `<name>.schema.json` and `<name>.example.json` here, and a row to the table in section 1.
4. Run the contract tier. `IntegrationEventTests` checks the name's shape, that the name and `schemaVersion` agree,
   that the schema's properties match the record's serialised properties exactly, that the example matches the
   schema, and that no field name in the payload looks like personal data.
