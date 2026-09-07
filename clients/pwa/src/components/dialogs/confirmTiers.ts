import type { ShellKind } from '../../design-system/foundations/types'

/**
 * The three confirmation tiers of docs/nfr/accessibility-localisation.md section 8.4.
 *
 *   confirm  a plain "are you sure". The action is significant but recoverable
 *   reason   a mandatory typed reason, stored on the audit event. Required for **corrections,
 *            reprints, holds, cancellations and manual lookups** — the actions whose audit trail is
 *            the only record of why somebody stepped outside the ordinary path
 *   typed    the person types a phrase to confirm. Reserved for **desktop and tablet administration
 *            actions** and never asked of somebody on a phone in a workshop, where it is a minute of
 *            one-handed typing in front of a waiting customer
 *
 * The tier is a property of the action, not of the screen: cancelling an order is a `reason` tier
 * wherever it is cancelled from. Only the third tier is affected by where the person is standing,
 * and `resolveConfirmTier` is where that happens.
 */
export const CONFIRM_TIERS = ['confirm', 'reason', 'typed'] as const

export type ConfirmTier = (typeof CONFIRM_TIERS)[number]

/** What a requested tier becomes on a given shell. */
export interface ResolvedConfirmTier {
  /** The tier actually rendered. */
  readonly tier: ConfirmTier
  /**
   * Whether the confirming control has to be pressed twice, with the second press explicitly armed
   * by the first. The phone substitute for the typed tier.
   */
  readonly requiresSecondPress: boolean
}

/**
 * Resolves a requested tier against the shell the person is actually in.
 *
 * One rule, and it only ever moves in one direction: **the typed tier does not exist on a phone.**
 * The blueprint and checklist item A11Y-BI-13 both state it as an absolute, and the substitute is
 * named — confirm with a reason, plus a second explicit press. That substitute is not a weaker
 * confirmation: the reason is still mandatory and still audited, and the second press asks for the
 * same deliberateness that typing a phrase asks for, in the form a thumb can give it.
 *
 * Nothing here ever raises a tier. A screen that wants a stronger confirmation on a phone says so by
 * asking for a stronger tier, because a component that silently escalated would be deciding policy.
 */
export function resolveConfirmTier(tier: ConfirmTier, shellKind: ShellKind): ResolvedConfirmTier {
  if (tier === 'typed' && shellKind === 'phone') {
    return { tier: 'reason', requiresSecondPress: true }
  }
  return { tier, requiresSecondPress: false }
}
