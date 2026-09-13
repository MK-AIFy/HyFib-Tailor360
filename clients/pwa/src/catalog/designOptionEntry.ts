/** A design option as the form holds it (#141). */
export interface DesignOptionDraft {
  readonly code: string
  readonly name: string
  readonly nameTamil: string
  readonly helpText: string
  readonly illustrationKey: string
  readonly illustrationAlt: string
  readonly priceListItemCode: string
  readonly timeImpactDays: number
  readonly displayOrder: number
  readonly active: boolean
  readonly reason: string
}

/** A blank one. */
export function blankDesignOptionDraft(): DesignOptionDraft {
  return {
    code: '',
    name: '',
    nameTamil: '',
    helpText: '',
    illustrationKey: '',
    illustrationAlt: '',
    priceListItemCode: '',
    timeImpactDays: 0,
    displayOrder: 0,
    active: true,
    reason: '',
  }
}
