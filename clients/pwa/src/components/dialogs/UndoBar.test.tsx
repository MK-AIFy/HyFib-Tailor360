import { afterEach, describe, expect, it, vi } from 'vitest'
import { act } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { UndoBar } from './UndoBar'

afterEach(() => {
  vi.useRealTimers()
})

/**
 * The clock is faked per test rather than for the whole file, because two of the things being
 * asserted here run on real timers of their own: user-event's pointer sequence, and axe-core, which
 * never resolves if the timers it is waiting on are frozen.
 */
function tick(ms: number) {
  act(() => {
    vi.advanceTimersByTime(ms)
  })
}

describe('UndoBar', () => {
  it('announces what happened, once and politely', () => {
    const { getByRole } = renderWithProviders(
      <UndoBar
        action="Shoulder set to 16 in"
        onExpire={() => undefined}
        onUndo={() => undefined}
      />,
    )

    expect(getByRole('status')).toHaveTextContent(
      'Shoulder set to 16 in. Undo is available for a moment.',
    )
  })

  it('keeps the ticking countdown out of the live region', () => {
    // A live region that updates ten times a second says nothing at all. The countdown is beside
    // the announcement and is aria-hidden.
    const { getByRole, container } = renderWithProviders(
      <UndoBar action="Filters cleared" onExpire={() => undefined} onUndo={() => undefined} />,
    )

    expect(getByRole('status').textContent).not.toMatch(/second/)
    expect(container.querySelector('.undo-bar__countdown')).toHaveAttribute('aria-hidden', 'true')
  })

  it('undoes when asked', async () => {
    const onUndo = vi.fn()
    const { getByRole } = renderWithProviders(
      <UndoBar action="Filters cleared" onExpire={() => undefined} onUndo={onUndo} />,
    )

    await userEvent.click(getByRole('button', { name: 'Undo' }))

    expect(onUndo).toHaveBeenCalledTimes(1)
  })

  it('closes the window after three seconds', () => {
    vi.useFakeTimers()
    const onExpire = vi.fn()
    renderWithProviders(
      <UndoBar action="Filters cleared" onExpire={onExpire} onUndo={() => undefined} />,
    )

    tick(2900)
    expect(onExpire).not.toHaveBeenCalled()

    tick(200)
    expect(onExpire).toHaveBeenCalledTimes(1)
  })

  it('takes a different window when one is asked for', () => {
    vi.useFakeTimers()
    const onExpire = vi.fn()
    renderWithProviders(
      <UndoBar
        action="Filters cleared"
        durationMs={10000}
        onExpire={onExpire}
        onUndo={() => undefined}
      />,
    )

    tick(5000)

    expect(onExpire).not.toHaveBeenCalled()
  })

  it('holds the window open while focus is inside it', () => {
    vi.useFakeTimers()
    // 2.2.1 Timing Adjustable, and checklist item A11Y-66: the control cannot vanish from under the
    // hand — or the Tab — that has reached it.
    const onExpire = vi.fn()
    const { getByRole } = renderWithProviders(
      <UndoBar action="Filters cleared" onExpire={onExpire} onUndo={() => undefined} />,
    )

    act(() => {
      getByRole('button', { name: 'Undo' }).focus()
    })
    tick(10000)

    expect(onExpire).not.toHaveBeenCalled()
  })

  it('says that it is being held, rather than merely stopping', () => {
    const { getByRole, container } = renderWithProviders(
      <UndoBar action="Filters cleared" onExpire={() => undefined} onUndo={() => undefined} />,
    )

    act(() => {
      getByRole('button', { name: 'Undo' }).focus()
    })

    expect(container.querySelector('.undo-bar__countdown')).toHaveTextContent(
      'Held open while you are on it.',
    )
  })

  it('runs again once focus leaves', () => {
    vi.useFakeTimers()
    const onExpire = vi.fn()
    const { getByRole } = renderWithProviders(
      <UndoBar action="Filters cleared" onExpire={onExpire} onUndo={() => undefined} />,
    )
    const undo = getByRole('button', { name: 'Undo' })

    act(() => {
      undo.focus()
    })
    tick(10000)
    act(() => {
      undo.blur()
    })
    tick(3100)

    expect(onExpire).toHaveBeenCalledTimes(1)
  })

  it('writes the remaining fraction as a custom property rather than an inline style', () => {
    vi.useFakeTimers()
    // The one value here that cannot be static, written through the CSSOM — the sanctioned escape
    // hatch. There is no `style` attribute in JSX anywhere in this application.
    const { container } = renderWithProviders(
      <UndoBar action="Filters cleared" onExpire={() => undefined} onUndo={() => undefined} />,
    )
    const bar = container.querySelector('.undo-bar')

    tick(1500)

    expect(bar?.getAttribute('style')).toContain('--undo-remaining')
    expect(bar?.getAttribute('style')).not.toMatch(/(^|;)\s*(width|transform|height)\s*:/)
  })

  it('reads its own words from the catalogue, which the pseudo-locale makes visible', () => {
    const { container } = renderWithProviders(
      <UndoBar action="Filters cleared" onExpire={() => undefined} onUndo={() => undefined} />,
      { locale: 'en-XA' },
    )

    expect(container.textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(
      <UndoBar
        action="Shoulder set to 16 in"
        onExpire={() => undefined}
        onUndo={() => undefined}
      />,
    )

    await expectNoAccessibilityViolations(container)
  })
})
