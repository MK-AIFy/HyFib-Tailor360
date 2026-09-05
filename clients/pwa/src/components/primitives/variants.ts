import type { Tone } from '../../design-system/foundations/types'

/**
 * The visual variants the primitives offer, kept in a `.ts` sibling rather than beside the
 * components that use them.
 *
 * Two reasons, one practical and one structural. React Fast Refresh cannot refresh a module that
 * exports both a component and a value, so a variant list living in `Button.tsx` would cost a full
 * reload on every keystroke. And a variant list is data: a story iterates it to render every state,
 * and a test iterates it to assert that each one still renders — neither of which should have to
 * import a component to read a list of strings.
 */

/**
 * Button emphasis.
 *
 *  primary    the one action a screen wants — one per view, and on a phone it is the one in the
 *             thumb-reach action bar
 *  secondary  an ordinary action, bordered rather than filled
 *  subtle     a tertiary action that must not compete: no fill, no border until hover
 *  danger     destructive and irreversible. Never placed beside a frequent action without the 24 px
 *             separation of docs/nfr/accessibility-localisation.md section 5 rule 2 (A11Y-69), and
 *             always confirmed by the ConfirmDialog tiers of the overlays family
 *
 * Emphasis is deliberately not the same axis as `ControlSize`. A `danger` button is usually
 * `standard`-sized; a `primary` shop-floor action is `primary`-sized because a Tailor taps it with a
 * needle in hand. Confusing the two is why the size union is named for the AL-03 control classes and
 * this one is named for emphasis.
 */
export const BUTTON_VARIANTS = ['primary', 'secondary', 'subtle', 'danger'] as const

export type ButtonVariant = (typeof BUTTON_VARIANTS)[number]

/** The tones an `Alert` can take. A tone chooses a colour pair; the icon and the word carry the meaning. */
export const ALERT_TONES = ['info', 'success', 'warning', 'danger'] as const

export type AlertTone = (typeof ALERT_TONES)[number] & Tone
