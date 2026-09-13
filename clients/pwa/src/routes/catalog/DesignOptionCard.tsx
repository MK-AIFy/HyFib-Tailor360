import { useIntl } from 'react-intl'
import { IconButton } from '../../components/primitives/IconButton'
import type { DesignPickerOption } from '../../catalog/types'
import { IllustrationPreview } from './IllustrationPreview'

export interface DesignOptionCardProps {
  readonly option: DesignPickerOption
  readonly name: string
  readonly kind: 'radio' | 'checkbox'
  readonly selected: boolean
  /** Set when an `excludes` rule forbids this option today. Shown, never hidden. */
  readonly disabledReason: string | null
  readonly onToggle: (checked: boolean) => void
  readonly onZoom: () => void
  readonly controlId: string
}

/**
 * One choice, as a card: an illustration, its label and help text (#142).
 *
 * There is no existing control for this — `RadioGroup` and `Checkbox` type their label as plain
 * text for voice control (2.5.3), which an illustration cannot be. So this is built directly on the
 * native input rather than on top of either, keeping the same conventions: the label wraps the whole
 * card so the hit target is the row and not the 24px box (docs/nfr/accessibility-localisation.md
 * section 5), `aria-required` mirrors what `RadioGroup` puts on each option of a required group, and
 * a forbidden option stays in the tab order, `disabled`, with its reason printed on the card rather
 * than the card disappearing — "never hidden" is `docs/prd/design-options.md` section 4's own words.
 *
 * The zoom control is a sibling of the label, not nested inside it: a label may only contain
 * phrasing content, and a button nested in one is exactly the markup that makes "which control did
 * that tap mean" ambiguous on a touch screen.
 */
export function DesignOptionCard({
  option,
  name,
  kind,
  selected,
  disabledReason,
  onToggle,
  onZoom,
  controlId,
}: DesignOptionCardProps) {
  const intl = useIntl()
  const disabled = disabledReason !== null
  const reasonId = `${controlId}-reason`
  const illustrationId = `${controlId}-illustration`
  const helpId = `${controlId}-help`
  const hasHelp = option.helpText.trim() !== ''
  const describedBy = [illustrationId, hasHelp ? helpId : null, disabled ? reasonId : null]
    .filter((id): id is string => id !== null)
    .join(' ')

  return (
    <div
      className="design-option-card"
      data-disabled={disabled ? 'true' : undefined}
      data-selected={selected ? 'true' : undefined}
    >
      <label className="design-option-card__control" htmlFor={controlId}>
        <input
          // The label wraps an illustration and help text too, whose own text would otherwise
          // become part of this control's accessible name — `aria-label` fixes the name to
          // exactly the visible option name (2.5.3 Label in Name), and `aria-describedby` carries
          // the rest, the illustration's alt text included, as a description instead.
          aria-describedby={describedBy}
          aria-label={option.name}
          checked={selected}
          className="design-option-card__input"
          disabled={disabled}
          id={controlId}
          name={name}
          onChange={(event) => {
            onToggle(event.target.checked)
          }}
          type={kind}
          value={option.code}
        />
        <span className="design-option-card__illustration" id={illustrationId}>
          <IllustrationPreview
            alt={option.illustrationAlt}
            illustrationKey={option.illustrationKey}
          />
        </span>
        <span className="design-option-card__name">{option.name}</span>
        {hasHelp ? (
          <span className="design-option-card__help" id={helpId}>
            {option.helpText}
          </span>
        ) : null}
      </label>

      <IconButton
        className="design-option-card__zoom"
        label={intl.formatMessage({ id: 'catalog.design.picker.zoom' }, { option: option.name })}
        name="search"
        onClick={onZoom}
      />

      {disabled ? (
        <p className="design-option-card__reason" id={reasonId}>
          {disabledReason}
        </p>
      ) : null}
    </div>
  )
}
