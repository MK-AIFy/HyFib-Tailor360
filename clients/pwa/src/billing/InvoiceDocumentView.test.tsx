import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AppIntlProvider } from '../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../design-system/testing/axe'
import { anAdjustmentNote, anInvoice, aCancelledInvoice } from './testing/fixtures'
import { InvoiceDocumentView } from './InvoiceDocumentView'

function renderView(props: Parameters<typeof InvoiceDocumentView>[0]) {
  return render(
    <AppIntlProvider locale="en-IN">
      <InvoiceDocumentView {...props} />
    </AppIntlProvider>,
  )
}

describe('InvoiceDocumentView', () => {
  it('renders every item of the closed list, as text, in the order the template prints it', () => {
    renderView({ invoice: anInvoice() })

    // Header.
    expect(screen.getByText('INV-CBE01-2627-000731')).toBeInTheDocument()
    expect(screen.getByText('Date 12-09-2026 · FY 2627')).toBeInTheDocument()
    expect(screen.getByText('I-7K3M9QW2XZ4B')).toBeInTheDocument()

    // Supplier block: the two fields #336 projects, plus the ones already published.
    expect(screen.getByText('Example Tailors Private Limited')).toBeInTheDocument()
    expect(screen.getByText('Example Tailors')).toBeInTheDocument()
    expect(screen.getByText('GSTIN 33AAACH7409R1Z8 · State 33')).toBeInTheDocument()

    // Bill-to block.
    expect(screen.getByText('Kavitha (counter)')).toBeInTheDocument()
    expect(screen.getByText('Customer C-CBE01-000201')).toBeInTheDocument()
    expect(screen.getByText('12 Second Street, Demo Nagar')).toBeInTheDocument()
    expect(screen.getByText('Peelamedu 641004')).toBeInTheDocument()

    // Reference row.
    expect(screen.getByText('O-CBE01-2627-000512')).toBeInTheDocument()
    expect(screen.getByText('Place of supply 33 · IntraState')).toBeInTheDocument()

    // Lines: description, item code + HSN/SAC, discount, taxable value, tax components, line total.
    expect(screen.getByText('Blouse stitching')).toBeInTheDocument()
    expect(screen.getByText('STITCH_BLOUSE · HSN/SAC 998821')).toBeInTheDocument()
    expect(screen.getByText('Sleeve alteration')).toBeInTheDocument()
    expect(screen.getByText('FESTIVE10 −₹20.00')).toBeInTheDocument()
    expect(screen.getByText('CGST 2.5%: ₹12.63')).toBeInTheDocument()
    expect(screen.getByText('SGST 2.5%: ₹12.63')).toBeInTheDocument()
    expect(screen.getByText('₹530.26')).toBeInTheDocument()

    // Totals: taxable value, grand total and balance due always; a zero row absent.
    // "Taxable value" is both the line table's column header and the totals row's label.
    expect(screen.getAllByText('Taxable value').length).toBeGreaterThanOrEqual(2)
    expect(screen.getByText('Grand total')).toBeInTheDocument()
    expect(screen.getByText('Balance due')).toBeInTheDocument()
    expect(screen.getAllByText('₹720.00')).toHaveLength(2)
    expect(screen.getByText('Round-off')).toBeInTheDocument()
    expect(screen.getByText('+₹0.74')).toBeInTheDocument()
  })

  it('omits a zero integratedTax and a zero roundOff, while taxableValue, grandTotal and Balance due always render', () => {
    renderView({
      invoice: anInvoice({
        totals: {
          subtotal: 0,
          discountTotal: 0,
          taxableValue: 500,
          centralTax: 12.5,
          stateTax: 12.5,
          integratedTax: 0,
          cess: 0,
          roundOff: 0,
          grandTotal: 525,
        },
      }),
    })

    // "Taxable value" is both the line table's column header and the totals row's label.
    expect(screen.getAllByText('Taxable value').length).toBeGreaterThanOrEqual(2)
    expect(screen.getByText('Grand total')).toBeInTheDocument()
    expect(screen.getByText('Balance due')).toBeInTheDocument()
    expect(screen.queryByText('IGST')).not.toBeInTheDocument()
    expect(screen.queryByText('Round-off')).not.toBeInTheDocument()
    expect(screen.queryByText('Subtotal')).not.toBeInTheDocument()
    expect(screen.queryByText('Discount')).not.toBeInTheDocument()
  })

  it('shows CANCELLED and keeps the number and totals when the invoice is cancelled', () => {
    renderView({ invoice: aCancelledInvoice() })

    expect(screen.getByText('CANCELLED')).toBeInTheDocument()
    expect(screen.getByText('INV-CBE01-2627-000731')).toBeInTheDocument()
    expect(screen.getAllByText('₹720.00')).toHaveLength(2)
  })

  it('renders a credit note in the same shape, with its own number, no barcode and no balance line', () => {
    const invoice = anInvoice()
    const note = anAdjustmentNote()

    renderView({ invoice, note })

    expect(screen.getByText('Credit note')).toBeInTheDocument()
    expect(screen.getByText('CN-CBE01-2627-000045')).toBeInTheDocument()
    // The note's line is joined to the invoice line it names for its description and classification.
    expect(screen.getByText('Blouse stitching')).toBeInTheDocument()
    expect(screen.getByText('Reason: Lining charged twice.')).toBeInTheDocument()
    expect(screen.queryByText('I-7K3M9QW2XZ4B')).not.toBeInTheDocument()
    expect(screen.queryByText('Balance due')).not.toBeInTheDocument()
  })

  it('renders "Draft" where a number would be, for a draft invoice', () => {
    renderView({
      invoice: anInvoice({ status: 'Draft', invoiceNumber: null, postedOn: null, postedAt: null }),
    })

    expect(screen.getByText('Draft')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = renderView({ invoice: aCancelledInvoice() })
    await expectNoAccessibilityViolations(container)
  })
})
