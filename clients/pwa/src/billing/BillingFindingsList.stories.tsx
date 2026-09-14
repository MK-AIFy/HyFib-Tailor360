import type { Meta, StoryObj } from '@storybook/react-vite'
import { BillingFindingsList } from './BillingFindingsList'
import { aBillingFinding } from './testing/pricingConfigFixtures'

/**
 * The shared billing findings list (E09-F01-5b): a publish validation's errors and warnings, each
 * with its own severity, target and sentence. Shared unchanged by the tax configuration editor and
 * the price-list editor (E09-F01-7b) — severity is read from each finding's own `severity` field,
 * never from a client-side list of codes (OD-19).
 */
const meta = {
  title: 'Billing/Findings list',
  component: BillingFindingsList,
  parameters: { layout: 'padded' },
} satisfies Meta<typeof BillingFindingsList>

export default meta
type Story = StoryObj<typeof meta>

/** No finding at all: a success alert, not a blank region. */
export const Empty: Story = { args: { findings: [] } }

/** Both severities present — the one story this component's own reuse depends on. */
export const ErrorsAndWarnings: Story = {
  args: {
    findings: [
      aBillingFinding({
        severity: 'Error',
        code: 'billing.intra-state-pair-incomplete',
        message: 'The tax code STITCHING_5 carries a CGST without its matching SGST.',
        target: 'taxCodes[STITCHING_5]',
      }),
      aBillingFinding({
        severity: 'Error',
        code: 'billing.no-tax-codes',
        message: 'This version carries no tax code at all.',
        target: null,
      }),
      aBillingFinding({
        severity: 'Warning',
        code: 'billing.nil-rated-code',
        message: 'The tax code ALTER_0 carries no rate at all.',
        target: 'taxCodes[ALTER_0]',
      }),
    ],
  },
}

/** Warnings only — a successful publish that still has something worth knowing about. */
export const WarningsOnly: Story = {
  args: {
    findings: [
      aBillingFinding({
        severity: 'Warning',
        code: 'billing.cess-only-code',
        message: 'The tax code CESS_ONLY carries cess with no other component.',
        target: 'taxCodes[CESS_ONLY]',
      }),
    ],
  },
}

/** The pseudo-locale, at about 40% growth — the reflow floor of 1.4.10, in a 320 px pane. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  args: {
    findings: [
      aBillingFinding({
        severity: 'Error',
        message: 'The tax code STITCHING_5 carries a CGST without its matching SGST.',
        target: 'taxCodes[STITCHING_5]',
      }),
      aBillingFinding({
        severity: 'Warning',
        code: 'billing.nil-rated-code',
        message: 'The tax code ALTER_0 carries no rate at all.',
        target: 'taxCodes[ALTER_0]',
      }),
    ],
  },
}
