import { describe, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { Alert } from './Alert'
import { Button } from './Button'
import { ALERT_TONES } from './variants'

describe('Alert', () => {
  it.each(ALERT_TONES)('states the %s severity in words as well as in colour', (tone) => {
    const { container } = renderWithProviders(<Alert tone={tone}>Something happened.</Alert>)

    // 1.4.1: the tone chooses a colour pair, the glyph gives a shape and this gives the word. Read
    // the alert in greyscale and the severity is still there.
    const words: Record<typeof tone, string> = {
      info: 'Information',
      success: 'Done',
      warning: 'Warning',
      danger: 'Problem',
    }
    expect(container.textContent).toContain(words[tone])
  })

  it('is not a live region by default, so an alert already on screen is not announced twice', () => {
    const { container } = renderWithProviders(<Alert tone="info">Nothing to do yet.</Alert>)
    const alert = container.querySelector('.alert')

    expect(alert).not.toHaveAttribute('role')
  })

  it('announces politely when it appeared after the screen did', () => {
    const { getByRole } = renderWithProviders(
      <Alert tone="success" live="polite">
        Measurements saved.
      </Alert>,
    )

    // 4.1.3 Status Messages. role="status" carries an implicit aria-live="polite".
    expect(getByRole('status')).toBeInTheDocument()
  })

  it('interrupts only when it has to', () => {
    const { getByRole } = renderWithProviders(
      <Alert tone="danger" live="assertive" title="Scan rejected">
        This label belongs to another branch.
      </Alert>,
    )

    // The two things that earn an assertive region: a rejected scan naming which rule failed, and an
    // error that has stopped the person mid-task (accessibility-localisation.md section 6).
    expect(getByRole('alert')).toBeInTheDocument()
  })

  it('is named by its title, so a screen reader can find it again', () => {
    const { getByRole } = renderWithProviders(
      <Alert tone="warning" live="polite" title="Working offline">
        Scans are being queued.
      </Alert>,
    )

    expect(getByRole('status', { name: 'Working offline' })).toBeInTheDocument()
  })

  it('has no dismiss control unless one is asked for, because some messages must not be dismissed', () => {
    const { queryByRole } = renderWithProviders(
      <Alert tone="danger">The connection has gone. This will not be queued.</Alert>,
    )

    // Section 8.3 calls the network banner "persistent, non-dismissible": a blocked action stays on
    // screen until the condition that blocked it clears.
    expect(queryByRole('button', { name: 'Dismiss this message' })).toBeNull()
  })

  it('dismisses when it is allowed to', async () => {
    const onDismiss = vi.fn()
    const { getByRole } = renderWithProviders(
      <Alert tone="info" onDismiss={onDismiss}>
        A new price list is available.
      </Alert>,
    )

    await userEvent.click(getByRole('button', { name: 'Dismiss this message' }))

    expect(onDismiss).toHaveBeenCalledTimes(1)
  })

  it('carries its own actions', async () => {
    const onRetry = vi.fn()
    const { getByRole } = renderWithProviders(
      <Alert tone="danger" actions={<Button onClick={onRetry}>Try again</Button>}>
        The scan could not be sent.
      </Alert>,
    )

    await userEvent.click(getByRole('button', { name: 'Try again' }))

    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('translates its own words, which the pseudo-locale makes visible', () => {
    const { container } = renderWithProviders(<Alert tone="warning">Queued.</Alert>, {
      locale: 'en-XA',
    })

    expect(container.textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(
      <Alert
        tone="danger"
        live="assertive"
        title="Scan rejected"
        actions={<Button>Re-scan</Button>}
        onDismiss={() => undefined}
      >
        The check character did not match.
      </Alert>,
    )

    await expectNoAccessibilityViolations(container)
  })
})
