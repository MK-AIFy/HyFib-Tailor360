import { useSyncExternalStore } from 'react'
import { BREAKPOINTS } from '../../design-system/foundations/breakpoints'
import type { ShellKind } from '../../design-system/foundations/types'

/**
 * Which shell the **viewport** is wide enough for — phone, tablet or desktop.
 *
 * It is a width question, never a user-agent question: a desktop window narrowed to 400 px is a
 * phone layout, which is exactly what 1.4.10 Reflow asks for, and a tablet in landscape is a tablet
 * whatever its operating system claims. `shellKindForWidth` in the foundations holds the same rule
 * for code that already has a width in hand.
 *
 * ## Why this is not the layout family's `useShellKind`
 *
 * That one measures a **container** and is the right answer for everything drawn inside the shell:
 * a region narrowed by a master-detail split should lay itself out for the width it actually has.
 * A modal has no such container. It is portalled to the end of `body` and covers the whole screen,
 * so the thing it has to fit is the viewport, and asking a container would give the wrong answer for
 * a dialog opened from inside a narrow pane on a wide desktop.
 *
 * Every component in this family also takes an explicit `shellKind` override, so a shell that has
 * already worked the answer out never has to run a second observer for it.
 */
const DESKTOP_QUERY = `(min-width: ${String(BREAKPOINTS.lg)}px)`
const TABLET_QUERY = `(min-width: ${String(BREAKPOINTS.md)}px)`

function subscribe(onStoreChange: () => void): () => void {
  if (typeof window.matchMedia !== 'function') {
    return () => undefined
  }
  const lists = [window.matchMedia(DESKTOP_QUERY), window.matchMedia(TABLET_QUERY)]
  for (const list of lists) {
    list.addEventListener('change', onStoreChange)
  }
  return () => {
    for (const list of lists) {
      list.removeEventListener('change', onStoreChange)
    }
  }
}

function getSnapshot(): ShellKind {
  if (typeof window.matchMedia !== 'function') {
    // No media-query support, and the test environment. The phone shell is the safe answer: every
    // control is at its largest, nothing is dense, and the typed-confirmation tier is unavailable.
    return 'phone'
  }
  if (window.matchMedia(DESKTOP_QUERY).matches) {
    return 'desktop'
  }
  if (window.matchMedia(TABLET_QUERY).matches) {
    return 'tablet'
  }
  return 'phone'
}

function getServerSnapshot(): ShellKind {
  return 'phone'
}

/**
 * @param override a shell chosen by the caller — a layout that already knows, a story, or a test
 *                 proving what a phone does with a tier it is not allowed to show.
 */
export function useViewportShellKind(override?: ShellKind): ShellKind {
  const detected = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot)
  return override ?? detected
}
