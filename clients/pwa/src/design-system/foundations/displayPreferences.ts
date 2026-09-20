import type { Density, TextSizePreference, ThemePreference } from './types'

/**
 * Applying a person's display preferences to the document.
 *
 * The #50 blueprint stores theme (system, light, dark, high-contrast for sunlight) and text size
 * (100, 125, 150%) server-side in `identity.user_preferences` and applies them at login, so that a
 * shared counter or workshop device does not leak one person's settings to the next or lose them at
 * sign-out. This is the client half of that: three attributes on `<html>`, which the token blocks in
 * src/styles/themes.css select on.
 *
 * Attributes rather than a stylesheet swap, and attributes rather than inline styles: the whole
 * palette is already in the cascade, the change is one attribute, and nothing has to be injected
 * into a page whose Content Security Policy forbids injected styles.
 *
 * `system` removes the attribute rather than setting it, which is what hands the decision back to
 * `prefers-color-scheme` and `prefers-contrast`.
 */
export interface DisplayPreferences {
  readonly theme: ThemePreference
  readonly textSize: TextSizePreference
  readonly density: Density
  /**
   * True to suppress animation this application controls, on top of whatever the operating system
   * already reduces. It adds a way to ask; it never takes one away — `prefers-reduced-motion: reduce`
   * still applies when this is false.
   */
  readonly reducedMotion: boolean
}

/** What a person gets before they have chosen anything. */
export const DEFAULT_DISPLAY_PREFERENCES: DisplayPreferences = {
  theme: 'system',
  textSize: '100',
  density: 'comfortable',
  reducedMotion: false,
}

export const THEME_ATTRIBUTE = 'data-theme'
export const TEXT_SIZE_ATTRIBUTE = 'data-text-size'
export const DENSITY_ATTRIBUTE = 'data-density'
export const REDUCED_MOTION_ATTRIBUTE = 'data-reduced-motion'

/**
 * Writes the preferences onto an element — `document.documentElement` in the application, a story
 * wrapper in Storybook.
 */
export function applyDisplayPreferences(
  element: Element,
  preferences: DisplayPreferences = DEFAULT_DISPLAY_PREFERENCES,
): void {
  if (preferences.theme === 'system') {
    element.removeAttribute(THEME_ATTRIBUTE)
  } else {
    element.setAttribute(THEME_ATTRIBUTE, preferences.theme)
  }
  element.setAttribute(TEXT_SIZE_ATTRIBUTE, preferences.textSize)
  element.setAttribute(DENSITY_ATTRIBUTE, preferences.density)

  // Absent rather than "false": the operating system's own prefers-reduced-motion still has to win
  // when this is not explicitly set, and themes.css only has to test for the attribute's presence.
  if (preferences.reducedMotion) {
    element.setAttribute(REDUCED_MOTION_ATTRIBUTE, 'true')
  } else {
    element.removeAttribute(REDUCED_MOTION_ATTRIBUTE)
  }
}

/** Reads back what is currently applied, falling back to the defaults for anything unrecognised. */
export function readDisplayPreferences(element: Element): DisplayPreferences {
  const theme = element.getAttribute(THEME_ATTRIBUTE)
  const textSize = element.getAttribute(TEXT_SIZE_ATTRIBUTE)
  const density = element.getAttribute(DENSITY_ATTRIBUTE)

  return {
    theme: isThemePreference(theme) ? theme : 'system',
    textSize: isTextSizePreference(textSize) ? textSize : '100',
    density: density === 'compact' ? 'compact' : 'comfortable',
    reducedMotion: element.getAttribute(REDUCED_MOTION_ATTRIBUTE) === 'true',
  }
}

function isThemePreference(value: string | null): value is ThemePreference {
  return value === 'light' || value === 'dark' || value === 'contrast' || value === 'system'
}

function isTextSizePreference(value: string | null): value is TextSizePreference {
  return value === '100' || value === '125' || value === '150'
}
