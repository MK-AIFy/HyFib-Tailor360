import { FormattedMessage } from 'react-intl'
import type { ReactNode } from 'react'
import { cx } from '../../foundations/cx'
import type { FieldAriaSource, FieldElementIds } from '../../foundations/FieldProps'
import type { ControlSize } from '../../foundations/types'
import './forms.css'

/**
 * The field shell: the label, the hint, the unit, the error and the warning that every control in
 * the system wears.
 *
 * Every control renders its own input inside one of these, which is what makes the form contract of
 * docs/nfr/accessibility-localisation.md section 8.1 structural rather than a convention somebody
 * has to remember. There is nowhere in this component to put a placeholder-only label, nowhere to
 * put an error that is not associated with the control, and no way to mark a field required without
 * the word appearing beside the label as well as `aria-required` on the control.
 *
 * Four decisions worth keeping:
 *
 *  1. **The required marker sits outside the `<label>` and is `aria-hidden`.** Inside it, "Required"
 *     would become part of the accessible name and a voice-control user saying "tap Waist" would
 *     miss (2.5.3). Outside it, the word is visible for a sighted user, `aria-required` carries the
 *     same fact to a screen reader, and neither hears it twice (A11Y-33, A11Y-43).
 *  2. **The unit is two elements.** The symbol — `in`, `cm`, `₹` — is shown and hidden from
 *     assistive technology; the word — `inches` — is visually hidden and carries `ids.unit`, which
 *     is what `fieldDescribedBy` points `aria-describedby` at. A screen reader therefore hears
 *     "inches", not "in" (A11Y-29).
 *  3. **The error and the warning are different things.** An error makes the field invalid and
 *     blocks the step; a warning is the confirmation band of a measurement template, which never
 *     blocks a save and must therefore be read to do anything at all (A11Y-ME-07).
 *  4. **Nothing here is coloured only.** The error carries a visually-hidden "Error:" prefix and an
 *     icon glyph; the warning carries its own. Colour is the third signal, never the first (1.4.1).
 */

/** The visual and target size of the control inside the field. */
type FieldFrameSize = ControlSize

export interface FieldMessagesProps {
  readonly ids: FieldElementIds
  readonly description?: string | undefined
  readonly error?: string | undefined
  readonly warning?: string | undefined
  readonly unit?: FieldAriaSource['unit'] | undefined
}

/**
 * The hint, the error, the warning and the spoken unit.
 *
 * Rendered below the control. The reading order a screen reader uses is the `aria-describedby`
 * order set by `fieldDescribedBy` — error, warning, unit, description — and is deliberately not the
 * same as the visual order, where a hint belongs above the problem it was meant to prevent.
 *
 * Exported because the choice controls do not use `Field`: a checkbox's label belongs beside its
 * box rather than above it, so those components lay themselves out and reuse this block, which is
 * the part that must not diverge.
 */
export function FieldMessages({ ids, description, error, warning, unit }: FieldMessagesProps) {
  return (
    <>
      {error === undefined ? null : (
        <p className="field__message field__message--error" id={ids.error}>
          <span aria-hidden="true" className="field__message-glyph">
            !
          </span>
          <span className="visually-hidden">
            <FormattedMessage id="forms.error.prefix" />{' '}
          </span>
          {error}
        </p>
      )}
      {warning === undefined ? null : (
        <p className="field__message field__message--warning" id={ids.warning}>
          <span aria-hidden="true" className="field__message-glyph">
            ?
          </span>
          <span className="visually-hidden">
            <FormattedMessage id="forms.warning.prefix" />{' '}
          </span>
          {warning}
        </p>
      )}
      {unit === undefined ? null : (
        <span className="visually-hidden" id={ids.unit}>
          {unit.label}
        </span>
      )}
      {description === undefined ? null : (
        <p className="field__description" id={ids.description}>
          {description}
        </p>
      )}
    </>
  )
}

/** The visible unit symbol, beside the control rather than over it so 150% text never clips it. */
function UnitSymbol({ unit }: { readonly unit: NonNullable<FieldAriaSource['unit']> }) {
  return (
    <span aria-hidden="true" className="field__unit" data-position={unit.position ?? 'trailing'}>
      {unit.symbol}
    </span>
  )
}

export interface FieldShellProps {
  readonly ids: FieldElementIds
  readonly label: string
  /*
   * Every optional member below is written `?: T | undefined` rather than `?: T`. The application
   * compiles with `exactOptionalPropertyTypes`, under which passing an explicitly undefined value
   * to a `?: T` property is an error — and every control forwards its own optional props straight
   * through to this shell. Widening here once is what keeps twelve controls free of a conditional
   * spread per prop.
   */
  readonly description?: string | undefined
  readonly error?: string | undefined
  readonly warning?: string | undefined
  readonly required?: boolean | undefined
  readonly disabled?: boolean | undefined
  readonly unit?: FieldAriaSource['unit'] | undefined
  /**
   * Whether the visible unit symbol is drawn beside the control. The spoken unit word is wired into
   * `aria-describedby` either way — a control that places the symbol itself, as the fraction input
   * does beside its assembled value, turns only the drawing off.
   */
  readonly showUnitSymbol?: boolean | undefined
  readonly size?: FieldFrameSize | undefined
  /** The control itself. One control per field; a set of them is a `FieldGroup`. */
  readonly children: ReactNode
  /** Rendered under the messages — the acknowledgement of a confirmation band, for instance. */
  readonly footer?: ReactNode
  readonly className?: string | undefined
}

/** A single labelled control. */
export function Field({
  ids,
  label,
  description,
  error,
  warning,
  required,
  disabled,
  unit,
  size = 'standard',
  children,
  footer,
  className,
  showUnitSymbol = true,
}: FieldShellProps) {
  const position = unit?.position ?? 'trailing'
  const symbol = showUnitSymbol ? unit : undefined

  return (
    <div
      className={cx('field', className)}
      data-size={size}
      data-invalid={error === undefined ? undefined : 'true'}
      data-disabled={disabled === true ? 'true' : undefined}
    >
      <span className="field__label-row">
        <label className="field__label" htmlFor={ids.control} id={ids.label}>
          {label}
        </label>
        {required === true ? (
          <span aria-hidden="true" className="field__required">
            <FormattedMessage id="forms.required" />
          </span>
        ) : null}
      </span>
      <span className="field__control-row">
        {symbol !== undefined && position === 'leading' ? <UnitSymbol unit={symbol} /> : null}
        {children}
        {symbol !== undefined && position === 'trailing' ? <UnitSymbol unit={symbol} /> : null}
      </span>
      <FieldMessages
        description={description}
        error={error}
        ids={ids}
        unit={unit}
        warning={warning}
      />
      {footer}
    </div>
  )
}

export interface FieldGroupShellProps extends Omit<FieldShellProps, 'ids'> {
  readonly ids: FieldElementIds
  /**
   * Where the group's own `aria-describedby` goes. A `<fieldset>` names its contents with the
   * `<legend>`; the hint, error and warning have to be pointed at explicitly because a fieldset has
   * no `for` attribute to inherit them through.
   */
  readonly describedBy?: string | undefined
}

/**
 * A set of controls that share one label: a radio group, the inch-fraction strip, a measurement
 * group.
 *
 * A real `<fieldset>` with a real `<legend>`, because checklist item A11Y-59 asks for the group
 * name to be announced before the first option and nothing else does that reliably. The legend is
 * also why the required marker moves inside it here: a legend cannot have a sibling in the
 * accessible name computation, and `aria-hidden` keeps it out of that name all the same.
 */
export function FieldGroup({
  ids,
  label,
  description,
  error,
  warning,
  required,
  disabled,
  unit,
  size = 'standard',
  children,
  footer,
  className,
  describedBy,
  showUnitSymbol = true,
}: FieldGroupShellProps) {
  return (
    <fieldset
      aria-describedby={describedBy}
      aria-invalid={error === undefined ? undefined : true}
      className={cx('field', 'field--group', className)}
      data-size={size}
      data-disabled={disabled === true ? 'true' : undefined}
      data-invalid={error === undefined ? undefined : 'true'}
      disabled={disabled}
      /*
       * Not `ids.control`. That id belongs to the first focusable control inside the group, because
       * it is what `FormErrorSummary` moves focus to and a `<fieldset>` cannot take focus — a
       * summary link that lands on a fieldset lands nowhere a person can type.
       */
      id={`${ids.control}-group`}
    >
      <legend className="field__legend" id={ids.label}>
        {label}
        {required === true ? (
          <span aria-hidden="true" className="field__required">
            <FormattedMessage id="forms.required" />
          </span>
        ) : null}
      </legend>
      <span className="field__control-row">
        {children}
        {unit === undefined || !showUnitSymbol ? null : <UnitSymbol unit={unit} />}
      </span>
      <FieldMessages
        description={description}
        error={error}
        ids={ids}
        unit={unit}
        warning={warning}
      />
      {footer}
    </fieldset>
  )
}
