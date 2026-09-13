import { FormattedMessage, useIntl } from 'react-intl'
import { useParams } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { getPayment } from '../../billing/billingApi'
import { ReceiptPanel } from '../../billing/ReceiptPanel'
import { LoadingState } from '../../components/states/LoadingState'
import './billing.css'

/**
 * One payment, read back — the in-app surface `ReceiptPanel` links to as today's stand-in for a
 * customer-facing receipt link (#165; see `ReceiptPanel`'s own note on the gap).
 *
 * Anyone signed in at the branch who holds `payments.record` can open it, the same key the payment
 * was recorded under; a payment taken at another branch reads as 404, which `AuthProblemAlert`
 * shows in the shop's own words rather than as a stack trace.
 */
export function PaymentDetailRoute() {
  const intl = useIntl()
  const { paymentId } = useParams()

  const payment = useAdminResource(`payment:${paymentId ?? ''}`, (signal) =>
    getPayment(paymentId ?? '', signal),
  )

  return (
    <section className="page billing">
      <h1>
        <FormattedMessage id="billing.payment.receipt.title" />
      </h1>

      <AuthProblemAlert failure={payment.failure} />

      {payment.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'billing.payment.balance.loading' })} />
      ) : payment.value === null ? null : (
        <ReceiptPanel payment={payment.value} />
      )}
    </section>
  )
}
