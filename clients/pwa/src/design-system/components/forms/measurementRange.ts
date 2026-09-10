import type { IntlShape } from 'react-intl'
import type { CentimetreDecimals, InchFractionStep } from '../../../i18n/units'
import { formattersForLocale } from './formatting'
import type { MeasurementDisplayUnit } from './measurement'

/**
 * A band of millimetres, said the way a tailor reads it: "between 15 3/8 in and 22 in".
 *
 * ## Why this is not inside `MeasurementField`
 *
 * The sentences existed already — as `intl` calls inside that control, reachable only by rendering a
 * whole entry field. So a table cell, a summary line or a printed sheet that wanted to *state* a
 * range had two options: render an input nobody may type in, or print raw millimetres. The
 * measurement-template screens took the second, and `TemplateDetailRoute` carried a comment saying
 * `100–2000 mm` was standing in until this existed (#103).
 *
 * Rendering it here rather than there also fixes the more important half. A tailor never sees
 * millimetres — `docs/prd/measurement-templates.md` section 2 — so a bound quoted in them cannot be
 * checked against the tape in anybody's hand, which is the entire purpose of showing it. And the
 * rounding has to be the *same* rounding the entry control does, because a display that rounds
 * differently from the server is indistinguishable to a tailor from a storage bug. Both go through
 * `formatMeasurement`, so there is one implementation rather than two that agree today.
 */
export interface MeasurementRangeOptions {
  /** The unit to say it in. Millimetres are never shown to staff, so this is `in` or `cm`. */
  readonly unit: MeasurementDisplayUnit
  /** The field's inch step, as a denominator. Inches only. */
  readonly step?: InchFractionStep
  /** The field's centimetre precision. Centimetres only. */
  readonly decimals?: CentimetreDecimals
}

/**
 * One bound, at the field's own precision and carrying its unit.
 *
 * @param intl The active `IntlShape`, for the locale's digits and separators.
 * @param millimetres The canonical value.
 * @param options The unit and the field's precision in it.
 */
export function formatBound(
  intl: IntlShape,
  millimetres: number,
  options: MeasurementRangeOptions,
): string {
  return formattersForLocale(intl.locale).formatMeasurement(millimetres, {
    unit: options.unit,
    ...(options.step === undefined ? {} : { step: options.step }),
    ...(options.decimals === undefined ? {} : { decimals: options.decimals }),
  })
}

/**
 * The whole band as a sentence, or undefined when the field declares none.
 *
 * ## Why undefined rather than an empty string
 *
 * "No bounds" is a real state with its own representation — `ValidationBands.None`, exactly
 * `0 / 0 / null / null`, because the two hard bounds are not nullable and that quad is the only way
 * to say it. A field in that state has nothing to say about its range, and a caller should render
 * nothing rather than "between 0 mm and 0 mm", which reads as a field that accepts only zero.
 *
 * A choice field is the same case for a different reason: it has no bands at all and the API nulls
 * them out itself.
 *
 * @param intl The active `IntlShape`.
 * @param bounds The stored quad, in canonical millimetres.
 * @param options The unit and the field's precision in it.
 */
export function formatMeasurementRange(
  intl: IntlShape,
  bounds: {
    readonly minimumMillimetres: number
    readonly maximumMillimetres: number
  },
  options: MeasurementRangeOptions,
): string | undefined {
  if (bounds.minimumMillimetres === 0 && bounds.maximumMillimetres === 0) {
    return undefined
  }

  return intl.formatMessage(
    { id: 'forms.measurement.expectedRange' },
    {
      minimum: formatBound(intl, bounds.minimumMillimetres, options),
      maximum: formatBound(intl, bounds.maximumMillimetres, options),
    },
  )
}

/**
 * Which unit a field's bands are read in, given what it declares a precision for.
 *
 * A field may support inches, centimetres or both (`TemplateField.DisplayUnits`, which is derived
 * from `FieldPrecision.Supports`). When it supports both, the version's own default unit decides,
 * so the range reads in the unit the capture wizard will open in — a reviewer checking a bound
 * against a tape should not have to convert it in their head first.
 *
 * @param field The field's precision pair.
 * @param preferred The version's default display unit, when it declares one.
 */
export function unitForBands(
  field: { readonly inchFraction: number; readonly centimetreDecimals: number },
  preferred?: string,
): MeasurementDisplayUnit | undefined {
  const inches = field.inchFraction > 0
  const centimetres = field.centimetreDecimals > 0

  if (!inches && !centimetres) {
    return undefined
  }
  if (inches && centimetres) {
    return preferred === 'Centimetre' ? 'cm' : 'in'
  }
  return inches ? 'in' : 'cm'
}
