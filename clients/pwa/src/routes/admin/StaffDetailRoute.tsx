import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useParams } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import type { ConfirmOutcome } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { StatusBadge } from '../../components/primitives/StatusBadge'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { ApiError } from '../../auth/apiClient'
import { useCurrentUser } from '../../auth/useSession'
import { commandStaffUser, readStaffUser } from '../../admin/adminApi'
import type { StaffCommand } from '../../admin/adminApi'
import { useAdminResource } from '../../admin/useAdminResource'
import { accountStatusKind } from '../../admin/accountStatus'
import type { MessageKey } from '../../i18n/en-IN'

/**
 * One staff account, and the six things an administrator can do to it.
 *
 * ## Why each command is confirmed with a reason
 *
 * Every one of them changes what somebody else can do, and half of them do it while that person is
 * standing at a till. The server refuses without a written reason; the dialog collects it at the
 * moment the decision is made, when it can still be described, rather than as a field on a form
 * somebody fills in on the way past.
 *
 * The sentence in each dialog says what happens **to the person**, not to the row. "They will be
 * signed out of every device" is what an administrator weighs; "the status becomes Suspended" is what
 * the database does, and nobody has ever hesitated over it.
 *
 * ## Why the retry key is minted when the dialog opens
 *
 * Not when the request is sent. A command that times out and is retried must reuse the key it first
 * used, or the retry is a second suspension rather than the same one — and the key has to survive the
 * in-place re-authentication that step-up may raise in between, which is exactly the window a key
 * generated at send time would fall into.
 *
 * ## Why the person's own account is refused here
 *
 * The server refuses it too, and this is the courtesy layer: an administrator who suspends their own
 * account is locked out of the screen that would undo it. Saying so before the control is pressed is
 * better than a refusal afterwards.
 */
export function StaffDetailRoute() {
  const intl = useIntl()
  const { userId = '' } = useParams()
  const caller = useCurrentUser()

  const account = useAdminResource(userId, (signal) => readStaffUser(userId, signal))

  const [pending, setPending] = useState<{
    readonly command: StaffCommand
    readonly idempotencyKey: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const user = account.value?.value ?? null
  const version = account.value?.version
  const isSelf = user !== null && user.userId === caller.userId

  const run = (outcome: ConfirmOutcome) => {
    if (pending === null || user === null || version === undefined) {
      return
    }

    const reason = outcome.reason?.trim() ?? ''
    if (reason === '') {
      setFailure(new ApiError('A reason is required.', { status: 400 }))
      return
    }

    setBusy(true)
    setFailure(null)

    void commandStaffUser({
      userId: user.userId,
      command: pending.command,
      reason,
      version,
      idempotencyKey: pending.idempotencyKey,
    })
      .then(() => {
        setNotice(intl.formatMessage({ id: 'admin.command.done' }, { name: user.displayName }))
        account.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
        setPending(null)
      })
  }

  if (account.value === null && account.loading) {
    return <LoadingState what={intl.formatMessage({ id: 'admin.user.loading' })} />
  }

  if (user === null) {
    return (
      <>
        <AuthProblemAlert failure={account.failure} />
        <EmptyState iconName="users" live="polite">
          {intl.formatMessage({ id: 'admin.user.notFound' })}
        </EmptyState>
      </>
    )
  }

  const conflict = failure instanceof ApiError && failure.status === 409

  return (
    <section>
      <p>
        <Link to="/admin/users">
          <FormattedMessage id="admin.back" />
        </Link>
      </p>

      <h2>{user.displayName}</h2>

      {notice === null ? null : (
        <Alert
          tone="success"
          live="polite"
          onDismiss={() => {
            setNotice(null)
          }}
        >
          {notice}
        </Alert>
      )}

      {conflict ? (
        <Alert
          tone="warning"
          live="assertive"
          title={intl.formatMessage({ id: 'admin.conflict.title' })}
          actions={
            <Button
              variant="secondary"
              onClick={() => {
                setFailure(null)
                account.reload()
              }}
            >
              <FormattedMessage id="admin.reload" />
            </Button>
          }
        >
          <FormattedMessage id="admin.conflict.body" />
        </Alert>
      ) : (
        <AuthProblemAlert failure={failure} />
      )}

      <dl className="admin__detailGrid">
        <dt>
          <FormattedMessage id="admin.user.signInName" />
        </dt>
        <dd>{user.userName}</dd>
        <dt>
          <FormattedMessage id="admin.user.email" />
        </dt>
        <dd>{user.email}</dd>
        <dt>
          <FormattedMessage id="admin.user.status" />
        </dt>
        <dd>
          <StatusBadge status={accountStatusKind(user.status)} />
        </dd>
        <dt>
          <FormattedMessage id="admin.user.secondFactor" />
        </dt>
        <dd>{user.mfaEnrolment}</dd>
        <dt>
          <FormattedMessage id="admin.user.lastSignIn" />
        </dt>
        <dd>
          {user.lastSignInAt === null
            ? intl.formatMessage({ id: 'admin.users.neverSignedIn' })
            : intl.formatDate(user.lastSignInAt, { dateStyle: 'medium', timeStyle: 'short' })}
        </dd>
        <dt>
          <FormattedMessage id="admin.user.created" />
        </dt>
        <dd>{intl.formatDate(user.createdAt, { dateStyle: 'medium' })}</dd>
      </dl>

      {isSelf ? (
        <Alert tone="info" live="polite">
          <FormattedMessage id="admin.user.self" />
        </Alert>
      ) : (
        <>
          <div className="admin__actions">
            {REVERSIBLE_COMMANDS.map((command) => (
              <Button
                key={command}
                variant="secondary"
                busy={busy && pending?.command === command}
                onClick={() => {
                  setPending({ command, idempotencyKey: crypto.randomUUID() })
                }}
              >
                {intl.formatMessage({ id: `admin.command.${command}` })}
              </Button>
            ))}
          </div>

          <div className="admin__actions admin__actions--destructive">
            {DESTRUCTIVE_COMMANDS.map((command) => (
              <Button
                key={command}
                variant="danger"
                busy={busy && pending?.command === command}
                onClick={() => {
                  setPending({ command, idempotencyKey: crypto.randomUUID() })
                }}
              >
                {intl.formatMessage({ id: `admin.command.${command}` })}
              </Button>
            ))}
          </div>
        </>
      )}

      {pending === null ? null : (
        <ConfirmDialog
          open
          tier="reason"
          action={pending.command}
          title={intl.formatMessage({ id: `admin.command.${pending.command}.title` as MessageKey })}
          confirmLabel={intl.formatMessage({ id: `admin.command.${pending.command}` })}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          irreversible={pending.command === 'deactivate'}
          onConfirm={run}
          onCancel={() => {
            setPending(null)
          }}
        >
          {intl.formatMessage(
            { id: `admin.command.${pending.command}.body` as MessageKey },
            { name: user.displayName },
          )}
        </ConfirmDialog>
      )}
    </section>
  )
}

/** The commands somebody can undo by pressing the other one. */
const REVERSIBLE_COMMANDS: readonly StaffCommand[] = ['suspend', 'reinstate', 'revoke-sessions']

/**
 * The commands that are not simply reversed.
 *
 * `reactivate` is here rather than beside `reinstate` on purpose: reopening a closed account clears
 * the password and every second factor, so it is a decision about somebody's identity rather than
 * about their access, and it belongs beside the one that closed them.
 */
const DESTRUCTIVE_COMMANDS: readonly StaffCommand[] = ['reset-mfa', 'deactivate', 'reactivate']
