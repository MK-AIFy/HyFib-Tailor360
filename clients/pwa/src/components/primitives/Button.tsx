import type { ButtonHTMLAttributes, ReactNode, Ref } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import type { ControlSize } from '../../design-system/foundations/types'
import { Icon } from './Icon'
import type { IconName } from './icons'
import type { ButtonVariant } from './variants'
import './Button.css'

/**
 * Attributes a caller may still pass through.
 *
 * `style` is omitted on purpose, and the omission is the house rule made mechanical: the application
 * runs under a Content Security Policy with a nonce, a `style` attribute in JSX is an inline style,
 * and a rule a type checker enforces is worth more than a rule a reviewer has to remember. A value
 * that genuinely cannot be static goes through `setCssVariable` and the CSSOM instead.
 *
 * `disabled` is omitted too — see the `busy` prop below.
 */
type PassThroughAttributes = Omit<
  ButtonHTMLAttributes<HTMLButtonElement>,
  'className' | 'children' | 'style' | 'type' | 'disabled' | 'aria-disabled' | 'aria-busy'
>

export interface ButtonProps extends PassThroughAttributes {
  /** The visible label. Always text: an icon-only control is `IconButton`, which demands a name. */
  readonly children: ReactNode
  /** Emphasis. Defaults to `secondary`, so an unconsidered button never claims to be the main one. */
  readonly variant?: ButtonVariant
  /**
   * Target size, from docs/nfr/accessibility-localisation.md section 5. Defaults to `standard`
   * (44 px). A shop-floor action a person reaches for with a garment in the other hand — scan,
   * capture, confirm, take payment, dispatch — is `primary` (56 px). `dense` (32 px) is a desktop
   * toolbar size and collapses back to 44 px on a coarse pointer, so it cannot reach a phone.
   */
  readonly size?: ControlSize
  /** A leading glyph. Decorative: the label beside it is what is announced. */
  readonly iconName?: IconName
  /** Which side the glyph sits on. `trailing` suits "Next" and anything that opens something. */
  readonly iconPosition?: 'leading' | 'trailing'
  /** Fills the width of its container — the phone action bar, a bottom sheet footer. */
  readonly fullWidth?: boolean
  /**
   * The action is in flight.
   *
   * Deliberately not `disabled`. A disabled button drops out of the tab order, and a button that
   * disappears from under the focus is exactly the failure checklist item A11Y-66 describes: on a
   * phone, focus falls to the document body and the person has to tab the whole screen again,
   * one-handed. `busy` keeps the control focusable, announces itself, and swallows the second tap.
   */
  readonly busy?: boolean
  /**
   * Genuinely not available, for a reason the screen states elsewhere. Never used to convey status,
   * and never as a substitute for `busy`.
   */
  readonly unavailable?: boolean
  /** Defaults to `button`: a button inside a form submits it unless it says otherwise, and that surprise has cost more data than it has ever saved. */
  readonly type?: 'button' | 'submit' | 'reset'
  readonly className?: string
  readonly ref?: Ref<HTMLButtonElement>
}

/**
 * The button.
 *
 * Three behaviours are worth knowing because they are requirements rather than preferences:
 *
 *  - **It fires on release.** That is native `click` behaviour and it is why this component does not
 *    reach for `onPointerDown`: 2.5.2 Pointer Cancellation wants a mis-touch to be abandonable by
 *    sliding off the control before letting go, which matters when the other hand is holding a
 *    garment (checklist item A11Y-77).
 *  - **The hit area is the target, not the ink.** The minimum block and inline size come from the
 *    size tokens, so a 20 px glyph inside a `standard` button still presents a 44 px target
 *    (section 5 rule 1, checklist item A11Y-68).
 *  - **The label is text and is inside the accessible name.** 2.5.3 Label in Name is what makes
 *    "tap Confirm order" work on voice control, and it is why there is no `aria-label` override here.
 */
export function Button({
  children,
  variant = 'secondary',
  size = 'standard',
  iconName,
  iconPosition = 'leading',
  fullWidth = false,
  busy = false,
  unavailable = false,
  type = 'button',
  className,
  onClick,
  ref,
  ...rest
}: ButtonProps) {
  const intl = useIntl()
  const inert = busy || unavailable

  return (
    <button
      {...rest}
      ref={ref}
      type={type}
      className={cx('button', className)}
      data-variant={variant}
      data-size={size}
      data-full-width={fullWidth ? 'true' : undefined}
      data-busy={busy ? 'true' : undefined}
      // aria-disabled rather than the disabled attribute: the control stays focusable, so nobody
      // loses their place mid-task, and assistive technology still says it cannot be used.
      aria-disabled={inert ? true : undefined}
      aria-busy={busy ? true : undefined}
      onClick={
        inert
          ? (event) => {
              event.preventDefault()
            }
          : onClick
      }
    >
      {iconName !== undefined && iconPosition === 'leading' ? <Icon name={iconName} /> : null}
      <span className="button__label">{children}</span>
      {iconName !== undefined && iconPosition === 'trailing' ? <Icon name={iconName} /> : null}
      {busy ? (
        <span className="visually-hidden">
          {intl.formatMessage({ id: 'primitives.button.busy' })}
        </span>
      ) : null}
    </button>
  )
}
