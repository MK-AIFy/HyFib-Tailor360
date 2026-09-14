import type { PriceListVersionSummary } from '../../billing/priceListTypes'

/**
 * What the version-conventions form holds while a person is typing.
 *
 * `taxInclusive` and `roundOff` start as `''` — neither is pre-selected, because the server refuses
 * an omitted choice rather than defaulting one (`billing.value-required`), and a form that pre-picked
 * an answer would be making that choice on somebody's behalf.
 *
 * `branchIdsTouched` is what lets an empty `branchIds` mean two different things: `false` is "nobody
 * has answered this yet", sent as `branchIds: null` and refused; `true` is "this is the real list,
 * even if it is empty", sent as `branchIds: []` and accepted — the state a draft pricing nowhere yet
 * is in. A checkbox list cannot tell those apart by its ticks alone, which is why this flag exists.
 */
export interface PriceListVersionDraft {
  readonly name: string
  readonly notes: string
  readonly effectiveFrom: string
  readonly taxInclusive: '' | 'true' | 'false'
  readonly roundOff: string
  readonly overrideThresholdPercent: string
  readonly branchIds: readonly string[]
  readonly branchIdsTouched: boolean
  /** The version this draft was cloned from, reported once the draft is created. Fixed once opened. */
  readonly cloneFromVersionId: string | null
  readonly reason: string
}

/** A blank draft, for starting a version with nothing carried over. */
export function blankPriceListVersionDraft(): PriceListVersionDraft {
  return {
    name: '',
    notes: '',
    effectiveFrom: '',
    taxInclusive: '',
    roundOff: '',
    overrideThresholdPercent: '',
    branchIds: [],
    branchIdsTouched: false,
    cloneFromVersionId: null,
    reason: '',
  }
}

/**
 * A draft pre-filled from an existing version, for cloning it into a new draft.
 *
 * Every convention is carried over as a starting point the person can still change before saving —
 * the server still requires the whole convention set on the request, cloning or not.
 */
export function priceListVersionDraftFromSummary(
  version: PriceListVersionSummary,
): PriceListVersionDraft {
  return {
    name: version.name,
    notes: version.notes ?? '',
    effectiveFrom: version.effectiveFrom,
    taxInclusive: version.taxInclusive ? 'true' : 'false',
    roundOff: version.roundOff,
    overrideThresholdPercent: String(version.overrideThresholdPercent),
    branchIds: version.branchIds,
    branchIdsTouched: true,
    cloneFromVersionId: version.priceListVersionId,
    reason: '',
  }
}

/**
 * A draft pre-filled from the version being edited, for the whole-value `DescribePriceListVersion`
 * write E09-F01-7's editor makes.
 *
 * Identical to {@link priceListVersionDraftFromSummary} except `cloneFromVersionId` is `null`: this
 * draft changes the version it was read from rather than starting a new one, so there is nothing to
 * clone from. Every convention already has a real answer here — `taxInclusive` is never `''` — because
 * the version being edited was drafted or last saved with one.
 */
export function priceListVersionDraftForEditing(
  version: PriceListVersionSummary,
): PriceListVersionDraft {
  return { ...priceListVersionDraftFromSummary(version), cloneFromVersionId: null }
}
