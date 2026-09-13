import { useState } from 'react'
import type { FormEvent } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { useParams } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { allocateAdvance, getOrderBalance, getPayment } from '../../billing/billingApi'
import type { InvoiceBalance, Payment } from '../../billing/types'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { Select } from '../../design-system/components/forms/Select'
import { getFormatters } from '../../i18n/formatters'
import './billing.css'

interface Loaded {
  readonly payment: Payment
  readonly invoices: readonly InvoiceBalance[]
}

/**
 * Allocating part of a payment's held advance to a posted invoice by hand (#165).
 *
 * ## Against the automatic rule, so under step-up and with a reason
 *
 * `payments.allocate_manual` is flagged for step-up in `docs/security/permission-matrix.md`, and
 * `allocateAdvance` in `billingApi.ts` already asks for the challenge on every call; this screen's
 * job is the reason and the confirmation, not the retry around the 403.
 */
export function AllocateAdvanceRoute() {
  const intl = useIntl()
  const network = useNetworkState()
  const formatters = getFormatters()
  const { paymentId } = useParams()

  const loaded = useAdminResource(
    `allocate:${paymentId ?? ''}`,
    async (signal): Promise<Loaded> => {
      const payment = await getPayment(paymentId ?? '', signal)
      const order = await getOrderBalance(payment.orderId, signal)
      return {
        payment,
        invoices: order.invoices.filter(
          (invoice) => invoice.status === 'Posted' && Number(invoice.outstanding) > 0,
        ),
      }
    },
  )

  const [invoiceId, setInvoiceId] = useState('')
  const [amount, setAmount] = useState<number | undefined>(undefined)
  const [incomplete, setIncomplete] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [allocationKey, setAllocationKey] = useState<{
    readonly fingerprint: string
    readonly key: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [result, setResult] = useState<Payment | null>(null)

  const held = Number(loaded.value?.payment.unappliedAdvance ?? 0)
  const chosenInvoice = loaded.value?.invoices.find((invoice) => invoice.invoiceId === invoiceId)

  const openConfirmation = (event: FormEvent): void => {
    event.preventDefault()
    if (
      invoiceId === '' ||
      amount === undefined ||
      amount <= 0 ||
      amount > held ||
      (chosenInvoice !== undefined && amount > Number(chosenInvoice.outstanding))
    ) {
      setIncomplete(true)
      return
    }
    setIncomplete(false)
    setConfirming(true)
  }

  const submit = async (outcome: { readonly reason?: string }): Promise<void> => {
    if (paymentId === undefined || amount === undefined) {
      return
    }
    const reason = outcome.reason?.trim() ?? ''
    setBusy(true)
    setFailure(null)

    const fingerprint = `${paymentId}:${invoiceId}:${amount}:${reason}`
    const key =
      allocationKey !== null && allocationKey.fingerprint === fingerprint
        ? allocationKey.key
        : crypto.randomUUID()
    setAllocationKey({ fingerprint, key })

    try {
      const payment = await allocateAdvance({
        paymentId,
        body: { invoiceId, amount, reason: reason === '' ? null : reason },
        idempotencyKey: key,
      })
      setAllocationKey(null)
      setConfirming(false)
      setResult(payment)
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="page billing">
      <h1>
        <FormattedMessage id="billing.allocate.title" />
      </h1>
      <p className="billing__lede">
        <FormattedMessage id="billing.allocate.body" />
      </p>

      <AuthProblemAlert failure={loaded.failure} />

      {loaded.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'billing.allocate.loading' })} />
      ) : loaded.value === null ? null : result !== null ? (
        <Alert
          live="polite"
          title={intl.formatMessage({ id: 'billing.allocate.allocated.title' })}
          tone="success"
        >
          {intl.formatMessage(
            { id: 'billing.allocate.allocated.body' },
            {
              amount: formatters.formatMoney(amount ?? 0),
              invoiceNumber: chosenInvoice?.invoiceNumber ?? '',
              remaining: formatters.formatMoney(result.unappliedAdvance),
            },
          )}
        </Alert>
      ) : held <= 0 ? (
        <EmptyState
          iconName="rupee"
          live="polite"
          title={intl.formatMessage({ id: 'billing.allocate.none.title' })}
        >
          {intl.formatMessage({ id: 'billing.allocate.none' })}
        </EmptyState>
      ) : (
        <>
          <p>
            {intl.formatMessage(
              { id: 'billing.allocate.held' },
              { amount: formatters.formatMoney(held) },
            )}
          </p>

          {loaded.value.invoices.length === 0 ? (
            <EmptyState iconName="rupee" live="polite">
              <FormattedMessage id="billing.allocate.invoice.none" />
            </EmptyState>
          ) : (
            <form className="billing__form" noValidate onSubmit={openConfirmation}>
              <Select
                emptyLabel={intl.formatMessage({ id: 'billing.allocate.invoice.choose' })}
                id="allocate-invoice"
                label={intl.formatMessage({ id: 'billing.allocate.invoice.label' })}
                name="invoiceId"
                onValueChange={setInvoiceId}
                options={loaded.value.invoices.map((invoice) => ({
                  value: invoice.invoiceId,
                  label: intl.formatMessage(
                    { id: 'billing.allocate.invoice.option' },
                    {
                      invoiceNumber: invoice.invoiceNumber,
                      outstanding: formatters.formatMoney(invoice.outstanding),
                    },
                  ),
                }))}
                required
                value={invoiceId}
              />

              <NumericStepper
                decimalPlaces={2}
                description={intl.formatMessage({ id: 'billing.allocate.amount.hint' })}
                id="allocate-amount"
                inputMode="decimal"
                label={intl.formatMessage({ id: 'billing.allocate.amount.label' })}
                max={
                  chosenInvoice === undefined
                    ? held
                    : Math.min(held, Number(chosenInvoice.outstanding))
                }
                min={0}
                name="amount"
                onValueChange={setAmount}
                required
                showRangeHint
                unit={{ symbol: '₹', label: intl.formatMessage({ id: 'units.rupee.label' }) }}
                {...(amount === undefined ? {} : { value: amount })}
              />

              {incomplete ? (
                <Alert live="assertive" tone="danger">
                  {invoiceId === ''
                    ? intl.formatMessage({ id: 'billing.allocate.invoice.required' })
                    : intl.formatMessage({ id: 'billing.allocate.amount.required' })}
                </Alert>
              ) : null}

              <BillingProblemAlert failure={failure} />

              {network.online ? (
                <Button iconName="rupee" size="primary" type="submit" variant="primary">
                  <FormattedMessage id="billing.allocate.allocate" />
                </Button>
              ) : (
                <OfflineBlockedAction
                  action={intl.formatMessage({ id: 'billing.allocate.offlineAction' })}
                />
              )}
            </form>
          )}
        </>
      )}

      {confirming ? (
        <ConfirmDialog
          action={intl.formatMessage({ id: 'billing.allocate.confirm.action' })}
          busy={busy}
          confirmLabel={intl.formatMessage({ id: 'billing.allocate.confirm.action' })}
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
          title={intl.formatMessage({ id: 'billing.allocate.confirm.title' })}
        >
          {intl.formatMessage(
            { id: 'billing.allocate.confirm.body' },
            {
              amount: formatters.formatMoney(amount ?? 0),
              invoiceNumber: chosenInvoice?.invoiceNumber ?? '',
            },
          )}
        </ConfirmDialog>
      ) : null}
    </section>
  )
}
