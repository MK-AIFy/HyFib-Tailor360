/**
 * One category or service type, as the form holds it.
 *
 * A single shape for both, because they share nine members and differ in five. The alternative is
 * two forms that drift apart in the seven controls they have in common, which is how a Tamil name
 * ends up mandatory on one and not the other.
 */
export interface CatalogEntryDraft {
  readonly code: string
  readonly name: string
  readonly nameTamil: string
  readonly description: string
  readonly displayOrder: number
  readonly branchIds: readonly string[]
  readonly reason: string
  /* Category only. */
  readonly parentCategoryId: string | null
  readonly featureFlagKey: string
  /* Service type only. */
  readonly measurementTemplateId: string
  readonly workflowDefinitionId: string
  readonly designOptionGroupIds: string
  readonly priceListItemCode: string
  readonly qcChecklistTemplateId: string
  readonly allowIncomplete: boolean
  readonly expectedDurationDays: number
  readonly intakeWarning: string
}

/** A blank one, under a parent when there is one. */
export function blankEntry(parentCategoryId: string | null = null): CatalogEntryDraft {
  return {
    code: '',
    name: '',
    nameTamil: '',
    description: '',
    displayOrder: 0,
    branchIds: [],
    reason: '',
    parentCategoryId,
    featureFlagKey: '',
    measurementTemplateId: '',
    workflowDefinitionId: '',
    designOptionGroupIds: '',
    priceListItemCode: '',
    qcChecklistTemplateId: '',
    allowIncomplete: false,
    expectedDurationDays: 5,
    intakeWarning: '',
  }
}
