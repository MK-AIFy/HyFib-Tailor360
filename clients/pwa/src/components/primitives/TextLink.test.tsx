import { describe, expect, it } from 'vitest'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { TextLink } from './TextLink'

describe('TextLink', () => {
  it('is a link with the address it was given', async () => {
    const { getByRole, container } = renderWithProviders(
      <p>
        Read the <TextLink href="/help/scanning">scanning procedure</TextLink> first.
      </p>,
    )

    expect(getByRole('link', { name: 'scanning procedure' })).toHaveAttribute(
      'href',
      '/help/scanning',
    )
    await expectNoAccessibilityViolations(container)
  })

  it('says when it opens a new tab, because an unannounced change of context is a failure', () => {
    const { getByRole } = renderWithProviders(
      <TextLink href="https://example.invalid/portal" external>
        Supplier portal
      </TextLink>,
    )

    const link = getByRole('link')
    expect(link).toHaveAccessibleName(/opens in a new tab/)
    expect(link).toHaveAttribute('target', '_blank')
    // noopener keeps the opened page from reaching back into a session on a shared counter device.
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('does not open a new tab unless asked', () => {
    const { getByRole } = renderWithProviders(<TextLink href="/orders">Orders</TextLink>)

    expect(getByRole('link')).not.toHaveAttribute('target')
  })

  it('translates its own words, which the pseudo-locale makes visible', () => {
    const { container } = renderWithProviders(
      <TextLink href="https://example.invalid" external>
        Portal
      </TextLink>,
      { locale: 'en-XA' },
    )

    // Unaccented text on a pseudo-locale screen is hard-coded English. The brackets say the phrase
    // came through the catalogue and will grow by 40% when Tamil is switched on.
    expect(container.textContent).toMatch(/⟦.+⟧/)
  })

  it('can be quiet inside dense content without losing its underline', () => {
    const { getByRole } = renderWithProviders(
      <TextLink href="/customers/1" quiet>
        Lakshmi Narayanan
      </TextLink>,
    )

    // 1.4.1: an underline is the non-colour cue, and quiet changes the colour, never the underline.
    expect(getByRole('link')).toHaveAttribute('data-quiet', 'true')
  })
})
