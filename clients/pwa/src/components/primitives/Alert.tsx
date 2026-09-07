import { useId } from 'react'
import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Icon } from './Icon'
import { IconButton } from './IconButton'
import type { IconName } from './icons'
import type { AlertTone } from './variants'
import type { MessageKey } from '../../i18n/en-IN'
import './Alert.css'

interface TonePresentation {
  readonly icon: IconName
  readonly messageKey: MessageKey
}

/** Each tone's glyph and its spoken word. Different shapes, so greyscale still distinguishes them. */
const TONE_PRESENTATION: Record<AlertTone, TonePresentation> = {
  info: { icon: 'info', messageKey: 'primitives.alert.info' },
  success: { icon: 'check-circle', messageKey: 'primitives.alert.success' },
  warning: { icon: 'alert-triangle', messageKey: 'primitives.alert.warning' },
  danger: { icon: 'alert-circle', messageKey: 'primitives.alert.danger' },
}

export interface AlertProps {
  readonly tone: AlertTone
  /** A heading for the message. Optional: a one-sentence alert reads better without one. */
  readonly title?: string
  readonly children: ReactNode
  /**
   * How assistive technology is told about it.
   *
   *   off        the default, and right for anything present when the screen renders — it is read in
   *              document order like the rest of the page, and a live region would announce it a
   *              second time
   *   polite     the message appeared after the screen did: a save confirmed, a queue updated, an
   *              action blocked because the connection went. 4.1.3 Status Messages
   *   assertive  the message interrupts, and only two things in this product earn that: a rejected
   *              scan naming which rule failed, and an error that has stopped what the person was
   *              doing (docs/nfr/accessibility-localisation.md section 6)
   */
  readonly live?: 'off' | 'polite' | 'assertive'
  /** Buttons belonging to the message — Retry, Re-scan, Sign in again. */
  readonly actions?: ReactNode
  /**
   * Renders a dismiss control. Omit it for anything the person must act on: a network banner and a
   * blocked action stay until the condition clears, which is why section 8.3 calls the network
   * banner "persistent, non-dismissible".
   */
  readonly onDismiss?: () => void
  readonly className?: string
}

/**
 * An inline message that stays put.
 *
 * It is not a toast, and this component is the reason there is no toast in this design system.
 * docs/nfr/accessibility-localisation.md section 6 forbids one for a scan result, for sync state and
 * for any actionable error, on two grounds that both hold on a shop floor: it disappears before
 * somebody holding a garment has read it, and a screen reader user may never hear it at all. An
 * alert therefore renders in the flow of the page, above the thing it is about, and waits.
 *
 * Colour is never the signal. Each tone has its own glyph shape and its own word — "Information",
 * "Warning", "Problem" — rendered for assistive technology even when a `title` is supplied, so the
 * severity survives greyscale, the high-contrast theme and a screen reader alike (1.4.1).
 */
export function Alert({
  tone,
  title,
  children,
  live = 'off',
  actions,
  onDismiss,
  className,
}: AlertProps) {
  const intl = useIntl()
  const titleId = useId()
  const presentation = TONE_PRESENTATION[tone]

  const liveAttributes =
    live === 'off'
      ? {}
      : live === 'assertive'
        ? { role: 'alert' as const }
        : { role: 'status' as const }

  return (
    <div
      className={cx('alert', className)}
      data-tone={tone}
      {...liveAttributes}
      {...(title === undefined ? {} : { 'aria-labelledby': titleId })}
    >
      <Icon name={presentation.icon} className="alert__icon" />
      <div className="alert__body">
        {/* The severity in words, for a reader who cannot see the glyph or the colour. It precedes
            the title so the message is classified before it is read. */}
        <span className="visually-hidden">
          {intl.formatMessage({ id: presentation.messageKey })}
          {': '}
        </span>
        {title === undefined ? null : (
          <p className="alert__title" id={titleId}>
            {title}
          </p>
        )}
        <div className="alert__content">{children}</div>
        {actions === undefined ? null : <div className="alert__actions">{actions}</div>}
      </div>
      {onDismiss === undefined ? null : (
        <IconButton
          className="alert__dismiss"
          name="close"
          label={intl.formatMessage({ id: 'primitives.alert.dismiss' })}
          onClick={onDismiss}
        />
      )}
    </div>
  )
}
