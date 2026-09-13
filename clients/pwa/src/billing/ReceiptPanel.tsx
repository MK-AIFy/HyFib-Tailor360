import { useState } from 'react'
import { Link } from 'react-router'
import { useIntl } from 'react-intl'
import { printReceipt } from './billingApi'
import { BillingProblemAlert } from './BillingProblemAlert'
import type { Payment } from './types'
import { Alert } from '../components/primitives/Alert'
import { Button } from '../components/primitives/Button'
import { ButtonGroup } from '../components/primitives/ButtonGroup'
import { getFormatters } from '../i18n/formatters'

export interface ReceiptPanelProps {
  readonly payment: Payment
}

/**
 * The receipt a payment issued, with the two ways this build can hand it onward.
 *
 * ## The customer-link gap
 *
 * `docs/prd/glossary.md`'s customer link supports exactly three purposes — `estimate`, `status` and
 * `feedback` — and a `receipt` purpose does not exist on the server today. Rather than invent one
 * here, this panel offers what does exist: the receipt sent to the branch's print queue, and the
 * in-app reference a colleague signed in at this branch can open to read or reprint it. Sharing that
 * reference *with the customer* is a follow-up, tracked outside this change.
 */
export function ReceiptPanel({ payment }: ReceiptPanelProps) {
  const intl = useIntl()
  const formatters = getFormatters()
  const receipt = payment.receipt

  const [printKey, setPrintKey] = useState<{
    readonly fingerprint: string
    readonly key: string
  } | null>(null)
  const [printBusy, setPrintBusy] = useState(false)
  const [printFailure, setPrintFailure] = useState<unknown>(null)
  const [printJobId, setPrintJobId] = useState<string | null>(null)

  if (receipt === null) {
    return null
  }

  const print = async (): Promise<void> => {
    setPrintBusy(true)
    setPrintFailure(null)
    const fingerprint = `${receipt.id}:1`
    const key =
      printKey !== null && printKey.fingerprint === fingerprint ? printKey.key : crypto.randomUUID()
    setPrintKey({ fingerprint, key })

    try {
      const job = await printReceipt({
        receiptId: receipt.id,
        body: { copies: 1 },
        idempotencyKey: key,
      })
      setPrintKey(null)
      setPrintJobId(job.printJobId)
    } catch (cause: unknown) {
      setPrintFailure(cause)
    } finally {
      setPrintBusy(false)
    }
  }

  return (
    <section className="billing__receipt" aria-labelledby="receipt-heading">
      <h2 id="receipt-heading">{intl.formatMessage({ id: 'billing.payment.receipt.title' })}</h2>
      <p>
        {intl.formatMessage(
          { id: 'billing.payment.receipt.summary' },
          {
            receiptNumber: receipt.receiptNumber,
            amount: formatters.formatMoney(receipt.amount),
            date: formatters.formatDateTime(receipt.issuedAt),
          },
        )}
      </p>
      {Number(receipt.allocated) > 0 ? (
        <p>
          {intl.formatMessage(
            { id: 'billing.payment.receipt.allocated' },
            { amount: formatters.formatMoney(receipt.allocated) },
          )}
        </p>
      ) : null}
      {Number(receipt.unappliedAdvance) > 0 ? (
        <p>
          {intl.formatMessage(
            { id: 'billing.payment.receipt.advance' },
            { amount: formatters.formatMoney(receipt.unappliedAdvance) },
          )}{' '}
          <Link to={`/billing/payments/${payment.id}/allocate`}>
            {intl.formatMessage({ id: 'billing.payment.receipt.allocateNow' })}
          </Link>
        </p>
      ) : null}
      <p>
        {intl.formatMessage(
          { id: 'billing.payment.receipt.outstanding' },
          { amount: formatters.formatMoney(receipt.orderOutstanding) },
        )}
      </p>

      <ButtonGroup>
        <Button
          busy={printBusy}
          iconName="receipt"
          onClick={() => {
            void print()
          }}
          variant="secondary"
        >
          {intl.formatMessage({
            id: printBusy ? 'billing.payment.receipt.printing' : 'billing.payment.receipt.print',
          })}
        </Button>
        <Link to={`/billing/payments/${payment.id}`}>
          {intl.formatMessage(
            { id: 'billing.payment.receipt.share.inApp' },
            {
              receiptNumber: receipt.receiptNumber,
            },
          )}
        </Link>
      </ButtonGroup>

      {printJobId === null ? null : (
        <Alert live="polite" tone="success">
          {intl.formatMessage({ id: 'billing.payment.receipt.printed' }, { jobId: printJobId })}
        </Alert>
      )}
      <BillingProblemAlert failure={printFailure} />

      <Alert title={intl.formatMessage({ id: 'billing.payment.receipt.share.title' })} tone="info">
        {intl.formatMessage({ id: 'billing.payment.receipt.share.body' })}
      </Alert>
    </section>
  )
}
