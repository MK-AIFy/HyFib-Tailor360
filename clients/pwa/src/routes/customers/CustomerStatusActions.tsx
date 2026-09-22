import { useEffect, useRef, useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ApiError } from '../../auth/apiClient'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { commandCustomerStatus } from '../../customers/customersApi'
import type { CustomerStatusCommand } from '../../customers/customersApi'
import {
  CUSTOMER_ALREADY_MERGED_CODE,
  CUSTOMER_STATUS_TRANSITION_CODE,
  CUSTOMER_VERSION_CONFLICT_CODE,
} from '../../customers/types'
import type { Customer } from '../../customers/types'

/**
 * Withdrawing a customer record from ordinary use, and putting one back (#26, #182, #618).
 *
 * ## Why this is a control on the record and not a screen of its own
 *
 * Correcting, merging, consent and the export each got their own address, because each is a separate
 * piece of work with steps. This is one decision with a reason attached, and it is **recoverable**:
 * `reactivate` is the same control pointing the other way. A whole screen for it would put a
 * navigation between somebody and a thing they can undo, which is the wrong shape of friction — the
 * friction that belongs here is the confirmation, and that is modal, so it covers the screen rather
 * than sharing a pane with the list.
 *
 * Confirm-with-reason, not the typed tier. Withdrawing is significant and reversible; the reason is
 * what the trail keeps, and it is the only place a later reader can ask why.
 *
 * ## Three refusals, and they are not the same thing
 *
 *  - **The version moved.** Somebody corrected the record while this screen was open, so the record
 *    being withdrawn is not the record that was read. Reload and decide again.
 *  - **It is already where it is being sent.** Somebody else withdrew it a moment ago. That is not a
 *    failure of the person pressing the button, and it should not read like one: the outcome they
 *    wanted is the outcome that exists.
 *  - **It was merged away.** A merge cannot be undone, so the record is finished and cannot come
 *    back to ordinary use. The only useful thing to say is to work on the record that survived.
 */
export function CustomerStatusActions({
  customer,
  version,
  onChanged,
}: {
  readonly customer: Customer
  /** The version the record was read at. The command is refused without it. */
  readonly version: string
  readonly onChanged: () => void
}) {
  const intl = useIntl()
  const network = useNetworkState()

  const [asking, setAsking] = useState<CustomerStatusCommand | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [attempt, setAttempt] = useState<{ readonly reason: string; readonly key: string } | null>(
    null,
  )

  const live = useRef(true)
  useEffect(() => {
    live.current = true
    return () => {
      live.current = false
    }
  }, [])

  const withdrawn = customer.status !== 'Active'
  const merged = customer.mergedIntoCustomerId !== null

  const send = (command: CustomerStatusCommand, reason: string) => {
    if (busy) {
      return
    }

    // The key belongs to the request, and the request is this command with this reason. Pressing
    // confirm again after a timeout replays; confirming a corrected reason does not.
    const key =
      attempt !== null && attempt.reason === `${command}:${reason}`
        ? attempt.key
        : crypto.randomUUID()
    setAttempt({ reason: `${command}:${reason}`, key })
    setBusy(true)
    setFailure(null)

    void commandCustomerStatus({
      customerId: customer.customerId,
      command,
      reason,
      version,
      idempotencyKey: key,
    })
      .then(() => {
        if (!live.current) {
          return
        }
        setAsking(null)
        setAttempt(null)
        onChanged()
      })
      .catch((cause: unknown) => {
        if (live.current) {
          setFailure(cause)
        }
      })
      .finally(() => {
        if (live.current) {
          setBusy(false)
        }
      })
  }

  /*
   * A record that was merged away is finished. It is not offered a way back, and saying why is more
   * use than a disabled control somebody has to guess at.
   */
  if (merged) {
    return (
      <Alert live="off" tone="info">
        <FormattedMessage id="customers.status.merged" />
      </Alert>
    )
  }

  const command: CustomerStatusCommand = withdrawn ? 'reactivate' : 'deactivate'

  return (
    <>
      {withdrawn ? (
        <Alert
          live="off"
          tone="warning"
          title={intl.formatMessage({ id: 'customers.status.withdrawn.title' })}
        >
          <FormattedMessage id="customers.status.withdrawn.body" />
        </Alert>
      ) : null}

      {asking === null ? <StatusProblem failure={failure} /> : null}

      {network.online ? (
        <Button
          busy={busy}
          iconName={withdrawn ? 'refresh' : 'pause'}
          onClick={() => {
            setFailure(null)
            setAsking(command)
          }}
          variant="secondary"
        >
          {intl.formatMessage({
            id: withdrawn ? 'customers.status.reactivate' : 'customers.status.deactivate',
          })}
        </Button>
      ) : (
        <OfflineBlockedAction
          action={intl.formatMessage({
            id: withdrawn
              ? 'customers.status.offlineReactivate'
              : 'customers.status.offlineDeactivate',
          })}
        />
      )}

      {asking === null ? null : (
        <ConfirmDialog
          action={intl.formatMessage({
            id:
              asking === 'reactivate'
                ? 'customers.status.confirm.reactivate.action'
                : 'customers.status.confirm.deactivate.action',
          })}
          busy={busy}
          confirmLabel={intl.formatMessage({
            id:
              asking === 'reactivate'
                ? 'customers.status.reactivate'
                : 'customers.status.deactivate',
          })}
          onCancel={() => {
            setAsking(null)
            setFailure(null)
          }}
          onConfirm={(outcome) => {
            send(asking, (outcome.reason ?? '').trim())
          }}
          open
          problem={<StatusProblem failure={failure} />}
          tier="reason"
          title={intl.formatMessage(
            {
              id:
                asking === 'reactivate'
                  ? 'customers.status.confirm.reactivate.title'
                  : 'customers.status.confirm.deactivate.title',
            },
            { name: customer.displayName },
          )}
        >
          <FormattedMessage
            id={
              asking === 'reactivate'
                ? 'customers.status.confirm.reactivate.body'
                : 'customers.status.confirm.deactivate.body'
            }
          />
        </ConfirmDialog>
      )}
    </>
  )
}

/** The three refusals, told apart, because they send the reader to three different places. */
function StatusProblem({ failure }: { readonly failure: unknown }) {
  const intl = useIntl()

  if (failure instanceof ApiError && failure.code === CUSTOMER_VERSION_CONFLICT_CODE) {
    return (
      <Alert live="assertive" tone="warning">
        {intl.formatMessage({ id: 'customers.status.conflict' })}
      </Alert>
    )
  }

  if (failure instanceof ApiError && failure.code === CUSTOMER_STATUS_TRANSITION_CODE) {
    // Somebody else got there first, and the outcome they wanted is the outcome that exists. It is
    // information, not an error against what they did, so it is neither assertive nor a danger tone.
    return (
      <Alert live="polite" tone="info">
        {intl.formatMessage({ id: 'customers.status.already' })}
      </Alert>
    )
  }

  if (failure instanceof ApiError && failure.code === CUSTOMER_ALREADY_MERGED_CODE) {
    return (
      <Alert live="assertive" tone="warning">
        {intl.formatMessage({ id: 'customers.status.merged' })}
      </Alert>
    )
  }

  return <AuthProblemAlert failure={failure} />
}
