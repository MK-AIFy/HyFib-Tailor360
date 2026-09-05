import type { ButtonHTMLAttributes, Ref } from 'react'
import { cx } from '../../design-system/foundations/cx'
import type { ControlSize } from '../../design-system/foundations/types'
import { Icon } from './Icon'
import type { IconName } from './icons'
import type { ButtonVariant } from './variants'
import './Button.css'

type PassThroughAttributes = Omit<
  ButtonHTMLAttributes<HTMLButtonElement>,
  | 'className'
  | 'children'
  | 'style'
  | 'type'
  | 'disabled'
  | 'aria-disabled'
  | 'aria-label'
  | 'title'
>

export interface IconButtonProps extends PassThroughAttributes {
  /**
   * The accessible name, and the only way to name this control. Required, with no default: an
   * unlabelled icon button is the single commonest screen-reader defect in a design system, and the
   * type checker is a cheaper place to catch it than a manual audit.
   *
   * Write what the control *does*, and include the thing it does it to. Checklist item A11Y-60 asks
   * whether a row action says **which** row it belongs to — "Print label, job J-CBE01-2627-000512-01"
   * rather than eleven identical "Print"s in a queue, which is a custody error waiting to happen.
   */
  readonly label: string
  readonly name: IconName
  readonly variant?: ButtonVariant
  readonly size?: ControlSize
  readonly unavailable?: boolean
  readonly type?: 'button' | 'submit' | 'reset'
  readonly className?: string
  readonly ref?: Ref<HTMLButtonElement>
}

/**
 * A button whose visible content is a glyph.
 *
 * The name is supplied as text and rendered visually hidden rather than as an `aria-label`. Three
 * things follow, all of them wanted:
 *
 *  - the name is translated, because it came through the message catalogue like every other string;
 *  - the name is in the accessible name computation the ordinary way, so 2.5.3 Label in Name and
 *    voice control behave as they do for a labelled button;
 *  - the name is findable in the DOM, so `getByRole('button', { name })` in a test is asserting the
 *    same string a screen reader will read.
 *
 * The glyph itself stays `aria-hidden`, so nothing is announced twice (checklist item A11Y-52).
 */
export function IconButton({
  label,
  name,
  variant = 'subtle',
  size = 'standard',
  unavailable = false,
  type = 'button',
  className,
  onClick,
  ref,
  ...rest
}: IconButtonProps) {
  return (
    <button
      {...rest}
      ref={ref}
      type={type}
      className={cx('button', 'icon-button', className)}
      data-variant={variant}
      data-size={size}
      aria-disabled={unavailable ? true : undefined}
      onClick={
        unavailable
          ? (event) => {
              event.preventDefault()
            }
          : onClick
      }
    >
      <Icon name={name} />
      <span className="visually-hidden">{label}</span>
    </button>
  )
}
