import { useCallback, useMemo, useState, useSyncExternalStore } from 'react'

/**
 * Whether the device believes it has a connection, and whether it has just got one back.
 *
 * ## What this can and cannot know
 *
 * `navigator.onLine` reports the **link**, not reachability. It is true on a captive portal in a
 * hotel, true when the shop's router is up but its uplink is down, and true for the two or three
 * seconds after a phone leaves the workshop's Wi-Fi. So this hook is deliberately only half of the
 * network story: it is the half that can be known before a request is made, and it drives the
 * persistent banner and the blocked-action state. The other half — a request that was believed to be
 * possible and failed anyway — is `RetryableError`, which is reached from the request itself.
 *
 * Treating `onLine === true` as a promise would produce the worst failure this product can have on a
 * counter: an action that looks queued and never arrives. Nothing in this design system queues
 * anything; #51 owns the bounded offline queue, and until then every blocked action says plainly
 * that it will not be sent later.
 *
 * ## Why `restored` exists
 *
 * Checklist item A11Y-OF-01 requires the offline banner to stay until the connection returns, and a
 * banner that simply vanishes tells a person nothing: they were holding a garment and looking at the
 * machine, not the screen. `restored` is true from the moment the connection comes back until the
 * person acknowledges it, so the return is stated rather than merely being the absence of a warning.
 * It is not a toast, and there is no timer on it.
 */
export interface NetworkState {
  /** True when the device reports a link. See the caveat above: it is a hint, not a promise. */
  readonly online: boolean
  /** True after the connection has returned, until `acknowledgeRestored` is called. */
  readonly restored: boolean
  /** Dismisses the "connection returned" message. The offline message cannot be dismissed. */
  readonly acknowledgeRestored: () => void
}

/**
 * The connection as the document sees it.
 *
 * Held in one module-level snapshot rather than per hook instance, for two reasons. Whether the
 * connection has dropped *in this session* is a fact about the document, not about a component, and
 * two banners on one screen disagreeing about it would be worse than either. And an external store
 * is what lets `useSyncExternalStore` do the subscribing: the alternative is an effect that calls
 * `setState`, which produces a cascading render on every connection change and which the React lint
 * rules rightly refuse.
 */
interface NetworkSnapshot {
  readonly online: boolean
  /** Incremented each time the connection comes back after having gone. Zero means never lost. */
  readonly restoredCount: number
}

let snapshot: NetworkSnapshot = { online: true, restoredCount: 0 }
let initialised = false
const listeners = new Set<() => void>()

function publish(next: NetworkSnapshot): void {
  snapshot = next
  for (const listener of listeners) {
    listener()
  }
}

function handleOffline(): void {
  if (!snapshot.online) {
    return
  }
  publish({ online: false, restoredCount: snapshot.restoredCount })
}

function handleOnline(): void {
  if (snapshot.online) {
    return
  }
  publish({ online: true, restoredCount: snapshot.restoredCount + 1 })
}

function subscribe(onStoreChange: () => void): () => void {
  if (listeners.size === 0) {
    window.addEventListener('offline', handleOffline)
    window.addEventListener('online', handleOnline)
  }
  listeners.add(onStoreChange)

  return () => {
    listeners.delete(onStoreChange)
    if (listeners.size === 0) {
      window.removeEventListener('offline', handleOffline)
      window.removeEventListener('online', handleOnline)
    }
  }
}

function getSnapshot(): NetworkSnapshot {
  if (!initialised) {
    initialised = true
    snapshot = { online: navigator.onLine, restoredCount: 0 }
  }
  return snapshot
}

/**
 * The server-render snapshot.
 *
 * There is no server rendering in this application, but `useSyncExternalStore` requires the third
 * argument, and the honest answer for a render with no `navigator` is "assume a connection": a
 * banner that flashed on every first paint would train people to ignore it.
 */
const SERVER_SNAPSHOT: NetworkSnapshot = { online: true, restoredCount: 0 }

function getServerSnapshot(): NetworkSnapshot {
  return SERVER_SNAPSHOT
}

export function useNetworkState(): NetworkState {
  const { online, restoredCount } = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot)

  // Seeded with the count at mount, so a component that appears long after a connection came back
  // does not greet its first viewer with news of a restoration they never saw go missing.
  const [acknowledged, setAcknowledged] = useState(restoredCount)
  const restored = online && restoredCount > acknowledged

  const acknowledgeRestored = useCallback(() => {
    setAcknowledged(restoredCount)
  }, [restoredCount])

  return useMemo(
    () => ({ online, restored, acknowledgeRestored }),
    [online, restored, acknowledgeRestored],
  )
}
