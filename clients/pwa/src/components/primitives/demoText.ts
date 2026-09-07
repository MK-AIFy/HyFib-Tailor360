import { useCallback } from 'react'
import { useIntl } from 'react-intl'
import { PSEUDO_LOCALE, pseudoLocaliseMessage } from '../../i18n/pseudo'

/**
 * Pseudo-localises the sample content in a story.
 *
 * A pseudo-locale story is required for every component (the #50 blueprint, and criterion 5 of the
 * Tamil enablement gate in docs/nfr/accessibility-localisation.md section 10.3). It proves the
 * layout survives 40% text growth. But a component's own strings are only half of what a screen
 * holds: a queue is mostly customer names, job numbers and column headings that the *caller* passes
 * in, and those are the strings that decide whether a table still fits.
 *
 * Story fixtures are not catalogue entries — inventing catalogue keys for "Lakshmi Narayanan" would
 * put synthetic data in the shipped message files — so this runs the same transformation the
 * pseudo-catalogue uses over whatever the story passes it, but only when the pseudo-locale is the
 * one selected in the toolbar. In `en-IN` and `ta-IN` the text is returned untouched.
 *
 * Stories only. Nothing under `src` that ships imports it.
 */
export function useDemoText(): (text: string) => string {
  const { locale } = useIntl()

  return useCallback(
    (text: string) => (locale === PSEUDO_LOCALE ? pseudoLocaliseMessage(text) : text),
    [locale],
  )
}
