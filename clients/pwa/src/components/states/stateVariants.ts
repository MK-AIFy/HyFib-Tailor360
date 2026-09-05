/**
 * The variants the state family offers, kept in a `.ts` sibling rather than beside the components.
 *
 * React Fast Refresh cannot refresh a module that exports both a component and a value, and a
 * variant list is data anyway: a story iterates it to render every state and a test iterates it to
 * assert that each one still renders, neither of which should have to import a component.
 */

/** The colour pair a state takes. The glyph shape and the words carry the meaning (1.4.1). */
export const STATE_TONES = ['neutral', 'info', 'warning', 'danger'] as const

export type StateTone = (typeof STATE_TONES)[number]

/**
 * How assistive technology is told about a state.
 *
 *   off        the state was on the screen when it rendered. It is read in document order, and a
 *              live region would announce it a second time.
 *   polite     the state appeared after the screen did — a search returned nothing, a list finished
 *              loading and was empty, a connection went. 4.1.3 Status Messages.
 *   assertive  the state interrupts. Only two things earn it in this product: a rejected scan
 *              naming which rule failed, and a failure that has stopped the person mid-task
 *              (docs/nfr/accessibility-localisation.md section 6).
 */
export const STATE_LIVENESS = ['off', 'polite', 'assertive'] as const

export type StateLiveness = (typeof STATE_LIVENESS)[number]

/** The heading levels a state region may render as. The caller knows the outline; the state does not. */
export type StateHeadingLevel = 2 | 3 | 4 | 5 | 6
