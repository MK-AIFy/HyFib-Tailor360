import { FormattedMessage, useIntl } from 'react-intl'
import { useParams } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { getInvoice } from '../../billing/billingApi'
import type { Invoice, InvoiceLine, InvoiceTotals } from '../../billing/types'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { LoadingState } from '../../components/states/LoadingState'
import { NetworkStatusBanner } from '../../components/states/NetworkStatusBanner'
import { getFormatters } from '../../i18n/formatters'
import type { Formatters } from '../../i18n/formatters'
import './billing.css'

/**
 * One invoice, with its lines, its tax components and its totals (#302).
 *
 * ## Why the totals block hides some of its own fields
 *
 * `InvoiceTotalsPayload` carries nine figures, but `BillingDocumentTemplate.cs` — the renderer of
 * the printed document this screen is the on-screen twin of — prints `Taxable value` and
 * `Grand total` unconditionally and omits every other line when it is zero. This screen follows the
 * identical rule (`TOTALS_ROWS` below), because an intra-state invoice's `IGST` row and a whole-rupee
 * invoice's `Round-off` row are not merely uninteresting zeroes here — printing them would be a
 * screen that disagrees with the document the accountant compares it against.
 */
export function InvoiceDetailRoute() {
  const intl = useIntl()
  const formatters = getFormatters()
  const { invoiceId } = useParams()

  const resource = useAdminResource(`invoice:${invoiceId ?? ''}`, (signal) =>
    getInvoice(invoiceId ?? '', signal).then((response) => response.value),
  )
  const invoice = resource.value

  return (
    <section className="page billing">
      <h1>
        {invoice === null
          ? intl.formatMessage({ id: 'billing.invoice.title' })
          : (invoice.invoiceNumber ?? intl.formatMessage({ id: 'billing.invoice.header.draft' }))}
      </h1>

      <NetworkStatusBanner />

      {resource.failure === null ? null : (
        <>
          <BillingProblemAlert failure={resource.failure} />
          <Button
            iconName="refresh"
            onClick={() => {
              resource.reload()
            }}
            variant="secondary"
          >
            <FormattedMessage id="states.error.retry" />
          </Button>
        </>
      )}

      {resource.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'billing.invoice.loading' })} />
      ) : invoice === null ? null : (
        <InvoiceDetail formatters={formatters} invoice={invoice} />
      )}
    </section>
  )
}

function InvoiceDetail({ invoice, formatters }: { invoice: Invoice; formatters: Formatters }) {
  const intl = useIntl()

  return (
    <>
      <p className="billing__lede">
        {intl.formatMessage({ id: `billing.invoices.status.${invoice.status.toLowerCase()}` })}
      </p>
      <p className="billing__hint">
        {intl.formatMessage(
          { id: 'billing.invoice.header.order' },
          { orderNumber: invoice.orderNumber },
        )}
      </p>
      {invoice.financialYear === null ? null : (
        <p className="billing__hint">
          {intl.formatMessage(
            { id: 'billing.invoice.header.financialYear' },
            { year: invoice.financialYear },
          )}
        </p>
      )}
      <p className="billing__hint">
        {invoice.postedAt !== null
          ? intl.formatMessage(
              { id: 'billing.invoice.header.posted' },
              { date: formatters.formatShortDate(invoice.postedAt) },
            )
          : invoice.discardedAt !== null
            ? intl.formatMessage(
                { id: 'billing.invoice.header.discarded' },
                { date: formatters.formatShortDate(invoice.discardedAt) },
              )
            : intl.formatMessage({ id: 'billing.invoice.header.notPosted' })}
      </p>

      {invoice.cancelled && invoice.cancellation !== null ? (
        <Alert
          live="polite"
          title={intl.formatMessage({ id: 'billing.invoice.cancelled.title' })}
          tone="warning"
        >
          <p className="state-line">
            {intl.formatMessage(
              { id: 'billing.invoice.cancelled.body' },
              {
                date: formatters.formatShortDate(invoice.cancellation.cancelledAt),
                reason: invoice.cancellation.reason,
              },
            )}
          </p>
        </Alert>
      ) : null}

      <section aria-labelledby="invoice-customer-heading">
        <h2 id="invoice-customer-heading">
          <FormattedMessage id="billing.invoice.customer.title" />
        </h2>
        <p>{invoice.customer.displayName}</p>
        {invoice.customer.addressLine === null ? null : <p>{invoice.customer.addressLine}</p>}
        {invoice.customer.locality === null && invoice.customer.postcode === null ? null : (
          <p>{[invoice.customer.locality, invoice.customer.postcode].filter(Boolean).join(' ')}</p>
        )}
      </section>

      <DataTable
        caption={intl.formatMessage({ id: 'billing.invoice.lines.caption' })}
        columns={[
          {
            id: 'line',
            header: intl.formatMessage({ id: 'billing.invoice.lines.column.line' }),
            cell: (row: InvoiceLine) => String(row.lineNumber),
          },
          {
            id: 'description',
            header: intl.formatMessage({ id: 'billing.invoice.lines.column.description' }),
            primary: true,
            cell: (row: InvoiceLine) => row.description,
          },
          {
            id: 'classification',
            header: intl.formatMessage({ id: 'billing.invoice.lines.column.classification' }),
            hideWhenNarrow: true,
            cell: (row: InvoiceLine) => row.classification,
          },
          {
            id: 'quantity',
            header: intl.formatMessage({ id: 'billing.invoice.lines.column.quantity' }),
            numeric: true,
            cell: (row: InvoiceLine) => formatters.formatNumber(row.quantity),
          },
          {
            id: 'rate',
            header: intl.formatMessage({ id: 'billing.invoice.lines.column.rate' }),
            numeric: true,
            hideWhenNarrow: true,
            cell: (row: InvoiceLine) => formatters.formatMoney(row.appliedRate),
          },
          {
            id: 'discount',
            header: intl.formatMessage({ id: 'billing.invoice.lines.column.discount' }),
            numeric: true,
            hideWhenNarrow: true,
            cell: (row: InvoiceLine) =>
              Number(row.discountAmount) > 0
                ? intl.formatMessage(
                    { id: 'billing.invoice.lines.discount.value' },
                    {
                      ruleCode: row.discountRuleCode ?? '',
                      amount: formatters.formatMoney(row.discountAmount),
                    },
                  )
                : intl.formatMessage({ id: 'billing.invoice.lines.discount.none' }),
          },
          {
            id: 'taxableValue',
            header: intl.formatMessage({ id: 'billing.invoice.lines.column.taxableValue' }),
            numeric: true,
            cell: (row: InvoiceLine) => formatters.formatMoney(row.taxableValue),
          },
          {
            id: 'tax',
            header: intl.formatMessage({ id: 'billing.invoice.lines.column.tax' }),
            cell: (row: InvoiceLine) => (
              <ul className="billing__taxList">
                {row.taxes.map((tax) => (
                  <li key={tax.kind}>
                    {intl.formatMessage(
                      { id: 'billing.invoice.lines.tax.component' },
                      {
                        kind: tax.kind,
                        rate: formatters.formatPercent(tax.ratePercent),
                        amount: formatters.formatMoney(tax.amount),
                      },
                    )}
                  </li>
                ))}
              </ul>
            ),
          },
          {
            id: 'total',
            header: intl.formatMessage({ id: 'billing.invoice.lines.column.total' }),
            numeric: true,
            cell: (row: InvoiceLine) => formatters.formatMoney(row.lineTotal),
          },
        ]}
        rowKey={(row) => row.garmentJobId}
        rowLabel={(row) => row.description}
        rows={invoice.lines}
      />

      <InvoiceTotalsBlock formatters={formatters} totals={invoice.totals} />

      {invoice.notes.length === 0 ? null : (
        <section aria-labelledby="invoice-notes-heading">
          <h2 id="invoice-notes-heading">
            <FormattedMessage id="billing.invoice.notes.title" />
          </h2>
          <ul className="billing__notes">
            {invoice.notes.map((note) => (
              <li key={note.noteId}>
                <article>
                  <h3>
                    {intl.formatMessage(
                      {
                        id:
                          note.kind === 'Credit'
                            ? 'billing.invoice.notes.credit'
                            : 'billing.invoice.notes.debit',
                      },
                      { number: note.number },
                    )}
                  </h3>
                  <p className="billing__hint">
                    {intl.formatMessage(
                      { id: 'billing.invoice.notes.posted' },
                      { date: formatters.formatShortDate(note.postedAt) },
                    )}
                  </p>
                  <p>
                    {intl.formatMessage(
                      { id: 'billing.invoice.notes.reason' },
                      { reason: note.reason },
                    )}
                  </p>
                  <p>{formatters.formatMoney(note.totals.grandTotal)}</p>
                </article>
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  )
}

/** One row of the totals block: whether it is shown when its amount is zero, and how it is signed. */
interface TotalsRow {
  readonly id: keyof InvoiceTotals
  readonly labelId: string
  readonly alwaysShown: boolean
  readonly signed: boolean
}

// The exact order and suppression rule `BillingDocumentTemplate.cs`'s `Line(...)` calls declare:
// every row is skipped when its amount is zero unless it is one of the renderer's two emphasised
// rows, which are shown unconditionally.
const TOTALS_ROWS: readonly TotalsRow[] = [
  { id: 'subtotal', labelId: 'billing.invoice.totals.subtotal', alwaysShown: false, signed: false },
  {
    id: 'discountTotal',
    labelId: 'billing.invoice.totals.discountTotal',
    alwaysShown: false,
    signed: false,
  },
  {
    id: 'taxableValue',
    labelId: 'billing.invoice.totals.taxableValue',
    alwaysShown: true,
    signed: false,
  },
  {
    id: 'centralTax',
    labelId: 'billing.invoice.totals.centralTax',
    alwaysShown: false,
    signed: false,
  },
  { id: 'stateTax', labelId: 'billing.invoice.totals.stateTax', alwaysShown: false, signed: false },
  {
    id: 'integratedTax',
    labelId: 'billing.invoice.totals.integratedTax',
    alwaysShown: false,
    signed: false,
  },
  { id: 'cess', labelId: 'billing.invoice.totals.cess', alwaysShown: false, signed: false },
  { id: 'roundOff', labelId: 'billing.invoice.totals.roundOff', alwaysShown: false, signed: true },
  {
    id: 'grandTotal',
    labelId: 'billing.invoice.totals.grandTotal',
    alwaysShown: true,
    signed: false,
  },
]

function formatSigned(formatters: Formatters, amount: number | string): string {
  const value = Number(amount)
  // A negative amount already carries `formatMoney`'s own sign; a positive one does not, by
  // convention, so this is the one place a "+" is added rather than read from the formatter.
  return value < 0 ? formatters.formatMoney(value) : `+${formatters.formatMoney(value)}`
}

function InvoiceTotalsBlock({
  totals,
  formatters,
}: {
  totals: InvoiceTotals
  formatters: Formatters
}) {
  const intl = useIntl()

  return (
    <section aria-labelledby="invoice-totals-heading">
      <h2 id="invoice-totals-heading">
        <FormattedMessage id="billing.invoice.totals.title" />
      </h2>
      <dl className="billing__totals">
        {TOTALS_ROWS.filter((row) => row.alwaysShown || Number(totals[row.id]) !== 0).map((row) => (
          <div className="billing__totalsRow" data-emphasis={row.alwaysShown} key={row.id}>
            <dt>{intl.formatMessage({ id: row.labelId })}</dt>
            <dd>
              {row.signed
                ? formatSigned(formatters, totals[row.id])
                : formatters.formatMoney(totals[row.id])}
            </dd>
          </div>
        ))}
      </dl>
    </section>
  )
}
