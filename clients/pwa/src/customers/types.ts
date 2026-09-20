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
