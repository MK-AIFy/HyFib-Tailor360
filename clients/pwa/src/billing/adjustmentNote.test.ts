import { describe, expect, it } from 'vitest'
import { noteTotalOf, remainingTaxableValueOf } from './adjustmentNote'
import { JOB_ID, anAdjustmentNote, anInvoice } from './testing/fixtures'
import type { Invoice, InvoiceLine } from './types'

/**
 * Pinned against `tests/fixtures/billing/pricing-golden-master.json`'s `walkthrough-blouse` case:
 * one line, garment job `JOB_ID`, taxable value 580 — the figure the server's own pricing engine
 * produces for "450 + 90 + 40, intra-state, exclusive", so this spec starts from the same number the
 * accountant's own golden master does rather than an invented one.
 */
const BLOUSE_LINE: InvoiceLine = {
  lineNumber: 1,
  garmentJobId: JOB_ID,
  itemCode: 'BLOUSE_PATTERN_STITCHING',
  description: 'Blouse pattern stitching',
  quantity: 1,
  catalogueRate: 450,
  appliedRate: 450,
  base: 450,
  surcharges: [],
  discountRuleCode: null,
  discountKind: null,
  discountValue: null,
  discountAmount: 0,
  gross: 580,
  taxableValue: 580,
  taxCode: 'GST5',
  classification: '998821',
  taxCodeKind: 'Services',
  taxes: [
    { kind: 'CGST', ratePercent: 2.5, amount: 14.5 },
    { kind: 'SGST', ratePercent: 2.5, amount: 14.5 },
  ],
  taxTotal: 29,
  lineTotal: 609,
  variance: 0,
}

function invoiceWithNotes(...notes: Invoice['notes']): Invoice {
  return anInvoice({ lines: [BLOUSE_LINE], notes })
}

describe('remainingTaxableValueOf', () => {
  it('answers the line’s own taxable value when nothing has been credited', () => {
    expect(remainingTaxableValueOf(invoiceWithNotes(), JOB_ID)).toBe(580)
  })

  it('subtracts one credit note line for the same job', () => {
    const invoice = invoiceWithNotes(
      anAdjustmentNote({
        kind: 'Credit',
        lines: [
          {
            lineNumber: 1,
            garmentJobId: JOB_ID,
            taxableValue: 90,
            taxes: [],
            taxTotal: 0,
            lineTotal: 90,
          },
        ],
      }),
    )

    expect(remainingTaxableValueOf(invoice, JOB_ID)).toBe(490)
  })

  it('subtracts every credit note line posted against the same job, not just the last one', () => {
    const invoice = invoiceWithNotes(
      anAdjustmentNote({
        noteId: 'note-1',
        kind: 'Credit',
        lines: [
          {
            lineNumber: 1,
            garmentJobId: JOB_ID,
            taxableValue: 90,
            taxes: [],
            taxTotal: 0,
            lineTotal: 90,
          },
        ],
      }),
      anAdjustmentNote({
        noteId: 'note-2',
        kind: 'Credit',
        lines: [
          {
            lineNumber: 1,
            garmentJobId: JOB_ID,
            taxableValue: 40,
            taxes: [],
            taxTotal: 0,
            lineTotal: 40,
          },
        ],
      }),
    )

    expect(remainingTaxableValueOf(invoice, JOB_ID)).toBe(450)
  })

  it('does not subtract a debit note — it adds to what is owed, never relieves it', () => {
    const invoice = invoiceWithNotes(
      anAdjustmentNote({
        kind: 'Debit',
        lines: [
          {
            lineNumber: 1,
            garmentJobId: JOB_ID,
            taxableValue: 40,
            taxes: [],
            taxTotal: 0,
            lineTotal: 40,
          },
        ],
      }),
    )

    // Same remaining value as an invoice carrying no notes at all — a debit note changes nothing
    // about what a credit note may still relieve, mirroring the server's own RemainingTaxableValueOf.
    expect(remainingTaxableValueOf(invoice, JOB_ID)).toBe(
      remainingTaxableValueOf(invoiceWithNotes(), JOB_ID),
    )
    expect(remainingTaxableValueOf(invoice, JOB_ID)).toBe(580)
  })

  it('answers zero for a garment job the invoice does not carry', () => {
    expect(remainingTaxableValueOf(invoiceWithNotes(), 'not-a-line-of-this-invoice')).toBe(0)
  })
})

describe('noteTotalOf', () => {
  it('answers zero over no rows', () => {
    expect(noteTotalOf([])).toBe(0)
  })

  it('answers the one row’s value', () => {
    expect(noteTotalOf([{ garmentJobId: JOB_ID, taxableValue: 90 }])).toBe(90)
  })

  it('sums several rows without rounding, to the paisa', () => {
    expect(
      noteTotalOf([
        { garmentJobId: JOB_ID, taxableValue: 10.01 },
        { garmentJobId: 'job-2', taxableValue: 5.02 },
        { garmentJobId: 'job-3', taxableValue: 0.03 },
      ]),
    ).toBeCloseTo(15.06, 2)
  })
})
