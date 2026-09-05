import type { SessionExpiry } from './types'

/**
 * When a session ends, and when to warn about it.
 *
 * Pure functions in a `.ts` sibling, because the arithmetic behind "warn two minutes before the
 * session dies" is exactly the thing a test should be able to assert without rendering a dialog or
 * advancing a timer — and because getting it wrong in either direction is a real failure. Warn too
 * late and 2.2.1 Timing Adjustable is not met: the person has no chance to answer. Warn against the
 * wrong deadline and the dialog appears while the session has hours left, which teaches people to
 * dismiss it.
 */

/** A parsed instant, or undefined when the server sent something unparseable. */
function instant(iso: string): number | undefined {
  const value = Date.parse(iso)
  return Number.isNaN(value) ? undefined : value
}

/**
 * When the session actually ends: the earlier of the two deadlines.
 *
 * A session dies at the idle deadline if nothing uses it, and at the absolute deadline however busy
 * it is. Watching only the idle one means a session kept warm all day disappears without warning at
 * the twelve-hour mark, which is precisely the case the absolute deadline exists to create.
 */
export function sessionDeadline(expiry: SessionExpiry): number | undefined {
  const idle = instant(expiry.idleExpiresAt)
  const absolute = instant(expiry.absoluteExpiresAt)

  if (idle === undefined) {
    return absolute
  }
  if (absolute === undefined) {
    return idle
  }
  return Math.min(idle, absolute)
}

/** When the warning is due: the lead the server named, before the deadline. */
export function warningDue(expiry: SessionExpiry): number | undefined {
  const deadline = sessionDeadline(expiry)
  if (deadline === undefined) {
    return undefined
  }
  return deadline - Math.max(0, expiry.warningLeadSeconds) * 1000
}

/**
 * How long until the warning is due, from a given moment. Never negative: a warning already due is
 * due now, and a caller that schedules a negative timeout would never fire it.
 */
export function millisecondsUntilWarning(expiry: SessionExpiry, now: number): number | undefined {
  const due = warningDue(expiry)
  return due === undefined ? undefined : Math.max(0, due - now)
}

/** How many whole seconds of session are left, rounded up so the last second is announced as one. */
export function secondsRemaining(expiry: SessionExpiry, now: number): number {
  const deadline = sessionDeadline(expiry)
  if (deadline === undefined) {
    return 0
  }
  return Math.max(0, Math.ceil((deadline - now) / 1000))
}

/** Whether the session has already ended. */
export function hasExpired(expiry: SessionExpiry, now: number): boolean {
  const deadline = sessionDeadline(expiry)
  return deadline !== undefined && now >= deadline
}

/**
 * Whether a strong factor was proved recently enough for an action that demands one.
 *
 * The server is the authority — `PermissionAuthorisationHandler` re-checks it, and a client that
 * decided this for itself would be a client that could be told to stop checking. This is used only to
 * ask *before* an action rather than after it is refused, which is the difference between a dialog
 * the person expected and an error they did not.
 */
export function isStepUpFresh(
  lastStrongAuthenticationAt: string | null,
  freshnessSeconds: number,
  now: number,
): boolean {
  if (lastStrongAuthenticationAt === null) {
    return false
  }
  const proved = instant(lastStrongAuthenticationAt)
  if (proved === undefined) {
    return false
  }
  return now - proved <= Math.max(0, freshnessSeconds) * 1000
}

/**
 * The step-up freshness window, in seconds.
 *
 * It mirrors `Security:StepUp:Freshness`, whose shipped default is five minutes. The client cannot
 * read server configuration, and this value is only ever used to decide whether to *offer* a
 * re-authentication before an action; the server decides whether one was needed.
 */
export const STEP_UP_FRESHNESS_SECONDS = 300
