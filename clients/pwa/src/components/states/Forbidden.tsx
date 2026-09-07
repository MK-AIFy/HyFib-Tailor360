import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Button } from '../primitives/Button'
import { StateRegion } from './StateRegion'
import type { StateHeadingLevel, StateLiveness } from './stateVariants'

export interface ForbiddenProps {
  /**
   * What was refused, in words and from the person's point of view: "Taking a payment", "Posting
   * the stocktake". Not a permission identifier — `billing.record_payment` is not a sentence.
   */
  readonly action: string
  /**
   * Who may do it, named. Checklist item A11Y-89 requires the forbidden state to name who can,
   * because "you do not have permission" leaves a Tailor holding a garment with nowhere to go. Pass
   * role names as they are shown elsewhere on screen — "Cashier", "Branch Manager".
   */
  readonly allowedRoles?: readonly string[]
  /**
   * The way back. Required, not optional: A11Y-89 asks for a keyboard-reachable way out rather than
   * a blank region or a dimmed control, and making it required is how that stops being a thing a
   * screen can forget. A router-driven screen passes a navigation callback.
   */
  readonly onBack: () => void
  readonly backLabel?: string
  /** Anything else worth saying — which branch this belongs to, when the permission changes. */
  readonly children?: ReactNode
  readonly live?: StateLiveness
  readonly headingLevel?: StateHeadingLevel
  readonly full?: boolean
  readonly className?: string
}

/**
 * This role may not do this.
 *
 * Deny-by-default makes the forbidden state a normal state rather than an error: a Tailor meets one
 * every time they open a billing screen, and a Delivery Staff member meets one daily. So it is
 * written as an ordinary explanation, not as a failure — no danger colour, no alarm glyph — and it
 * does the three things checklist item A11Y-89 asks for: it says this role may not, it names who
 * may, and it leaves a keyboard-reachable way back.
 *
 * A control that is simply absent cannot be asked about, and a control that is present and dimmed
 * explains nothing. That is why a screen renders this rather than hiding the region silently.
 */
export function Forbidden({
  action,
  allowedRoles,
  onBack,
  backLabel,
  children,
  live = 'polite',
  headingLevel = 2,
  full = false,
  className,
}: ForbiddenProps) {
  const intl = useIntl()

  const roles = allowedRoles ?? []
  const whoCan =
    roles.length === 0
      ? intl.formatMessage({ id: 'states.forbidden.askManager' })
      : intl.formatMessage(
          { id: 'states.forbidden.whoCan' },
          // Intl.ListFormat rather than a join: "Cashier and Branch Manager" in English is one
          // conjunction, and Tamil punctuates a list differently. A comma-joined string is the
          // commonest way a translated list stops reading like a sentence.
          { roles: new Intl.ListFormat(intl.locale, { type: 'conjunction' }).format(roles) },
        )

  return (
    <StateRegion
      actions={
        <Button iconName="chevron-left" onClick={onBack} variant="secondary">
          {backLabel ?? intl.formatMessage({ id: 'states.forbidden.back' })}
        </Button>
      }
      className={cx(className)}
      full={full}
      headingLevel={headingLevel}
      iconName="x-circle"
      live={live}
      title={intl.formatMessage({ id: 'states.forbidden.title' })}
      /* The shape says "not this"; the tone keeps it out of the danger palette. A forbidden state is
         a normal state a Tailor meets daily, not an emergency, and colouring it like a failure would
         teach people to read the real failures as routine. */
      tone="info"
    >
      <p className="state-region__line">
        {intl.formatMessage({ id: 'states.forbidden.body' }, { action })}
      </p>
      <p className="state-region__line">{whoCan}</p>
      {children}
    </StateRegion>
  )
}
