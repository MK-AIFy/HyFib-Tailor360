import { apiRequest } from '../auth/apiClient'
import type { AccountPreferences } from '../auth/types'
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
 * ## The interface is the point, and the local stub below is only half of it
 *
 * The #50 blueprint is explicit: theme (system, light, dark, high-contrast for sunlight), text size
 * (100 / 125 / 150%), row density and reduced motion are stored server-side in
 * `identity.user_preferences` and applied at login, **so that a shared counter or workshop device
 * does not leak one person's settings to the next or lose them at sign-out**. That is not a nicety.
 * Counter and workshop devices are shared by assumption A4 of the plan, and a Tailor who has set
 * 150% text does not want to hand the next person a screen they did not choose, nor to set it again
 * at the start of every shift.
 *
 * `createServerDisplayPreferencesStore()` below is that server-backed implementation, against the
 * `PUT /api/v1/me/preferences` endpoint #351 (e12-f01-1) published. `DisplayPreferencesProvider`
 * chooses between it and the local stub by reading the session (#374, e12-f01-2): a signed-in
 * account always gets its own stored preferences, and an anonymous device gets the local ones — see
 * that provider's own remarks for exactly how and why.
 *
 * Both methods are asynchronous even though the local stub is synchronous, precisely so that
 * swapping in a network-backed store was never a change to any caller.
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
  const reducedMotion =
    typeof candidate['reducedMotion'] === 'boolean'
      ? candidate['reducedMotion']
      : DEFAULT_DISPLAY_PREFERENCES.reducedMotion

  return { theme, textSize, density, reducedMotion }
}

/**
 * The local stub, used only for an anonymous device.
 *
 * `localStorage` is per-browser-profile, not per-person — the exact shortcoming the interface's own
 * remarks describe, which is why `DisplayPreferencesProvider` (#374, e12-f01-2) never reaches for
 * this store once somebody is signed in: the server-backed store above replaces it wholesale for
 * that case, rather than extending it. What is left here is deliberate rather than a leftover — a
 * device nobody has signed into yet still needs a working theme switch, most of all the
 * high-contrast one, because somebody may be standing at it trying to read the sign-in screen.
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

/*
 * The server vocabulary on one side, the document attributes on the other. An unrecognised server
 * value falls back to the default rather than throwing, the same rule `parseDisplayPreferences`
 * applies to a stored one: an account carrying a value from a future release renders, rather than
 * blanking the screen.
 */
const THEME_FROM_SERVER: Readonly<Record<string, ThemePreference>> = {
  System: 'system',
  Light: 'light',
  Dark: 'dark',
  HighContrast: 'contrast',
}

const THEME_TO_SERVER: Readonly<Record<ThemePreference, string>> = {
  system: 'System',
  light: 'Light',
  dark: 'Dark',
  contrast: 'HighContrast',
}

const TEXT_SIZE_FROM_SERVER: Readonly<Record<string, TextSizePreference>> = {
  Standard: '100',
  Large: '125',
  Larger: '150',
}

const TEXT_SIZE_TO_SERVER: Readonly<Record<TextSizePreference, string>> = {
  '100': 'Standard',
  '125': 'Large',
  '150': 'Larger',
}

const DENSITY_FROM_SERVER: Readonly<Record<string, Density>> = {
  Comfortable: 'comfortable',
  Compact: 'compact',
}

const DENSITY_TO_SERVER: Readonly<Record<Density, string>> = {
  comfortable: 'Comfortable',
  compact: 'Compact',
}

function fromAccountPreferences(account: AccountPreferences): DisplayPreferences {
  return {
    theme: THEME_FROM_SERVER[account.theme] ?? DEFAULT_DISPLAY_PREFERENCES.theme,
    textSize: TEXT_SIZE_FROM_SERVER[account.textSize] ?? DEFAULT_DISPLAY_PREFERENCES.textSize,
    density: DENSITY_FROM_SERVER[account.density] ?? DEFAULT_DISPLAY_PREFERENCES.density,
    reducedMotion: account.reducedMotion,
  }
}

/**
 * The server-backed store, against the account's already-loaded preferences.
 *
 * `read()` never issues a second `GET /api/v1/me` — the caller already has the account from the
 * session, and this resolves the document shape of what it already holds. `write()` sends the full
 * replacement `PUT /api/v1/me/preferences` demands, carrying `locale`, `timeZoneId` and
 * `landingRoute` back **byte-for-byte** from `current`: this slice owns none of the three, and
 * dropping or defaulting one would silently reset a person's language or landing screen, which
 * nothing on screen would show.
 */
export function createServerDisplayPreferencesStore(
  current: AccountPreferences,
): DisplayPreferencesStore {
  return {
    read(): Promise<DisplayPreferences> {
      return Promise.resolve(fromAccountPreferences(current))
    },
    async write(preferences: DisplayPreferences): Promise<void> {
      await apiRequest('/api/v1/me/preferences', {
        method: 'PUT',
        body: {
          locale: current.locale,
          timeZoneId: current.timeZoneId,
          theme: THEME_TO_SERVER[preferences.theme],
          textSize: TEXT_SIZE_TO_SERVER[preferences.textSize],
          density: DENSITY_TO_SERVER[preferences.density],
          reducedMotion: preferences.reducedMotion,
          landingRoute: current.landingRoute,
        },
      })
    },
  }
}
