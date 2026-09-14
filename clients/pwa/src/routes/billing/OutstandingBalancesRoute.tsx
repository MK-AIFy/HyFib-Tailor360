import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { listOutstandingBalances } from '../../billing/billingApi'
import type { OutstandingBalanceRow } from '../../billing/types'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { getFormatters } from '../../i18n/formatters'
import './billing.css'

/**
 * The page size, cited from `IInvoiceStore.InvoiceListQuery.DefaultLimit` rather than chosen here,
 * as `InvoiceRegisterRoute` already does — the client and the server agree on what a page is
 * without either one guessing at the other's default.
 */
const PAGE_SIZE = 20

/**
 * Every posted invoice at this branch with money still owed against it (#165, #421), a page at a
 * time through the server's own aggregate.
 *
 * `docs/security/permission-matrix.md` scopes `billing.create_invoice` — the key this reads
 * behind, there being no narrower one — to `current-branch`, so this is always the caller's own
 * branch; no branch picker exists on this screen or anywhere else in Billing.
 *
 * ## Why the empty state needs both an empty page and a null cursor
 *
 * `nextCursor` is non-null whenever the server's scan stopped without exhausting the branch's
 * posted invoices — including a page whose `rows` came back empty, because a source page of posted
 * invoices that are all settled answers no rows on its own. Showing the empty state on such a page
 * would tell the reader there is nothing outstanding when the read simply has not looked far enough
 * yet, so the empty state is reserved for the one case that actually means that: no rows, and no
 * cursor left to keep looking with.
 */
export function OutstandingBalancesRoute() {
  const intl = useIntl()
  const formatters = getFormatters()

  const [cursors, setCursors] = useState<readonly string[]>([])
  const [seen, setSeen] = useState<readonly OutstandingBalanceRow[]>([])

  const cursor = cursors.at(-1)

  const page = useAdminResource(cursor ?? '', (signal) =>
    listOutstandingBalances({
      limit: PAGE_SIZE,
      ...(cursor === undefined ? {} : { cursor }),
      signal,
    }),
  )

  // Concatenated as they arrive, as every other cursor-paged screen here does: Show more must not
  // lose the rows already on screen.
  const rows =
    cursor === undefined ? (page.value?.rows ?? []) : [...seen, ...(page.value?.rows ?? [])]
  const exhausted = page.value !== null && page.value.nextCursor === null

  return (
    <section className="page billing">
      <h1>
        <FormattedMessage id="billing.outstanding.title" />
      </h1>
      <p className="billing__lede">
        <FormattedMessage id="billing.outstanding.body" />
      </p>

      <AuthProblemAlert failure={page.failure} />

      {page.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'billing.outstanding.loading' })} />
      ) : rows.length === 0 && exhausted && page.failure === null ? (
        <EmptyState
          iconName="rupee"
          live="polite"
          title={intl.formatMessage({ id: 'billing.outstanding.empty.title' })}
        >
          {intl.formatMessage({ id: 'billing.outstanding.empty' })}
        </EmptyState>
      ) : (
        <>
          {/* role="status" carries an implicit aria-live="polite": Show more appends a page nobody
              asked to have the count re-announced for on its own, so this is the one place that
              answers A11Y-LF-01 and A11Y-LF-06 for this screen. */}
          <p className="billing__hint" role="status">
            {intl.formatMessage({ id: 'billing.outstanding.count' }, { count: rows.length })}
          </p>
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
                cell: (row: OutstandingBalanceRow) => row.invoiceNumber,
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
        </>
      )}

      {page.value?.nextCursor == null ? null : (
        <div className="billing__actions">
          <Button
            onClick={() => {
              setSeen(rows)
              setCursors((previous) => [...previous, page.value?.nextCursor ?? ''])
            }}
            variant="secondary"
          >
            <FormattedMessage id="billing.outstanding.loadMore" />
          </Button>
        </div>
      )}
    </section>
  )
}
