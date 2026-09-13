import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { useParams } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { approveReconciliation, getCashierSessionForReconciliation } from '../../billing/billingApi'
import type { ReconciliationBatch } from '../../billing/types'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { getFormatters } from '../../i18n/formatters'
import './billing.css'

/**
 * Approving a closed cashier session's variance (#165), by someone other than the cashier who
 * closed it. `payments.approve_reconciliation` carries step-up and a reason
 * (`docs/security/permission-matrix.md`); `approveReconciliation` in `billingApi.ts` already asks
 * for the challenge on every call, so this screen's job is the reason and the confirmation.
 *
 * Reads the session through `getCashierSessionForReconciliation`, authorised by
 * `payments.approve_reconciliation` itself rather than the general-purpose read's own
 * `payments.session` — an Owner holding only the approval permission was refused that read with a
 * 403 and never saw the variance to approve. Fixed server-side in #220 with a dedicated,
 * narrowly-scoped read rather than widening what the Owner can read generally.
 */
export function ReconciliationApprovalRoute() {
  const intl = useIntl()
  const network = useNetworkState()
  const formatters = getFormatters()
  const { sessionId } = useParams()

  const session = useAdminResource(`cashier-session:${sessionId ?? ''}`, (signal) =>
    getCashierSessionForReconciliation(sessionId ?? '', signal),
  )

  const [confirming, setConfirming] = useState(false)
  const [approvalKey, setApprovalKey] = useState<{
    readonly fingerprint: string
    readonly key: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [approved, setApproved] = useState<ReconciliationBatch | null>(null)

  const batch = session.value?.reconciliationBatch ?? null

  const submit = async (outcome: { readonly reason?: string }): Promise<void> => {
    if (sessionId === undefined) {
      return
    }
    const reasonText = outcome.reason?.trim() ?? ''
    setBusy(true)
    setFailure(null)

    const fingerprint = `${sessionId}:${reasonText}`
    const key =
      approvalKey !== null && approvalKey.fingerprint === fingerprint
        ? approvalKey.key
        : crypto.randomUUID()
    setApprovalKey({ fingerprint, key })

    try {
      const result = await approveReconciliation({
        sessionId,
        body: { reason: reasonText === '' ? null : reasonText },
        idempotencyKey: key,
      })
      setApprovalKey(null)
      setConfirming(false)
      setApproved(result)
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="page billing">
      <h1>
        <FormattedMessage id="billing.reconciliation.title" />
      </h1>

      <AuthProblemAlert failure={session.failure} />

      {session.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'billing.reconciliation.loading' })} />
      ) : session.value === null ? null : approved !== null ? (
        <Alert
          live="polite"
          title={intl.formatMessage({ id: 'billing.reconciliation.approved.title' })}
          tone="success"
        >
          <FormattedMessage id="billing.reconciliation.approved.body" />
        </Alert>
      ) : batch === null || batch.approvedAt !== null ? (
        <EmptyState
          iconName="check"
          live="polite"
          title={intl.formatMessage({ id: 'billing.reconciliation.notRequired.title' })}
        >
          {intl.formatMessage({ id: 'billing.reconciliation.notRequired' })}
        </EmptyState>
      ) : (
        <>
          <p className="billing__lede">
            {intl.formatMessage(
              { id: 'billing.reconciliation.body' },
              { amount: formatters.formatMoney(batch.variance) },
            )}
          </p>

          <ul>
            {batch.modeLines.map((line) => (
              <li key={line.modeCode}>
                {intl.formatMessage(
                  { id: 'billing.reconciliation.modeline' },
                  { mode: line.modeCode, variance: formatters.formatMoney(line.variance) },
                )}
              </li>
            ))}
          </ul>

          <BillingProblemAlert failure={failure} />

          {network.online ? (
            <Button
              iconName="check"
              onClick={() => {
                setConfirming(true)
              }}
              size="primary"
              variant="primary"
            >
              <FormattedMessage id="billing.reconciliation.approve" />
            </Button>
          ) : (
            <OfflineBlockedAction
              action={intl.formatMessage({ id: 'billing.reconciliation.offlineAction' })}
            />
          )}
        </>
      )}

      {confirming ? (
        <ConfirmDialog
          action={intl.formatMessage({ id: 'billing.reconciliation.confirm.action' })}
          busy={busy}
          confirmLabel={intl.formatMessage({ id: 'billing.reconciliation.confirm.action' })}
          irreversible
          onCancel={() => {
            setConfirming(false)
          }}
          onConfirm={(outcome) => {
            void submit(outcome)
          }}
          open
          problem={<BillingProblemAlert failure={failure} />}
          tier="reason"
          title={intl.formatMessage({ id: 'billing.reconciliation.confirm.title' })}
        >
          <FormattedMessage id="billing.reconciliation.confirm.body" />
        </ConfirmDialog>
      ) : null}
    </section>
  )
}
