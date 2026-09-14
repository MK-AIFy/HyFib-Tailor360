/** What the create/rename form holds while a person is typing. */
export interface PriceListDraft {
  readonly code: string
  readonly name: string
  readonly reason: string
}

/** A blank draft, for creating a new price list. */
export function blankPriceListDraft(): PriceListDraft {
  return { code: '', name: '', reason: '' }
}

/** A draft for renaming an existing price list — the code plays no part in a rename. */
export function priceListDraftForRename(name: string): PriceListDraft {
  return { code: '', name, reason: '' }
}
