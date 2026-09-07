import { describe, expect, it } from 'vitest'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { StatusBadge } from './StatusBadge'
import { STATUS_KINDS, STATUS_PRESENTATION } from './statuses'
import { enIN } from '../../i18n/en-IN'

describe('StatusBadge', () => {
  it.each(STATUS_KINDS)('renders %s as a word, never as a colour alone', (status) => {
    const { container } = renderWithProviders(<StatusBadge status={status} />)

    // 1.4.1 Use of Colour, and the verification row for it asks for a Storybook story per status.
    // This is the same assertion made once per status in a run that costs nothing.
    const expected = enIN[STATUS_PRESENTATION[status].messageKey]
    expect(container.textContent).toContain(expected)
  })

  it.each(STATUS_KINDS)('renders a glyph beside the %s word', (status) => {
    const { container } = renderWithProviders(<StatusBadge status={status} />)

    const svg = container.querySelector('svg')
    expect(svg).not.toBeNull()
    // The glyph is decoration: the word is what carries the status.
    expect(svg).toHaveAttribute('aria-hidden', 'true')
  })

  it('gives statuses that appear in the same list different glyph shapes', () => {
    // Paid beside unpaid, QC passed beside QC failed, cancelled beside QC failed. If any pair shared
    // a shape, the word would be doing all the work and the icon would be decoration pretending to
    // be information.
    const pairs: readonly (readonly [
      (typeof STATUS_KINDS)[number],
      (typeof STATUS_KINDS)[number],
    ])[] = [
      ['paid', 'unpaid'],
      ['qc-passed', 'qc-failed'],
      ['qc-failed', 'cancelled'],
      ['ready', 'delivered'],
      ['due-soon', 'overdue'],
    ]

    for (const [left, right] of pairs) {
      expect(STATUS_PRESENTATION[left].icon).not.toBe(STATUS_PRESENTATION[right].icon)
    }
  })

  it('shows a qualifier after the status word', () => {
    const { container } = renderWithProviders(<StatusBadge status="overdue" detail="4 days" />)

    expect(container.textContent).toContain('Overdue')
    expect(container.textContent).toContain('4 days')
  })

  it('is not a live region: a badge that announced itself would talk over the screen', () => {
    const { container } = renderWithProviders(<StatusBadge status="ready" />)
    const badge = container.querySelector('.status-badge')

    // 4.1.3 is satisfied by whatever changed the status — the scan result, the save — not by the
    // badge re-announcing itself on every render.
    expect(badge).not.toHaveAttribute('aria-live')
    expect(badge).not.toHaveAttribute('role')
  })

  it('translates the status word', () => {
    const { container } = renderWithProviders(<StatusBadge status="held" />, { locale: 'ta-IN' })

    expect(container.textContent).toContain('தடை')
  })

  it('grows with the pseudo-locale, which is what the 40% rule is proved against', () => {
    const { container } = renderWithProviders(<StatusBadge status="rework" />, { locale: 'en-XA' })

    expect(container.textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(
      <p>
        <StatusBadge status="overdue" detail="4 days" prominent />
      </p>,
    )

    await expectNoAccessibilityViolations(container)
  })
})
