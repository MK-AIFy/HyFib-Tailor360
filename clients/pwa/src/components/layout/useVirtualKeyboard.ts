import { useEffect, useState } from 'react'

/**
 * How much of the viewport the on-screen keyboard is currently covering.
 *
 * ## Why the shell has to know
 *
 * WCAG 2.4.11 Focus Not Obscured is the failure a phone form produces first: the field being typed
 * in ends up behind the bottom navigation, and the keyboard has already taken half of what was left.
 * The product fixes it in three places, and all three are needed:
 *
 *  1. `index.html` carries `interactive-widget=resizes-content`, so a browser that honours it
 *     shrinks the layout viewport and the page can scroll the field into view by itself;
 *  2. `html` carries `scroll-padding-bottom`, so when the browser scrolls a focused control into
 *     view it stops clear of the bottom bar rather than flush against it;
 *  3. this hook, because iOS Safari honours neither. There the layout viewport does not change at
 *     all when the keyboard opens — only `visualViewport` does — so nothing above would fire, and
 *     the shell has to hide the bottom bar and pad its scroll container itself.
 *
 * ## The measurement
 *
 * `window.innerHeight - visualViewport.height` is the height the keyboard has taken, minus whatever
 * the page has been scrolled within the visual viewport. A threshold separates a keyboard from a
 * collapsing browser URL bar: the URL bar on the reference devices of
 * docs/nfr/support-matrix.md section 2 is under 120 CSS px, a keyboard is 250 px and up, so 150 px
 * sits in the gap with room on both sides.
 *
 * Where `visualViewport` is missing — an older browser, jsdom, a server render — the answer is
 * "closed, 0 px". That is the safe failure: a bottom bar that stays visible is a smaller problem
 * than one that disappears when it should not.
 */
export interface VirtualKeyboardState {
  /** True while the keyboard is covering the viewport. */
  readonly open: boolean
  /** How many CSS pixels it covers, rounded. Zero when closed. */
  readonly height: number
}

/** Below this many pixels the difference is a URL bar, not a keyboard. */
export const KEYBOARD_THRESHOLD_PX = 150

const CLOSED: VirtualKeyboardState = { open: false, height: 0 }

export function useVirtualKeyboard(): VirtualKeyboardState {
  const [state, setState] = useState<VirtualKeyboardState>(CLOSED)

  useEffect(() => {
    const viewport = window.visualViewport
    if (viewport === null || viewport === undefined) {
      return undefined
    }

    const update = () => {
      const covered = Math.round(window.innerHeight - viewport.height)
      setState(
        covered > KEYBOARD_THRESHOLD_PX ? { open: true, height: Math.max(covered, 0) } : CLOSED,
      )
    }

    update()
    // `scroll` as well as `resize`: on iOS the visual viewport is scrolled rather than resized when
    // the keyboard pushes the page, and only the scroll event fires.
    viewport.addEventListener('resize', update)
    viewport.addEventListener('scroll', update)
    return () => {
      viewport.removeEventListener('resize', update)
      viewport.removeEventListener('scroll', update)
    }
  }, [])

  return state
}
