import { FormattedMessage, useIntl } from 'react-intl'
import type { AdjustmentNote, Invoice, InvoiceLine, InvoiceTotals } from './types'
import { getFormatters } from '../i18n/formatters'
import type { Formatters } from '../i18n/formatters'

export interface InvoiceDocumentViewProps {
  readonly invoice: Invoice
  /** Renders this note instead of the invoice itself, in the same shape (#336). */
  readonly note?: AdjustmentNote
  readonly className?: string
}

/**
 * The invoice, or one of its notes, as a document — in real text, in the order
 * `BillingDocumentTemplate.cs` prints it.
 *
 * ## Why this exists beside the interactive detail screen
 *
 * `InvoiceDetailRoute` (E09-F02-4) is the application's own view of an invoice: a heading, a status
 * line, a lines table read as a table. This is the *document* — the supplier block, the barcode as
 * text, the reference row with the place of supply, and a totals block ordered and suppressed exactly
 * as the rendered PDF is — because a cashier reading this aloud at the counter, or printing it without
 * a printer bridge, needs the words the accountant's copy has, not a paraphrase of them.
 *
 * ## Why there is no `<img>`
 *
 * `docs/nfr/a11y-checklist.md` records A11Y-DP-01, A11Y-DP-03, A11Y-DP-04 and A11Y-DP-05 as failing
 * the moment a document preview becomes a picture: a screen reader cannot read a bitmap, cannot
 * reflow it at 200% zoom, and cannot select its text. So the `I-` barcode payload prints as the text
 * it already is — the Code 128 bars are the downloadable PDF's, not this screen's, to draw.
 *
 * ## Why nothing here is computed
 *
 * Every figure is read from the payload the server sent and printed as-is. `INV-INV-03` is a Billing
 * invariant about the stored calculation, not a client one, but the same discipline holds here for a
 * different reason: a client that summed its own totals could disagree with the server by a paisa of
 * rounding, and a document that disagrees with itself is worse than one that is merely plain.
 */
export function InvoiceDocumentView({ invoice, note, className }: InvoiceDocumentViewProps) {
  const intl = useIntl()
  const formatters = getFormatters()

  const number = note === undefined ? invoice.invoiceNumber : note.number
  const isNote = note !== undefined
  const kindTitle =
    note === undefined
      ? 'billing.invoice.document.title.invoice'
      : note.kind === 'Credit'
        ? 'billing.invoice.document.title.credit'
        : 'billing.invoice.document.title.debit'
  const issuedOn = note === undefined ? invoice.postedOn : note.postedAt
  const totals = note === undefined ? invoice.totals : note.totals
  const lines = documentLines(invoice, note)

  return (
    <div className={['billing__document', className].filter(Boolean).join(' ')}>
      <div className="billing__document-header">
        <div>
          <p className="billing__document-title">{intl.formatMessage({ id: kindTitle })}</p>
          <p className="billing__document-number">
            {number ?? intl.formatMessage({ id: 'billing.invoice.header.draft' })}
          </p>
          <p className="billing__hint">
            {issuedOn === null
              ? intl.formatMessage({ id: 'billing.invoice.header.notPosted' })
              : intl.formatMessage(
                  { id: 'billing.invoice.document.issued' },
                  {
                    date: formatters.formatShortDate(issuedOn),
                    year: invoice.financialYear ?? '—',
                  },
                )}
          </p>
          {invoice.cancelled ? (
            <p className="billing__document-cancelled">
              <FormattedMessage id="billing.invoice.document.cancelledMark" />
            </p>
          ) : null}
          {note === undefined && invoice.barcodePayload !== null ? (
            <p className="billing__document-barcode">{invoice.barcodePayload}</p>
          ) : null}
        </div>
      </div>

      <div className="billing__document-parties">
        <section aria-labelledby="document-supplier-heading">
          <h3 id="document-supplier-heading">
            <FormattedMessage id="billing.invoice.document.supplier.title" />
          </h3>
          <p className="billing__document-name">{invoice.calculation.supplierLegalName}</p>
          {invoice.calculation.supplierTradeName === null ||
          invoice.calculation.supplierTradeName === invoice.calculation.supplierLegalName ? null : (
            <p>{invoice.calculation.supplierTradeName}</p>
          )}
          <p className="billing__hint">
            {intl.formatMessage(
              { id: 'billing.invoice.document.supplier.gstin' },
              {
                gstin: invoice.calculation.gstin,
                stateCode: invoice.calculation.supplierStateCode,
              },
            )}
          </p>
          {note === undefined ? (
            <p className="billing__hint billing__document-artefactNote">
              <FormattedMessage id="billing.invoice.document.artefactNote" />
            </p>
          ) : null}
        </section>

        <section aria-labelledby="document-customer-heading">
          <h3 id="document-customer-heading">
            <FormattedMessage id="billing.invoice.customer.title" />
          </h3>
          <p className="billing__document-name">{invoice.customer.displayName}</p>
          <p className="billing__hint">
            {intl.formatMessage(
              { id: 'billing.invoice.document.customer.number' },
              { number: invoice.customer.customerNumber },
            )}
          </p>
          {invoice.customer.addressLine === null ? null : <p>{invoice.customer.addressLine}</p>}
          {invoice.customer.locality === null && invoice.customer.postcode === null ? null : (
            <p>
              {[invoice.customer.locality, invoice.customer.postcode].filter(Boolean).join(' ')}
            </p>
          )}
        </section>

        <section aria-labelledby="document-reference-heading">
          <h3 id="document-reference-heading">
            {intl.formatMessage({
              id: isNote
                ? 'billing.invoice.document.reference.note'
                : 'billing.invoice.document.reference.order',
            })}
          </h3>
          <p className="billing__document-name">
            {isNote ? (invoice.invoiceNumber ?? '—') : invoice.orderNumber}
          </p>
          <p className="billing__hint">
            {intl.formatMessage(
              { id: 'billing.invoice.document.reference.placeOfSupply' },
              {
                state: invoice.calculation.placeOfSupplyStateCode,
                scheme: invoice.calculation.scheme,
              },
            )}
          </p>
          {isNote && note.reason.length > 0 ? (
            <p>
              {intl.formatMessage({ id: 'billing.invoice.notes.reason' }, { reason: note.reason })}
            </p>
          ) : null}
        </section>
      </div>

      <div
        aria-label={intl.formatMessage({ id: 'billing.invoice.lines.caption' })}
        className="billing__document-lines-scroll"
        role="region"
        tabIndex={0}
      >
        <table className="billing__document-lines">
          <caption className="visually-hidden">
            {intl.formatMessage({ id: 'billing.invoice.lines.caption' })}
          </caption>
          <thead>
            <tr>
              <th scope="col">{intl.formatMessage({ id: 'billing.invoice.lines.column.line' })}</th>
              <th scope="col">
                {intl.formatMessage({ id: 'billing.invoice.lines.column.description' })}
              </th>
              <th scope="col">
                {intl.formatMessage({ id: 'billing.invoice.lines.column.quantity' })}
              </th>
              <th scope="col">{intl.formatMessage({ id: 'billing.invoice.lines.column.rate' })}</th>
              <th scope="col">
                {intl.formatMessage({ id: 'billing.invoice.lines.column.taxableValue' })}
              </th>
              <th scope="col">{intl.formatMessage({ id: 'billing.invoice.lines.column.tax' })}</th>
              <th scope="col">
                {intl.formatMessage({ id: 'billing.invoice.lines.column.total' })}
              </th>
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.garmentJobId}>
                <td>{String(line.lineNumber)}</td>
                <td>
                  <p>{line.description}</p>
                  <p className="billing__hint">
                    {intl.formatMessage(
                      { id: 'billing.invoice.document.line.itemAndClassification' },
                      { itemCode: line.itemCode, classification: line.classification },
                    )}
                  </p>
                  {line.surcharges.map((surcharge) => (
                    <p className="billing__hint" key={surcharge.itemCode}>
                      {intl.formatMessage(
                        { id: 'billing.invoice.document.line.surcharge' },
                        {
                          description: surcharge.description,
                          amount: formatters.formatMoney(surcharge.amount),
                        },
                      )}
                    </p>
                  ))}
                  {Number(line.discountAmount) > 0 ? (
                    <p className="billing__hint">
                      {intl.formatMessage(
                        { id: 'billing.invoice.lines.discount.value' },
                        {
                          ruleCode: line.discountRuleCode ?? '',
                          amount: formatters.formatMoney(line.discountAmount),
                        },
                      )}
                    </p>
                  ) : null}
                </td>
                <td>{line.quantity === null ? '—' : formatters.formatNumber(line.quantity)}</td>
                <td>
                  {line.appliedRate === null ? '—' : formatters.formatMoney(line.appliedRate)}
                </td>
                <td>{formatters.formatMoney(line.taxableValue)}</td>
                <td>
                  {line.taxes.map((tax) => (
                    <p key={tax.kind}>
                      {intl.formatMessage(
                        { id: 'billing.invoice.lines.tax.component' },
                        {
                          kind: tax.kind,
                          rate: formatters.formatPercent(tax.ratePercent),
                          amount: formatters.formatMoney(tax.amount),
                        },
                      )}
                    </p>
                  ))}
                </td>
                <td>{formatters.formatMoney(line.lineTotal)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <DocumentTotals formatters={formatters} showBalanceDue={!isNote} totals={totals} />
    </div>
  )
}

/** One rendered line: either an invoice's own, or a note's line joined to the invoice line it moves. */
interface DocumentLine {
  readonly garmentJobId: string
  readonly lineNumber: number | string
  readonly description: string
  readonly itemCode: string
  readonly classification: string
  readonly quantity: number | string | null
  readonly appliedRate: number | string | null
  readonly surcharges: InvoiceLine['surcharges']
  readonly discountRuleCode: string | null
  readonly discountAmount: number | string
  readonly taxableValue: number | string
  readonly taxes: InvoiceLine['taxes']
  readonly lineTotal: number | string
}

function documentLines(
  invoice: Invoice,
  note: AdjustmentNote | undefined,
): readonly DocumentLine[] {
  if (note === undefined) {
    return invoice.lines.map((line) => ({
      garmentJobId: line.garmentJobId,
      lineNumber: line.lineNumber,
      description: line.description,
      itemCode: line.itemCode,
      classification: line.classification,
      quantity: line.quantity,
      appliedRate: line.appliedRate,
      surcharges: line.surcharges,
      discountRuleCode: line.discountRuleCode,
      discountAmount: line.discountAmount,
      taxableValue: line.taxableValue,
      taxes: line.taxes,
      lineTotal: line.lineTotal,
    }))
  }

  // A note's own line carries only what moved — the description, item code and classification are
  // read from the invoice line it names, exactly as DocumentModels.cs's Note() joins them.
  return note.lines.map((line) => {
    const invoiceLine = invoice.lines.find(
      (candidate) => candidate.garmentJobId === line.garmentJobId,
    )
    return {
      garmentJobId: line.garmentJobId,
      lineNumber: line.lineNumber,
      description: invoiceLine?.description ?? '',
      itemCode: invoiceLine?.itemCode ?? '',
      classification: invoiceLine?.classification ?? '',
      quantity: null,
      appliedRate: null,
      surcharges: [],
      discountRuleCode: null,
      discountAmount: 0,
      taxableValue: line.taxableValue,
      taxes: line.taxes,
      lineTotal: line.lineTotal,
    }
  })
}

interface TotalsRow {
  readonly id: keyof InvoiceTotals
  readonly labelId: string
  readonly alwaysShown: boolean
  readonly signed: boolean
}

// The exact order and suppression rule `BillingDocumentTemplate.cs`'s `Line(...)` calls declare: a
// row is skipped when its amount is zero unless it is one of the template's emphasised rows, which
// are shown unconditionally.
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

function DocumentTotals({
  totals,
  formatters,
  showBalanceDue,
}: {
  readonly totals: InvoiceTotals
  readonly formatters: Formatters
  readonly showBalanceDue: boolean
}) {
  const intl = useIntl()

  return (
    <section aria-labelledby="document-totals-heading">
      <h3 id="document-totals-heading">
        <FormattedMessage id="billing.invoice.totals.title" />
      </h3>
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
        {showBalanceDue ? (
          <div className="billing__totalsRow" data-emphasis="true">
            <dt>
              <FormattedMessage id="billing.invoice.document.balanceDue" />
            </dt>
            <dd>{formatters.formatMoney(totals.grandTotal)}</dd>
          </div>
        ) : null}
      </dl>
    </section>
  )
}
