import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useVirtualKeyboardOpen } from './useVirtualKeyboardOpen'

/** A stand-in for `window.visualViewport`, which jsdom does not implement. */
function installVisualViewport(height: number) {
  const listeners = new Set<() => void>()
  const viewport = {
    height,
    addEventListener: (_type: string, listener: () => void) => {
      listeners.add(listener)
    },
    removeEventListener: (_type: string, listener: () => void) => {
      listeners.delete(listener)
    },
  }

  vi.stubGlobal('visualViewport', viewport)

  return {
    resizeTo(next: number) {
      viewport.height = next
      act(() => {
        for (const listener of listeners) {
          listener()
        }
      })
    },
    listenerCount: () => listeners.size,
  }
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('useVirtualKeyboardOpen', () => {
  it('reports no keyboard where the browser cannot tell us about one', () => {
    // jsdom, an older browser: the bar simply never hides, which is the safe failure. A visible
    // navigation bar is a smaller problem than a missing one.
    const { result } = renderHook(() => useVirtualKeyboardOpen())

    expect(result.current).toBe(false)
  })

  it('reports a keyboard when the visual viewport shrinks well below the layout viewport', () => {
    const viewport = installVisualViewport(window.innerHeight)
    const { result } = renderHook(() => useVirtualKeyboardOpen())

    expect(result.current).toBe(false)

    viewport.resizeTo(window.innerHeight - 320)

    expect(result.current).toBe(true)
  })

  it('ignores a shrink small enough to be a collapsing URL bar', () => {
    const viewport = installVisualViewport(window.innerHeight)
    const { result } = renderHook(() => useVirtualKeyboardOpen())

    // The URL bar on the reference devices is under 120 px; the threshold is 150 px so that a
    // scroll does not hide the navigation out from under a thumb.
    viewport.resizeTo(window.innerHeight - 100)

    expect(result.current).toBe(false)
  })

  it('reports the keyboard closing again', () => {
    const viewport = installVisualViewport(window.innerHeight)
    const { result } = renderHook(() => useVirtualKeyboardOpen())

    viewport.resizeTo(window.innerHeight - 320)
    viewport.resizeTo(window.innerHeight)

    expect(result.current).toBe(false)
  })

  it('stops listening when it goes away', () => {
    const viewport = installVisualViewport(window.innerHeight)
    const { unmount } = renderHook(() => useVirtualKeyboardOpen())

    expect(viewport.listenerCount()).toBe(1)

    unmount()

    expect(viewport.listenerCount()).toBe(0)
  })
})
