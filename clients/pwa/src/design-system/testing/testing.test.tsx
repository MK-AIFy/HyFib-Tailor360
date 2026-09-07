import { describe, expect, it } from 'vitest'
import { FormattedMessage } from 'react-intl'
import { expectNoAccessibilityViolations, runAccessibilityChecks } from './axe'
import { renderWithProviders } from './renderWithProviders'
import { readDisplayPreferences } from '../foundations/displayPreferences'

describe('renderWithProviders', () => {
  it('supplies the message catalogue, so a component can format a message', () => {
    const { getByText } = renderWithProviders(<FormattedMessage id="nav.home" />)

    expect(getByText('Home')).toBeInTheDocument()
  })

  it('renders the Tamil catalogue when asked', () => {
    const { getByText } = renderWithProviders(<FormattedMessage id="nav.home" />, {
      locale: 'ta-IN',
    })

    expect(getByText('முகப்பு')).toBeInTheDocument()
  })

  it('renders the pseudo-locale, which is how the 40% growth rule is proved', () => {
    const { container } = renderWithProviders(<FormattedMessage id="nav.home" />, {
      locale: 'en-XA',
    })

    expect(container.textContent).toMatch(/^⟦.+⟧$/)
  })

  it('applies the display preferences to the document, where the token blocks select on them', () => {
    renderWithProviders(<p>Anything</p>, {
      preferences: { theme: 'contrast', textSize: '150', density: 'comfortable' },
    })

    expect(readDisplayPreferences(document.documentElement)).toEqual({
      theme: 'contrast',
      textSize: '150',
      density: 'comfortable',
    })
  })
})

describe('the axe helper', () => {
  it('passes a well-formed control', async () => {
    const { container } = renderWithProviders(<button type="button">Confirm order</button>)

    await expect(expectNoAccessibilityViolations(container)).resolves.toBeUndefined()
  })

  it('catches a control with no accessible name, which is the defect it exists for', async () => {
    const { container } = renderWithProviders(
      <button type="button">
        <span aria-hidden="true">×</span>
      </button>,
    )

    await expect(expectNoAccessibilityViolations(container)).rejects.toThrow(/button-name/)
  })

  it('catches a field with no label, which is what the FieldProps contract exists to prevent', async () => {
    const { container } = renderWithProviders(<input type="text" name="waist" />)

    await expect(expectNoAccessibilityViolations(container)).rejects.toThrow(/label/)
  })

  it('catches a role that is missing a required attribute', async () => {
    const { container } = renderWithProviders(
      <div role="checkbox" tabIndex={0} aria-label="Include lining" />,
    )

    await expect(expectNoAccessibilityViolations(container)).rejects.toThrow(/aria-required-attr/)
  })

  it('leaves the rules jsdom cannot judge switched off rather than reporting a false pass', async () => {
    const { container } = renderWithProviders(<p>Nothing here has a computed colour.</p>)
    const results = await runAccessibilityChecks(container)

    const checked = [...results.passes, ...results.violations, ...results.incomplete].map(
      (result) => result.id,
    )
    expect(checked).not.toContain('color-contrast')
    expect(checked).not.toContain('target-size')
  })
})
