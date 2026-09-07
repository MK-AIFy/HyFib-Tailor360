import { describe, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { Button } from '../primitives/Button'
import { EmptyState } from './EmptyState'
import { ErrorState } from './ErrorState'
import { Forbidden } from './Forbidden'
import { LoadingState } from './LoadingState'

/**
 * The five states DoD item 7 requires a story for. Loading, empty, error and forbidden are here;
 * offline is `NetworkStatusBanner` and `OfflineBlockedAction`, which have their own tests.
 *
 * Section 4.12 of docs/nfr/a11y-checklist.md asks the same three questions of every one of them, so
 * the tests are grouped by the question rather than by the component wherever that reads better.
 */
describe('EmptyState', () => {
  it('says what is empty and what to do next', () => {
    // A11Y-88: an empty state that says only "no results" reads exactly like a screen that has not
    // finished loading, and an empty state is where a screen-reader journey usually stops.
    const { getByRole } = renderWithProviders(
      <EmptyState title="No jobs in this queue">
        Jobs appear here when a Tailor Master assigns them.
      </EmptyState>,
    )

    expect(getByRole('heading', { level: 2, name: 'No jobs in this queue' })).toBeInTheDocument()
    expect(getByRole('status')).toHaveTextContent(
      'Jobs appear here when a Tailor Master assigns them.',
    )
  })

  it('says that nothing has gone wrong, when the caller supplies nothing', () => {
    const { getByRole } = renderWithProviders(<EmptyState />)

    expect(getByRole('status')).toHaveTextContent('Nothing has gone wrong.')
  })

  it('is read in document order when it was there from the first paint', () => {
    const { queryByRole } = renderWithProviders(<EmptyState live="off" />)

    // A live region would announce it a second time.
    expect(queryByRole('status')).toBeNull()
  })

  it('carries the control that answers it', async () => {
    const onAdd = vi.fn()
    const { getByRole } = renderWithProviders(
      <EmptyState actions={<Button onClick={onAdd}>Add the first customer</Button>} />,
    )

    await userEvent.click(getByRole('button', { name: 'Add the first customer' }))

    expect(onAdd).toHaveBeenCalledTimes(1)
  })

  it('takes the heading level from the caller, which knows the outline', () => {
    const { getByRole } = renderWithProviders(<EmptyState headingLevel={3} title="No results" />)

    expect(getByRole('heading', { level: 3, name: 'No results' })).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(
      <EmptyState actions={<Button>Clear the filters</Button>} title="No results">
        No orders match these filters.
      </EmptyState>,
    )

    await expectNoAccessibilityViolations(container)
  })
})

describe('LoadingState', () => {
  it('says what is loading, not merely that something is', () => {
    // A11Y-44 wants the busy state announced when it starts. "Loading" alone tells a person who
    // cannot see the screen nothing about which part of it is busy.
    const { getByRole } = renderWithProviders(<LoadingState what="the delivery queue" />)

    expect(getByRole('status')).toHaveTextContent('Loading the delivery queue…')
  })

  it('marks itself busy, so its contents are not read as final', () => {
    const { getByRole } = renderWithProviders(<LoadingState what="this customer" />)

    expect(getByRole('status')).toHaveAttribute('aria-busy', 'true')
  })

  it('says it in words as well as in a turning glyph', () => {
    // A11Y-73: with reduce-motion on, nothing is lost. The sentence is what remains.
    const { getByRole } = renderWithProviders(<LoadingState what="the workboard" />)

    expect(getByRole('heading', { level: 2 })).toHaveTextContent('Loading the workboard…')
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(<LoadingState what="the delivery queue" />)

    await expectNoAccessibilityViolations(container)
  })
})

describe('ErrorState', () => {
  it('says what could not be shown and what to do', () => {
    const { getByRole } = renderWithProviders(<ErrorState />)

    expect(getByRole('heading', { level: 2 })).toHaveTextContent('This could not be shown')
    expect(getByRole('status')).toHaveTextContent(
      'tell your shop administrator if it keeps happening',
    )
  })

  it('repeats a read when asked, which is always safe', async () => {
    const onRetry = vi.fn()
    const { getByRole } = renderWithProviders(<ErrorState onRetry={onRetry} />)

    await userEvent.click(getByRole('button', { name: 'Try again' }))

    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('offers no retry when the caller has nothing safe to repeat', () => {
    const { queryByRole } = renderWithProviders(<ErrorState />)

    expect(queryByRole('button')).toBeNull()
  })

  it('announces politely rather than interrupting', () => {
    // A tile that did not load is neither a rejected scan nor a failure that stopped the person.
    const { queryByRole } = renderWithProviders(<ErrorState />)

    expect(queryByRole('alert')).toBeNull()
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(<ErrorState onRetry={() => undefined} />)

    await expectNoAccessibilityViolations(container)
  })
})

describe('Forbidden', () => {
  it('says this role may not, and names what was refused', () => {
    const { getByRole } = renderWithProviders(
      <Forbidden action="Taking a payment" onBack={() => undefined} />,
    )

    expect(getByRole('heading', { level: 2 })).toHaveTextContent(
      'You do not have permission to do this',
    )
    expect(getByRole('status')).toHaveTextContent(
      'Taking a payment is not part of what your role can do.',
    )
  })

  it('names who can, as a sentence rather than a comma-joined list', () => {
    // A11Y-89 requires the forbidden state to name who can. Intl.ListFormat rather than a join,
    // because a translated list punctuated by hand stops reading like a sentence.
    const { getByRole } = renderWithProviders(
      <Forbidden
        action="Taking a payment"
        allowedRoles={['Cashier', 'Branch Manager']}
        onBack={() => undefined}
      />,
    )

    expect(getByRole('status')).toHaveTextContent('This is done by: Cashier and Branch Manager.')
  })

  it('sends the person to somebody when it does not know who can', () => {
    const { getByRole } = renderWithProviders(
      <Forbidden action="Approving the variance" onBack={() => undefined} />,
    )

    expect(getByRole('status')).toHaveTextContent('Ask your branch manager who can do this.')
  })

  it('always leaves a keyboard-reachable way back', async () => {
    const onBack = vi.fn()
    const { getByRole } = renderWithProviders(
      <Forbidden action="Taking a payment" onBack={onBack} />,
    )

    const back = getByRole('button', { name: 'Go back' })
    back.focus()
    await userEvent.keyboard('{Enter}')

    expect(onBack).toHaveBeenCalledTimes(1)
  })

  it('reads as an ordinary explanation rather than an alarm', () => {
    // Deny-by-default makes this a normal state a Tailor meets daily, not an error case.
    const { queryByRole } = renderWithProviders(
      <Forbidden action="Taking a payment" onBack={() => undefined} />,
    )

    expect(queryByRole('alert')).toBeNull()
  })

  it('reads its own words from the catalogue, which the pseudo-locale makes visible', () => {
    const { container } = renderWithProviders(
      <Forbidden action="Taking a payment" onBack={() => undefined} />,
      { locale: 'en-XA' },
    )

    expect(container.textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(
      <Forbidden
        action="Taking a payment"
        allowedRoles={['Cashier', 'Owner']}
        onBack={() => undefined}
      >
        <p>Payments are recorded at the counter.</p>
      </Forbidden>,
    )

    await expectNoAccessibilityViolations(container)
  })
})
