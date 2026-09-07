/**
 * The shared vocabulary of the design system.
 *
 * Every union here is an `as const` tuple plus a derived type, never a TypeScript enum: the
 * application compiles with `erasableSyntaxOnly`, and a tuple additionally gives a runtime list that
 * a Storybook control or a test can iterate.
 */

/**
 * The meaning a piece of interface carries. A tone chooses a colour pair — never the meaning
 * itself: docs/nfr/accessibility-localisation.md section 4.1 forbids status conveyed by colour
 * alone, so every toned component also carries an icon and a word.
 */
export const TONES = ['neutral', 'brand', 'info', 'success', 'warning', 'danger'] as const

export type Tone = (typeof TONES)[number]

/**
 * Control sizes, fixed by docs/nfr/accessibility-localisation.md section 5 (AL-03) and measured by
 * checklist item A11Y-68:
 *
 *   primary   56 x 56 CSS px, 12 px spacing — scan, capture, confirm, take payment, dispatch
 *   standard  44 x 44 CSS px, 8 px spacing — every other interactive control
 *   dense     32 x 32 CSS px, 8 px spacing — desktop toolbars and table controls only, and the
 *             compact-density rule in themes.css restores 44 px on a coarse pointer, so this size
 *             cannot reach a phone or a tablet however it is set
 */
export const CONTROL_SIZES = ['primary', 'standard', 'dense'] as const

export type ControlSize = (typeof CONTROL_SIZES)[number]

/** The shell a screen is being rendered in. Chosen from the container width, not from the device. */
export const SHELL_KINDS = ['phone', 'tablet', 'desktop'] as const

export type ShellKind = (typeof SHELL_KINDS)[number]

/** Row and control density. Compact is a desktop affordance; see CONTROL_SIZES. */
export const DENSITIES = ['comfortable', 'compact'] as const

export type Density = (typeof DENSITIES)[number]

/**
 * The display preferences stored server-side in `identity.user_preferences` and applied at login, so
 * a shared counter or workshop device does not leak one person's settings to the next or lose them
 * at sign-out. `system` follows `prefers-color-scheme` and `prefers-contrast`; the other three are
 * explicit and win over the operating system.
 */
export const THEME_PREFERENCES = ['system', 'light', 'dark', 'contrast'] as const

export type ThemePreference = (typeof THEME_PREFERENCES)[number]

/** The product's own text-size preference, separate from browser zoom (checklist item A11Y-72). */
export const TEXT_SIZE_PREFERENCES = ['100', '125', '150'] as const

export type TextSizePreference = (typeof TEXT_SIZE_PREFERENCES)[number]

/**
 * The eight roles the reference journeys are walked as, taken from the #50 evidence list and from
 * `A11Y-RJ-01`..`A11Y-RJ-08` in docs/nfr/a11y-checklist.md.
 *
 * These are journey subjects, not the authorisation model. They differ from the RACI columns in
 * docs/prd/raci.md on purpose: measurement staff is a permission bundle rather than a RACI column,
 * and branch manager is not among the eight because its journeys are the owner's.
 */
export const JOURNEY_ROLES = [
  'reception',
  'measurement-staff',
  'tailor',
  'tailor-master',
  'inventory',
  'cashier',
  'delivery',
  'owner',
] as const

export type JourneyRole = (typeof JOURNEY_ROLES)[number]
