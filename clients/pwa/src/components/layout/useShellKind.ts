import { shellKindForWidth } from '../../design-system/foundations/breakpoints'
import type { ShellKind } from '../../design-system/foundations/types'
import { useElementWidth } from './useElementWidth'
import type { RefObject } from 'react'

/**
 * Which of the three shells a container is wide enough for.
 *
 * The thresholds are `BREAKPOINTS` in the foundations, so the CSS container queries in the layout
 * stylesheet and this decision cannot drift apart: under 768 px is the phone shell, under 1024 px
 * the tablet, and 1024 px and above the desktop.
 *
 * The `override` exists for two honest reasons and no others. A story renders all three shells side
 * by side to be reviewed, and a test asserts the phone shell without a browser that has a width. It
 * is not a device check and must never become one: a desktop window dragged to 400 px gets the phone
 * shell, which is exactly what 1.4.10 Reflow asks for.
 */
export function useShellKind(
  ref: RefObject<HTMLElement | null>,
  override?: ShellKind | undefined,
): ShellKind {
  const width = useElementWidth(ref)
  return override ?? shellKindForWidth(width)
}
