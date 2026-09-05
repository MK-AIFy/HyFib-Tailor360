import { describe, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { RetryableError } from './RetryableError'

describe('RetryableError', () => {
  it('interrupts, because it has stopped the person mid-task', () => {
    const { getByRole } = renderWithProviders(
      <RetryableError action="Recording the payment" onRetry={() => undefined} />,
    )

    // One of the two things that earn an assertive region in this product
    // (docs/nfr/accessibility-localisation.md section 6). A Cashier who has just taken a customer's
    // card cannot be left to notice a polite region on their own.
    expect(getByRole('alert')).toBeInTheDocument()
  })

  it('names what did not go through', () => {
    const { getByRole } = renderWithProviders(
      <RetryableError action="Recording the payment" onRetry={() => undefined} />,
    )

    expect(getByRole('alert')).toHaveTextContent('Recording the payment did not go through')
  })

  it.each([
    [503, 'The shop system could not finish this'],
    [429, 'Too many requests were sent at once'],
    [409, 'Somebody else changed this while you were working on it'],
    [408, 'The shop system took too long to answer'],
  ])('turns %i into a sentence a person can act on', (status, expected) => {
    const { getByRole } = renderWithProviders(
      <RetryableError
        action="Confirming the order"
        onRetry={() => undefined}
        problem={{ status }}
      />,
    )

    expect(getByRole('alert')).toHaveTextContent(expected)
  })

  it('says a request that never got an answer dropped, rather than blaming the server', () => {
    const { getByRole } = renderWithProviders(
      <RetryableError action="Confirming the order" cause="network" onRetry={() => undefined} />,
    )

    expect(getByRole('alert')).toHaveTextContent(
      'The connection dropped before the shop system answered',
    )
  })

  it('promises that trying again cannot make it happen twice', () => {
    // The promise depends on the caller resending with the same Idempotency-Key, which is why the
    // prop documentation states it as a contract rather than a suggestion.
    const { getByRole } = renderWithProviders(
      <RetryableError action="Recording the payment" onRetry={() => undefined} />,
    )

    expect(getByRole('alert')).toHaveTextContent('cannot end up happening twice')
    expect(getByRole('alert')).toHaveTextContent('Nothing you have typed has been lost.')
  })

  it('shows the correlation identifier so a support call has something to go on', () => {
    const { getByRole } = renderWithProviders(
      <RetryableError
        action="Posting the invoice"
        onRetry={() => undefined}
        problem={{ status: 500, correlationId: '01JAV7Q0YQ' }}
      />,
    )

    expect(getByRole('alert')).toHaveTextContent('quote this reference: 01JAV7Q0YQ')
  })

  it('shows a server sentence that reads like a sentence', () => {
    const { getByRole } = renderWithProviders(
      <RetryableError
        action="Posting the invoice"
        onRetry={() => undefined}
        problem={{ status: 409, detail: 'This invoice was already posted at 4:31 PM.' }}
      />,
    )

    expect(getByRole('alert')).toHaveTextContent('This invoice was already posted at 4:31 PM.')
  })

  it('drops a server sentence that is really a stack trace', () => {
    // Section 8.2: never a stack, never a code. The rule is a function in problemDetails.ts, and
    // this is the proof it is wired in rather than merely written down.
    const { getByRole } = renderWithProviders(
      <RetryableError
        action="Posting the invoice"
        onRetry={() => undefined}
        problem={{
          status: 500,
          detail: 'Object reference not set\n   at HyFib.Billing.Post(Invoice invoice)',
        }}
      />,
    )

    const alert = getByRole('alert')
    expect(alert).not.toHaveTextContent('Object reference not set')
    // The person still gets a sentence, which is the point of dropping the other one.
    expect(alert).toHaveTextContent('The shop system could not finish this')
  })

  it('retries when asked', async () => {
    const onRetry = vi.fn()
    const { getByRole } = renderWithProviders(
      <RetryableError action="Recording the payment" onRetry={onRetry} />,
    )

    await userEvent.click(getByRole('button', { name: 'Try again' }))

    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('swallows a second press while a retry is in flight, without losing the tab stop', async () => {
    const onRetry = vi.fn()
    const { getByRole } = renderWithProviders(
      <RetryableError action="Recording the payment" onRetry={onRetry} retrying />,
    )

    const button = getByRole('button', { name: /Trying again/ })
    await userEvent.click(button)

    expect(onRetry).not.toHaveBeenCalled()
    // Not `disabled`: a control that drops out of the tab order under the focus is checklist item
    // A11Y-66, and on a phone it costs a whole re-tab, one-handed.
    expect(button).toHaveAttribute('aria-disabled', 'true')
    expect(button).not.toHaveAttribute('disabled')
  })

  it('reads its own words from the catalogue, which the pseudo-locale makes visible', () => {
    const { container } = renderWithProviders(
      <RetryableError action="Recording the payment" onRetry={() => undefined} />,
      { locale: 'en-XA' },
    )

    expect(container.textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(
      <RetryableError
        action="Recording the payment"
        onRetry={() => undefined}
        problem={{ status: 500, correlationId: '01JAV7Q0YQ', detail: 'The branch is closed.' }}
      />,
    )

    await expectNoAccessibilityViolations(container)
  })
})
