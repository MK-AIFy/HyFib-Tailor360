import { render } from '@testing-library/react'
import type { RenderOptions, RenderResult } from '@testing-library/react'
import type { ReactElement, ReactNode } from 'react'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import type { RenderableLocale } from '../../i18n/IntlProvider'
import {
  DEFAULT_DISPLAY_PREFERENCES,
  applyDisplayPreferences,
} from '../foundations/displayPreferences'
import type { DisplayPreferences } from '../foundations/displayPreferences'

/**
 * The one way a design-system component is rendered in a test.
 *
 * Every component test needs the same three things — a message catalogue, the display preferences
 * on the document, and Testing Library's own render — and getting any of them wrong produces a test
 * that passes for the wrong reason. Rendering a field without an `IntlProvider` throws on the first
 * `FormattedMessage`; rendering one in the wrong theme proves nothing about the theme it will ship
 * in.
 *
 * Providers a later issue adds — the TanStack Query client of the #50 version migration, the
 * preferences context, a router — belong here, so that adding one does not mean editing every test
 * in the system.
 */
export interface RenderWithProvidersOptions extends Omit<RenderOptions, 'wrapper'> {
  /** The catalogue to render in. Pass the pseudo-locale to assert the 40% growth tolerance. */
  readonly locale?: RenderableLocale
  /** Theme, text size and density. Defaults to system, 100% and comfortable. */
  readonly preferences?: DisplayPreferences
}

export function renderWithProviders(
  ui: ReactElement,
  options: RenderWithProvidersOptions = {},
): RenderResult {
  const { locale = 'en-IN', preferences = DEFAULT_DISPLAY_PREFERENCES, ...renderOptions } = options

  applyDisplayPreferences(document.documentElement, preferences)

  function Wrapper({ children }: { readonly children: ReactNode }) {
    return <AppIntlProvider locale={locale}>{children}</AppIntlProvider>
  }

  return render(ui, { wrapper: Wrapper, ...renderOptions })
}
