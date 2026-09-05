import { useState } from 'react'
import { useIntl } from 'react-intl'
import { Link } from 'react-router'
import { AUTOCOMPLETE } from '../../design-system/components/forms/autocomplete'
import { TextField } from '../../design-system/components/forms/TextField'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { AUTH_ROUTES } from '../../auth/authRoutes'
import { requestRecovery } from '../../auth/authApi'
import '../../auth/auth.css'

/**
 * Asking for a password reset link.
 *
 * The screen says the same thing whether or not the address belongs to an account, because the
 * server answers the same way and takes the same time over it. That is the whole design: an
 * "unknown address" message here would turn this form into a way of discovering who works at the
 * shop, which is worth more to somebody preparing a phishing message than the reset itself.
 *
 * Because that can look like the form did nothing, the confirmation says explicitly why it is worded
 * as it is. A person who has typed the wrong address needs to know the message will not arrive, and
 * the honest way to tell them is to say what this screen can and cannot know.
 */
export function RecoveryRequestRoute() {
  const intl = useIntl()
  const [email, setEmail] = useState('')
  const [busy, setBusy] = useState(false)
  const [sent, setSent] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [fieldError, setFieldError] = useState<string | undefined>(undefined)

  const submit = () => {
    if (busy) {
      return
    }
    if (email.trim().length === 0) {
      setFieldError(intl.formatMessage({ id: 'auth.validation.emailRequired' }))
      return
    }

    setBusy(true)
    setFailure(null)
    setFieldError(undefined)

    void requestRecovery(email.trim())
      .then(() => {
        setSent(true)
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  if (sent) {
    return (
      <section className="auth-panel">
        <h1>{intl.formatMessage({ id: 'auth.recovery.request.sent.title' })}</h1>
        <Alert live="polite" tone="success">
          {intl.formatMessage({ id: 'auth.recovery.request.sent.body' })}
        </Alert>
        <p className="auth-hint">{intl.formatMessage({ id: 'auth.recovery.request.sent.note' })}</p>
        <p>
          <Link className="text-link" to={AUTH_ROUTES.signIn}>
            {intl.formatMessage({ id: 'auth.recovery.backToSignIn' })}
          </Link>
        </p>
      </section>
    )
  }

  return (
    <section className="auth-panel">
      <h1>{intl.formatMessage({ id: 'auth.recovery.request.title' })}</h1>
      <p>{intl.formatMessage({ id: 'auth.recovery.request.intro' })}</p>

      <AuthProblemAlert failure={failure} />

      <form
        className="auth-form"
        onSubmit={(event) => {
          event.preventDefault()
          submit()
        }}
      >
        <TextField
          autoComplete={AUTOCOMPLETE.email}
          description={intl.formatMessage({ id: 'auth.recovery.request.email.description' })}
          enterKeyHint="send"
          id="recovery-email"
          inputMode="email"
          label={intl.formatMessage({ id: 'auth.recovery.request.email.label' })}
          name="email"
          onValueChange={(value) => {
            setEmail(value)
            setFieldError(undefined)
          }}
          required
          type="email"
          value={email}
          {...(fieldError === undefined ? {} : { error: fieldError })}
        />

        <Button busy={busy} fullWidth size="primary" type="submit" variant="primary">
          {intl.formatMessage({ id: 'auth.recovery.request.submit' })}
        </Button>
      </form>

      <p className="auth-section">
        <Link className="text-link" to={AUTH_ROUTES.signIn}>
          {intl.formatMessage({ id: 'auth.recovery.backToSignIn' })}
        </Link>
      </p>
    </section>
  )
}
