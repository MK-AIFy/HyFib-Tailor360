import { useState } from 'react'
import { useIntl } from 'react-intl'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { answerChallenge } from '../../auth/authApi'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { AUTH_ROUTES, redirectTargetFrom } from '../../auth/authRoutes'
import { FactorFields } from '../../auth/FactorFields'
import { usePasskeySignIn } from '../../auth/usePasskeySignIn'
import { useSession } from '../../auth/useSession'
import type { ChallengeFactor } from '../../auth/types'
import '../../auth/auth.css'

/** How few unspent recovery codes is worth telling somebody about. */
const LOW_RECOVERY_CODES = 3

/**
 * The second factor.
 *
 * The session that got here proves a password and nothing else. It reaches no endpoint that requires
 * a second factor, which is why this screen is safe to reach with it and why answering successfully
 * replaces the session rather than upgrading it.
 *
 * ## Remembering the device is opt-in, and worded for a shop
 *
 * "Remember this device" is the control that turns a second factor back into a single one, and on a
 * shared counter tablet that is the wrong answer for everybody who uses it afterwards. The label
 * says so rather than leaving it to be inferred, and the box starts unticked: a default that skips
 * the challenge is a default that silently weakens every account signed in from that device.
 *
 * ## Recovery codes are counted down, out loud
 *
 * A person who has just spent one is the person who will need the next one, and the moment they find
 * out there are none left is the moment they cannot get in. The count is shown after every answer,
 * and below three the screen says where to print more.
 */
export function MfaChallengeRoute() {
  const intl = useIntl()
  const navigate = useNavigate()
  const location = useLocation()
  const { status, user, recordAuthentication } = useSession()
  const passkey = usePasskeySignIn()

  const [factor, setFactor] = useState<ChallengeFactor>('totp')
  const [code, setCode] = useState('')
  const [remember, setRemember] = useState(false)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [fieldError, setFieldError] = useState<string | undefined>(undefined)

  const target = redirectTargetFrom(location.state)

  if (status === 'loading') {
    return <LoadingState what={intl.formatMessage({ id: 'auth.guard.loading' })} />
  }

  if (status !== 'active' || user === null) {
    // The first factor's session has gone — a reload after it expired, or a sign-out elsewhere. The
    // answer is the whole sign-in, not this half of it.
    return <Navigate replace to={AUTH_ROUTES.signIn} />
  }

  const factors = user.security.factors

  if (!factors.authenticator && !factors.recoveryCode && !factors.passkey) {
    return (
      <section className="auth-panel">
        <h1>{intl.formatMessage({ id: 'auth.challenge.title' })}</h1>
        <EmptyState
          iconName="alert-triangle"
          title={intl.formatMessage({ id: 'auth.challenge.none.title' })}
        >
          {intl.formatMessage({ id: 'auth.challenge.none.body' })}
        </EmptyState>
      </section>
    )
  }

  const submit = () => {
    if (busy) {
      return
    }
    if (code.trim().length === 0) {
      setFieldError(intl.formatMessage({ id: 'auth.validation.codeRequired' }))
      return
    }

    setBusy(true)
    setFailure(null)
    setFieldError(undefined)

    void answerChallenge({ factor, code: code.trim(), rememberDevice: remember })
      .then(async () => {
        setCode('')
        await recordAuthentication('complete')
        await navigate(target, { replace: true })
      })
      .catch((cause: unknown) => {
        setCode('')
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const remaining = user.security.unusedRecoveryCodes

  return (
    <section className="auth-panel">
      <h1>{intl.formatMessage({ id: 'auth.challenge.title' })}</h1>
      <p>{intl.formatMessage({ id: 'auth.challenge.intro' })}</p>

      <AuthProblemAlert failure={failure} />
      <AuthProblemAlert failure={passkey.failure} />
      {passkey.cancelled ? (
        <Alert live="polite" tone="info">
          {intl.formatMessage({ id: 'auth.signIn.passkey.cancelled' })}
        </Alert>
      ) : null}

      <form
        className="auth-form"
        onSubmit={(event) => {
          event.preventDefault()
          submit()
        }}
      >
        <FactorFields
          code={code}
          codeId="challenge-code"
          factor={factor}
          factors={factors}
          onCodeChange={(value) => {
            setCode(value)
            setFieldError(undefined)
          }}
          onFactorChange={(next) => {
            setFactor(next)
            setFieldError(undefined)
          }}
          {...(fieldError === undefined ? {} : { error: fieldError })}
        />

        <Checkbox
          description={intl.formatMessage({ id: 'auth.challenge.remember.description' })}
          label={intl.formatMessage({ id: 'auth.challenge.remember.label' })}
          name="rememberDevice"
          onValueChange={setRemember}
          value={remember}
        />

        <Button busy={busy} fullWidth size="primary" type="submit" variant="primary">
          {intl.formatMessage({ id: 'auth.challenge.submit' })}
        </Button>
      </form>

      {factors.recoveryCode ? (
        <p className="auth-hint">
          {intl.formatMessage({ id: 'auth.challenge.remaining' }, { count: remaining })}
          {remaining > 0 && remaining <= LOW_RECOVERY_CODES
            ? ` ${intl.formatMessage({ id: 'auth.challenge.reissueSoon' })}`
            : ''}
        </p>
      ) : null}

      {factors.passkey && passkey.supported ? (
        <section className="auth-section">
          <h2>{intl.formatMessage({ id: 'auth.signIn.otherSection' })}</h2>
          <div className="auth-actions">
            <Button
              busy={passkey.busy}
              iconName="check-circle"
              onClick={() => {
                void passkey.start().then(async (result) => {
                  if (result !== null) {
                    await recordAuthentication(result.step)
                    await navigate(target, { replace: true })
                  }
                })
              }}
            >
              {intl.formatMessage({ id: 'auth.challenge.passkey' })}
            </Button>
          </div>
        </section>
      ) : null}
    </section>
  )
}
