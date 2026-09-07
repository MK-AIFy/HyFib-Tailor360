import { useCallback, useEffect, useRef, useState } from 'react'

/** What a screen has, and what it is doing about it. */
export interface AdminResource<T> {
  /** The value, or null while it is being read for the first time or after a failure. */
  readonly value: T | null
  /** Why the read failed, or null. Rendered through `AuthProblemAlert`, never as its message. */
  readonly failure: unknown
  /** True only before anything has arrived. A reload keeps the old value on screen instead. */
  readonly loading: boolean
  /** Reads it again. Safe to call from an event handler. */
  readonly reload: () => void
}

/**
 * Reads something from the administration API, and reads it again on demand.
 *
 * Every administrative screen has the same shape — read on mount, show the five states, read again
 * after a command changed something — and writing that effect eight times is eight chances to forget
 * the cancellation. Three details are worth naming, because they are what a hand-written copy gets
 * wrong and what the lint rules are pointing at when it does:
 *
 *  - **Nothing is set synchronously in the effect body.** `loading` is derived from what the screen
 *    has, not tracked as a third state; setting it on the way in would be a cascading render on every
 *    mount. It also gives the behaviour that is wanted anyway: a reload keeps the value on screen
 *    while the new one is in flight, because blanking it flashes the screen back to a skeleton and
 *    loses the reader's place at the exact moment they are checking what their last command did.
 *  - **The dependency is a key, not an array.** A caller composing an array inline would defeat the
 *    hook rules' static check; a string the caller builds from what the read actually depends on is
 *    something both they and the linter can see.
 *  - **The read is held in a ref.** Callers pass an inline lambda, which is a different function on
 *    every render; depending on it directly would read in a loop. The key decides when to read, the
 *    ref decides what to read with.
 *
 * This is the effect, once. It is not a cache and not a second data-fetching path — server state
 * belongs to the shared query cache when one arrives.
 *
 * @param key What the read depends on, as a string. Changing it reads again.
 * @param read Reads the value. It is given an `AbortSignal` and must pass it to the request.
 */
export function useAdminResource<T>(
  key: string,
  read: (signal: AbortSignal) => Promise<T>,
): AdminResource<T> {
  const [value, setValue] = useState<T | null>(null)
  const [failure, setFailure] = useState<unknown>(null)
  const [reloadToken, setReloadToken] = useState(0)

  const latestRead = useRef(read)
  useEffect(() => {
    latestRead.current = read
  })

  useEffect(() => {
    const controller = new AbortController()
    let cancelled = false

    void latestRead
      .current(controller.signal)
      .then((result) => {
        if (!cancelled) {
          setValue(result)
          setFailure(null)
        }
      })
      .catch((cause: unknown) => {
        if (!cancelled && !(cause instanceof DOMException && cause.name === 'AbortError')) {
          setFailure(cause)
        }
      })

    return () => {
      cancelled = true
      controller.abort()
    }
  }, [key, reloadToken])

  const reload = useCallback(() => {
    setReloadToken((previous) => previous + 1)
  }, [])

  return { value, failure, loading: value === null && failure === null, reload }
}
