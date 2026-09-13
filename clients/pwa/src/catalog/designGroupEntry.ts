/** A design option group as the form holds it (#141). */
export interface DesignGroupDraft {
  readonly code: string
  readonly name: string
  readonly nameTamil: string
  readonly selectionMode: string
  readonly required: boolean
  readonly displayOrder: number
  readonly branchIds: readonly string[]
  readonly reason: string
}

/** A blank one. */
export function blankDesignGroupDraft(): DesignGroupDraft {
  return {
    code: '',
    name: '',
    nameTamil: '',
    selectionMode: 'SingleChoice',
    required: true,
    displayOrder: 0,
    branchIds: [],
    reason: '',
  }
}
