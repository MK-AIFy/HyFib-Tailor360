import { DEFAULT_DISPLAY_PREFERENCES } from '../design-system/foundations/displayPreferences'
import type { DisplayPreferences } from '../design-system/foundations/displayPreferences'
import {
  DENSITIES,
  TEXT_SIZE_PREFERENCES,
  THEME_PREFERENCES,
} from '../design-system/foundations/types'
import type {
  Density,
  TextSizePreference,
  ThemePreference,
} from '../design-system/foundations/types'

/**
 * Where a person's display preferences are read from and written to.
 *
 * ## The interface is the point, and the implementation below is not
 *
 * The #50 blueprint is explicit: theme (system, light, dark, high-contrast for sunlight) and text
 * size (100 / 125 / 150%) are stored server-side in `identity.user_preferences` and applied at
 * login, **so that a shared counter or workshop device does not leak one person's settings to the
 * next or lose them at sign-out**. That is not a nicety. Counter and workshop devices are shared by
 * assumption A4 of the plan, and a Tailor who has set 150% text does not want to hand the next
 * person a screen they did not choose, nor to set it again at the start of every shift.
 *
 * The identity module does not exist yet — authentication is #23 and the administered user
 * preferences are #25 — and this issue must not invent an endpoint for it. So the shape of the
 * dependency is fixed here, in two methods, and the only implementation today is the local stub
 * below. When #25 lands, a `createServerDisplayPreferencesStore()` implements the same two methods
 * against the real endpoint, `DisplayPreferencesProvider` is handed that instead, and **nothing else
 * in the application changes**.
 *
 * Both methods are asynchronous even though the stub is synchronous, precisely so that swapping in
 * a network-backed store is not a change to every caller.
 */
export interface DisplayPreferencesStore {
  /** Reads the stored preferences, falling back to the defaults for anything missing or unreadable. */
  read(): Promise<DisplayPreferences>
  /** Persists the preferences. Rejects only on a fault the caller can do something about. */
  write(preferences: DisplayPreferences): Promise<void>
}

/**
 * The key the local stub writes under.
 *
 * Namespaced like `tailor360.locale`, and for the same reason: a shop device's browser profile is
 * shared with whatever else is installed on it.
 */
export const DISPLAY_PREFERENCES_STORAGE_KEY = 'tailor360.displayPreferences'

function isOneOf<T extends string>(values: readonly T[], candidate: unknown): candidate is T {
  return typeof candidate === 'string' && (values as readonly string[]).includes(candidate)
}

/**
 * Reads a stored object back into preferences, rejecting anything it does not recognise.
 *
 * Exported because it is the whole of the parsing, and a parser is far easier to test than a store.
 * Everything unrecognised falls back to the default rather than throwing: a person whose stored
 * theme is a value from a future version of the application gets the system theme, not a blank
 * screen.
 */
export function parseDisplayPreferences(payload: unknown): DisplayPreferences {
  if (typeof payload !== 'object' || payload === null) {
    return DEFAULT_DISPLAY_PREFERENCES
  }

  const candidate = payload as Record<string, unknown>
  const theme: ThemePreference = isOneOf(THEME_PREFERENCES, candidate['theme'])
    ? candidate['theme']
    : DEFAULT_DISPLAY_PREFERENCES.theme
  const textSize: TextSizePreference = isOneOf(TEXT_SIZE_PREFERENCES, candidate['textSize'])
    ? candidate['textSize']
    : DEFAULT_DISPLAY_PREFERENCES.textSize
  const density: Density = isOneOf(DENSITIES, candidate['density'])
    ? candidate['density']
    : DEFAULT_DISPLAY_PREFERENCES.density

  return { theme, textSize, density }
}

/**
 * The local stub. **Temporary, and it has a known shortcoming.**
 *
 * `localStorage` is per-browser-profile, not per-person, so on a shared counter device this store
 * does exactly what the blueprint says must not happen: one person's settings are handed to the
 * next, and they survive sign-out. That is accepted only because there is no session to bind them to
 * yet. It is not a design; it is the smallest thing that lets a person change the theme today, and
 * it is replaced wholesale by the server-backed store at #25 rather than being extended.
 *
 * Every access is wrapped, because a locked-down kiosk profile and a private-browsing window both
 * throw on `localStorage` — and a theme preference is never worth breaking a shell over.
 */
export function createLocalDisplayPreferencesStore(): DisplayPreferencesStore {
  return {
    read(): Promise<DisplayPreferences> {
      try {
        const raw = window.localStorage.getItem(DISPLAY_PREFERENCES_STORAGE_KEY)
        if (raw === null) {
          return Promise.resolve(DEFAULT_DISPLAY_PREFERENCES)
        }
        return Promise.resolve(parseDisplayPreferences(JSON.parse(raw)))
      } catch {
        return Promise.resolve(DEFAULT_DISPLAY_PREFERENCES)
      }
    },
    write(preferences: DisplayPreferences): Promise<void> {
      try {
        window.localStorage.setItem(DISPLAY_PREFERENCES_STORAGE_KEY, JSON.stringify(preferences))
      } catch {
        // Nothing to do and nothing to tell the user: the preference has already been applied to the
        // document, so this session is correct even though the next one will not remember it.
      }
      return Promise.resolve()
    },
  }
}

/** A store that remembers nothing, for a test or a story that must not touch the browser's storage. */
export function createInMemoryDisplayPreferencesStore(
  initial: DisplayPreferences = DEFAULT_DISPLAY_PREFERENCES,
): DisplayPreferencesStore {
  let current = initial
  return {
    read: () => Promise.resolve(current),
    write: (preferences) => {
      current = preferences
      return Promise.resolve()
    },
  }
}
