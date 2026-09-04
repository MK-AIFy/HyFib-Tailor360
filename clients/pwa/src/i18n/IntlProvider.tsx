import { useEffect, useMemo } from 'react'
import type { ReactNode } from 'react'
import { IntlProvider } from 'react-intl'
import { enIN } from './en-IN'
import type { MessageCatalogue } from './en-IN'
import { taIN } from './ta-IN'

/** The locales the shell can render today. Tamil ships behind the review gate described in ta-IN.ts. */
export const SUPPORTED_LOCALES = ['en-IN', 'ta-IN'] as const

export type SupportedLocale = (typeof SUPPORTED_LOCALES)[number]

/** English (India) is the default: it is the only complete catalogue. */
export const DEFAULT_LOCALE: SupportedLocale = 'en-IN'

/**
 * Where the preference lives until the identity module exists. The durable home is
 * identity.user_preferences on the server (#50), so that a shared shop device does not leak or lose
 * one user's choice to the next.
 */
export const LOCALE_STORAGE_KEY = 'tailor360.locale'

const CATALOGUES: Record<SupportedLocale, MessageCatalogue> = {
  'en-IN': enIN,
  'ta-IN': taIN,
}

function isSupported(candidate: string): candidate is SupportedLocale {
  return (SUPPORTED_LOCALES as readonly string[]).includes(candidate)
}

function readStoredPreference(): string | null {
  try {
    return window.localStorage.getItem(LOCALE_STORAGE_KEY)
  } catch {
    // Private browsing and locked-down kiosk profiles can throw on access; the default locale is a
    // perfectly good answer, so this must never break the shell.
    return null
  }
}

/**
 * Picks a catalogue for a preference string. An exact match wins; otherwise the language subtag is
 * matched (so "ta" and "ta-LK" both reach the Tamil catalogue); otherwise English (India).
 */
function resolveLocale(preference: string | null | undefined): SupportedLocale {
  if (!preference) {
    return DEFAULT_LOCALE
  }
  if (isSupported(preference)) {
    return preference
  }
  const language = preference.split('-')[0]?.toLowerCase()
  const byLanguage = SUPPORTED_LOCALES.find((locale) => locale.split('-')[0] === language)
  return byLanguage ?? DEFAULT_LOCALE
}

interface AppIntlProviderProps {
  readonly children: ReactNode
  /** Overrides the stored and browser preference. Used by tests and by the language switcher. */
  readonly locale?: SupportedLocale
}

/**
 * Wraps the application in react-intl and keeps <html lang> in step, which screen readers and
 * hyphenation both depend on.
 */
export function AppIntlProvider({ children, locale }: AppIntlProviderProps) {
  const active = useMemo(
    () => locale ?? resolveLocale(readStoredPreference() ?? window.navigator.language),
    [locale],
  )

  useEffect(() => {
    document.documentElement.lang = active
  }, [active])

  return (
    <IntlProvider locale={active} defaultLocale={DEFAULT_LOCALE} messages={CATALOGUES[active]}>
      {children}
    </IntlProvider>
  )
}
