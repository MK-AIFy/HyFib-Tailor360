import { createContext } from 'react'
import type { ApiError } from '../auth/apiClient'
import type { DisplayPreferences } from '../design-system/foundations/displayPreferences'
import type {
  Density,
  TextSizePreference,
  ThemePreference,
} from '../design-system/foundations/types'

/**
 * How the last change to reach the store is going, for a screen that wants to say so.
 *
 *   idle     nothing has been changed yet this session
 *   saving   the write is in flight
 *   saved    it landed
 *   failed   it did not; `saveError` says why, and `retrySave` resends the same change
 *   offline  the device has no connection, so nothing was even attempted — see `OfflineBlockedAction`
 */
export const PREFERENCES_SAVE_STATUSES = ['idle', 'saving', 'saved', 'failed', 'offline'] as const

export type PreferencesSaveStatus = (typeof PREFERENCES_SAVE_STATUSES)[number]

/**
 * What a screen can do with the display preferences.
 *
 * Four setters rather than one, because the settings are independent and a screen that changes the
 * theme must not have to know what the text size currently is to do it.
 *
 * `loaded` exists because the store is asynchronous. Until it has answered, the document carries
 * `initial` (the defaults, ordinarily); a settings screen shows its controls as soon as the answer
 * arrives rather than flickering from a guess to the real value.
 */
export interface DisplayPreferencesValue {
  readonly preferences: DisplayPreferences
  readonly loaded: boolean
  readonly setTheme: (theme: ThemePreference) => void
  readonly setTextSize: (textSize: TextSizePreference) => void
  readonly setDensity: (density: Density) => void
  readonly setReducedMotion: (reducedMotion: boolean) => void
  /**
   * True when the account is signed in and preferences are saved to it; false when they are held on
   * this device only. What a settings screen reads to choose between "stored on this device" and
   * "saved to your account", without needing a session of its own.
   */
  readonly accountBacked: boolean
  readonly saveStatus: PreferencesSaveStatus
  /** The failure behind a `failed` status, so a screen can render it with `RetryableError`. */
  readonly saveError: ApiError | undefined
  /** Resends the change that `failed`, or retries once the connection is back after `offline`. */
  readonly retrySave: () => void
}

/** Undefined outside the provider, so the hook can fail loudly rather than silently do nothing. */
export const DisplayPreferencesContext = createContext<DisplayPreferencesValue | undefined>(
  undefined,
)
