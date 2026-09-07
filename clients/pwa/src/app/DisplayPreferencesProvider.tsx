import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
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
import type { DisplayPreferencesValue } from './displayPreferencesContext'
import { createLocalDisplayPreferencesStore } from './preferences'
import type { DisplayPreferencesStore } from './preferences'

export interface DisplayPreferencesProviderProps {
  readonly children: ReactNode
  /**
   * Where the preferences are read from and written to.
   *
   * Defaults to the local stub in `preferences.ts`. The whole reason it is a prop is that the
   * server-backed store of #25 is meant to be dropped in here and nowhere else.
   */
  readonly store?: DisplayPreferencesStore
  /** What to apply before the store has answered. Stories and tests use it to render a fixed state. */
  readonly initial?: DisplayPreferences
  /**
   * The element the three attributes are written onto. Defaults to `document.documentElement`,
   * because `data-text-size` scales `--text-size-scale`, which everything inherits from the root.
   */
  readonly target?: HTMLElement
}

/**
 * Applies a person's theme, text size and row density to the document, and lets a screen change
 * them.
 *
 * ## How the preference reaches the pixels
 *
 * It does not go through JavaScript styling at all. The provider writes three attributes onto
 * `<html>` — `data-theme`, `data-text-size`, `data-density` — and themes.css already contains every
 * palette and every scale keyed on them. So the change is one attribute write, the whole cascade is
 * already loaded, and nothing has to be injected into a page whose Content Security Policy forbids
 * injected styles. `system` removes the attribute rather than setting it, which is what hands the
 * decision back to `prefers-color-scheme` and `prefers-contrast`.
 *
 * ## Order of operations
 *
 * The document is updated first and the store is written afterwards, deliberately. A person changing
 * the text size because they cannot read the screen sees the effect on the next frame, whether or
 * not the write succeeds; and when the store is a network call at #25, a slow shop connection will
 * not make the control feel broken.
 *
 * A failed write is swallowed. It means this session is correct and the next one will not remember —
 * which is worth no interruption at all, and certainly not an error message on top of a screen the
 * person was already struggling to read.
 */
export function DisplayPreferencesProvider({
  children,
  store,
  initial = DEFAULT_DISPLAY_PREFERENCES,
  target,
}: DisplayPreferencesProviderProps) {
  const activeStore = useMemo(() => store ?? createLocalDisplayPreferencesStore(), [store])
  const [preferences, setPreferences] = useState<DisplayPreferences>(initial)
  const [loaded, setLoaded] = useState(false)

  useEffect(() => {
    let cancelled = false
    void activeStore
      .read()
      .then((stored) => {
        if (!cancelled) {
          setPreferences(stored)
          setLoaded(true)
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

  const update = useCallback(
    (next: DisplayPreferences) => {
      setPreferences(next)
      void activeStore.write(next).catch(() => {
        // Deliberately silent — see the note above.
      })
    },
    [activeStore],
  )

  const value = useMemo<DisplayPreferencesValue>(
    () => ({
      preferences,
      loaded,
      setTheme: (theme: ThemePreference) => {
        update({ ...preferences, theme })
      },
      setTextSize: (textSize: TextSizePreference) => {
        update({ ...preferences, textSize })
      },
      setDensity: (density: Density) => {
        update({ ...preferences, density })
      },
    }),
    [preferences, loaded, update],
  )

  return (
    <DisplayPreferencesContext.Provider value={value}>
      {children}
    </DisplayPreferencesContext.Provider>
  )
}
