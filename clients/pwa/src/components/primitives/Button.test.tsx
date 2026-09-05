import { describe, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { Button } from './Button'
import { BUTTON_VARIANTS } from './variants'

describe('Button', () => {
  it('renders its label as the accessible name, which is what makes voice control work', () => {
    const { getByRole } = renderWithProviders(<Button>Confirm order</Button>)

    // 2.5.3 Label in Name: the visible words are the accessible name, so "tap Confirm order" works.
    expect(getByRole('button', { name: 'Confirm order' })).toBeInTheDocument()
  })

  it('defaults to type="button", so a button inside a form does not submit it by accident', () => {
    const { getByRole } = renderWithProviders(<Button>Add a note</Button>)

    expect(getByRole('button')).toHaveAttribute('type', 'button')
  })

  it('carries the size as a data attribute, which is what the AL-03 target tokens select on', () => {
    const { getByRole } = renderWithProviders(<Button size="primary">Scan</Button>)

    // primary is the 56 px shop-floor class of accessibility-localisation.md section 5.
    expect(getByRole('button')).toHaveAttribute('data-size', 'primary')
  })

  it('defaults to the standard 44 px size and the secondary emphasis', () => {
    const { getByRole } = renderWithProviders(<Button>Save</Button>)
    const button = getByRole('button')

    expect(button).toHaveAttribute('data-size', 'standard')
    expect(button).toHaveAttribute('data-variant', 'secondary')
  })

  it.each(BUTTON_VARIANTS)('renders the %s variant', async (variant) => {
    const { getByRole, container } = renderWithProviders(
      <Button variant={variant}>Take payment</Button>,
    )

    expect(getByRole('button')).toHaveAttribute('data-variant', variant)
    await expectNoAccessibilityViolations(container)
  })

  it('renders a decorative glyph that assistive technology never announces twice', () => {
    const { getByRole, container } = renderWithProviders(
      <Button iconName="scan">Scan the label</Button>,
    )

    expect(getByRole('button', { name: 'Scan the label' })).toBeInTheDocument()
    expect(container.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
  })

  it('fires on activation', async () => {
    const onClick = vi.fn()
    const { getByRole } = renderWithProviders(<Button onClick={onClick}>Dispatch</Button>)

    await userEvent.click(getByRole('button'))

    expect(onClick).toHaveBeenCalledTimes(1)
  })

  describe('while busy', () => {
    it('stays focusable, so nobody loses their place mid-submit', async () => {
      const { getByRole } = renderWithProviders(<Button busy>Post the invoice</Button>)
      const button = getByRole('button')

      await userEvent.tab()

      // A disabled attribute would drop it out of the tab order and dump focus on the body, which
      // is the failure checklist item A11Y-66 describes.
      expect(button).toHaveFocus()
      expect(button).not.toBeDisabled()
    })

    it('marks itself disabled and busy for assistive technology', () => {
      const { getByRole } = renderWithProviders(<Button busy>Post the invoice</Button>)
      const button = getByRole('button')

      expect(button).toHaveAttribute('aria-disabled', 'true')
      expect(button).toHaveAttribute('aria-busy', 'true')
    })

    it('says so in words, not only in a spinner', () => {
      const { getByRole } = renderWithProviders(<Button busy>Post the invoice</Button>)

      expect(getByRole('button').textContent).toContain('Working')
    })

    it('swallows the second tap', async () => {
      const onClick = vi.fn()
      const { getByRole } = renderWithProviders(
        <Button busy onClick={onClick}>
          Post the invoice
        </Button>,
      )

      await userEvent.click(getByRole('button'))

      expect(onClick).not.toHaveBeenCalled()
    })
  })

  it('does not act when unavailable, and keeps its place in the tab order', async () => {
    const onClick = vi.fn()
    const { getByRole } = renderWithProviders(
      <Button unavailable onClick={onClick}>
        Approve the variance
      </Button>,
    )

    await userEvent.click(getByRole('button'))

    expect(onClick).not.toHaveBeenCalled()
    expect(getByRole('button')).toHaveAttribute('aria-disabled', 'true')
    expect(getByRole('button')).not.toBeDisabled()
  })

  it('takes its own strings from the catalogue, which the pseudo-locale proves', () => {
    const { getByRole } = renderWithProviders(<Button busy>Post</Button>, { locale: 'en-XA' })

    // Unaccented text on a pseudo-locale screen is hard-coded English; the brackets are what say the
    // busy label came through the catalogue and will grow when Tamil is switched on.
    expect(getByRole('button').textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(
      <Button variant="primary" size="primary" iconName="check">
        Complete the phase
      </Button>,
    )

    await expectNoAccessibilityViolations(container)
  })
})
