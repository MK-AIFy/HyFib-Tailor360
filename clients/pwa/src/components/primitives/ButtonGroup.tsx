import type { ReactNode } from 'react'
import { cx } from '../../design-system/foundations/cx'
import type { ControlSize } from '../../design-system/foundations/types'
import './Button.css'

export interface ButtonGroupProps {
  readonly children: ReactNode
  /**
   * Which spacing rule applies. docs/nfr/accessibility-localisation.md section 5 sets 12 px between
   * primary shop-floor actions and 8 px between standard controls, and this is where those numbers
   * are actually spent — a size token on a button sets the target, a gap on the group sets the
   * spacing, and A11Y-68 measures both.
   */
  readonly size?: ControlSize
  readonly orientation?: 'horizontal' | 'vertical'
  /**
   * A destructive or irreversible action, rendered last and separated from everything before it by
   * at least 24 px.
   *
   * Section 5 rule 2 and checklist item A11Y-69: Dispatch does not sit beside Cancel order, and
   * Delete evidence does not sit beside Add evidence. Passing the destructive action here rather
   * than as another child is what makes the separation structural instead of a spacing decision
   * somebody has to remember on every screen.
   */
  readonly destructiveAction?: ReactNode
  /** A label when the group is a toolbar of related controls rather than a form's action row. */
  readonly label?: string
  readonly className?: string
}

/**
 * A row or column of buttons, spaced by the rule rather than by eye.
 *
 * `role="group"` only when the caller names it: an unnamed group role adds a layer to the screen
 * reader's tree and says nothing, which is worse than no role at all.
 */
export function ButtonGroup({
  children,
  size = 'standard',
  orientation = 'horizontal',
  destructiveAction,
  label,
  className,
}: ButtonGroupProps) {
  return (
    <div
      className={cx('button-group', className)}
      data-size={size}
      data-orientation={orientation}
      {...(label === undefined ? {} : { role: 'group', 'aria-label': label })}
    >
      {children}
      {destructiveAction === undefined ? null : (
        <span className="button-group__separated">{destructiveAction}</span>
      )}
    </div>
  )
}
