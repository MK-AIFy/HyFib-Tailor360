import { getFormatters } from '../../../i18n/formatters'
import type { Formatters } from '../../../i18n/formatters'
import { resolveLocale } from '../../../i18n/locales'

/**
 * The formatter set for whatever locale react-intl is currently rendering.
 *
 * `useIntl().locale` is a plain string and can be the pseudo-locale, which is not a supported
 * locale and has no `Intl` data of its own. `resolveLocale` maps it back to English (India) by
 * language subtag, so a pseudo-locale story still shows real numbers and real dates — which is the
 * point of the pseudo-locale: it grows the *words* by 40% and leaves the values alone, so a layout
 * that breaks under Tamil breaks here too.
 *
 * A hook rather than a call inside each component so that the branch timezone, when the branch
 * context arrives with #24, is added in one place instead of in every field.
 */
export function formattersForLocale(locale: string): Formatters {
  return getFormatters(resolveLocale(locale))
}
