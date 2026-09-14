import { describe, expect, it } from 'vitest'
import { expectNoAccessibilityViolations } from '../design-system/testing/axe'
import { renderWithProviders } from '../design-system/testing/renderWithProviders'
import { BillingFindingsList } from './BillingFindingsList'
import { aBillingFinding } from './testing/pricingConfigFixtures'

describe('BillingFindingsList', () => {
  it('shows a success alert when there is no finding', () => {
    const { getByRole } = renderWithProviders(<BillingFindingsList findings={[]} />)

    expect(getByRole('status')).toHaveTextContent('Every check passed.')
  })

  it('separates errors from warnings, each naming its own severity, target and sentence', () => {
    const error = aBillingFinding({
      severity: 'Error',
      code: 'billing.intra-state-pair-missing',
      message: 'The tax code GST5 carries a CGST without its matching SGST.',
      target: 'taxCodes[GST5]',
    })
    const warning = aBillingFinding({
      severity: 'Warning',
      code: 'billing.nil-rated-code',
      message: 'The tax code GST0 carries no rate at all.',
      target: 'taxCodes[GST0]',
    })

    const { getByText, getByRole } = renderWithProviders(
      <BillingFindingsList findings={[error, warning]} />,
    )

    expect(getByRole('status')).toBeInTheDocument()
    expect(
      getByText('The tax code GST5 carries a CGST without its matching SGST.'),
    ).toBeInTheDocument()
    expect(getByText('The tax code GST0 carries no rate at all.')).toBeInTheDocument()
    expect(getByText('taxCodes[GST5]')).toBeInTheDocument()
    expect(getByText('Error')).toBeInTheDocument()
    expect(getByText('Warning')).toBeInTheDocument()
  })

  it('shows a warning from a successful publish as a warning, never as a failure or silently', () => {
    const warning = aBillingFinding({ severity: 'Warning', target: null })

    const { getByText, queryByText } = renderWithProviders(
      <BillingFindingsList findings={[warning]} />,
    )

    expect(getByText('Warning')).toBeInTheDocument()
    expect(queryByText('Error')).not.toBeInTheDocument()
  })

  it('has no accessibility violations with both severities present', async () => {
    const { container } = renderWithProviders(
      <BillingFindingsList
        findings={[
          aBillingFinding({ severity: 'Error' }),
          aBillingFinding({ severity: 'Warning', code: 'billing.nil-rated-code' }),
        ]}
      />,
    )

    await expectNoAccessibilityViolations(container)
  })

  it('has no accessibility violations when empty', async () => {
    const { container } = renderWithProviders(<BillingFindingsList findings={[]} />)

    await expectNoAccessibilityViolations(container)
  })
})
