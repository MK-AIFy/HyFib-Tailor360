import { useState } from 'react'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { useShellStatus } from '../../components/layout/useShellStatus'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ButtonGroup } from '../../components/primitives/ButtonGroup'
import { Card } from '../../components/primitives/Card'
import { StatusBadge } from '../../components/primitives/StatusBadge'
import { TextLink } from '../../components/primitives/TextLink'
import { useDemoText } from '../../components/primitives/demoText'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { RadioGroup } from '../../design-system/components/forms/RadioGroup'
import { Switch } from '../../design-system/components/forms/Switch'
import { TextField } from '../../design-system/components/forms/TextField'
import { getFormatters } from '../../i18n/formatters'
import { NOW, STAFF } from '../fixtures/branch'
import { INVOICE, PAYMENT_METHODS, RECEIPT_NUMBER } from '../fixtures/billing'

/**
 * The key the client holds for this attempt.
 *
 * Fixed rather than generated, because it is the point of step 9: a retry after a session expiry
 * must reuse the key it started with, or the same payment is recorded twice. It is also fixed so the
 * screenshots of #52 do not change on every render.
 */
const IDEMPOTENCY_KEY = '0199cf40-0000-7000-8000-0000000004a1'

/**
 * `A11Y-RJ-06` — Cashier: taking a payment.
 *
 * Walked as `A11Y-PZ-05` (payment recording) in docs/nfr/a11y-checklist.md, which section 6.8 says
 * plainly is not to be walked twice. This screen is what that priority-zero record is walked
 * against, and all ten of its steps are reachable here.
 *
 * ## The balance is stated before anything can be typed
 *
 * Step 2 is "hear the balance due, labelled, **before** entering anything". So the summary precedes
 * the form in the document order, not merely above it on the screen — a person on a screen reader
 * meets the number before the field, which is the only ordering that makes the field mean anything.
 *
 * ## An overpayment is an error with a suggestion, not a refusal
 *
 * 3.3.3 Error Suggestion: the message says what the balance is and offers the amount that would
 * settle it. A cashier with a queue at the counter does not need to be told they are wrong; they
 * need the right number.
 *
 * ## The session can expire mid-entry, and nothing typed is lost
 *
 * Re-authentication happens **in place**: the form is not unmounted, the amount and the mode stay
 * exactly as typed, and the retry reuses the `Idempotency-Key` the attempt started with — so the
 * payment is recorded once, not twice. That combination is step 9, and it is the difference between
 * an inconvenience and a second charge on somebody's card.
 *
 * ## Offline is blocked, never queued
 *
 * A payment is not an idempotent scan submission and never enters the offline queue. The blocked
 * state says "this will not be queued" in as many words.
 *
 * There is no backend: the state is `useState` over the fixtures.
 */
export function CashierPaymentScreen() {
  const t = useDemoText()
  const formatters = getFormatters()
  const status = useShellStatus()

  const [source, setSource] = useState<'invoice' | 'order' | null>(null)
  const [method, setMethod] = useState<string>('')
  const [amount, setAmount] = useState<number>(INVOICE.balance)
  const [advance, setAdvance] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [receipt, setReceipt] = useState<string | null>(null)
  const [sessionExpired, setSessionExpired] = useState(false)
  const [online, setOnline] = useState(true)

  const balance = INVOICE.balance
  const overpaid = !advance && amount > balance

  /*
   * Opening a payment clears the last one.
   *
   * The receipt is the evidence that a payment was recorded; leaving it on the screen while the
   * source, the amount and the mode all changed underneath it says the *new* payment has been taken
   * when nothing has. Everything specific to one attempt is cleared together, in one place, so the
   * next thing added to this screen cannot be forgotten here.
   */
  function openPayment(next: 'invoice' | 'order') {
    setSource(next)
    setAdvance(next === 'order')
    setAmount(next === 'order' ? 500 : balance)
    setReceipt(null)
    setSessionExpired(false)
  }

  const amountError = overpaid
    ? t(
        `That is more than the balance. The balance on ${INVOICE.number} is ${formatters.formatMoney(balance)} — enter ${formatters.formatMoney(balance)}, or record the difference as an advance against the customer instead.`,
      )
    : undefined

  return (
    <section className="page journey-screen">
      <h1>{t('Take a payment')}</h1>
      <p>
        {t(STAFF.cashier)} · {formatters.formatDateTime(NOW)}
      </p>

      <section aria-labelledby="pay-open" className="journey-section">
        <h2 id="pay-open">{t('1. Open the payment')}</h2>
        <ButtonGroup>
          <Button
            onClick={() => {
              openPayment('invoice')
            }}
            variant={source === 'invoice' ? 'primary' : 'secondary'}
          >
            {t(`From invoice ${INVOICE.number}`)}
          </Button>
          <Button
            onClick={() => {
              openPayment('order')
            }}
            variant={source === 'order' ? 'primary' : 'secondary'}
          >
            {t(`From order ${INVOICE.order} — no invoice yet`)}
          </Button>
        </ButtonGroup>
      </section>

      {source === null ? null : (
        <>
          {/* Step 2: the balance, labelled, before the form. Document order, not just position. */}
          <section aria-labelledby="pay-balance" className="journey-section">
            <h2 id="pay-balance">{t('2. What is owed')}</h2>
            <Card
              headingLevel={3}
              meta={<StatusBadge status={advance ? 'draft' : 'unpaid'} />}
              title={t(advance ? 'No invoice has been posted yet' : 'Invoice balance')}
            >
              <dl className="journey-summary">
                <dt>{t('Customer')}</dt>
                <dd>{t(INVOICE.customer)}</dd>
                {advance ? null : (
                  <>
                    <dt>{t('Invoice total, including GST')}</dt>
                    <dd className="journey-amount">{formatters.formatMoney(INVOICE.total)}</dd>
                    <dt>{t('Already received')}</dt>
                    <dd className="journey-amount">
                      {formatters.formatMoney(INVOICE.advanceReceived)}
                    </dd>
                  </>
                )}
                <dt>{t(advance ? 'Advance to take' : 'Balance due now')}</dt>
                <dd className="journey-amount journey-total">
                  {formatters.formatMoney(advance ? amount : balance)}
                </dd>
              </dl>
              {!advance ? null : (
                <p>
                  {t(
                    'An advance taken before an invoice exists is held unapplied against this customer. It is not income yet, and it is applied to the invoice when the invoice is posted.',
                  )}
                </p>
              )}
            </Card>
          </section>

          <section aria-labelledby="pay-enter" className="journey-section">
            <h2 id="pay-enter">{t('3. Mode and amount')}</h2>

            {/* Re-authentication renders above the form and never unmounts it: step 9. */}
            {!sessionExpired ? null : (
              <Alert
                actions={
                  <Button
                    iconName="check"
                    onClick={() => {
                      setSessionExpired(false)
                      status.announceAutosave(t('Signed in again. Nothing you typed was lost.'))
                    }}
                    variant="primary"
                  >
                    {t('Sign in again')}
                  </Button>
                }
                live="assertive"
                title={t('Your session expired')}
                tone="warning"
              >
                {t(
                  `Sign in again to finish. The amount and the mode are still here, and this attempt keeps the same key (${IDEMPOTENCY_KEY}) — so it is recorded once however many times you send it.`,
                )}
              </Alert>
            )}

            <RadioGroup
              label={t('Payment mode')}
              name="paymentMethod"
              onValueChange={(next) => {
                setMethod(next)
                status.announceAutosave(
                  t(
                    `${PAYMENT_METHODS.find((candidate) => candidate.value === next)?.label ?? next} selected.`,
                  ),
                )
              }}
              options={PAYMENT_METHODS.map((candidate) => ({
                value: candidate.value,
                label: t(candidate.label),
              }))}
              required
              value={method}
            />

            <NumericStepper
              decimalPlaces={2}
              description={t('Indian rupees. Enter the amount taken, not the change given.')}
              label={t('Amount taken')}
              min={0}
              name="paymentAmount"
              onValueChange={setAmount}
              step={50}
              unit={{ symbol: '₹', label: t('rupees'), position: 'leading' }}
              value={amount}
              {...(amountError === undefined ? {} : { error: amountError })}
            />

            <TextField
              description={t(
                'Optional. A cheque number, a UPI reference, the last four of a card.',
              )}
              label={t('Reference')}
              name="paymentReference"
              readOnly
              value=""
            />

            {/* Story controls: the application reads the real session and connection. */}
            <ButtonGroup label={t('Walkthrough controls')}>
              <Button
                onClick={() => {
                  setSessionExpired(true)
                }}
              >
                {t('Expire the session now')}
              </Button>
            </ButtonGroup>
            <Switch
              label={t('Working offline')}
              name="offline"
              onValueChange={(next) => {
                setOnline(!next)
              }}
              value={!online}
            />

            {online ? (
              <ButtonGroup size="primary">
                <Button
                  iconName="rupee"
                  onClick={() => {
                    setConfirming(true)
                  }}
                  size="primary"
                  unavailable={method === '' || overpaid || sessionExpired}
                  variant="primary"
                >
                  {t('Take the payment')}
                </Button>
              </ButtonGroup>
            ) : (
              <OfflineBlockedAction action={t('recording a payment')} online={false}>
                {t(
                  'This will not be queued. A payment taken on a device and sent later can be taken twice, or taken against a balance that has already been settled at another counter. Reconnect and take it then.',
                )}
              </OfflineBlockedAction>
            )}
          </section>

          {receipt === null ? null : (
            <Alert live="polite" title={t('Payment recorded')} tone="success">
              <p>
                {t(
                  `${formatters.formatMoney(amount)} by ${PAYMENT_METHODS.find((candidate) => candidate.value === method)?.label ?? ''}. Receipt ${receipt}.`,
                )}
              </p>
              <p>
                {/* The receipt document is reachable from here, not only from a toast that has gone. */}
                <TextLink href={`/billing/receipts/${receipt}`}>
                  {t(`Open receipt ${receipt}`)}
                </TextLink>
              </p>
              <StatusBadge
                detail={
                  advance
                    ? t('held unapplied')
                    : formatters.formatMoney(Math.max(balance - amount, 0))
                }
                status={advance ? 'draft' : amount >= balance ? 'paid' : 'unpaid'}
              />
            </Alert>
          )}
        </>
      )}

      <ConfirmDialog
        action="recording this payment"
        confirmLabel={t('Record the payment')}
        onCancel={() => {
          setConfirming(false)
        }}
        onConfirm={() => {
          setConfirming(false)
          setReceipt(RECEIPT_NUMBER)
        }}
        open={confirming}
        tier="confirm"
        title={t('Record this payment?')}
      >
        {t(
          advance
            ? `${formatters.formatMoney(amount)} by ${PAYMENT_METHODS.find((candidate) => candidate.value === method)?.label ?? ''}, held unapplied against ${INVOICE.customer} until an invoice is posted.`
            : `${formatters.formatMoney(amount)} by ${PAYMENT_METHODS.find((candidate) => candidate.value === method)?.label ?? ''} against ${INVOICE.number}. That leaves ${formatters.formatMoney(Math.max(balance - amount, 0))} outstanding.`,
        )}
      </ConfirmDialog>
    </section>
  )
}
