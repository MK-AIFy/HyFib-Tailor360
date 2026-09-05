import { useState } from 'react'
import { useIntl } from 'react-intl'
import { Link, useSearchParams } from 'react-router'
import { AUTOCOMPLETE } from '../../design-system/components/forms/autocomplete'
import { FormErrorSummary } from '../../design-system/components/forms/FormErrorSummary'
import { TextField } from '../../design-system/components/forms/TextField'
import { fieldErrorsFromProblemDetails } from '../../design-system/foundations/FieldProps'
import type { FieldErrorEntry } from '../../design-system/foundations/FieldProps'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ErrorState } from '../../components/states/ErrorState'
import { ApiError } from '../../auth/apiClient'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { AUTH_ROUTES, RECOVERY_TOKEN_PARAM } from '../../auth/authRoutes'
import { confirmRecovery } from '../../auth/authApi'
import type { RecoveryCompleted } from '../../auth/types'
import '../../auth/auth.css'

/** Where the server's per-field messages land on this screen. */
const FIELD_CONTROLS: Readonly<Record<string, string>> = {
  password: 'recovery-password',
  newPassword: 'recovery-password',
}

/**
 * Spending a recovery link and setting a new password.
 *
 * ## The token
 *
 * It arrives in the address bar, because that is what a link is. It is read once, held in this
 * component, spent, and never written anywhere else — not to storage, not to a second URL, not into
 * any telemetry. The server treats it as single-use and expiring within the hour, and reports
 * unknown, spent, expired, withdrawn and wrong-purpose tokens as the same failure, so this screen
 * has exactly one sentence for all of them and does not speculate about which it was.
 *
 * ## The password policy
 *
 * The rules are stated **before** the field rather than reported after it: a policy a person
 * discovers one refusal at a time is a policy that produces `Password1!`. The server is still the
 * authority — it checks length, character variety, whether the password contains the account's own
 * name, and whether it appears in a breach list — and its per-field messages are shown in the field
 * and in the summary, because they say things this screen cannot know.
 *
 * ## Confirming by typing it twice
 *
 * The one client-side rule here, and it earns its place: a mistyped new password on an account that
 * cannot sign in is a second recovery link and another twenty minutes.
 */
export function RecoveryConfirmRoute() {
  const intl = useIntl()
  const [params] = useSearchParams()
  const token = params.get(RECOVERY_TOKEN_PARAM) ?? ''

  const [password, setPassword] = useState('')
  const [repeat, setRepeat] = useState('')
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [errors, setErrors] = useState<readonly FieldErrorEntry[]>([])
  const [attempt, setAttempt] = useState(0)
  const [done, setDone] = useState<RecoveryCompleted | null>(null)

  if (token.length === 0) {
    return (
      <section className="auth-panel">
        <h1>{intl.formatMessage({ id: 'auth.recovery.confirm.missing.title' })}</h1>
        <ErrorState title={intl.formatMessage({ id: 'auth.recovery.confirm.missing.title' })}>
          {intl.formatMessage({ id: 'auth.recovery.confirm.missing.body' })}
        </ErrorState>
        <p>
          <Link className="text-link" to={AUTH_ROUTES.recovery}>
            {intl.formatMessage({ id: 'auth.recovery.request.title' })}
          </Link>
        </p>
      </section>
    )
  }

  if (done !== null) {
    return (
      <section className="auth-panel">
        <h1>{intl.formatMessage({ id: 'auth.recovery.confirm.done.title' })}</h1>
        <Alert live="polite" tone="success">
          {intl.formatMessage(
            { id: 'auth.recovery.confirm.done.body' },
            { count: done.sessionsRevoked },
          )}
        </Alert>
        {done.multiFactorStillRequired ? (
          <p className="auth-hint">
            {intl.formatMessage({ id: 'auth.recovery.confirm.done.stillMfa' })}
          </p>
        ) : null}
        <p>
          <Link className="text-link" to={AUTH_ROUTES.signIn}>
            {intl.formatMessage({ id: 'auth.recovery.backToSignIn' })}
          </Link>
        </p>
      </section>
    )
  }

  const submit = () => {
    if (busy) {
      return
    }

    const found: FieldErrorEntry[] = []
    if (password.length === 0) {
      found.push({
        name: 'password',
        message: intl.formatMessage({ id: 'auth.validation.newPasswordRequired' }),
        controlId: 'recovery-password',
      })
    }
    if (repeat.length === 0) {
      found.push({
        name: 'repeat',
        message: intl.formatMessage({ id: 'auth.validation.repeatRequired' }),
        controlId: 'recovery-repeat',
      })
    } else if (password !== repeat) {
      found.push({
        name: 'repeat',
        message: intl.formatMessage({ id: 'auth.recovery.confirm.mismatch' }),
        controlId: 'recovery-repeat',
      })
    }

    setAttempt((previous) => previous + 1)
    setErrors(found)
    if (found.length > 0) {
      return
    }

    setBusy(true)
    setFailure(null)

    void confirmRecovery({ token, newPassword: password })
      .then((result) => {
        setPassword('')
        setRepeat('')
        setDone(result)
      })
      .catch((cause: unknown) => {
        if (cause instanceof ApiError && cause.problem !== undefined) {
          // The server's per-field messages say things this screen cannot know — that the password
          // repeats the account's own name, or appears in a breach list. They belong in the field.
          const fromServer = fieldErrorsFromProblemDetails(
            cause.problem,
            (name) => FIELD_CONTROLS[name],
          )
          setErrors(fromServer)
          setAttempt((previous) => previous + 1)
          if (fromServer.length > 0) {
            return
          }
        }
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const errorFor = (name: string): string | undefined =>
    errors.find((entry) => entry.name === name)?.message

  const passwordError = errorFor('password') ?? errorFor('newPassword')
  const repeatError = errorFor('repeat')

  return (
    <section className="auth-panel">
      <h1>{intl.formatMessage({ id: 'auth.recovery.confirm.title' })}</h1>
      <p>{intl.formatMessage({ id: 'auth.recovery.confirm.intro' })}</p>

      <FormErrorSummary errors={errors} submissionId={attempt} />
      <AuthProblemAlert failure={failure} />

      <form
        className="auth-form"
        onSubmit={(event) => {
          event.preventDefault()
          submit()
        }}
      >
        <TextField
          autoComplete={AUTOCOMPLETE.newPassword}
          description={intl.formatMessage({ id: 'auth.recovery.confirm.password.description' })}
          id="recovery-password"
          label={intl.formatMessage({ id: 'auth.recovery.confirm.password.label' })}
          name="password"
          onValueChange={setPassword}
          required
          type="password"
          value={password}
          {...(passwordError === undefined ? {} : { error: passwordError })}
        />

        <TextField
          autoComplete={AUTOCOMPLETE.newPassword}
          description={intl.formatMessage({ id: 'auth.recovery.confirm.repeat.description' })}
          enterKeyHint="done"
          id="recovery-repeat"
          label={intl.formatMessage({ id: 'auth.recovery.confirm.repeat.label' })}
          name="repeat"
          onValueChange={setRepeat}
          required
          type="password"
          value={repeat}
          {...(repeatError === undefined ? {} : { error: repeatError })}
        />

        <Button busy={busy} fullWidth size="primary" type="submit" variant="primary">
          {intl.formatMessage({ id: 'auth.recovery.confirm.submit' })}
        </Button>
      </form>
    </section>
  )
}
