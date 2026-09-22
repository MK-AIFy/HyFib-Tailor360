/**
 * A customer as the search lists them (#26).
 *
 * A card, not a record: enough to tell two people apart at a counter and not enough to be a contact
 * list. `visibleToCaller` is false for a record created at a branch the caller is not assigned to,
 * and then the name and number are the masked disambiguation the server chose to send — the client
 * never unmasks anything.
 */
export interface CustomerCard {
  readonly customerId: string
  readonly customerNumber: string
  readonly displayName: string
  readonly nativeName: string | null
  /** Always masked: the last digits only, however much the caller may see elsewhere. */
  readonly maskedPhone: string
  readonly owningBranchId: string
  readonly visibleToCaller: boolean
  readonly status: string
  readonly lastSeenAt: string
}

/** One page of a search, and the cursor for the next. */
export interface CustomerPage {
  readonly customers: readonly CustomerCard[]
  readonly nextCursor: string | null
}

/** A previous name, spelling or merged customer number held against a record. */
export interface CustomerAlias {
  readonly kind: string
  readonly value: string
  readonly recordedAt: string
}

/**
 * One customer record, as the detail and edit screens see it.
 *
 * The six contact fields are populated only for a caller holding `customers.read_contact` —
 * `contactIncluded` is what tells a withheld field apart from one the customer never gave. See
 * `docs/security/field-visibility.md` and `CustomerPayload`'s own remarks on the server.
 */
export interface Customer {
  readonly customerId: string
  readonly customerNumber: string
  readonly displayName: string
  readonly nativeName: string | null
  readonly phone: string | null
  readonly alternatePhone: string | null
  readonly email: string | null
  readonly addressLine: string | null
  readonly locality: string | null
  readonly postcode: string | null
  /** False means the six fields above were withheld; true with a null value means none was given. */
  readonly contactIncluded: boolean
  readonly language: string
  readonly status: string
  readonly owningBranchId: string
  readonly visibilityBranchIds: readonly string[]
  readonly aliases: readonly CustomerAlias[]
  readonly createdAt: string
  readonly updatedAt: string
  readonly mergedIntoCustomerId: string | null
  readonly mergedAt: string | null
}

/** What the create screen sends. Every field is optional on the wire; the server validates. */
export interface CustomerDetailsInput {
  readonly displayName?: string
  readonly nativeName?: string
  readonly phone?: string
  readonly alternatePhone?: string
  readonly email?: string
  readonly addressLine?: string
  readonly locality?: string
  readonly postcode?: string
  readonly language?: string
}

/** A record the one being created or edited might duplicate, and why. */
export interface DuplicateCandidate {
  readonly customer: CustomerCard
  readonly confidence: 'High' | 'Medium' | 'Low'
  /** What matched, strongest first, as sentences — never shown as codes. */
  readonly reasons: readonly string[]
}

/** The stable code a creation attempt is refused with when it resembles an existing record. */
export const CUSTOMER_DUPLICATES_CODE = 'customers.duplicates-not-reviewed'

/**
 * The stable code a correction is refused with when the record moved on since it was read.
 *
 * Named because the edit screen tells this refusal apart from every other 409 it could meet: this
 * one is answered by reloading and reapplying the edit, and answering the others that way would
 * silently retry something the server refused for a different reason.
 */
export const CUSTOMER_VERSION_CONFLICT_CODE = 'customers.version-conflict'

/**
 * One thing that happened to a customer, from whichever module recorded it.
 *
 * The host composes these from every module that holds part of the history, so a screen reads one
 * list rather than joining several. Two fields are pairs rather than values, and both distinctions
 * are the point of the shape:
 *
 *  - `reason` with `reasonPermission` — a null reason and a null permission means nobody gave a
 *    reason; a null reason with a permission named means one was given and this caller may not read
 *    it. Rendering both as blank is the thing `field-visibility.md` says a client must not do.
 *  - `referenceId` with `expandPermission` — what the entry points at, and what a caller must hold
 *    to open it. A reference the caller cannot expand is still worth naming; it is just not a link.
 */
export interface CustomerTimelineEntry {
  readonly entryId: string
  /** UTC, by the server's clock. Formatted for display through the shared `formatters` module. */
  readonly occurredAt: string
  /** The module that contributed it — `customers`, and later `orders`, `billing`, `custody`. */
  readonly source: string
  /** The stable dotted kind, `customers.consent.recorded`, which this screen turns into an icon. */
  readonly kind: string
  /** What happened, already in the shop's words. Never built from `kind` on the client. */
  readonly title: string
  readonly detail: string | null
  /** Null with a `reasonPermission` means withheld; null with neither means none was given. */
  readonly reason: string | null
  readonly reasonPermission: string | null
  readonly referenceType: string | null
  readonly referenceId: string | null
  readonly expandPermission: string | null
  readonly branchId: string | null
  /** Who did it, as their name was at the time, or null for the system. Staff, never the customer. */
  readonly actorDisplayName: string | null
}

/**
 * One page of a customer's merged history, newest first.
 *
 * `unavailableSources` is the field that makes this safe to read: a module that could not answer is
 * named, so a gap in somebody's history is visible rather than looking like nothing happened. A
 * screen that ignored it would quietly tell a manager a customer had never complained.
 */
export interface CustomerTimelinePage {
  readonly entries: readonly CustomerTimelineEntry[]
  readonly nextCursor: string | null
  readonly unavailableSources: readonly string[]
}

/** What `GET /api/v1/customers/{id}/duplicates` answers: who might be this same person. */
export interface DuplicateReview {
  readonly candidates: readonly DuplicateCandidate[]
}

/**
 * What a completed merge did, in numbers a person can check against what they expected.
 *
 * It is reported rather than assumed because a merge cannot be undone: "3 records re-pointed" is the
 * only chance anybody gets to notice that the wrong pair was folded together, and a screen that just
 * said "Merged" would take that chance away.
 */
export interface CustomerMergeOutcome {
  readonly customer: Customer
  readonly mergeId: string
  readonly mergedCustomerId: string
  /** The folded record's number, which stays searchable as an alias on the survivor. */
  readonly mergedCustomerNumber: string
  readonly aliasesRecorded: number
  readonly visibilityBranchesAdded: number
  readonly recordsRepointed: number
  readonly mergedAt: string
}

/**
 * The code a merge is refused with when the record being *folded in* moved on since it was read.
 *
 * Distinct from `CUSTOMER_VERSION_CONFLICT_CODE`, which is the survivor going stale, and the two are
 * never treated alike: they send the reader to different records, and the server deliberately sends
 * no `ETag` with this one because an `ETag` would describe the survivor — which is not what changed.
 */
export const CUSTOMER_MERGED_RECORD_CHANGED_CODE = 'customers.merged-record-changed'

/** One answer a customer gave about one purpose, against the wording she was read at the time. */
export interface ConsentAnswer {
  readonly recordId: string
  readonly purposeKey: string
  /** `Granted`, `Declined` or `Withdrawn`. */
  readonly decision: string
  /**
   * The wording version she was asked under. Read from the register by the server, never sent.
   *
   * `number`, although the generated contract says `number | string`: the document types every
   * `int32` as a union with a string (74 of them — #612), which the server never actually emits.
   * Widening this to match would push the union into every reader of the value to describe a shape
   * that does not occur, so the narrower, true type is kept here and the document is the thing to
   * fix.
   */
  readonly wordingVersion: number
  readonly recordedAt: string
  /** Where it was taken, in the words whoever took it wrote. Free text, not a code. */
  readonly source: string
  readonly recordedBy: string | null
  readonly branchId: string | null
}

/**
 * One purpose and where the customer stands on it.
 *
 * `canBeAnswered` is the server's answer to "may an answer be recorded now", and it is false for two
 * different reasons — the purpose is retired, or it has no published wording for an answer to name.
 * A screen must not recompute it from `isRetired` alone: a consent record names the version the
 * customer was read, and there would be nothing to name.
 */
export interface ConsentPurpose {
  readonly key: string
  readonly name: string
  readonly description: string | null
  readonly isRetired: boolean
  readonly currentWordingVersion: number
  readonly canBeAnswered: boolean
  /** `NeverAsked`, `Granted`, `Declined` or `Withdrawn`. */
  readonly status: string
  /** Every answer, newest first, so `answers[0]` is the one that stands. */
  readonly answers: readonly ConsentAnswer[]
}

/** Where a customer stands on every purpose the shop asks about. */
export interface CustomerConsent {
  readonly purposes: readonly ConsentPurpose[]
}

/**
 * How a customer wants to be reached.
 *
 * `hasBeenRecorded` is what decides whether a `PUT` carries `If-Match`: the precondition is required
 * once a preference exists and must be *omitted* before then, because there is no version of a row
 * that does not exist. `version` is null in exactly that case.
 */
export interface CommunicationPreferences {
  readonly customerId: string
  readonly hasBeenRecorded: boolean
  /** `Sms`, `WhatsApp`, `Email`. Empty is how she says do not message me. */
  readonly allowedChannels: readonly string[]
  readonly language: string
  /** Wall-clock at the branch, `HH:mm:ss`. Both ends or neither; may run backwards over midnight. */
  readonly quietHoursStart: string | null
  readonly quietHoursEnd: string | null
  readonly updatedAt: string | null
  readonly version: string | null
}

/** The three channels the shop can send on. Named once, in the order the counter screen shows them. */
export const COMMUNICATION_CHANNELS = ['Sms', 'WhatsApp', 'Email'] as const

/** The three answers a customer can give. `NeverAsked` is a status, never a decision. */
export const CONSENT_DECISIONS = ['Granted', 'Declined', 'Withdrawn'] as const

/**
 * The receipt a subject-access export hands back — not the document.
 *
 * It names the copy and says when the download stops working. The document itself is fetched from
 * the download route, which re-authorises and is audited on every call, and is never given a URL:
 * rule 9, and the reason an export is a two-step operation rather than a link.
 *
 * `supersededCount` is how many earlier exports this one destroyed. Generating an export destroys
 * any previous copy, so at most one copy of a person's record exists outside the record at a time —
 * which is a property somebody answering a second request needs to be told about rather than left
 * to discover when the first download stops working.
 */
export interface CustomerExport {
  readonly exportId: string
  readonly customerId: string
  readonly documentCode: string
  readonly documentVersion: number
  /** The handling class of what is inside, from `docs/nfr/data-classification.md`. */
  readonly classification: string
  readonly contentType: string
  readonly byteCount: number
  readonly generatedAt: string
  /** When the copy is emptied. The record that it was taken, by whom and why is kept. */
  readonly expiresAt: string
  readonly supersededCount: number
}

/** The code the download answers with once the copy has gone — expired, or replaced by a newer one. */
export const CUSTOMER_EXPORT_EXPIRED_CODE = 'customers.export-expired'

/**
 * The code a status command is refused with when the record is already where it is being sent.
 *
 * Deactivating a withdrawn record, or reactivating an active one. Neither is a failure of the
 * person's — they are looking at a screen that was drawn before somebody else acted — so the screen
 * says what happened rather than rendering it as an error against what they did.
 */
export const CUSTOMER_STATUS_TRANSITION_CODE = 'customers.status-transition-not-allowed'

/**
 * The code a reactivation is refused with when the record was merged away.
 *
 * A merge cannot be undone, so a record that has been merged is finished: it is not returned to
 * ordinary use. The reader is sent to the record that survived.
 */
export const CUSTOMER_ALREADY_MERGED_CODE = 'customers.already-merged'
