import { useEffect, useRef, useState } from 'react'
import { useIntl } from 'react-intl'
import { Dialog } from '../components/dialogs/Dialog'
import { Button } from '../components/primitives/Button'
import { secondsRemaining } from './expiry'
import type { SessionExpiry } from './types'
import './auth.css'

/**
 * The two-minute warning.
 *
 * WCAG 2.2.1 Timing Adjustable is the requirement and it is specific: where a time limit exists, the
 * person must be warned at least twenty seconds before it expires and must be able to extend it with
 * a simple action. Two minutes is the lead the server names, and "Carry on working" is the simple
 * action — it makes one ordinary request, which slides the idle deadline, and the dialog closes.
 *
 * ## How the countdown is announced
 *
 * The visible number changes every second, and that number is `aria-hidden`. A live region updating
 * once a second would talk over everything a screen-reader user is trying to hear, which is a worse
 * outcome than no countdown at all. The announcement is a separate polite region that changes only
 * when the remaining time crosses two minutes, one minute, thirty seconds and ten seconds — four
 * announcements, each of which says both how long is left and what to do about it.
 *
 * ## What happens if nobody answers
 *
 * The session ends and `onExpired` fires, which is what turns this dialog into the re-authentication
 * one without anything leaving the screen. Nothing is lost either way: this dialog does not sit on
 * top of unsaved work by accident, it sits on top of it deliberately, and the work is still there
 * afterwards.
 */
export interface SessionExpiryDialogProps {
  /**
   * When the session ends. There is no `open` prop: the dialog is mounted when the warning is due
   * and unmounted when it is answered, which is what keeps the countdown honest — a dialog kept
   * mounted and hidden would carry the previous session's remaining seconds into the next one.
   */
  readonly expiry: SessionExpiry
  /** Makes a request that slides the idle deadline, and closes the dialog. */
  readonly onKeepWorking: () => void
  /** Ends the session now, because the person is walking away from a shared device. */
  readonly onSignOut: () => void
  /** The countdown reached zero. */
  readonly onExpired: () => void
  /** A request raised by one of the controls is in flight. */
  readonly busy?: boolean
}

/** The moments the polite region speaks, longest first. */
const ANNOUNCEMENT_THRESHOLDS = [120, 60, 30, 10] as const

function announcementFor(seconds: number): number {
  for (const threshold of ANNOUNCEMENT_THRESHOLDS) {
    if (seconds > threshold) {
      continue
    }
    return threshold
  }
  return ANNOUNCEMENT_THRESHOLDS[0]
}

export function SessionExpiryDialog({
  expiry,
  onKeepWorking,
  onSignOut,
  onExpired,
  busy = false,
}: SessionExpiryDialogProps) {
  const intl = useIntl()
  const keepWorkingRef = useRef<HTMLButtonElement>(null)
  const [seconds, setSeconds] = useState(() => secondsRemaining(expiry, Date.now()))

  // The callback changes identity on every render of the provider; held in a ref so the interval
  // below is created once per opening rather than torn down and rebuilt every second.
  const expiredRef = useRef(onExpired)
  useEffect(() => {
    expiredRef.current = onExpired
  })

  useEffect(() => {
    const timer = setInterval(() => {
      const remaining = secondsRemaining(expiry, Date.now())
      setSeconds(remaining)
      if (remaining <= 0) {
        clearInterval(timer)
        expiredRef.current()
      }
    }, 1000)

    return () => {
      clearInterval(timer)
    }
  }, [expiry])

  return (
    <Dialog
      // Dismissing it is the same as ignoring it, and ignoring it is a real answer: the session ends
      // and the re-authentication dialog takes over. So there is nothing to protect from a mis-touch.
      description={
        <>
          <p>{intl.formatMessage({ id: 'auth.expiry.body' })}</p>
          <p aria-hidden="true" className="auth-countdown">
            {intl.formatMessage({ id: 'auth.expiry.countdown' }, { seconds })}
          </p>
        </>
      }
      footer={
        <>
          <Button busy={busy} onClick={onSignOut} variant="secondary">
            {intl.formatMessage({ id: 'auth.expiry.signOutNow' })}
          </Button>
          <Button busy={busy} onClick={onKeepWorking} ref={keepWorkingRef} variant="primary">
            {intl.formatMessage({ id: 'auth.expiry.keepWorking' })}
          </Button>
        </>
      }
      initialFocusRef={keepWorkingRef}
      onClose={onExpired}
      open
      title={intl.formatMessage({ id: 'auth.expiry.title' })}
    >
      {/* Present from the moment the dialog opens rather than appearing with its text: a live region
          inserted at the same time as its content is the commonest reason an announcement is missed. */}
      <p className="visually-hidden" role="status">
        {intl.formatMessage({ id: 'auth.expiry.announce' }, { seconds: announcementFor(seconds) })}
      </p>
    </Dialog>
  )
}
