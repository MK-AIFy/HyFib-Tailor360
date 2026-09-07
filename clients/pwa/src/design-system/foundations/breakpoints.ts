import type { ShellKind } from './types'

/**
 * Layout widths, in CSS pixels.
 *
 * A media query cannot read a custom property, so these numbers exist twice: here, and as
 * `--breakpoint-*` in src/styles/tokens.css. They are asserted equal by
 * src/design-system/foundations/breakpoints.test.ts, so the pair cannot drift.
 *
 * The values are not arbitrary. 320 is the reflow floor of WCAG 1.4.10; 360 is the reference
 * device's logical width (docs/nfr/support-matrix.md section 2); 768, 1024 and 1280 are the tablet
 * and desktop widths the overflow helper and the Playwright projects of #52 run at.
 */
export const BREAKPOINTS = {
  xs: 320,
  sm: 360,
  md: 768,
  lg: 1024,
  xl: 1280,
} as const

export type Breakpoint = keyof typeof BREAKPOINTS

/**
 * The widths every layout is proved against — docs/nfr/support-matrix.md section 2 and the #50
 * blueprint. The overflow helper iterates exactly this list.
 */
export const LAYOUT_PROOF_WIDTHS = [320, 360, 768, 1024, 1280] as const

/**
 * Which shell a width belongs to. The decision is made from the width of the container the screen
 * is in, never from a user-agent string: a desktop window narrowed to 400 px gets the phone shell,
 * which is exactly what 1.4.10 Reflow asks for.
 */
export function shellKindForWidth(width: number): ShellKind {
  if (width < BREAKPOINTS.md) {
    return 'phone'
  }
  if (width < BREAKPOINTS.lg) {
    return 'tablet'
  }
  return 'desktop'
}
