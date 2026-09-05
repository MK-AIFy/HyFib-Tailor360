import { useEffect, useState } from 'react'

/**
 * Whether the on-screen keyboard is currently covering part of the viewport.
 *
 * WCAG 2.4.11 Focus Not Obscured is the single most common failure on a phone form: the field a
 * person is typing in disappears behind the bottom navigation, and on a small screen the keyboard
 * takes most of what is left. `index.html` already carries `interactive-widget=resizes-content`, and
 * `html` carries `scroll-padding-bottom`, so a focused field scrolls clear of the bar. This is the
 * other half: while the keyboard is up, the bottom bar gets out of the way entirely.
 *
 * The measurement is `visualViewport`, which is the only thing that reports the keyboard on iOS
 * Safari, where the layout viewport does not change at all. A drop of more than 150 px between the
 * visual and the layout viewport is a keyboard rather than a URL bar collapsing — the URL bar on the
 * reference devices is under 120 px.
 *
 * ## Scope note
 *
 * This is a minimal local implementation, written for the navigation family because the bottom bar
 * cannot be correct without it. The #50 blueprint places a fuller `useVirtualKeyboard` in the shared
 * hooks directory, alongside `useMediaQuery` and `useContainerSize`, for the shells to use when they
 * size their scroll containers. When that arrives this should be deleted and the import re-pointed;
 * the behaviour is meant to be identical, and `BottomNav` takes an explicit `keyboardOpen` override
 * so a shell that already knows the answer never has to run two observers.
 */
const KEYBOARD_THRESHOLD_PX = 150

export function useVirtualKeyboardOpen(): boolean {
  const [open, setOpen] = useState(false)

  useEffect(() => {
    const viewport = window.visualViewport
    if (viewport === null || viewport === undefined) {
      // No visualViewport: an older browser, or jsdom. The bar simply never hides, which is the
      // safe failure — a visible navigation bar is a smaller problem than a missing one.
      return undefined
    }

    const update = () => {
      setOpen(window.innerHeight - viewport.height > KEYBOARD_THRESHOLD_PX)
    }

    update()
    viewport.addEventListener('resize', update)
    return () => {
      viewport.removeEventListener('resize', update)
    }
  }, [])

  return open
}
