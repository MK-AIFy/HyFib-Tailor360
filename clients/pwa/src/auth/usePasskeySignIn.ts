import { useCallback, useState } from 'react'
import { beginPasskeyAssertion, completePasskeyAssertion } from './authApi'
import { assertPasskey, isPasskeySupported, PasskeyCancelled } from './passkeys'
import type { SignInResult } from './types'

/**
 * Signing in with a passkey, from either of the two screens that offer it.
 *
 * A passkey satisfies both factors at once — the authenticator has already verified the person
 * before it will sign anything — so this is a complete sign-in rather than a first step. That is why
 * it is offered on the challenge screen as well as on the sign-in screen: somebody who has lost the
 * phone their authenticator is on may still have the laptop their passkey is on.
 *
 * Cancellation is not failure and is reported separately. Dismissing the platform's own dialog is
 * something people do constantly — the wrong finger, the wrong key, a second thought — and putting a
 * red error on the screen for it teaches them that the feature is broken.
 */
export interface PasskeySignIn {
  /** Whether this browser can run the ceremony at all. Checked before the control is offered. */
  readonly supported: boolean
  readonly busy: boolean
  /** The last failure, for the screen to describe. Null after a cancellation. */
  readonly failure: unknown
  /** True when the person dismissed the platform dialog. Says "nothing has changed", not "error". */
  readonly cancelled: boolean
  /** Runs the ceremony. Resolves with the sign-in, or null when it was cancelled or failed. */
  readonly start: () => Promise<SignInResult | null>
}

export function usePasskeySignIn(): PasskeySignIn {
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [cancelled, setCancelled] = useState(false)

  const start = useCallback(async (): Promise<SignInResult | null> => {
    setBusy(true)
    setFailure(null)
    setCancelled(false)
    try {
      const challenge = await beginPasskeyAssertion()
      const credential = await assertPasskey(challenge.options)
      return await completePasskeyAssertion({ ceremonyId: challenge.ceremonyId, credential })
    } catch (cause) {
      if (cause instanceof PasskeyCancelled) {
        setCancelled(true)
      } else {
        setFailure(cause)
      }
      return null
    } finally {
      setBusy(false)
    }
  }, [])

  return { supported: isPasskeySupported(), busy, failure, cancelled, start }
}
