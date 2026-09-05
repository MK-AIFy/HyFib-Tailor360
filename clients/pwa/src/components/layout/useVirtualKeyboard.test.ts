import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { KEYBOARD_THRESHOLD_PX, useVirtualKeyboard } from './useVirtualKeyboard'

/**
 * jsdom implements no `visualViewport` at all, which is itself one of the cases the hook has to
 * survive, so the stub is installed per test rather than globally.
 */
interface ViewportStub {
  resize: (height: number) => void
}

const listeners = new Set<() => void>()

function installVisualViewport(height: number): ViewportStub {
  let current = height
  const viewport = {
    get height() {
      return current
    },
    addEventListener: (_type: string, listener: () => void) => {
      listeners.add(listener)
    },
    removeEventListener: (_type: string, listener: () => void) => {
      listeners.delete(listener)
    },
  }

  Object.defineProperty(window, 'visualViewport', {
    configurable: true,
    value: viewport,
  })

  return {
    resize(next: number) {
      current = next
      act(() => {
        for (const listener of [...listeners]) {
          listener()
        }
      })
    },
  }
}

afterEach(() => {
  listeners.clear()
  Object.defineProperty(window, 'visualViewport', { configurable: true, value: undefined })
})

describe('useVirtualKeyboard', () => {
  it('reports the keyboard closed where the browser cannot say', () => {
    // No visualViewport: an older browser, jsdom, a server render. A bottom bar that stays visible
    // is a smaller problem than one that disappears when it should not.
    const { result } = renderHook(() => useVirtualKeyboard())

    expect(result.current).toEqual({ open: false, height: 0 })
  })

  it('reports the keyboard open once it covers more than a browser chrome would', () => {
    const viewport = installVisualViewport(window.innerHeight)
    const { result } = renderHook(() => useVirtualKeyboard())

    expect(result.current.open).toBe(false)

    viewport.resize(window.innerHeight - 320)

    expect(result.current.open).toBe(true)
    expect(result.current.height).toBe(320)
  })

  it('ignores a collapsing browser URL bar', () => {
    // The URL bar on the reference devices of support-matrix.md section 2 is under 120 CSS px. A bar
    // that scrolls away must not take the navigation with it.
    const viewport = installVisualViewport(window.innerHeight)
    renderHook(() => useVirtualKeyboard())

    const { result } = renderHook(() => useVirtualKeyboard())
    viewport.resize(window.innerHeight - (KEYBOARD_THRESHOLD_PX - 30))

    expect(result.current.open).toBe(false)
  })

  it('reports the keyboard closed again when it is dismissed', () => {
    const viewport = installVisualViewport(window.innerHeight)
    const { result } = renderHook(() => useVirtualKeyboard())

    viewport.resize(window.innerHeight - 300)
    expect(result.current.open).toBe(true)

    viewport.resize(window.innerHeight)
    expect(result.current).toEqual({ open: false, height: 0 })
  })
})
