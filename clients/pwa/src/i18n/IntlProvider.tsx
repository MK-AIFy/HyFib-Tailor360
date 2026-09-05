import { useEffect, useMemo } from 'react'
import type { ReactNode } from 'react'
import { IntlProvider } from 'react-intl'
import { enIN } from './en-IN'
import type { MessageCatalogue } from './en-IN'
import { DEFAULT_LOCALE, LOCALE_STORAGE_KEY, resolveLocale } from './locales'
import type { SupportedLocale } from './locales'
import { PSEUDO_LOCALE, pseudoCatalogue } from './pseudo'
import { taIN } from './ta-IN'

/**
 * The locale list, the storage key and the resolver live in `./locales`, which has no React in it
 * and can therefore be imported by the formatters, by the pseudo-locale builder and by a plain unit
 * test. Only the types are re-exported here, because a type export costs a component file nothing.
 */
export type { SupportedLocale }

/**
 * A locale the shell can be asked to render. The pseudo-locale is included because Storybook and the
 * layout-growth tests render it; it is not in `SUPPORTED_LOCALES` and is never offered to staff.
 */
export type RenderableLocale = SupportedLocale | typeof PSEUDO_LOCALE

function catalogueFor(locale: RenderableLocale): MessageCatalogue {
  if (locale === PSEUDO_LOCALE) {
    return pseudoCatalogue()
  }
  return locale === 'ta-IN' ? taIN : enIN
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

interface AppIntlProviderProps {
  readonly children: ReactNode
  /** Overrides the stored and browser preference. Used by tests, stories and the language switcher. */
  readonly locale?: RenderableLocale
}

/**
 * Wraps the application in react-intl and keeps `<html lang>` in step, which screen readers and
 * hyphenation both depend on.
 */
export function AppIntlProvider({ children, locale }: AppIntlProviderProps) {
  const active = useMemo(
    (): RenderableLocale =>
      locale ?? resolveLocale(readStoredPreference() ?? window.navigator.language),
    [locale],
  )

  useEffect(() => {
    document.documentElement.lang = active
  }, [active])

  return (
    <IntlProvider locale={active} defaultLocale={DEFAULT_LOCALE} messages={catalogueFor(active)}>
      {children}
    </IntlProvider>
  )
}
