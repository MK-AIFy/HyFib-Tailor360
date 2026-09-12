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
