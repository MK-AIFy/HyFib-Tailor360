import { useState } from 'react'
import { useIntl } from 'react-intl'
import { useNavigate } from 'react-router'
import { TextField } from '../../design-system/components/forms/TextField'
import { AUTOCOMPLETE } from '../../design-system/components/forms/autocomplete'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { TextLink } from '../../components/primitives/TextLink'
import { beginAuthenticatorEnrolment, confirmAuthenticatorEnrolment } from '../../auth/authApi'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { AFTER_SIGN_IN, AUTH_ROUTES } from '../../auth/authRoutes'
import { CopyButton } from '../../auth/CopyButton'
import { QrCode } from '../../auth/QrCode'
import { RecoveryCodes } from '../../auth/RecoveryCodes'
import { useSession } from '../../auth/useSession'
import type { AuthenticatorEnrolment } from '../../auth/types'
import '../../auth/auth.css'

/**
 * Setting up an authenticator app.
 *
 * ## Three ways in, because one of them always fails
 *
 * The QR code is the fast path and it is useless in the commonest case on a shop floor: the
 * authenticator app is on the same phone as this screen, and a phone cannot photograph itself. So
 * the `otpauth:` link is offered beside it — one tap, and the app opens with the account already
 * filled in — and the setup key is offered beneath both, in groups of four, with a copy control, for
 * the desktop browser whose authenticator is on a phone that will not scan an old monitor.
 *
 * All three carry the same secret. That is why the warning above them is not decoration: for as long
 * as this screen is open, anybody who can see it can add themselves as this account's second factor.
 *
 * ## The confirmation is not a formality
 *
 * Typing a code back proves the app and the server agree about the time before the password stops
 * being enough on its own. Without it, a clock thirty seconds out on a workshop tablet becomes an
 * account nobody can sign in to, discovered at the next sign-in rather than now.
 *
 * ## Nothing here is stored
 *
 * The secret lives in this component's state and goes when the screen does. The recovery codes that
 * follow it are rendered once and are never written anywhere either.
 */
export function AuthenticatorEnrolmentRoute() {
  const intl = useIntl()
  const navigate = useNavigate()
  const { pendingStep, refresh } = useSession()

  const [enrolment, setEnrolment] = useState<AuthenticatorEnrolment | null>(null)
  const [codes, setCodes] = useState<readonly string[] | null>(null)
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [fieldError, setFieldError] = useState<string | undefined>(undefined)

  const required = pendingStep === 'multiFactorEnrolmentRequired'

  const begin = () => {
    if (busy) {
      return
    }
    setBusy(true)
    setFailure(null)

    void beginAuthenticatorEnrolment()
      .then((started) => {
        setEnrolment(started)
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const confirm = () => {
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

    void confirmAuthenticatorEnrolment(code.trim())
      .then(async (sheet) => {
        setCode('')
        // The secret has done its job and is dropped before the codes are painted, so it is not
        // still in memory behind a screen somebody will leave open while they find a pen.
        setEnrolment(null)
        setCodes(sheet.recoveryCodes)
        await refresh()
      })
      .catch((cause: unknown) => {
        setCode('')
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  if (codes !== null) {
    return (
      <section className="page">
        <h1>{intl.formatMessage({ id: 'auth.enrol.confirmed' })}</h1>
        <RecoveryCodes
          codes={codes}
          onDone={() => {
            void navigate(required ? AFTER_SIGN_IN : AUTH_ROUTES.security, { replace: true })
          }}
        />
      </section>
    )
  }

  return (
    <section className="page">
      <h1>{intl.formatMessage({ id: 'auth.enrol.title' })}</h1>
      <p>{intl.formatMessage({ id: 'auth.enrol.intro' })}</p>

      {required ? (
        <Alert tone="info" title={intl.formatMessage({ id: 'auth.enrol.required.title' })}>
          {intl.formatMessage({ id: 'auth.enrol.required.body' })}
        </Alert>
      ) : null}

      <AuthProblemAlert failure={failure} />

      {enrolment === null ? (
        <div className="auth-actions">
          <Button busy={busy} onClick={begin} size="primary" variant="primary">
            {intl.formatMessage({ id: 'auth.enrol.begin' })}
          </Button>
        </div>
      ) : (
        <>
          <Alert tone="warning">{intl.formatMessage({ id: 'auth.enrol.secret.warning' })}</Alert>

          <section className="auth-section">
            <h2>{intl.formatMessage({ id: 'auth.enrol.scan.title' })}</h2>
            <div className="qr-frame">
              <QrCode
                label={intl.formatMessage(
                  { id: 'auth.enrol.qr.alt' },
                  { account: enrolment.accountName, issuer: enrolment.issuer },
                )}
                value={enrolment.otpAuthUri}
              />
            </div>
            <p>
              <TextLink href={enrolment.otpAuthUri}>
                {intl.formatMessage({ id: 'auth.enrol.open' })}
              </TextLink>
            </p>
            <p className="auth-hint">{intl.formatMessage({ id: 'auth.enrol.open.description' })}</p>
          </section>

          <section className="auth-section">
            <h2>{intl.formatMessage({ id: 'auth.enrol.manual.title' })}</h2>
            <p className="auth-hint">
              {intl.formatMessage(
                { id: 'auth.enrol.manual.description' },
                { digits: enrolment.digits, seconds: enrolment.periodSeconds },
              )}
            </p>
            <p>
              <span className="setup-key">{enrolment.manualEntryKey}</span>
            </p>
            <CopyButton
              confirmation={intl.formatMessage({ id: 'auth.enrol.copied' })}
              failure={intl.formatMessage({ id: 'auth.enrol.copyFailed' })}
              label={intl.formatMessage({ id: 'auth.enrol.copy' })}
              value={enrolment.manualEntryKey}
            />
          </section>

          <section className="auth-section">
            <h2>{intl.formatMessage({ id: 'auth.enrol.confirm.title' })}</h2>
            <p>{intl.formatMessage({ id: 'auth.enrol.confirm.intro' })}</p>
            <form
              className="auth-form"
              onSubmit={(event) => {
                event.preventDefault()
                confirm()
              }}
            >
              <TextField
                autoComplete={AUTOCOMPLETE.oneTimeCode}
                description={intl.formatMessage({ id: 'auth.enrol.code.description' })}
                enterKeyHint="done"
                id="enrolment-code"
                inputMode="numeric"
                label={intl.formatMessage({ id: 'auth.enrol.code.label' })}
                maxLength={enrolment.digits}
                name="code"
                onValueChange={(value) => {
                  setCode(value)
                  setFieldError(undefined)
                }}
                required
                value={code}
                {...(fieldError === undefined ? {} : { error: fieldError })}
              />
              <div className="auth-actions">
                <Button busy={busy} size="primary" type="submit" variant="primary">
                  {intl.formatMessage({ id: 'auth.enrol.confirm.submit' })}
                </Button>
                <Button
                  onClick={() => {
                    setEnrolment(null)
                    setCode('')
                  }}
                >
                  {intl.formatMessage({ id: 'auth.enrol.restart' })}
                </Button>
              </div>
            </form>
          </section>
        </>
      )}
    </section>
  )
}
