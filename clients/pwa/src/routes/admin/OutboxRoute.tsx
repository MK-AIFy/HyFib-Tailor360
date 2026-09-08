import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import type { ConfirmOutcome } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { ApiError } from '../../auth/apiClient'
import { listDeadLetters, replayDeadLetter } from '../../admin/adminApi'
import { useAdminResource } from '../../admin/useAdminResource'
import type { DeadLetteredMessage } from '../../admin/types'

/**
 * The messages the shop published that could not be delivered.
 *
 * ## Why the confirmation is blunt about duplicates
 *
 * A message dead-letters after exhausting its attempts, and a provider that accepted it and then
 * failed to say so looks identical to one that refused it. Sending it again therefore risks a second
 * message to a customer or a second entry in an accounting system — a real consequence outside the
 * building, which is why this operation carries step-up as well as a second factor and why the dialog
 * says what it says rather than "this will retry the message".
 *
 * ## Why there is no "send everything again"
 *
 * The port has one and the command-line tool uses it, deliberately: draining a whole dead letter is a
 * decision made with the logs open after an outage has been diagnosed, and one operator's
 * "everything" is another's duplicate-delivery storm. A screen offers the messages one at a time,
 * each with the failure it was given up on.
 *
 * ## Why an already-replayed message answers "no longer waiting"
 *
 * The server answers 404 both for a message that never existed and for one somebody else has already
 * put back, and to a person at this screen those are the same fact: there is nothing here to send.
 * Saying "not found" would read as a fault in the application.
 */
export function OutboxRoute() {
  const intl = useIntl()
  const letters = useAdminResource('dead-letters', (signal) => listDeadLetters(signal))

  const [pending, setPending] = useState<{
    readonly message: DeadLetteredMessage
    readonly idempotencyKey: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)

  const apply = (outcome: ConfirmOutcome) => {
    if (pending === null) {
      return
    }

    const reason = outcome.reason?.trim() ?? ''
    if (reason === '') {
      setFailure(new ApiError('A reason is required.', { status: 400 }))
      return
    }

    setBusy(true)
    setFailure(null)

    void replayDeadLetter({
      messageId: pending.message.id,
      reason,
      idempotencyKey: pending.idempotencyKey,
    })
      .then(() => {
        letters.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
        setPending(null)
      })
  }

  const rows = letters.value ?? []
  const gone = failure instanceof ApiError && failure.status === 404

  return (
    <section>
      <h2>
        <FormattedMessage id="admin.outbox.title" />
      </h2>

      {gone ? (
        <Alert
          tone="info"
          live="polite"
          actions={
            <Button
              variant="secondary"
              onClick={() => {
                setFailure(null)
                letters.reload()
              }}
            >
              <FormattedMessage id="admin.reload" />
            </Button>
          }
        >
          <FormattedMessage id="admin.outbox.gone" />
        </Alert>
      ) : (
        <AuthProblemAlert failure={failure ?? letters.failure} />
      )}

      {letters.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'admin.outbox.loading' })} />
      ) : rows.length === 0 ? (
        <EmptyState iconName="check-circle" live="polite">
          {intl.formatMessage({ id: 'admin.outbox.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'admin.outbox.caption' })}
          rows={rows}
          rowKey={(row) => row.id}
          rowLabel={(row) => row.eventType}
          columns={[
            {
              id: 'event',
              header: intl.formatMessage({ id: 'admin.outbox.column.event' }),
              cell: (row: DeadLetteredMessage) => row.eventType,
            },
            {
              id: 'when',
              header: intl.formatMessage({ id: 'admin.outbox.column.when' }),
              cell: (row: DeadLetteredMessage) =>
                row.deadLetteredAt === null
                  ? '—'
                  : intl.formatDate(row.deadLetteredAt, {
                      dateStyle: 'medium',
                      timeStyle: 'short',
                    }),
            },
            {
              id: 'attempts',
              header: intl.formatMessage({ id: 'admin.outbox.column.attempts' }),
              cell: (row: DeadLetteredMessage) =>
                intl.formatMessage({ id: 'admin.outbox.attempts' }, { count: row.attemptCount }),
            },
            {
              id: 'error',
              header: intl.formatMessage({ id: 'admin.outbox.column.error' }),
              cell: (row: DeadLetteredMessage) => row.lastError ?? '—',
            },
          ]}
          rowActions={(row: DeadLetteredMessage) => (
            <Button
              variant="secondary"
              busy={busy && pending?.message.id === row.id}
              onClick={() => {
                setPending({ message: row, idempotencyKey: crypto.randomUUID() })
              }}
            >
              {intl.formatMessage({ id: 'admin.outbox.replay' })}
            </Button>
          )}
        />
      )}

      {pending === null ? null : (
        <ConfirmDialog
          open
          tier="reason"
          action="replay-outbox-message"
          title={intl.formatMessage({ id: 'admin.outbox.replayTitle' })}
          confirmLabel={intl.formatMessage({ id: 'admin.outbox.replay' })}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          onConfirm={apply}
          onCancel={() => {
            setPending(null)
          }}
        >
          {intl.formatMessage({ id: 'admin.outbox.replayBody' })}
        </ConfirmDialog>
      )}
    </section>
  )
}
