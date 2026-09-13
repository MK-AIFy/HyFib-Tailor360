import { useState } from 'react'
import type { FormEvent } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { useSearchParams } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { getOrderBalance, listAvailablePaymentModes, recordPayment } from '../../billing/billingApi'
import { ReceiptPanel } from '../../billing/ReceiptPanel'
import type { Payment } from '../../billing/types'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ButtonGroup } from '../../components/primitives/ButtonGroup'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { SegmentedControl } from '../../design-system/components/forms/SegmentedControl'
import { TextField } from '../../design-system/components/forms/TextField'
import { getFormatters } from '../../i18n/formatters'
import './billing.css'

/**
 * Taking a payment against an order (#161, #165).
 *
 * ## Why recording is behind a confirmation
 *
 * `docs/nfr/accessibility-localisation.md` section 4.5 names recording a payment among the five acts
 * 3.3.4 Error Prevention protects: reviewable, confirmable and reversible only by a compensating
 * record. So the form itself never calls the server — pressing its button opens `ConfirmDialog`,
 * which states the amount, the mode and the order before the act that cannot be undone by editing.
 *
 * ## The retry key
 *
 * Minted inside the dialog's own confirm handler, held by the fingerprint of what is actually being
 * recorded — the order, the mode, the amount and the reference — so a lost answer retried with the
 * *same* values replays the first outcome, while changing any of them (a different mode chosen after
 * a refusal, say) is a different command and mints a fresh key.
 */
export function TakePaymentRoute() {
  const intl = useIntl()
  const network = useNetworkState()
  const formatters = getFormatters()
  const [params] = useSearchParams()
  const orderId = params.get('orderId') ?? ''
  const orderNumber = params.get('orderNumber') ?? orderId

  // Set while a reload's read is in flight, and cleared the moment it settles — success or failure
  // alike. `useAdminResource` deliberately keeps the previous balance on screen while a reload is in
  // flight (its own documented design, not changed here), so without this the screen would keep
  // showing — and letting the person quick-fill — the balance from *before* the payment that was
  // just recorded, until the fresh read happened to land (Codex review, PR #217).
  const [awaitingFreshBalance, setAwaitingFreshBalance] = useState(false)

  const balance = useAdminResource(`order-balance:${orderId}`, async (signal) => {
    try {
      return await getOrderBalance(orderId, signal)
    } finally {
      setAwaitingFreshBalance(false)
    }
  })
  const modes = useAdminResource('payment-modes', (signal) => listAvailablePaymentModes(signal))

  const [amount, setAmount] = useState<number | undefined>(undefined)
  const [modeCode, setModeCode] = useState('')
  const [reference, setReference] = useState('')
  const [tendered, setTendered] = useState<number | undefined>(undefined)
  const [incomplete, setIncomplete] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [paymentKey, setPaymentKey] = useState<{
    readonly fingerprint: string
    readonly key: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [result, setResult] = useState<Payment | null>(null)

  const chosenMode = (modes.value ?? []).find((mode) => mode.code === modeCode)
  const outstanding =
    balance.value === null || awaitingFreshBalance ? undefined : Number(balance.value.outstanding)
  const isCash = chosenMode?.code === 'CASH'
  const change =
    isCash && amount !== undefined && tendered !== undefined ? tendered - amount : undefined

  const complete = (): boolean =>
    amount !== undefined &&
    amount > 0 &&
    modeCode !== '' &&
    (chosenMode?.requiresReference !== true || reference.trim() !== '')

  const openConfirmation = (event: FormEvent): void => {
    event.preventDefault()
    if (!complete()) {
      setIncomplete(true)
      return
    }
    setIncomplete(false)
    setConfirming(true)
  }

  const record = async (): Promise<void> => {
    if (amount === undefined) {
      return
    }
    setBusy(true)
    setFailure(null)

    const fingerprint = `${orderId}:${modeCode}:${amount}:${reference.trim()}`
    const key =
      paymentKey !== null && paymentKey.fingerprint === fingerprint
        ? paymentKey.key
        : crypto.randomUUID()
    setPaymentKey({ fingerprint, key })

    try {
      const payment = await recordPayment({
        body: {
          orderId,
          modeCode: modeCode === '' ? null : modeCode,
          amount,
          reference: reference.trim() === '' ? null : reference.trim(),
        },
        idempotencyKey: key,
      })
      setPaymentKey(null)
      setConfirming(false)
      setResult(payment)
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setBusy(false)
    }
  }

  const startAnother = (): void => {
    setResult(null)
    setAmount(undefined)
    setModeCode('')
    setReference('')
    setTendered(undefined)
    setIncomplete(false)
    // The balance a payment was just recorded against is now out of date — reload it, and hide the
    // stale figure (and the quick-fill button it drives) until the fresh one lands.
    setAwaitingFreshBalance(true)
    balance.reload()
  }

  if (orderId === '') {
    return (
      <section className="page billing">
        <h1>
          <FormattedMessage id="billing.payment.title" />
        </h1>
        <EmptyState iconName="alert-circle" live="polite">
          <FormattedMessage id="billing.problem.orderNotKnown" />
        </EmptyState>
      </section>
    )
  }

  return (
    <section className="page billing">
      <h1>
        <FormattedMessage id="billing.payment.title" />
      </h1>
      <p className="billing__lede">
        <FormattedMessage id="billing.payment.body" />
      </p>
      <p className="billing__order">
        {intl.formatMessage({ id: 'billing.payment.order' }, { orderNumber })}
      </p>

      {result !== null ? (
        <>
          <Alert
            live="polite"
            title={intl.formatMessage({ id: 'billing.payment.recorded.title' })}
            tone="success"
          >
            {intl.formatMessage(
              { id: 'billing.payment.recorded.body' },
              {
                receiptNumber: result.receipt?.receiptNumber ?? '—',
                amount: formatters.formatMoney(result.amount),
              },
            )}
          </Alert>
          <ReceiptPanel payment={result} />
          <Button iconName="rupee" onClick={startAnother} variant="secondary">
            <FormattedMessage id="billing.payment.recorded.another" />
          </Button>
        </>
      ) : (
        <>
          <AuthProblemAlert failure={balance.failure} />

          {balance.loading || awaitingFreshBalance ? (
            <LoadingState what={intl.formatMessage({ id: 'billing.payment.balance.loading' })} />
          ) : balance.value === null ? null : (
            <Alert
              live="off"
              tone={outstanding !== undefined && outstanding > 0 ? 'warning' : 'info'}
            >
              {outstanding !== undefined && outstanding > 0
                ? intl.formatMessage(
                    { id: 'billing.payment.balance.outstanding' },
                    { amount: formatters.formatMoney(outstanding) },
                  )
                : intl.formatMessage({ id: 'billing.payment.balance.none' })}
              {Number(balance.value.unappliedAdvances) > 0 ? (
                <>
                  {' '}
                  {intl.formatMessage(
                    { id: 'billing.payment.balance.advanceHeld' },
                    { amount: formatters.formatMoney(balance.value.unappliedAdvances) },
                  )}
                </>
              ) : null}
            </Alert>
          )}

          <form className="billing__form" noValidate onSubmit={openConfirmation}>
            <ButtonGroup>
              {outstanding !== undefined && outstanding > 0 ? (
                <Button
                  onClick={() => {
                    setAmount(outstanding)
                  }}
                  type="button"
                  variant="secondary"
                >
                  {intl.formatMessage(
                    { id: 'billing.payment.quickfill.balance' },
                    { amount: formatters.formatMoney(outstanding) },
                  )}
                </Button>
              ) : null}
              <Button
                onClick={() => {
                  setAmount(undefined)
                }}
                type="button"
                variant="secondary"
              >
                <FormattedMessage id="billing.payment.quickfill.advance" />
              </Button>
            </ButtonGroup>
            <p className="billing__hint">
              <FormattedMessage id="billing.payment.quickfill.advance.description" />
            </p>

            <NumericStepper
              decimalPlaces={2}
              description={intl.formatMessage({ id: 'billing.payment.amount.hint' })}
              id="payment-amount"
              inputMode="decimal"
              label={intl.formatMessage({ id: 'billing.payment.amount.label' })}
              min={0}
              name="amount"
              onValueChange={setAmount}
              required
              showRangeHint={false}
              size="primary"
              unit={{ symbol: '₹', label: intl.formatMessage({ id: 'units.rupee.label' }) }}
              {...(amount === undefined ? {} : { value: amount })}
            />

            {modes.loading ? (
              <LoadingState what={intl.formatMessage({ id: 'billing.payment.mode.loading' })} />
            ) : (modes.value ?? []).length === 0 ? (
              <EmptyState
                iconName="rupee"
                live="polite"
                title={intl.formatMessage({ id: 'billing.payment.mode.empty.title' })}
              >
                {intl.formatMessage({ id: 'billing.payment.mode.empty' })}
              </EmptyState>
            ) : (
              <SegmentedControl
                id="payment-mode"
                label={intl.formatMessage({ id: 'billing.payment.mode.label' })}
                name="modeCode"
                onValueChange={setModeCode}
                options={(modes.value ?? []).map((mode) => ({
                  value: mode.code,
                  label: mode.name,
                }))}
                required
                value={modeCode}
              />
            )}

            {chosenMode?.requiresReference === true ? (
              <TextField
                description={intl.formatMessage({ id: 'billing.payment.reference.hint' })}
                id="payment-reference"
                label={intl.formatMessage({ id: 'billing.payment.reference.label' })}
                name="reference"
                onValueChange={setReference}
                required
                value={reference}
              />
            ) : null}

            {isCash ? (
              <>
                <NumericStepper
                  decimalPlaces={2}
                  description={intl.formatMessage({ id: 'billing.payment.tendered.hint' })}
                  id="payment-tendered"
                  inputMode="decimal"
                  label={intl.formatMessage({ id: 'billing.payment.tendered.label' })}
                  min={0}
                  name="tendered"
                  onValueChange={setTendered}
                  showRangeHint={false}
                  unit={{ symbol: '₹', label: intl.formatMessage({ id: 'units.rupee.label' }) }}
                  {...(tendered === undefined ? {} : { value: tendered })}
                />
                {change === undefined ? null : (
                  <p className="billing__change" role="status">
                    {change >= 0
                      ? intl.formatMessage(
                          { id: 'billing.payment.change' },
                          { amount: formatters.formatMoney(change) },
                        )
                      : intl.formatMessage(
                          { id: 'billing.payment.change.short' },
                          { amount: formatters.formatMoney(-change) },
                        )}
                  </p>
                )}
              </>
            ) : null}

            {incomplete ? (
              <Alert live="assertive" tone="danger">
                {chosenMode?.requiresReference === true && reference.trim() === ''
                  ? intl.formatMessage({ id: 'billing.payment.reference.required' })
                  : intl.formatMessage({ id: 'billing.payment.amount.required' })}
              </Alert>
            ) : null}

            <BillingProblemAlert failure={failure} />

            {network.online ? (
              <Button iconName="rupee" size="primary" type="submit" variant="primary">
                <FormattedMessage id="billing.payment.record" />
              </Button>
            ) : (
              <OfflineBlockedAction
                action={intl.formatMessage({ id: 'billing.payment.offlineAction' })}
              />
            )}
          </form>

          {confirming ? (
            <ConfirmDialog
              action={intl.formatMessage({ id: 'billing.payment.confirm.action' })}
              busy={busy}
              confirmLabel={intl.formatMessage({ id: 'billing.payment.confirm.action' })}
              irreversible
              onCancel={() => {
                setConfirming(false)
              }}
              onConfirm={() => {
                void record()
              }}
              open
              problem={<BillingProblemAlert failure={failure} />}
              tier="confirm"
              title={intl.formatMessage({ id: 'billing.payment.confirm.title' })}
            >
              {intl.formatMessage(
                { id: 'billing.payment.confirm.body' },
                {
                  amount: formatters.formatMoney(amount ?? 0),
                  mode: chosenMode?.name ?? modeCode,
                  orderNumber,
                },
              )}
            </ConfirmDialog>
          ) : null}
        </>
      )}
    </section>
  )
}
