import { useState } from 'react'
import { useIntl } from 'react-intl'
import { Link, useNavigate } from 'react-router'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { AUTH_ROUTES } from '../../auth/authRoutes'
import { reissueRecoveryCodes } from '../../auth/authApi'
import { RecoveryCodes } from '../../auth/RecoveryCodes'
import { useCurrentUser, useSession } from '../../auth/useSession'
import { PasskeySection } from './PasskeySection'
import '../../auth/auth.css'

/**
 * Sign-in and security: what this account can prove itself with.
 *
 * One screen rather than four, because the question a person arrives with is "am I safe, and what do
 * I do if I lose my phone" — and answering it means seeing the authenticator, the recovery codes and
 * the passkeys next to each other. Splitting them into separate screens makes each one look complete
 * on its own, which is how somebody ends up with an authenticator, no recovery codes, and no idea
 * that the two go together.
 *
 * Printing a new sheet of recovery codes destroys the old one immediately, whether or not it has
 * been used, so it is confirmed before it happens. The plain tier is right: it is significant, and it
 * is completely recoverable by printing another.
 */
export function SecurityRoute() {
  const intl = useIntl()
  const navigate = useNavigate()
  const user = useCurrentUser()
  const { refresh, signOut } = useSession()

  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [codes, setCodes] = useState<readonly string[] | null>(null)

  const security = user.security
  const enrolled = security.mfaEnrolment === 'Enrolled'

  const reissue = () => {
    setBusy(true)
    setFailure(null)

    void reissueRecoveryCodes()
      .then(async (sheet) => {
        setConfirming(false)
        setCodes(sheet.recoveryCodes)
        await refresh()
      })
      .catch((cause: unknown) => {
        setConfirming(false)
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  if (codes !== null) {
    return (
      <section className="page">
        <h1>{intl.formatMessage({ id: 'auth.codes.title' })}</h1>
        <RecoveryCodes
          codes={codes}
          onDone={() => {
            setCodes(null)
          }}
        />
      </section>
    )
  }

  return (
    <section className="page">
      <h1>{intl.formatMessage({ id: 'auth.security.title' })}</h1>
      <p>{intl.formatMessage({ id: 'auth.security.intro' })}</p>

      {security.mustChangePassword ? (
        <Alert
          tone="warning"
          title={intl.formatMessage({ id: 'auth.security.mustChangePassword.title' })}
        >
          {intl.formatMessage({ id: 'auth.security.mustChangePassword.body' })}
        </Alert>
      ) : null}

      <AuthProblemAlert failure={failure} />

      <section className="auth-section">
        <h2>{intl.formatMessage({ id: 'auth.security.authenticator.title' })}</h2>
        <p>
          {intl.formatMessage({
            id: `auth.security.authenticator.${security.mfaEnrolment}`,
          })}
        </p>
        <div className="auth-actions">
          <Button
            iconName="settings"
            onClick={() => {
              void navigate(AUTH_ROUTES.authenticator)
            }}
            variant={enrolled ? 'secondary' : 'primary'}
          >
            {intl.formatMessage({
              id: enrolled
                ? 'auth.security.authenticator.replace'
                : 'auth.security.authenticator.setUp',
            })}
          </Button>
        </div>
      </section>

      <section className="auth-section">
        <h2>{intl.formatMessage({ id: 'auth.security.codes.title' })}</h2>
        <p>
          {enrolled
            ? intl.formatMessage(
                { id: 'auth.security.codes.count' },
                { count: security.unusedRecoveryCodes },
              )
            : intl.formatMessage({ id: 'auth.security.codes.none' })}
        </p>
        {enrolled ? (
          <div className="auth-actions">
            <Button
              busy={busy}
              iconName="refresh"
              onClick={() => {
                setConfirming(true)
              }}
            >
              {intl.formatMessage({ id: 'auth.codes.reissue' })}
            </Button>
          </div>
        ) : null}
      </section>

      <PasskeySection />

      <section className="auth-section">
        <h2>{intl.formatMessage({ id: 'auth.security.sessions.title' })}</h2>
        <p>{intl.formatMessage({ id: 'auth.security.sessions.body' })}</p>
        <p>
          <Link className="text-link" to={AUTH_ROUTES.sessions}>
            {intl.formatMessage({ id: 'auth.security.sessions.link' })}
          </Link>
        </p>
      </section>

      <section className="auth-section">
        <h2>{intl.formatMessage({ id: 'auth.security.signOut.title' })}</h2>
        <p>{intl.formatMessage({ id: 'auth.security.signOut.body' })}</p>
        <div className="auth-actions">
          {/*
            Not confirmed. Signing out is the safe direction — nothing is lost that a sign-in does not
            restore — and on a shared counter device the person doing it is usually walking away from
            it right now. A confirmation here buys nothing and costs a second at exactly the wrong
            moment.
          */}
          <Button
            busy={busy}
            iconName="close"
            onClick={() => {
              void signOut()
            }}
          >
            {intl.formatMessage({ id: 'auth.security.signOut' })}
          </Button>
        </div>
      </section>

      <ConfirmDialog
        action={intl.formatMessage({ id: 'auth.codes.reissue.action' })}
        busy={busy}
        confirmLabel={intl.formatMessage({ id: 'auth.codes.reissue.confirm' })}
        onCancel={() => {
          setConfirming(false)
        }}
        onConfirm={reissue}
        open={confirming}
        tier="confirm"
        title={intl.formatMessage({ id: 'auth.codes.reissue.title' })}
      >
        {intl.formatMessage({ id: 'auth.codes.reissue.body' })}
      </ConfirmDialog>
    </section>
  )
}
