import { describe, expect, it } from 'vitest'
import { ApiError } from './apiClient'
import { authProblemMessage, hasFieldErrors } from './authProblems'

function failure(code: string, extra: Record<string, unknown> = {}): ApiError {
  return new ApiError('a failure', { status: 401, problem: { code, ...extra } })
}

describe('what a failure is allowed to say', () => {
  it('gives a wrong password, an unknown account, a suspended one and a locked one the same sentence', () => {
    // The server answers all of these with `identity.invalid-credentials` and takes the same time
    // over each — down to verifying against a decoy hash on the unknown-account path. A client that
    // told them apart would hand the enumeration oracle straight back.
    const message = authProblemMessage(failure('identity.invalid-credentials'))
    expect(message?.id).toBe('auth.problem.invalidCredentials')
  })

  it('says nothing specific about a code that came back from the server unlisted', () => {
    // Falls through to the plain-language description of the failure class, which says what kind of
    // thing went wrong without speculating about the specifics.
    expect(authProblemMessage(failure('identity.something-new'))).toBeUndefined()
  })

  it('names the wait a throttle asked for, and over-estimates when it did not', () => {
    expect(
      authProblemMessage(failure('identity.too-many-attempts', { retryAfterSeconds: 30 })),
    ).toEqual({ id: 'auth.problem.tooManyAttempts', values: { seconds: 30 } })
    // A person told to wait too long tries again and succeeds; the other direction is a person told
    // to try now and refused again.
    expect(authProblemMessage(failure('identity.too-many-attempts'))?.values).toEqual({
      seconds: 60,
    })
  })

  it('gives every way of failing a passkey one answer', () => {
    for (const code of [
      'identity.passkey-verification-failed',
      'identity.passkey-ceremony-not-valid',
      'identity.passkey-response-not-readable',
      'identity.passkey-counter-went-backwards',
    ]) {
      expect(authProblemMessage(failure(code))?.id).toBe('auth.problem.passkeyRefused')
    }
  })

  it('gives a wrong authenticator code, a spent recovery code and a replayed one one answer', () => {
    for (const code of [
      'identity.mfa-code-invalid',
      'identity.recovery-code-invalid',
      'identity.totp-code-replayed',
    ]) {
      expect(authProblemMessage(failure(code))?.id).toBe('auth.problem.codeInvalid')
    }
  })

  it('says nothing at all about something that is not a failed request', () => {
    expect(authProblemMessage(new Error('a render failed'))).toBeUndefined()
    expect(authProblemMessage(null)).toBeUndefined()
    expect(authProblemMessage(new ApiError('no body at all'))).toBeUndefined()
  })
})

describe('a failure that belongs in a field', () => {
  it('is recognised by its per-field messages', () => {
    expect(
      hasFieldErrors(
        new ApiError('validation', {
          status: 400,
          problem: { code: 'identity.password-too-short', errors: { password: ['Too short.'] } },
        }),
      ),
    ).toBe(true)
  })

  it('is not a plain failure with no fields named', () => {
    expect(hasFieldErrors(failure('identity.invalid-credentials'))).toBe(false)
    expect(hasFieldErrors(new Error('not a request'))).toBe(false)
  })
})
