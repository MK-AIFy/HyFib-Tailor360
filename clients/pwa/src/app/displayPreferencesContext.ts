import { createContext } from 'react'
import type { DisplayPreferences } from '../design-system/foundations/displayPreferences'
import type {
  Density,
  TextSizePreference,
  ThemePreference,
} from '../design-system/foundations/types'

/**
 * What a screen can do with the display preferences.
 *
 * Three setters rather than one, because the three settings are independent and a screen that
 * changes the theme must not have to know what the text size currently is to do it.
 *
 * `loaded` exists because the store is asynchronous and will one day be a network call. Until it has
 * answered, the document carries the defaults; a settings screen shows its controls as soon as the
 * answer arrives rather than flickering from a guess to the real value.
 */
export interface DisplayPreferencesValue {
  readonly preferences: DisplayPreferences
  readonly loaded: boolean
  readonly setTheme: (theme: ThemePreference) => void
  readonly setTextSize: (textSize: TextSizePreference) => void
  readonly setDensity: (density: Density) => void
}

/** Undefined outside the provider, so the hook can fail loudly rather than silently do nothing. */
export const DisplayPreferencesContext = createContext<DisplayPreferencesValue | undefined>(
  undefined,
)
