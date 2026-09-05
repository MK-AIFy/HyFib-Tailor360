import { describe, expect, it } from 'vitest'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { Button } from './Button'
import { ButtonGroup } from './ButtonGroup'
import { Card } from './Card'
import { StatusBadge } from './StatusBadge'

describe('Card', () => {
  it('is an article named by its title, so it appears once in the landmark list', () => {
    const { getByRole } = renderWithProviders(
      <Card title="J-CBE01-2627-000512-01" headingLevel={2}>
        Blouse, Aari work
      </Card>,
    )

    expect(getByRole('article', { name: 'J-CBE01-2627-000512-01' })).toBeInTheDocument()
  })

  it('renders the heading at the level the caller asked for', () => {
    const { getByRole } = renderWithProviders(
      <Card title="Payment summary" headingLevel={4}>
        Balance due
      </Card>,
    )

    // The card cannot guess its level: only the caller knows what it nested this inside, and a
    // component that guessed would produce the skipped headings A11Y-25 looks for.
    expect(getByRole('heading', { level: 4, name: 'Payment summary' })).toBeInTheDocument()
  })

  it('is a plain container when it has no title, because an unnamed article says nothing', () => {
    const { queryByRole, container } = renderWithProviders(<Card>Just some content.</Card>)

    expect(queryByRole('article')).toBeNull()
    expect(container.querySelector('.card')).not.toBeNull()
  })

  it('is never itself a control, so the things inside it stay reachable', () => {
    const { container, getByRole } = renderWithProviders(
      <Card
        title="J-CBE01-2627-000512-01"
        headingLevel={3}
        actions={
          <ButtonGroup>
            <Button>Print label</Button>
          </ButtonGroup>
        }
      >
        Due 12-09-2026
      </Card>,
    )

    // A card that were a button could not hold a button. Nesting interactive elements is what makes
    // a row unreachable past its first control.
    const card = container.querySelector('article')
    expect(card?.tagName).toBe('ARTICLE')
    expect(getByRole('button', { name: 'Print label' })).toBeInTheDocument()
  })

  it('puts a status badge on the title row without it becoming the name', () => {
    const { getByRole, container } = renderWithProviders(
      <Card title="J-CBE01-2627-000512-01" headingLevel={3} meta={<StatusBadge status="overdue" />}>
        Blouse
      </Card>,
    )

    expect(getByRole('article')).toHaveAccessibleName('J-CBE01-2627-000512-01')
    expect(container.textContent).toContain('Overdue')
  })

  it('marks the selected card by more than a tint', () => {
    const { getByRole } = renderWithProviders(
      <Card title="Order 4021" headingLevel={3} selected>
        Selected in the master-detail pane
      </Card>,
    )

    // The attribute drives a background *and* an inline rule; 1.4.1 again.
    expect(getByRole('article')).toHaveAttribute('data-selected', 'true')
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(
      <Card
        title="J-CBE01-2627-000512-01"
        headingLevel={2}
        meta={<StatusBadge status="ready" />}
        raised
        actions={
          <ButtonGroup>
            <Button variant="primary">Take payment</Button>
          </ButtonGroup>
        }
      >
        Blouse, Aari work. Due 12-09-2026.
      </Card>,
    )

    await expectNoAccessibilityViolations(container)
  })
})
