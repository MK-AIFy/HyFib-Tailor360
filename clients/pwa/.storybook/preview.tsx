import type { Decorator, Preview } from '@storybook/react-vite'
import { AppIntlProvider } from '../src/i18n/IntlProvider'
import type { RenderableLocale } from '../src/i18n/IntlProvider'
import { PSEUDO_LOCALE } from '../src/i18n/pseudo'
import { applyDisplayPreferences } from '../src/design-system/foundations/displayPreferences'
import type { DisplayPreferences } from '../src/design-system/foundations/displayPreferences'
import '../src/styles/layers.css'
import '../src/styles/tokens.css'
import '../src/styles/themes.css'
import '../src/styles/global.css'

/**
 * The preview environment every story renders in.
 *
 * The four toolbar controls are not conveniences; each one is a gate the design system has to pass:
 *
 *   locale     `en-IN`, `ta-IN` and the pseudo-locale. A pseudo-locale story is required for every
 *              component (accessibility-localisation.md section 4.2 and the #50 blueprint): it
 *              proves the layout survives 40% text growth, and it makes any hard-coded English
 *              string visible, because hard-coded text is the only unaccented text on the screen.
 *   theme      system, light, dark and the high-contrast sunlight theme. A component that only
 *              looks right in one of them has a colour hard-coded somewhere.
 *   text size  the product's own 100 / 125 / 150% preference, which is what a person with
 *              presbyopia actually switches on (checklist item A11Y-72).
 *   density    comfortable and compact. Compact collapses back to the 44 px standard target on a
 *              coarse pointer, so the story also demonstrates that the rule holds.
 */

interface StoryGlobals {
  readonly locale?: RenderableLocale
  readonly theme?: DisplayPreferences['theme']
  readonly textSize?: DisplayPreferences['textSize']
  readonly density?: DisplayPreferences['density']
}

const withDisplayPreferences: Decorator = (Story, context) => {
  const globals = context.globals as StoryGlobals

  applyDisplayPreferences(document.documentElement, {
    theme: globals.theme ?? 'system',
    textSize: globals.textSize ?? '100',
    density: globals.density ?? 'comfortable',
  })

  return (
    <AppIntlProvider locale={globals.locale ?? 'en-IN'}>
      <Story />
    </AppIntlProvider>
  )
}

const preview: Preview = {
  decorators: [withDisplayPreferences],
  initialGlobals: {
    locale: 'en-IN',
    theme: 'system',
    textSize: '100',
    density: 'comfortable',
  },
  globalTypes: {
    locale: {
      description: 'Message catalogue',
      toolbar: {
        title: 'Locale',
        icon: 'globe',
        items: [
          { value: 'en-IN', title: 'English (India)' },
          { value: 'ta-IN', title: 'Tamil (India)' },
          { value: PSEUDO_LOCALE, title: 'Pseudo-locale — 40% growth' },
        ],
        dynamicTitle: true,
      },
    },
    theme: {
      description: 'Display theme',
      toolbar: {
        title: 'Theme',
        icon: 'paintbrush',
        items: [
          { value: 'system', title: 'System' },
          { value: 'light', title: 'Light' },
          { value: 'dark', title: 'Dark' },
          { value: 'contrast', title: 'High contrast — sunlight' },
        ],
        dynamicTitle: true,
      },
    },
    textSize: {
      description: 'Product text size',
      toolbar: {
        title: 'Text size',
        icon: 'zoom',
        items: [
          { value: '100', title: '100%' },
          { value: '125', title: '125%' },
          { value: '150', title: '150%' },
        ],
        dynamicTitle: true,
      },
    },
    density: {
      description: 'Row and control density',
      toolbar: {
        title: 'Density',
        icon: 'grid',
        items: [
          { value: 'comfortable', title: 'Comfortable' },
          { value: 'compact', title: 'Compact — desktop' },
        ],
        dynamicTitle: true,
      },
    },
  },
  parameters: {
    layout: 'centered',
    controls: {
      matchers: {
        date: /Date$/i,
      },
    },
    a11y: {
      // Report rather than block while the system is being built; #52 owns the enforcing gate.
      test: 'todo',
    },
    // The widths the overflow helper and the Playwright projects of #52 use, so a story can be
    // reviewed at the same sizes the automated check will assert.
    viewport: {
      options: {
        reflowFloor: { name: '320 — reflow floor', styles: { width: '320px', height: '800px' } },
        referencePhone: {
          name: '360 — reference device',
          styles: { width: '360px', height: '800px' },
        },
        tablet: { name: '768 — counter tablet', styles: { width: '768px', height: '1024px' } },
        desktop: { name: '1024 — desktop', styles: { width: '1024px', height: '768px' } },
        wide: { name: '1280 — wide desktop', styles: { width: '1280px', height: '800px' } },
      },
    },
  },
}

export default preview
