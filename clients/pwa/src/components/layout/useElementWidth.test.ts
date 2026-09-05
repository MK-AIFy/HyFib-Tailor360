import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { readViewportWidth, useElementWidth } from './useElementWidth'

type ObserverCallback = (entries: readonly ResizeObserverEntry[]) => void

const callbacks = new Set<ObserverCallback>()

/** jsdom implements no ResizeObserver, so the observed path needs one installing. */
function installResizeObserver(): void {
  class StubResizeObserver {
    // A field and an assignment rather than a parameter property: the project compiles with
    // erasableSyntaxOnly, which is what keeps the source runnable by a type-stripping runtime.
    private readonly callback: ObserverCallback

    constructor(callback: ObserverCallback) {
      this.callback = callback
      callbacks.add(callback)
    }
    observe(): void {
      /* nothing to observe in jsdom; the test drives the callback directly */
    }
    unobserve(): void {
      /* no-op */
    }
    disconnect(): void {
      callbacks.delete(this.callback)
    }
  }

  Object.defineProperty(window, 'ResizeObserver', {
    configurable: true,
    writable: true,
    value: StubResizeObserver,
  })
}

function reportWidth(inlineSize: number): void {
  const entry = {
    borderBoxSize: [{ inlineSize, blockSize: 100 }],
    contentRect: { width: inlineSize } as DOMRectReadOnly,
  } as unknown as ResizeObserverEntry

  act(() => {
    for (const callback of [...callbacks]) {
      callback([entry])
    }
  })
}

function setViewportWidth(width: number): void {
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: width })
}

afterEach(() => {
  callbacks.clear()
  Object.defineProperty(window, 'ResizeObserver', { configurable: true, value: undefined })
  setViewportWidth(1024)
})

describe('useElementWidth', () => {
  it('falls back to the viewport where there is no observer', () => {
    // Not a second-class path: for a shell that fills the window it gives the same answer, which is
    // every shell this application ships.
    setViewportWidth(360)
    const ref = { current: null }

    const { result } = renderHook(() => useElementWidth(ref))

    expect(result.current).toBe(360)
  })

  it('follows a viewport resize, which is also what a rotation is', () => {
    setViewportWidth(768)
    const ref = { current: null }
    const { result } = renderHook(() => useElementWidth(ref))

    act(() => {
      setViewportWidth(1024)
      window.dispatchEvent(new Event('resize'))
    })

    expect(result.current).toBe(1024)
  })

  it('measures the element rather than the window when it can', () => {
    // The point of measuring the container: a shell inside a split-screen window, a Slide Over pane
    // or a Storybook frame is narrower than the window, and the layout has to follow the container.
    installResizeObserver()
    setViewportWidth(1280)
    const element = document.createElement('div')
    document.body.append(element)
    const ref = { current: element }

    const { result } = renderHook(() => useElementWidth(ref))
    reportWidth(420)

    expect(result.current).toBe(420)
    element.remove()
  })

  it('reads the viewport width without a window of its own', () => {
    setViewportWidth(360)

    expect(readViewportWidth()).toBe(360)
  })
})
