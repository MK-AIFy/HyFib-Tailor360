import { useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { ApiError } from '../auth/apiClient'
import { SessionContext } from '../auth/sessionContext'
import { useNetworkState } from '../components/states/useNetworkState'
import {
  DEFAULT_DISPLAY_PREFERENCES,
  applyDisplayPreferences,
} from '../design-system/foundations/displayPreferences'
import type { DisplayPreferences } from '../design-system/foundations/displayPreferences'
import type {
  Density,
  TextSizePreference,
  ThemePreference,
} from '../design-system/foundations/types'
import { DisplayPreferencesContext } from './displayPreferencesContext'
import type { DisplayPreferencesValue, PreferencesSaveStatus } from './displayPreferencesContext'
import {
  createLocalDisplayPreferencesStore,
  createServerDisplayPreferencesStore,
} from './preferences'
import type { DisplayPreferencesStore } from './preferences'

export interface DisplayPreferencesProviderProps {
  readonly children: ReactNode
  /**
   * Where the preferences are read from and written to.
   *
   * Pinning this always wins over the session — every existing test and story does, to render a
   * fixed state without a `SessionProvider` in the tree. Leave it unset in the application, where the
   * signed-in account or the local stub is chosen automatically; see the provider's own remarks.
   */
  readonly store?: DisplayPreferencesStore
  /** What to apply before the store has answered. Stories and tests use it to render a fixed state. */
  readonly initial?: DisplayPreferences
  /**
   * The element the four attributes are written onto. Defaults to `document.documentElement`,
   * because `data-text-size` scales `--text-size-scale`, which everything inherits from the root.
   */
  readonly target?: HTMLElement
}

/**
 * Applies a person's theme, text size, row density and reduced-motion choice to the document, and
 * lets a screen change them.
 *
 * ## Which store, and why this reads the session directly rather than through `useSession`
 *
 * With no `store` prop, the provider chooses one from the session it finds in React context:
 *
 *   - signed in (`active`, with an account) → the server store, against that account's own
 *     preferences — so a shared counter tablet applies the person who just signed in and nobody else.
 *   - signed out (`anonymous`), or no session in the tree at all → the local device stub, exactly as
 *     before this issue. An anonymous person at `/settings/display` still gets a working panel.
 *   - not yet known (`loading`, or `unavailable`) → nothing is read and nothing changes; the document
 *     keeps showing `initial` (the defaults) until the answer arrives, so the first paint is never a
 *     guess dressed up as a fact.
 *
 * It reads `SessionContext` directly rather than calling `useSession()`, which throws with no
 * `SessionProvider` above it — and a great many existing tests and stories render this provider on
 * its own, pinning a fixed store instead. `null` from the context means exactly what "no session
 * exists yet" would mean anywhere else in the application: behave as anonymous.
 *
 * A new store — a different `userId`, or a transition between signed in and anonymous — is a new
 * object, which is what makes the read effect below fire again and replace whatever was applied. That
 * is the whole leak this issue closes: the previous person's choice never survives a sign-out or a
 * different sign-in on the same device.
 *
 * ## How the preference reaches the pixels
 *
 * It does not go through JavaScript styling at all. The provider writes four attributes onto `<html>`
 * — `data-theme`, `data-text-size`, `data-density`, `data-reduced-motion` — and themes.css already
 * contains every palette, every scale and the motion tokens keyed on them. `system` removes the theme
 * attribute rather than setting it, handing the decision back to `prefers-color-scheme`; the
 * reduced-motion attribute is likewise only ever present when explicitly asked for, so the operating
 * system's own `prefers-reduced-motion: reduce` keeps working when this is false.
 *
 * ## Order of operations, and what a screen can read about the write
 *
 * The document is updated first and the store is written afterwards, deliberately: a person changing
 * the text size because they cannot read the screen sees the effect on the next frame whether or not
 * the write succeeds, and a slow shop connection never makes the control feel broken. Unlike the local
 * stub, the server write can fail or be skipped for want of a connection, so `saveStatus` and
 * `saveError` say what happened and `retrySave` resends the same change — a settings screen no longer
 * has to guess.
 */
export function DisplayPreferencesProvider({
  children,
  store,
  initial = DEFAULT_DISPLAY_PREFERENCES,
  target,
}: DisplayPreferencesProviderProps) {
  const session = useContext(SessionContext)
  const network = useNetworkState()

  const accountBacked = store === undefined && session?.status === 'active' && session.user !== null

  const activeStore = useMemo<DisplayPreferencesStore | null>(() => {
    if (store !== undefined) {
      return store
    }
    if (session === null || session.status === 'anonymous') {
      return createLocalDisplayPreferencesStore()
    }
    if (session.status === 'active' && session.user !== null) {
      return createServerDisplayPreferencesStore(session.user.preferences)
    }
    // 'loading' or 'unavailable': not yet known. Nothing is read and nothing changes below.
    return null
  }, [store, session])

  const [preferences, setPreferences] = useState<DisplayPreferences>(initial)
  const [loaded, setLoaded] = useState(false)
  const [saveStatus, setSaveStatus] = useState<PreferencesSaveStatus>('idle')
  const [saveError, setSaveError] = useState<ApiError | undefined>(undefined)

  /**
   * The last value a write was attempted with, for `retrySave`. Not React state on purpose: a retry
   * must resend the exact object a stale closure captured, and reading it back out of state would
   * risk resending whatever the screen has moved on to since.
   */
  const pendingRef = useRef<DisplayPreferences | null>(null)

  useEffect(() => {
    if (activeStore === null) {
      return undefined
    }
    let cancelled = false
    void activeStore
      .read()
      .then((stored) => {
        if (!cancelled) {
          setPreferences(stored)
          setLoaded(true)
          setSaveStatus('idle')
          setSaveError(undefined)
          pendingRef.current = null
        }
      })
      .catch(() => {
        if (!cancelled) {
          setLoaded(true)
        }
      })
    return () => {
      cancelled = true
    }
  }, [activeStore])

  useEffect(() => {
    applyDisplayPreferences(target ?? document.documentElement, preferences)
  }, [preferences, target])

  const attemptWrite = useCallback(
    (next: DisplayPreferences, online: boolean) => {
      if (activeStore === null) {
        return
      }
      pendingRef.current = next

      // Never attempted, not merely swallowed: billing, payment and inventory are the online-only
      // surfaces named by clients/pwa/CLAUDE.md section 6, and a preference follows the same rule
      // once it is account-backed rather than a device-local write that cannot meaningfully fail.
      if (accountBacked && !online) {
        setSaveStatus('offline')
        return
      }

      setSaveStatus('saving')
      void activeStore
        .write(next)
        .then(() => {
          if (pendingRef.current === next) {
            setSaveStatus('saved')
            setSaveError(undefined)
          }
        })
        .catch((cause: unknown) => {
          if (pendingRef.current === next) {
            setSaveStatus('failed')
            setSaveError(
              cause instanceof ApiError ? cause : new ApiError('The change could not be saved.'),
            )
          }
        })
    },
    [activeStore, accountBacked],
  )

  const update = useCallback(
    (next: DisplayPreferences) => {
      setPreferences(next)
      attemptWrite(next, network.online)
    },
    [attemptWrite, network.online],
  )

  const retrySave = useCallback(() => {
    if (pendingRef.current !== null) {
      attemptWrite(pendingRef.current, network.online)
    }
  }, [attemptWrite, network.online])

  const value = useMemo<DisplayPreferencesValue>(
    () => ({
      preferences,
      loaded,
      accountBacked,
      saveStatus,
      saveError,
      retrySave,
      setTheme: (theme: ThemePreference) => {
        update({ ...preferences, theme })
      },
      setTextSize: (textSize: TextSizePreference) => {
        update({ ...preferences, textSize })
      },
      setDensity: (density: Density) => {
        update({ ...preferences, density })
      },
      setReducedMotion: (reducedMotion: boolean) => {
        update({ ...preferences, reducedMotion })
      },
    }),
    [preferences, loaded, accountBacked, saveStatus, saveError, retrySave, update],
  )

  return (
    <DisplayPreferencesContext.Provider value={value}>
      {children}
    </DisplayPreferencesContext.Provider>
  )
}
