import { describe, expect, it } from 'vitest'
import {
  hasExpired,
  isStepUpFresh,
  millisecondsUntilWarning,
  secondsRemaining,
  sessionDeadline,
  warningDue,
} from './expiry'
import type { SessionExpiry } from './types'

const NOW = Date.parse('2026-09-05T10:00:00.000Z')

function expiry(idleMinutes: number, absoluteMinutes: number, lead = 120): SessionExpiry {
  return {
    idleExpiresAt: new Date(NOW + idleMinutes * 60_000).toISOString(),
    absoluteExpiresAt: new Date(NOW + absoluteMinutes * 60_000).toISOString(),
    warningLeadSeconds: lead,
    mfaSatisfied: true,
  }
}

describe('when a session ends', () => {
  it('ends at the idle deadline when that is the nearer of the two', () => {
    expect(sessionDeadline(expiry(30, 600))).toBe(NOW + 30 * 60_000)
  })

  it('ends at the absolute deadline when that is the nearer of the two', () => {
    // The case this exists for: a session kept warm all day. Watching only the idle deadline would
    // let it disappear without warning at the twelve-hour mark, which is exactly when somebody is
    // most likely to be mid-task rather than idle.
    expect(sessionDeadline(expiry(30, 5))).toBe(NOW + 5 * 60_000)
  })

  it('falls back to whichever deadline it could read when the other is not a date', () => {
    const broken: SessionExpiry = {
      idleExpiresAt: 'not a date',
      absoluteExpiresAt: new Date(NOW + 60_000).toISOString(),
      warningLeadSeconds: 120,
      mfaSatisfied: false,
    }
    expect(sessionDeadline(broken)).toBe(NOW + 60_000)
  })

  it('has no answer when neither deadline can be read', () => {
    const broken: SessionExpiry = {
      idleExpiresAt: '',
      absoluteExpiresAt: '',
      warningLeadSeconds: 120,
      mfaSatisfied: false,
    }
    expect(sessionDeadline(broken)).toBeUndefined()
    expect(millisecondsUntilWarning(broken, NOW)).toBeUndefined()
    expect(secondsRemaining(broken, NOW)).toBe(0)
  })
})

describe('the two-minute warning', () => {
  it('is due the lead the server named before the deadline', () => {
    expect(warningDue(expiry(30, 600))).toBe(NOW + 30 * 60_000 - 120_000)
  })

  it('counts down to the warning from now', () => {
    expect(millisecondsUntilWarning(expiry(30, 600), NOW)).toBe(28 * 60_000)
  })

  it('is due immediately rather than in the past, so a timer still fires', () => {
    // A negative delay is a timeout that never runs, which would mean a session ending with no
    // warning at all — the one failure this whole mechanism exists to prevent.
    expect(millisecondsUntilWarning(expiry(1, 600), NOW)).toBe(0)
  })

  it('leaves at least twenty seconds to answer in, which is what 2.2.1 asks for', () => {
    const lead = expiry(30, 600).warningLeadSeconds
    expect(lead).toBeGreaterThanOrEqual(20)
  })
})

describe('the countdown', () => {
  it('rounds up, so the last part-second is still announced as a second', () => {
    expect(secondsRemaining(expiry(30, 600), NOW + 30 * 60_000 - 1)).toBe(1)
  })

  it('never goes below zero', () => {
    expect(secondsRemaining(expiry(30, 600), NOW + 60 * 60_000)).toBe(0)
  })

  it('knows when the session has already gone', () => {
    expect(hasExpired(expiry(30, 600), NOW)).toBe(false)
    expect(hasExpired(expiry(30, 600), NOW + 30 * 60_000)).toBe(true)
  })
})

describe('step-up freshness', () => {
  it('is stale when no strong factor was ever proved', () => {
    expect(isStepUpFresh(null, 300, NOW)).toBe(false)
  })

  it('is fresh inside the window and stale outside it', () => {
    const proved = new Date(NOW - 4 * 60_000).toISOString()
    expect(isStepUpFresh(proved, 300, NOW)).toBe(true)
    expect(isStepUpFresh(proved, 60, NOW)).toBe(false)
  })

  it('is stale when the server sent something that is not a date', () => {
    // Fail closed. The alternative is treating an unreadable timestamp as a recent proof, which
    // would skip the step-up prompt before an action the server is about to refuse anyway.
    expect(isStepUpFresh('yesterday', 300, NOW)).toBe(false)
  })
})
