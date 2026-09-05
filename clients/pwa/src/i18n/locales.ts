/**
 * The locales the application supports, and how a preference string is resolved to one.
 *
 * Separate from IntlProvider.tsx so that a module with no React in it — the formatters, the
 * pseudo-locale builder, a test — can import the locale type without importing a component.
 */

/** The locales the shell can render. Tamil ships behind the enablement gate described in ta-IN.ts. */
export const SUPPORTED_LOCALES = ['en-IN', 'ta-IN'] as const

export type SupportedLocale = (typeof SUPPORTED_LOCALES)[number]

/** English (India) is the default: it is the only complete catalogue. */
export const DEFAULT_LOCALE: SupportedLocale = 'en-IN'

/**
 * Where the locale preference lives until the identity module exists. The durable home is
 * `identity.user_preferences` on the server, applied at login, so that a shared shop device does not
 * leak or lose one user's choice to the next.
 */
export const LOCALE_STORAGE_KEY = 'tailor360.locale'

export function isSupportedLocale(candidate: string): candidate is SupportedLocale {
  return (SUPPORTED_LOCALES as readonly string[]).includes(candidate)
}

/**
 * Picks a catalogue for a preference string. An exact match wins; otherwise the language subtag is
 * matched, so "ta" and "ta-LK" both reach the Tamil catalogue; otherwise English (India).
 */
export function resolveLocale(preference: string | null | undefined): SupportedLocale {
  if (preference === null || preference === undefined || preference === '') {
    return DEFAULT_LOCALE
  }
  if (isSupportedLocale(preference)) {
    return preference
  }
  const language = preference.split('-')[0]?.toLowerCase()
  return SUPPORTED_LOCALES.find((locale) => locale.split('-')[0] === language) ?? DEFAULT_LOCALE
}
