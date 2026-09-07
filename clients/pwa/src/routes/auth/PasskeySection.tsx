import { useEffect, useState } from 'react'
import { useIntl } from 'react-intl'
import { TextField } from '../../design-system/components/forms/TextField'
import { AUTOCOMPLETE } from '../../design-system/components/forms/autocomplete'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import {
  beginPasskeyRegistration,
  completePasskeyRegistration,
  listPasskeys,
  removePasskey,
} from '../../auth/authApi'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { createPasskey, isPasskeySupported, PasskeyCancelled } from '../../auth/passkeys'
import type { PasskeySummary } from '../../auth/types'
import '../../auth/auth.css'

/**
 * Registering and removing passkeys.
 *
 * ## The name is asked for first, and it is not optional
 *
 * A list of passkeys called "Passkey", "Passkey" and "Passkey" is a list nobody can safely remove
 * anything from, and the moment somebody needs to remove one is the moment they have lost a device.
 * So the name is asked for before the ceremony starts, while the person is still looking at the
 * device they are naming.
 *
 * ## Cancelling is not failing
 *
 * The platform's own dialog is dismissed constantly — the wrong finger, a second thought, a key not
 * to hand. That reports as `NotAllowedError`, exactly as a genuine refusal does, and putting a red
 * banner on the screen for it teaches people the feature is broken. It says "nothing has been
 * added" instead, politely.
 *
 * ## Removal is confirmed, and never silently the last factor
 *
 * The server refuses to remove the only thing an account can prove itself with, and the screen says
 * so in words when it does. The confirmation itself is the plain tier: this is significant and
 * completely recoverable — the person can register the device again in a minute.
 */
export interface PasskeySectionProps {
  /** The heading level this section sits at inside the screen around it. */
  readonly headingLevel?: 2 | 3
}

type Loading = { readonly kind: 'loading' }
type Ready = { readonly kind: 'ready'; readonly passkeys: readonly PasskeySummary[] }
type Failed = { readonly kind: 'failed'; readonly failure: unknown }
type State = Loading | Ready | Failed

export function PasskeySection({ headingLevel = 2 }: PasskeySectionProps) {
  const intl = useIntl()
  const supported = isPasskeySupported()
  const Heading = headingLevel === 3 ? 'h3' : 'h2'

  const [state, setState] = useState<State>({ kind: 'loading' })
  /*
   * Bumped to ask for the list again. The read lives in an effect keyed on it rather than in a
   * function the effect calls, because state set synchronously inside an effect is a cascading
   * render — and because keying it this way gives the request a cancellation on unmount for free.
   */
  const [reloadToken, setReloadToken] = useState(0)
  const [naming, setNaming] = useState(false)
  const [label, setLabel] = useState('')
  const [labelError, setLabelError] = useState<string | undefined>(undefined)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [removing, setRemoving] = useState<PasskeySummary | null>(null)

  useEffect(() => {
    let cancelled = false

    void listPasskeys()
      .then((passkeys) => {
        if (!cancelled) {
          setState({ kind: 'ready', passkeys })
        }
      })
      .catch((cause: unknown) => {
        if (!cancelled) {
          setState({ kind: 'failed', failure: cause })
        }
      })

    return () => {
      cancelled = true
    }
  }, [reloadToken])

  const reload = () => {
    setState({ kind: 'loading' })
    setReloadToken((previous) => previous + 1)
  }

  const add = () => {
    if (busy) {
      return
    }
    const name = label.trim()
    if (name.length === 0) {
      setLabelError(intl.formatMessage({ id: 'auth.passkeys.label.missing' }))
      return
    }

    setBusy(true)
    setFailure(null)
    setNotice(null)
    setLabelError(undefined)

    void beginPasskeyRegistration()
      .then(async (challenge) => {
        const credential = await createPasskey(challenge.options)
        return await completePasskeyRegistration({
          ceremonyId: challenge.ceremonyId,
          credential,
          label: name,
        })
      })
      .then((registered) => {
        setNaming(false)
        setLabel('')
        setNotice(intl.formatMessage({ id: 'auth.passkeys.added' }, { label: registered.label }))
        reload()
      })
      .catch((cause: unknown) => {
        if (cause instanceof PasskeyCancelled) {
          setNotice(intl.formatMessage({ id: 'auth.passkeys.cancelled' }))
          return
        }
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const remove = (passkey: PasskeySummary) => {
    setBusy(true)
    setFailure(null)
    setNotice(null)

    void removePasskey(passkey.passkeyId)
      .then(() => {
        setNotice(intl.formatMessage({ id: 'auth.passkeys.removed' }, { label: passkey.label }))
        reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
        setRemoving(null)
      })
  }

  return (
    <section className="auth-section">
      <Heading>{intl.formatMessage({ id: 'auth.passkeys.title' })}</Heading>
      <p>{intl.formatMessage({ id: 'auth.passkeys.intro' })}</p>

      {notice === null ? null : (
        <Alert live="polite" tone="info">
          {notice}
        </Alert>
      )}
      <AuthProblemAlert failure={failure} />

      {state.kind === 'loading' ? (
        <LoadingState what={intl.formatMessage({ id: 'auth.passkeys.list.label' })} />
      ) : null}

      {state.kind === 'failed' ? <AuthProblemAlert failure={state.failure} /> : null}

      {state.kind === 'ready' && state.passkeys.length === 0 ? (
        <EmptyState iconName="info" title={intl.formatMessage({ id: 'auth.passkeys.empty.title' })}>
          {intl.formatMessage({ id: 'auth.passkeys.empty.body' })}
        </EmptyState>
      ) : null}

      {state.kind === 'ready' && state.passkeys.length > 0 ? (
        <ul
          aria-label={intl.formatMessage({ id: 'auth.passkeys.list.label' })}
          className="device-list"
        >
          {state.passkeys.map((passkey) => (
            <li className="device" key={passkey.passkeyId}>
              <p className="device__name">{passkey.label}</p>
              <div className="device__facts">
                <p>
                  {intl.formatMessage(
                    { id: 'auth.passkeys.added.on' },
                    { date: intl.formatDate(passkey.createdAt, { dateStyle: 'medium' }) },
                  )}
                </p>
                <p>
                  {passkey.lastUsedAt === null
                    ? intl.formatMessage({ id: 'auth.passkeys.neverUsed' })
                    : intl.formatMessage(
                        { id: 'auth.passkeys.lastUsed' },
                        { date: intl.formatDate(passkey.lastUsedAt, { dateStyle: 'medium' }) },
                      )}
                </p>
                <p>
                  {intl.formatMessage({
                    id: passkey.isBackedUp ? 'auth.passkeys.backedUp' : 'auth.passkeys.deviceOnly',
                  })}
                </p>
              </div>
              <div className="device__actions">
                <Button
                  iconName="close"
                  onClick={() => {
                    setRemoving(passkey)
                  }}
                  variant="secondary"
                >
                  {intl.formatMessage({ id: 'auth.passkeys.remove' })}
                </Button>
              </div>
            </li>
          ))}
        </ul>
      ) : null}

      {!supported ? (
        <p className="auth-hint">{intl.formatMessage({ id: 'auth.passkeys.unsupported' })}</p>
      ) : naming ? (
        <form
          className="auth-form"
          onSubmit={(event) => {
            event.preventDefault()
            add()
          }}
        >
          <TextField
            autoComplete={AUTOCOMPLETE.off}
            description={intl.formatMessage({ id: 'auth.passkeys.label.description' })}
            id="passkey-label"
            label={intl.formatMessage({ id: 'auth.passkeys.label.label' })}
            name="label"
            onValueChange={(value) => {
              setLabel(value)
              setLabelError(undefined)
            }}
            required
            value={label}
            {...(labelError === undefined ? {} : { error: labelError })}
          />
          <div className="auth-actions">
            <Button busy={busy} type="submit" variant="primary">
              {intl.formatMessage({ id: 'auth.passkeys.save' })}
            </Button>
            <Button
              onClick={() => {
                setNaming(false)
                setLabel('')
                setLabelError(undefined)
              }}
            >
              {intl.formatMessage({ id: 'auth.passkeys.cancel' })}
            </Button>
          </div>
        </form>
      ) : (
        <div className="auth-actions">
          <Button
            iconName="plus"
            onClick={() => {
              setNaming(true)
              setNotice(null)
            }}
          >
            {intl.formatMessage({ id: 'auth.passkeys.add' })}
          </Button>
        </div>
      )}

      {removing === null ? null : (
        <ConfirmDialog
          action={intl.formatMessage(
            { id: 'auth.passkeys.remove.action' },
            { label: removing.label },
          )}
          busy={busy}
          confirmLabel={intl.formatMessage({ id: 'auth.passkeys.remove.confirm' })}
          onCancel={() => {
            setRemoving(null)
          }}
          onConfirm={() => {
            remove(removing)
          }}
          open
          tier="confirm"
          title={intl.formatMessage(
            { id: 'auth.passkeys.remove.title' },
            { label: removing.label },
          )}
        >
          {intl.formatMessage({ id: 'auth.passkeys.remove.body' })}
        </ConfirmDialog>
      )}
    </section>
  )
}
