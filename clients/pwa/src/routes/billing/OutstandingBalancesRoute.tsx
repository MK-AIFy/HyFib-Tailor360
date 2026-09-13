import { FormattedMessage, useIntl } from 'react-intl'
import { Link } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { listOutstandingBalances } from '../../billing/billingApi'
import type { OutstandingBalanceRow } from '../../billing/types'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { getFormatters } from '../../i18n/formatters'
import './billing.css'

/**
 * Every posted invoice at this branch with money still owed against it (#165).
 *
 * `docs/security/permission-matrix.md` scopes `billing.create_invoice` — the key this reads
 * behind, there being no narrower one — to `current-branch`, so this is always the caller's own
 * branch; no branch picker exists on this screen or anywhere else in Billing.
 */
export function OutstandingBalancesRoute() {
  const intl = useIntl()
  const formatters = getFormatters()

  const balances = useAdminResource('outstanding-balances', (signal) =>
    listOutstandingBalances(signal),
  )
  const rows = balances.value ?? []

  return (
    <section className="page billing">
      <h1>
        <FormattedMessage id="billing.outstanding.title" />
      </h1>
      <p className="billing__lede">
        <FormattedMessage id="billing.outstanding.body" />
      </p>

      <AuthProblemAlert failure={balances.failure} />

      {balances.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'billing.outstanding.loading' })} />
      ) : rows.length === 0 && balances.failure === null ? (
        <EmptyState
          iconName="rupee"
          live="polite"
          title={intl.formatMessage({ id: 'billing.outstanding.empty.title' })}
        >
          {intl.formatMessage({ id: 'billing.outstanding.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'billing.outstanding.caption' })}
          columns={[
            {
              id: 'order',
              header: intl.formatMessage({ id: 'billing.outstanding.column.order' }),
              primary: true,
              cell: (row: OutstandingBalanceRow) => row.orderNumber,
            },
            {
              id: 'customer',
              header: intl.formatMessage({ id: 'billing.outstanding.column.customer' }),
              cell: (row: OutstandingBalanceRow) => row.customerDisplayName,
            },
            {
              id: 'invoice',
              header: intl.formatMessage({ id: 'billing.outstanding.column.invoice' }),
              hideWhenNarrow: true,
              cell: (row: OutstandingBalanceRow) => row.invoiceNumber ?? '—',
            },
            {
              id: 'total',
              header: intl.formatMessage({ id: 'billing.outstanding.column.total' }),
              numeric: true,
              hideWhenNarrow: true,
              cell: (row: OutstandingBalanceRow) => formatters.formatMoney(row.grandTotal),
            },
            {
              id: 'outstanding',
              header: intl.formatMessage({ id: 'billing.outstanding.column.outstanding' }),
              numeric: true,
              cell: (row: OutstandingBalanceRow) => formatters.formatMoney(row.outstanding),
            },
          ]}
          rowActions={(row: OutstandingBalanceRow) => (
            <Link
              aria-label={intl.formatMessage(
                { id: 'billing.outstanding.takePayment.label' },
                { order: row.orderNumber },
              )}
              to={`/billing/payments/new?${new URLSearchParams({
                orderId: row.orderId,
                orderNumber: row.orderNumber,
              }).toString()}`}
            >
              {intl.formatMessage({ id: 'billing.outstanding.takePayment' })}
            </Link>
          )}
          rowKey={(row) => row.invoiceId}
          rowLabel={(row) => row.orderNumber}
          rows={rows}
        />
      )}
    </section>
  )
}
