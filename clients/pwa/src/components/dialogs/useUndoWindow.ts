import { useCallback, useEffect, useRef, useState } from 'react'

/** The three seconds of docs/nfr/accessibility-localisation.md section 8.4, in milliseconds. */
export const UNDO_WINDOW_MS = 3000

/** How often the remaining time is recalculated. Fine enough for a countdown, coarse enough to be free. */
const TICK_MS = 100

export interface UndoWindowOptions {
  /** Defaults to three seconds. */
  readonly durationMs?: number
  /** Called once, when the window closes without an undo. */
  readonly onExpire: () => void
}

export interface UndoWindow {
  /** Milliseconds left, never below zero. */
  readonly remainingMs: number
  /** True while the countdown is held open. */
  readonly held: boolean
  /** Holds the window open — the pointer is over it, or focus is inside it. */
  readonly hold: () => void
  /** Lets it run again. */
  readonly release: () => void
}

/**
 * The countdown behind the three-second undo.
 *
 * ## The honest note about 2.2.1
 *
 * A control that disappears after three seconds is a time limit, and WCAG 2.2.1 Timing Adjustable
 * asks for time limits to be turnable off, adjustable or extendable. The mitigation here is `hold`,
 * and it is a real one rather than a formality: the window stops while the pointer is over the bar
 * and while focus is anywhere inside it, so a person who has reached the Undo control — by thumb,
 * by Tab or by a screen reader's own navigation — cannot have it vanish from under them. That also
 * answers checklist item A11Y-66, which is about focus landing on the document body when the element
 * under it is removed.
 *
 * What it does not do is guarantee that somebody reaches the bar within three seconds. That is why
 * this tier is reserved for **non-sensitive field actions** and nothing else: a value that was set,
 * a row that was reordered, a filter that was cleared — every one of them still editable by ordinary
 * means afterwards, so a missed undo costs a second edit rather than a supervisor correction.
 * Anything where a missed window would cost more than that takes a `ConfirmDialog` tier instead,
 * before the fact rather than after it.
 */
export function useUndoWindow({
  durationMs = UNDO_WINDOW_MS,
  onExpire,
}: UndoWindowOptions): UndoWindow {
  const [remainingMs, setRemainingMs] = useState(durationMs)
  const [held, setHeld] = useState(false)
  // The caller's callback usually changes identity on every render; held in a ref, written in an
  // effect rather than during render, so the timer below is not torn down and restarted — which
  // would quietly make the window longer every time the parent re-rendered.
  const expireRef = useRef(onExpire)
  useEffect(() => {
    expireRef.current = onExpire
  })

  const expired = remainingMs <= 0

  // One interval, and a state updater with no side effect in it. Both halves matter: an interval
  // keeps ticking without needing a re-render to schedule the next tick, and a side effect inside an
  // updater is a side effect React is free to run more than once — which is how a caller ends up
  // being told twice that one undo window closed.
  useEffect(() => {
    if (held || expired) {
      return undefined
    }

    const timer = setInterval(() => {
      setRemainingMs((previous) => Math.max(0, previous - TICK_MS))
    }, TICK_MS)

    return () => {
      clearInterval(timer)
    }
  }, [held, expired])

  // Keyed on the boolean rather than on the number, so the caller is told exactly once.
  useEffect(() => {
    if (expired) {
      expireRef.current()
    }
  }, [expired])

  const hold = useCallback(() => {
    setHeld(true)
  }, [])

  const release = useCallback(() => {
    setHeld(false)
  }, [])

  return { remainingMs, held, hold, release }
}
