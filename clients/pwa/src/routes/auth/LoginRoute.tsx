import { useState } from 'react'
import { useIntl } from 'react-intl'
import { Link, useLocation, useNavigate } from 'react-router'
import { AUTOCOMPLETE } from '../../design-system/components/forms/autocomplete'
import { FormErrorSummary } from '../../design-system/components/forms/FormErrorSummary'
import { TextField } from '../../design-system/components/forms/TextField'
import type { FieldErrorEntry } from '../../design-system/foundations/FieldProps'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ApiError } from '../../auth/apiClient'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { AUTH_ROUTES, redirectTargetFrom } from '../../auth/authRoutes'
import { signIn } from '../../auth/authApi'
import { usePasskeySignIn } from '../../auth/usePasskeySignIn'
import { useSession } from '../../auth/useSession'
import type { SignInResult } from '../../auth/types'
import '../../auth/auth.css'

/**
 * Signing in.
 *
 * ## What the screen will not tell you
 *
 * There is one failure message, and it is the same for a wrong password, an account that does not
 * exist, one that is suspended, one that never completed its invitation and one that is locked out.
 * The server answers all five identically and takes the same time over each; a screen that added
 * "no account with that name" would give the difference straight back. Somebody genuinely locked out
 * finds out from their administrator, who can see the reason and is the person who can act on it.
 *
 * ## What it does tell you
 *
 * That the training banner is above it, if this is a training device. That nothing has been typed
 * into anything but this form. And, after a sign-out, that the sign-out worked — a screen that looks
 * identical whether or not the previous action succeeded is a screen people press twice.
 *
 * ## The three steps this screen can end in
 *
 * `complete` goes where the person was heading. `multiFactorRequired` goes to the challenge.
 * `multiFactorEnrolmentRequired` goes to the authenticator setup, because the account is required to
 * hold a second factor and does not yet — and the session it has until then reaches nothing that
 * needs one.
 */
export function LoginRoute() {
  const intl = useIntl()
  const navigate = useNavigate()
  const location = useLocation()
  const { recordAuthentication } = useSession()
  const passkey = usePasskeySignIn()

  const [identifier, setIdentifier] = useState('')
  const [password, setPassword] = useState('')
  const [captcha, setCaptcha] = useState('')
  const [captchaRequired, setCaptchaRequired] = useState(false)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [errors, setErrors] = useState<readonly FieldErrorEntry[]>([])
  const [attempt, setAttempt] = useState(0)

  const target = redirectTargetFrom(location.state)

  const goOnwards = async (result: SignInResult) => {
    await recordAuthentication(result.step)
    if (result.step === 'multiFactorRequired') {
      await navigate(AUTH_ROUTES.verify, { replace: true, state: { from: target } })
      return
    }
    if (result.step === 'multiFactorEnrolmentRequired') {
      await navigate(AUTH_ROUTES.authenticator, { replace: true, state: { from: target } })
      return
    }
    await navigate(target, { replace: true })
  }

  const submit = () => {
    if (busy) {
      return
    }

    /*
     * The only client-side validation is "you have not typed anything". Anything more would be this
     * screen having an opinion about what a valid credential looks like, which is both wrong — the
     * policy is the server's — and a hint about the shape of one.
     */
    const missing: FieldErrorEntry[] = []
    if (identifier.trim().length === 0) {
      missing.push({
        name: 'identifier',
        message: intl.formatMessage({ id: 'auth.validation.identifierRequired' }),
        controlId: 'sign-in-identifier',
      })
    }
    if (password.length === 0) {
      missing.push({
        name: 'password',
        message: intl.formatMessage({ id: 'auth.validation.passwordRequired' }),
        controlId: 'sign-in-password',
      })
    }

    setAttempt((previous) => previous + 1)
    setErrors(missing)
    if (missing.length > 0) {
      return
    }

    setBusy(true)
    setFailure(null)

    void signIn({
      identifier: identifier.trim(),
      password,
      ...(captcha.trim().length === 0 ? {} : { captchaResponse: captcha.trim() }),
    })
      .then(async (result) => {
        // The password is dropped the instant it is no longer needed. It is never put in a ref, a
        // context, storage or a log, and the field is emptied so a shared device does not hold it.
        setPassword('')
        await goOnwards(result)
      })
      .catch((cause: unknown) => {
        setPassword('')
        setFailure(cause)
        if (cause instanceof ApiError && cause.code === 'identity.captcha-required') {
          setCaptchaRequired(true)
        }
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const startPasskey = () => {
    void passkey.start().then(async (result) => {
      if (result !== null) {
        await goOnwards(result)
      }
    })
  }

  return (
    <section className="auth-panel">
      <h1>{intl.formatMessage({ id: 'auth.signIn.title' })}</h1>
      <p>{intl.formatMessage({ id: 'auth.signIn.intro' })}</p>

      <FormErrorSummary errors={errors} submissionId={attempt} />
      <AuthProblemAlert
        failure={failure}
        title={intl.formatMessage({ id: 'auth.signIn.failed' })}
      />
      {passkey.cancelled ? (
        <Alert live="polite" tone="info">
          {intl.formatMessage({ id: 'auth.signIn.passkey.cancelled' })}
        </Alert>
      ) : null}
      <AuthProblemAlert failure={passkey.failure} />

      <form
        className="auth-form"
        onSubmit={(event) => {
          event.preventDefault()
          submit()
        }}
      >
        <TextField
          autoComplete={AUTOCOMPLETE.username}
          description={intl.formatMessage({ id: 'auth.signIn.identifier.description' })}
          id="sign-in-identifier"
          label={intl.formatMessage({ id: 'auth.signIn.identifier.label' })}
          name="identifier"
          onValueChange={(value) => {
            setIdentifier(value)
            setErrors([])
          }}
          required
          value={identifier}
          {...(errors.some((entry) => entry.name === 'identifier')
            ? { error: intl.formatMessage({ id: 'auth.validation.identifierRequired' }) }
            : {})}
        />

        <TextField
          autoComplete={AUTOCOMPLETE.currentPassword}
          description={intl.formatMessage({ id: 'auth.signIn.password.description' })}
          enterKeyHint="go"
          id="sign-in-password"
          label={intl.formatMessage({ id: 'auth.signIn.password.label' })}
          name="password"
          onValueChange={(value) => {
            setPassword(value)
            setErrors([])
          }}
          required
          type="password"
          value={password}
          {...(errors.some((entry) => entry.name === 'password')
            ? { error: intl.formatMessage({ id: 'auth.validation.passwordRequired' }) }
            : {})}
        />

        {captchaRequired ? (
          <TextField
            autoComplete={AUTOCOMPLETE.off}
            description={intl.formatMessage({ id: 'auth.signIn.captcha.description' })}
            id="sign-in-captcha"
            label={intl.formatMessage({ id: 'auth.signIn.captcha.label' })}
            name="captchaResponse"
            onValueChange={setCaptcha}
            required
            value={captcha}
          />
        ) : null}

        <Button busy={busy} fullWidth size="primary" type="submit" variant="primary">
          {intl.formatMessage({ id: 'auth.signIn.submit' })}
        </Button>
      </form>

      <section className="auth-section">
        <h2>{intl.formatMessage({ id: 'auth.signIn.otherSection' })}</h2>
        {passkey.supported ? (
          <>
            <p className="auth-hint">
              {intl.formatMessage({ id: 'auth.signIn.passkey.description' })}
            </p>
            <div className="auth-actions">
              <Button busy={passkey.busy} iconName="check-circle" onClick={startPasskey}>
                {intl.formatMessage({ id: 'auth.signIn.passkey' })}
              </Button>
            </div>
          </>
        ) : (
          <p className="auth-hint">
            {intl.formatMessage({ id: 'auth.signIn.passkey.unsupported' })}
          </p>
        )}
        <p>
          <Link className="text-link" to={AUTH_ROUTES.recovery}>
            {intl.formatMessage({ id: 'auth.signIn.forgot' })}
          </Link>
        </p>
      </section>
    </section>
  )
}
