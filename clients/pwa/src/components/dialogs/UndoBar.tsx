import { useEffect, useRef } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { setCssVariable } from '../../design-system/foundations/setCssVariable'
import { Button } from '../primitives/Button'
import { UNDO_WINDOW_MS, useUndoWindow } from './useUndoWindow'
import './dialogs.css'

export interface UndoBarProps {
  /**
   * What just happened, in words and in the past tense: "Shoulder set to 16 1/2 in", "Filters
   * cleared". It is announced once, when the bar appears.
   */
  readonly action: string
  /** Puts it back. */
  readonly onUndo: () => void
  /** Called when the window closes without an undo, so the screen can take the bar away. */
  readonly onExpire: () => void
  /** Defaults to three seconds, which is the figure section 8.4 fixes. */
  readonly durationMs?: number
  readonly undoLabel?: string
  readonly className?: string
}

/**
 * Three seconds to take it back.
 *
 * The counterpart to `ConfirmDialog`, and the reason most field edits need no confirmation at all:
 * docs/nfr/accessibility-localisation.md section 8.4 pairs them deliberately — non-sensitive field
 * actions offer an undo afterwards, sensitive ones ask before. Asking twice for a shoulder
 * measurement is how people learn to dismiss confirmations without reading them, which is what makes
 * the confirmation on the invoice worthless.
 *
 * ## It is not a toast
 *
 * Section 6 forbids a toast for a scan result, for sync state and for any actionable error, and this
 * is none of the three — but the rule behind it still applies, so the bar follows it. It renders in
 * the flow of the page rather than floating over content, so it never covers the control a person is
 * working with (2.4.11); it holds its countdown open while the pointer is over it or focus is inside
 * it, so it cannot disappear from under the hand reaching for it; and the announcement is made once,
 * politely, rather than being re-read on every tick. The countdown itself is `aria-hidden` for that
 * last reason: a live region that updates ten times a second says nothing at all.
 */
export function UndoBar({
  action,
  onUndo,
  onExpire,
  durationMs = UNDO_WINDOW_MS,
  undoLabel,
  className,
}: UndoBarProps) {
  const intl = useIntl()
  const { remainingMs, held, hold, release } = useUndoWindow({ durationMs, onExpire })
  const barRef = useRef<HTMLDivElement | null>(null)

  // The one value here that genuinely cannot be static, written as a custom property through the
  // CSSOM — the sanctioned escape hatch documented in foundations/setCssVariable.ts. There is no
  // inline style anywhere in this application, and a `style` attribute in JSX would be one.
  useEffect(() => {
    const bar = barRef.current
    if (bar !== null) {
      setCssVariable(bar, '--undo-remaining', String(remainingMs / durationMs))
    }
  }, [remainingMs, durationMs])

  const seconds = Math.ceil(remainingMs / 1000)

  return (
    <div
      className={cx('undo-bar', className)}
      data-held={held ? 'true' : undefined}
      onBlur={release}
      onFocus={hold}
      onPointerEnter={hold}
      onPointerLeave={release}
      ref={barRef}
    >
      {/* Announced once, when the bar appears. Nothing that changes goes inside this region. */}
      <p className="undo-bar__message" role="status">
        {intl.formatMessage({ id: 'dialogs.undo.announcement' }, { action })}
      </p>
      <span aria-hidden="true" className="undo-bar__countdown">
        {held
          ? intl.formatMessage({ id: 'dialogs.undo.held' })
          : intl.formatMessage({ id: 'dialogs.undo.remaining' }, { seconds })}
      </span>
      <span aria-hidden="true" className="undo-bar__progress" />
      <Button iconName="refresh" onClick={onUndo} variant="secondary">
        {undoLabel ?? intl.formatMessage({ id: 'dialogs.undo.label' })}
      </Button>
    </div>
  )
}
