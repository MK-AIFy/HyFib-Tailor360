import { useState } from 'react'
import type { FormEvent } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { useSearchParams } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import {
  approveDispatchException,
  getOrderBalanceForDispatchException,
} from '../../billing/billingApi'
import type { DispatchException } from '../../billing/types'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import { getFormatters } from '../../i18n/formatters'
import './billing.css'

const MAXIMUM_EXPIRY_HOURS = 72
const DEFAULT_EXPIRY_HOURS = 24

/** One job reference per line or comma, trimmed, with the blanks dropped. */
function parseJobIds(text: string): readonly string[] {
  return text
    .split(/[\n,]/)
    .map((line) => line.trim())
    .filter((line) => line !== '')
}

/**
 * Approving a single-use dispatch exception (#164, #165): the Owner letting named garment jobs of
 * an order leave the branch despite an outstanding balance, once only and until it expires.
 *
 * ## Why the job references are typed rather than picked from a list
 *
 * The garment-job screens belong to Orders, which this module does not read from (ARCH-010) and
 * which has no client screens yet. Until they exist, the person approving the exception — who has
 * the order and its jobs in front of them at the counter or on the phone with the branch — types or
 * pastes the job references this exception is bound to; the server refuses any that do not belong
 * to a live job of the order (`billing.dispatch-exception-job-not-of-order`).
 *
 * Reads the balance through `getOrderBalanceForDispatchException`, authorised by
 * `billing.approve_dispatch_exception` itself rather than the general-purpose balance read's own
 * `payments.record` — the Owner, the only role this screen is for, holds the former but not the
 * latter, so the pre-filled balance was previously always a 403. Fixed server-side in #220 with a
 * dedicated, narrowly-scoped read rather than widening what the Owner can read generally.
 */
export function DispatchExceptionApprovalRoute() {
  const intl = useIntl()
  const network = useNetworkState()
  const formatters = getFormatters()
  const [params] = useSearchParams()

  const [orderId, setOrderId] = useState(params.get('orderId') ?? '')
  const balance = useAdminResource(`dispatch-balance:${orderId}`, async (signal) =>
    orderId === '' ? null : await getOrderBalanceForDispatchException(orderId, signal),
  )

  /**
   * `balance.value` for the order this screen is looking at *right now*, or null when there is
   * none yet. `useAdminResource` deliberately keeps the previous order's balance on screen while a
   * new order's read is in flight (its own documented design, not changed here), so a plain
   * `balance.value !== null` check would let order A's balance answer for order B the moment the
   * reference field changes. Comparing the loaded balance's own `orderId` is what tells the two
   * apart (Codex review, PR #217).
   */
  const currentBalance =
    balance.value !== null && balance.value.orderId === orderId ? balance.value : null

  const [jobsText, setJobsText] = useState('')
  const [maxOutstandingOverride, setMaxOutstandingOverride] = useState<number | undefined>(
    undefined,
  )
  const [reasonCode, setReasonCode] = useState('')
  const [reasonText, setReasonText] = useState('')
  const [expiryHours, setExpiryHours] = useState(DEFAULT_EXPIRY_HOURS)
  const [incomplete, setIncomplete] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [approvalKey, setApprovalKey] = useState<{
    readonly fingerprint: string
    readonly key: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [approved, setApproved] = useState<DispatchException | null>(null)

  const jobIds = parseJobIds(jobsText)
  const maxOutstanding =
    maxOutstandingOverride ??
    (currentBalance === null ? undefined : Number(currentBalance.outstanding))

  /**
   * Order-A-specific state must not survive a switch to order B — a maximum quick-filled or typed
   * against one order must never be submitted for another. Resetting it here, rather than only
   * clearing it on submit, is what stops `complete()` from reusing the old value the instant the
   * order reference changes (Codex review, PR #217): with the override gone and `currentBalance`
   * already `null` for the new, not-yet-loaded order, `maxOutstanding` is `undefined` and the form
   * refuses to open its confirmation until either the fresh balance loads or the person re-enters an
   * allowance for the order actually named in `orderId`.
   */
  const handleOrderIdChange = (value: string): void => {
    setOrderId(value)
    setMaxOutstandingOverride(undefined)
  }

  /**
   * The expiry actually offered, fixed the moment the confirmation opens rather than recomputed
   * from `Date.now()` on every render or on every retry — a retry of the same confirmed act must
   * present the same body the first attempt did, or the idempotency key covers nothing.
   */
  const [expiresAtPreview, setExpiresAtPreview] = useState<string | null>(null)

  const complete = (): boolean =>
    orderId !== '' &&
    jobIds.length > 0 &&
    maxOutstanding !== undefined &&
    maxOutstanding > 0 &&
    reasonCode.trim() !== '' &&
    reasonText.trim() !== '' &&
    expiryHours > 0 &&
    expiryHours <= MAXIMUM_EXPIRY_HOURS

  const openConfirmation = (event: FormEvent): void => {
    event.preventDefault()
    if (!complete()) {
      setIncomplete(true)
      return
    }
    setIncomplete(false)
    setExpiresAtPreview(new Date(Date.now() + expiryHours * 60 * 60 * 1000).toISOString())
    setConfirming(true)
  }

  const submit = async (): Promise<void> => {
    if (maxOutstanding === undefined || expiresAtPreview === null) {
      return
    }
    setBusy(true)
    setFailure(null)

    const body = {
      orderId,
      jobIds,
      maxOutstandingAmount: maxOutstanding,
      reasonCode: reasonCode.trim(),
      reasonText: reasonText.trim(),
      expiresAt: expiresAtPreview,
    }
    const fingerprint = JSON.stringify(body)
    const key =
      approvalKey !== null && approvalKey.fingerprint === fingerprint
        ? approvalKey.key
        : crypto.randomUUID()
    setApprovalKey({ fingerprint, key })

    try {
      const exception = await approveDispatchException({ body, idempotencyKey: key })
      setApprovalKey(null)
      setConfirming(false)
      setApproved(exception)
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setBusy(false)
    }
  }

  const startAnother = (): void => {
    setApproved(null)
    setJobsText('')
    setMaxOutstandingOverride(undefined)
    setReasonCode('')
    setReasonText('')
    setExpiryHours(DEFAULT_EXPIRY_HOURS)
    setIncomplete(false)
  }

  return (
    <section className="page billing">
      <h1>
        <FormattedMessage id="billing.dispatch.title" />
      </h1>
      <p className="billing__lede">
        <FormattedMessage id="billing.dispatch.body" />
      </p>

      {approved !== null ? (
        <>
          <Alert
            live="polite"
            title={intl.formatMessage({ id: 'billing.dispatch.approved.title' })}
            tone="success"
          >
            {intl.formatMessage(
              { id: 'billing.dispatch.approved.body' },
              {
                amount: formatters.formatMoney(approved.maxOutstandingAmount),
                expiry: formatters.formatDateTime(approved.expiresAt),
              },
            )}
          </Alert>
          <Button iconName="alert-circle" onClick={startAnother} variant="secondary">
            <FormattedMessage id="billing.dispatch.approved.another" />
          </Button>
        </>
      ) : (
        <form className="billing__form" noValidate onSubmit={openConfirmation}>
          <TextField
            description={intl.formatMessage({ id: 'billing.dispatch.order.hint' })}
            id="dispatch-order"
            label={intl.formatMessage({ id: 'billing.dispatch.order.label' })}
            name="orderId"
            onValueChange={handleOrderIdChange}
            required
            value={orderId}
          />

          <AuthProblemAlert failure={balance.failure} />
          {orderId === '' ? null : currentBalance !== null ? (
            <Alert live="off" tone="warning">
              {intl.formatMessage(
                { id: 'billing.dispatch.balance.outstanding' },
                { amount: formatters.formatMoney(currentBalance.outstanding) },
              )}
            </Alert>
          ) : balance.failure === null ? (
            <LoadingState what={intl.formatMessage({ id: 'billing.dispatch.balance.loading' })} />
          ) : null}

          <TextArea
            description={intl.formatMessage({ id: 'billing.dispatch.jobs.hint' })}
            id="dispatch-jobs"
            label={intl.formatMessage({ id: 'billing.dispatch.jobs.label' })}
            name="jobs"
            onValueChange={setJobsText}
            required
            rows={4}
            value={jobsText}
          />

          <NumericStepper
            decimalPlaces={2}
            description={intl.formatMessage({ id: 'billing.dispatch.maxOutstanding.hint' })}
            id="dispatch-max-outstanding"
            inputMode="decimal"
            label={intl.formatMessage({ id: 'billing.dispatch.maxOutstanding.label' })}
            min={0}
            name="maxOutstandingAmount"
            onValueChange={setMaxOutstandingOverride}
            required
            showRangeHint={false}
            unit={{ symbol: '₹', label: intl.formatMessage({ id: 'units.rupee.label' }) }}
            {...(maxOutstanding === undefined ? {} : { value: maxOutstanding })}
          />

          <TextField
            description={intl.formatMessage({ id: 'billing.dispatch.reasonCode.hint' })}
            id="dispatch-reason-code"
            label={intl.formatMessage({ id: 'billing.dispatch.reasonCode.label' })}
            name="reasonCode"
            onValueChange={setReasonCode}
            required
            value={reasonCode}
          />

          <TextArea
            description={intl.formatMessage({ id: 'billing.dispatch.reasonText.hint' })}
            id="dispatch-reason-text"
            label={intl.formatMessage({ id: 'billing.dispatch.reasonText.label' })}
            name="reasonText"
            onValueChange={setReasonText}
            required
            value={reasonText}
          />

          <NumericStepper
            description={intl.formatMessage({ id: 'billing.dispatch.expiry.hint' })}
            id="dispatch-expiry"
            label={intl.formatMessage({ id: 'billing.dispatch.expiry.label' })}
            max={MAXIMUM_EXPIRY_HOURS}
            min={1}
            name="expiryHours"
            onValueChange={setExpiryHours}
            required
            value={expiryHours}
          />

          {incomplete ? (
            <Alert live="assertive" tone="danger">
              {jobIds.length === 0
                ? intl.formatMessage({ id: 'billing.dispatch.jobs.required' })
                : intl.formatMessage({ id: 'billing.problem.dispatchAmountNotPositive' })}
            </Alert>
          ) : null}

          <BillingProblemAlert failure={failure} />

          {network.online ? (
            <Button iconName="alert-circle" size="primary" type="submit" variant="primary">
              <FormattedMessage id="billing.dispatch.approve" />
            </Button>
          ) : (
            <OfflineBlockedAction
              action={intl.formatMessage({ id: 'billing.dispatch.offlineAction' })}
            />
          )}
        </form>
      )}

      {confirming ? (
        <ConfirmDialog
          action={intl.formatMessage({ id: 'billing.dispatch.confirm.action' })}
          busy={busy}
          confirmLabel={intl.formatMessage({ id: 'billing.dispatch.confirm.action' })}
          irreversible
          onCancel={() => {
            setConfirming(false)
          }}
          onConfirm={() => {
            void submit()
          }}
          open
          problem={<BillingProblemAlert failure={failure} />}
          tier="confirm"
          title={intl.formatMessage({ id: 'billing.dispatch.confirm.title' })}
        >
          {intl.formatMessage(
            { id: 'billing.dispatch.confirm.body' },
            {
              amount: formatters.formatMoney(maxOutstanding ?? 0),
              expiry: expiresAtPreview === null ? '' : formatters.formatDateTime(expiresAtPreview),
            },
          )}
        </ConfirmDialog>
      ) : null}
    </section>
  )
}
