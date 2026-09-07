import { useContext } from 'react'
import { DisplayPreferencesContext } from './displayPreferencesContext'
import type { DisplayPreferencesValue } from './displayPreferencesContext'

/**
 * Reads and changes the display preferences.
 *
 * Throws outside a `DisplayPreferencesProvider`, rather than returning the defaults. A settings
 * control whose changes go nowhere looks like it is working, and a person adjusting the text size
 * because they cannot read the screen is the last person who should have to discover that.
 */
export function useDisplayPreferences(): DisplayPreferencesValue {
  const value = useContext(DisplayPreferencesContext)
  if (value === undefined) {
    throw new Error(
      'useDisplayPreferences was called outside a DisplayPreferencesProvider. The provider is mounted in main.tsx; wrap the tree in it for an isolated story or test.',
    )
  }
  return value
}
