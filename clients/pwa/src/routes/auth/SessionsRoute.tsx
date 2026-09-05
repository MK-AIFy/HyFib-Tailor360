import { useEffect, useState } from 'react'
import { useIntl } from 'react-intl'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { Icon } from '../../components/primitives/Icon'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { listSessions, revokeSession } from '../../auth/authApi'
import { useSession } from '../../auth/useSession'
import type { SessionDevice } from '../../auth/types'
import '../../auth/auth.css'

/**
 * Where this account is signed in, and how to end any of it.
 *
 * The screen exists for one moment: somebody thinks their password is known. Everything on it is
 * arranged for that moment rather than for browsing. The address each session was last seen from is
 * shown because "somewhere I do not recognise" is the whole reason to look; it is personal data,
 * shown to its subject and to nobody else, and the server marks the response `no-store` so a shared
 * counter browser cannot hold a copy of it.
 *
 * **Sign out everywhere is the answer to that moment**, and it is deliberately the more prominent of
 * the two controls. Ending one device is tidying; ending all of them — including this one, and every
 * remembered device — is the control that actually helps, and a person in a hurry should not have to
 * work out that they need it.
 *
 * Both are confirmed, and both confirmations say what will happen to whoever is using the device,
 * because the person answering is often not the only person affected.
 */
export function SessionsRoute() {
  const intl = useIntl()
  const { signOutEverywhere } = useSession()

  const [devices, setDevices] = useState<readonly SessionDevice[] | null>(null)
  const [loadFailure, setLoadFailure] = useState<unknown>(null)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [revoking, setRevoking] = useState<SessionDevice | null>(null)
  const [endingAll, setEndingAll] = useState(false)
  /*
   * Bumped to ask for the list again. The read lives in an effect keyed on it rather than in a
   * function the effect calls, because state set synchronously inside an effect is a cascading
   * render — and because keying it this way gives the request a cancellation on unmount for free.
   */
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    let cancelled = false

    void listSessions()
      .then((rows) => {
        if (!cancelled) {
          setDevices(rows)
        }
      })
      .catch((cause: unknown) => {
        if (!cancelled) {
          setLoadFailure(cause)
        }
      })

    return () => {
      cancelled = true
    }
  }, [reloadToken])

  const reload = () => {
    setDevices(null)
    setLoadFailure(null)
    setReloadToken((previous) => previous + 1)
  }

  const revoke = (device: SessionDevice) => {
    setBusy(true)
    setFailure(null)

    void revokeSession(device.sessionId)
      .then(() => {
        setNotice(
          intl.formatMessage({ id: 'auth.sessions.revoked' }, { device: device.deviceLabel }),
        )
        reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
        setRevoking(null)
      })
  }

  const endEverything = () => {
    setBusy(true)
    setFailure(null)

    void signOutEverywhere()
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
        setEndingAll(false)
      })
  }

  const others = devices?.filter((device) => !device.isCurrent) ?? []

  return (
    <section className="page">
      <h1>{intl.formatMessage({ id: 'auth.sessions.title' })}</h1>
      <p>{intl.formatMessage({ id: 'auth.sessions.intro' })}</p>

      {notice === null ? null : (
        <Alert live="polite" tone="info">
          {notice}
        </Alert>
      )}
      <AuthProblemAlert failure={failure} />

      {devices === null && loadFailure === null ? (
        <LoadingState what={intl.formatMessage({ id: 'auth.sessions.loading' })} />
      ) : null}

      {loadFailure === null ? null : <AuthProblemAlert failure={loadFailure} />}

      {devices !== null && others.length === 0 ? (
        <EmptyState
          iconName="check-circle"
          title={intl.formatMessage({ id: 'auth.sessions.empty.title' })}
        >
          {intl.formatMessage({ id: 'auth.sessions.empty.body' })}
        </EmptyState>
      ) : null}

      {devices === null ? null : (
        <ul
          aria-label={intl.formatMessage({ id: 'auth.sessions.list.label' })}
          className="device-list"
        >
          {devices.map((device) => (
            <li
              className="device"
              data-current={device.isCurrent ? 'true' : undefined}
              key={device.sessionId}
            >
              <p className="device__name">
                {device.deviceLabel}
                {device.isCurrent ? (
                  <>
                    {' — '}
                    {intl.formatMessage({ id: 'auth.sessions.current' })}
                  </>
                ) : null}
              </p>
              <div className="device__facts">
                <p>
                  {device.ipAddress === null
                    ? intl.formatMessage({ id: 'auth.sessions.address.unknown' })
                    : intl.formatMessage(
                        { id: 'auth.sessions.address' },
                        { address: device.ipAddress },
                      )}
                </p>
                <p>
                  {intl.formatMessage(
                    { id: 'auth.sessions.startedAt' },
                    {
                      date: intl.formatDate(device.createdAt, {
                        dateStyle: 'medium',
                        timeStyle: 'short',
                      }),
                    },
                  )}
                </p>
                <p>
                  {intl.formatMessage(
                    { id: 'auth.sessions.lastSeen' },
                    {
                      date: intl.formatDate(device.lastSeenAt, {
                        dateStyle: 'medium',
                        timeStyle: 'short',
                      }),
                    },
                  )}
                </p>
                <p>
                  {/* An icon and a word, never colour alone: this is the difference between a
                      session that proved a second factor and one that only knows a password. */}
                  <Icon name={device.mfaSatisfied ? 'check-circle' : 'alert-circle'} />{' '}
                  {intl.formatMessage({
                    id: device.mfaSatisfied
                      ? 'auth.sessions.mfaSatisfied'
                      : 'auth.sessions.passwordOnly',
                  })}
                </p>
              </div>
              <div className="device__actions">
                <Button
                  iconName="close"
                  onClick={() => {
                    setRevoking(device)
                  }}
                  variant="secondary"
                >
                  {intl.formatMessage({ id: 'auth.sessions.revoke' })}
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <section className="auth-section">
        <h2>{intl.formatMessage({ id: 'auth.sessions.everywhere.title' })}</h2>
        <p>{intl.formatMessage({ id: 'auth.sessions.everywhere.body' })}</p>
        <div className="auth-actions">
          <Button
            busy={busy}
            iconName="alert-triangle"
            onClick={() => {
              setEndingAll(true)
            }}
            size="primary"
            variant="danger"
          >
            {intl.formatMessage({ id: 'auth.sessions.everywhere' })}
          </Button>
        </div>
      </section>

      {revoking === null ? null : (
        <ConfirmDialog
          action={intl.formatMessage(
            { id: 'auth.sessions.revoke.action' },
            { device: revoking.deviceLabel },
          )}
          busy={busy}
          confirmLabel={intl.formatMessage({ id: 'auth.sessions.revoke.confirm' })}
          onCancel={() => {
            setRevoking(null)
          }}
          onConfirm={() => {
            revoke(revoking)
          }}
          open
          tier="confirm"
          title={intl.formatMessage(
            { id: 'auth.sessions.revoke.title' },
            { device: revoking.deviceLabel },
          )}
        >
          {intl.formatMessage({
            id: revoking.isCurrent
              ? 'auth.sessions.revoke.bodyCurrent'
              : 'auth.sessions.revoke.body',
          })}
        </ConfirmDialog>
      )}

      <ConfirmDialog
        action={intl.formatMessage({ id: 'auth.sessions.everywhere.action' })}
        busy={busy}
        confirmLabel={intl.formatMessage({ id: 'auth.sessions.everywhere.confirm' })}
        onCancel={() => {
          setEndingAll(false)
        }}
        onConfirm={endEverything}
        open={endingAll}
        tier="confirm"
        title={intl.formatMessage({ id: 'auth.sessions.everywhere.title' })}
      >
        {intl.formatMessage({ id: 'auth.sessions.everywhere.body' })}
      </ConfirmDialog>
    </section>
  )
}
