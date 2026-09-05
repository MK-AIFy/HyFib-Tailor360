import { describe, expect, it } from 'vitest'
import { CONFIRM_TIERS, resolveConfirmTier } from './confirmTiers'
import { SHELL_KINDS } from '../../design-system/foundations/types'

describe('resolveConfirmTier', () => {
  it.each(SHELL_KINDS)('leaves a plain confirm alone on a %s', (shell) => {
    expect(resolveConfirmTier('confirm', shell)).toEqual({
      tier: 'confirm',
      requiresSecondPress: false,
    })
  })

  it.each(SHELL_KINDS)('leaves confirm-with-reason alone on a %s', (shell) => {
    // The tier is a property of the action, not of the screen: cancelling an order needs a reason
    // wherever it is cancelled from.
    expect(resolveConfirmTier('reason', shell)).toEqual({
      tier: 'reason',
      requiresSecondPress: false,
    })
  })

  it.each(['tablet', 'desktop'] as const)('allows typed confirmation on a %s', (shell) => {
    expect(resolveConfirmTier('typed', shell)).toEqual({
      tier: 'typed',
      requiresSecondPress: false,
    })
  })

  it('never asks a phone for a typed confirmation', () => {
    // The blueprint and checklist item A11Y-BI-13 both state it as an absolute. Typing a phrase
    // one-handed in a workshop in front of a waiting customer is a reason to hand the phone to
    // somebody else, which is how confirmations get answered by the wrong person.
    expect(resolveConfirmTier('typed', 'phone')).toEqual({
      tier: 'reason',
      requiresSecondPress: true,
    })
  })

  it('never raises a tier', () => {
    // A component that silently escalated would be deciding policy. Every resolution is either the
    // tier that was asked for, or the one named substitute for the typed tier.
    for (const tier of CONFIRM_TIERS) {
      for (const shell of SHELL_KINDS) {
        const resolved = resolveConfirmTier(tier, shell)
        const rank = CONFIRM_TIERS.indexOf(resolved.tier)
        expect(rank).toBeLessThanOrEqual(CONFIRM_TIERS.indexOf(tier))
      }
    }
  })
})
