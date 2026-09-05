import { afterEach, describe, expect, it } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useNetworkState } from './useNetworkState'

/**
 * The connection is a fact about the document, so the store behind the hook is module state. Every
 * test therefore leaves the connection as it found it.
 */
afterEach(() => {
  act(() => {
    window.dispatchEvent(new Event('online'))
  })
})

function goOffline() {
  act(() => {
    window.dispatchEvent(new Event('offline'))
  })
}

function goOnline() {
  act(() => {
    window.dispatchEvent(new Event('online'))
  })
}

describe('useNetworkState', () => {
  it('starts from what the device reports', () => {
    const { result } = renderHook(() => useNetworkState())

    expect(result.current.online).toBe(true)
    expect(result.current.restored).toBe(false)
  })

  it('follows the connection going and coming back', () => {
    const { result } = renderHook(() => useNetworkState())

    goOffline()
    expect(result.current.online).toBe(false)
    // Nothing to celebrate while it is still gone.
    expect(result.current.restored).toBe(false)

    goOnline()
    expect(result.current.online).toBe(true)
    // A11Y-OF-01: the return is stated rather than being the mere absence of a warning.
    expect(result.current.restored).toBe(true)
  })

  it('keeps the restored message until it is acknowledged, with no timer on it', () => {
    const { result } = renderHook(() => useNetworkState())

    goOffline()
    goOnline()
    expect(result.current.restored).toBe(true)

    act(() => {
      result.current.acknowledgeRestored()
    })

    expect(result.current.restored).toBe(false)
  })

  it('drops the restored message when the connection goes again', () => {
    const { result } = renderHook(() => useNetworkState())

    goOffline()
    goOnline()
    goOffline()

    // Two connection messages on one screen is one too many; the offline one is the one that matters.
    expect(result.current.restored).toBe(false)
    expect(result.current.online).toBe(false)
  })

  it('does not greet a component mounted afterwards with news it never saw', () => {
    const first = renderHook(() => useNetworkState())
    goOffline()
    goOnline()
    expect(first.result.current.restored).toBe(true)

    const later = renderHook(() => useNetworkState())

    expect(later.result.current.restored).toBe(false)
  })

  it('agrees with itself across two consumers', () => {
    // A banner in the shell and a blocked action in a form must not disagree about the connection.
    const banner = renderHook(() => useNetworkState())
    const action = renderHook(() => useNetworkState())

    goOffline()

    expect(banner.result.current.online).toBe(false)
    expect(action.result.current.online).toBe(false)
  })
})
