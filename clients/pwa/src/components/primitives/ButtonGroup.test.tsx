import { describe, expect, it } from 'vitest'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { Button } from './Button'
import { ButtonGroup } from './ButtonGroup'

describe('ButtonGroup', () => {
  it('separates a destructive action from the actions beside it', async () => {
    const { container, getByRole } = renderWithProviders(
      <ButtonGroup destructiveAction={<Button variant="danger">Cancel order</Button>}>
        <Button variant="primary">Confirm order</Button>
        <Button>Save as draft</Button>
      </ButtonGroup>,
    )

    // Section 5 rule 2 and checklist item A11Y-69: Dispatch does not sit beside Cancel order. The
    // separation is structural — a wrapper the CSS gives a 24 px margin — rather than a spacing
    // decision each screen has to remember.
    const separated = container.querySelector('.button-group__separated')
    expect(separated).not.toBeNull()
    expect(separated).toContainElement(getByRole('button', { name: 'Cancel order' }))
    await expectNoAccessibilityViolations(container)
  })

  it('renders the destructive action last, after everything it must not be confused with', () => {
    const { container } = renderWithProviders(
      <ButtonGroup destructiveAction={<Button variant="danger">Delete evidence</Button>}>
        <Button>Add evidence</Button>
      </ButtonGroup>,
    )

    const names = Array.from(container.querySelectorAll('button')).map(
      (button) => button.textContent,
    )
    expect(names).toEqual(['Add evidence', 'Delete evidence'])
  })

  it('carries the spacing class for the size it is given', () => {
    const { container } = renderWithProviders(
      <ButtonGroup size="primary">
        <Button size="primary">Scan</Button>
      </ButtonGroup>,
    )

    // 12 px between primary shop-floor actions, 8 px between standard ones — the group is where
    // that number is actually spent.
    expect(container.querySelector('.button-group')).toHaveAttribute('data-size', 'primary')
  })

  it('is a named group only when it has a name', () => {
    const { queryByRole, rerender } = renderWithProviders(
      <ButtonGroup>
        <Button>Print</Button>
      </ButtonGroup>,
    )

    // An unnamed group role adds a level to the screen reader's tree and says nothing.
    expect(queryByRole('group')).toBeNull()

    rerender(
      <ButtonGroup label="Job card actions">
        <Button>Print</Button>
      </ButtonGroup>,
    )

    expect(queryByRole('group', { name: 'Job card actions' })).not.toBeNull()
  })

  it('stacks vertically when asked, for a bottom sheet footer', () => {
    const { container } = renderWithProviders(
      <ButtonGroup orientation="vertical">
        <Button fullWidth>Take payment</Button>
      </ButtonGroup>,
    )

    expect(container.querySelector('.button-group')).toHaveAttribute('data-orientation', 'vertical')
  })
})
