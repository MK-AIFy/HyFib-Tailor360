import type { Meta, StoryObj } from '@storybook/react-vite'
import { ApiError } from '../../auth/apiClient'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { PSEUDO_LOCALE } from '../../i18n/pseudo'
import { DisplayPreferencesContext } from '../../app/displayPreferencesContext'
import type { DisplayPreferencesValue } from '../../app/displayPreferencesContext'
import { DisplayPreferencesProvider } from '../../app/DisplayPreferencesProvider'
import { createInMemoryDisplayPreferencesStore } from '../../app/preferences'
import type { DisplayPreferences } from '../../design-system/foundations/displayPreferences'
import { DisplayPreferencesPanel } from './DisplayPreferencesPanel'

/**
 * Theme, text size, row density and reduced motion — the four settings the product owes a person.
 *
 * Each of them answers something specific. The high-contrast theme is for reading the screen in
 * afternoon sunlight at the counter, which is what checklist item A11Y-54 asks a person to judge
 * outdoors at arm's length. The 100 / 125 / 150% text size is the product's own, separate from the
 * browser zoom, and it is the one a person with presbyopia actually finds (checklist item A11Y-72).
 * Compact density is the desktop affordance, and it collapses back to the 44 px standard target on a
 * coarse pointer, so it cannot reach a phone however it is set. Reduced motion adds a way to ask for
 * less animation than the device already gives, and never takes the device's own answer away.
 *
 * The change applies on the next frame and there is no Save button: the only way to judge whether a
 * setting is the one you wanted is to see it, and the sample line is there so that judgement can be
 * made without leaving the screen.
 *
 * **Two homes for the same four settings (#374).** Signed out — or in an isolated story or test with
 * its own store, exactly as the four interactive stories below are — the choice lives in a local
 * stub, on this device. Signed in, it is saved to `identity.user_preferences` and applied at login,
 * so a shared counter device neither leaks one person's settings to the next nor loses them at
 * sign-out. `Saved`, `Saving`, `Failed` and `Offline` below fix the panel at each outcome of that
 * save by supplying the context value directly, because driving a real network failure or a real
 * offline event is what the integration tests do; a story fixes the state a reviewer needs to see.
 *
 * The four interactive stories drive the panel from an in-memory store, so reviewing them never
 * writes to the browser's storage. Note that the panel writes its attributes onto the document, so a
 * story leaves the whole Storybook frame in the theme it was set to — which is the effect, not a
 * defect.
 */
const meta = {
  title: 'Layout/Display preferences',
  component: DisplayPreferencesPanel,
  parameters: { layout: 'padded' },
} satisfies Meta<typeof DisplayPreferencesPanel>

export default meta
type Story = StoryObj<typeof meta>

function withPreferences(initial: DisplayPreferences): Story {
  return {
    render: function PreferencesStory() {
      return (
        <DisplayPreferencesProvider store={createInMemoryDisplayPreferencesStore(initial)}>
          <DisplayPreferencesPanel />
        </DisplayPreferencesProvider>
      )
    },
  }
}

/** What a person gets before they have chosen anything: the device decides. */
export const Default: Story = withPreferences({
  theme: 'system',
  textSize: '100',
  density: 'comfortable',
  reducedMotion: false,
})

/** The counter in sunlight: the high-contrast theme, chosen and stored. */
export const HighContrast: Story = withPreferences({
  theme: 'contrast',
  textSize: '100',
  density: 'comfortable',
  reducedMotion: false,
})

/** Reading glasses left at home, which is the normal case after forty. */
export const LargestText: Story = withPreferences({
  theme: 'system',
  textSize: '150',
  density: 'comfortable',
  reducedMotion: false,
})

/** A back-office desktop with the rows tightened up. */
export const CompactDesktop: Story = withPreferences({
  theme: 'light',
  textSize: '100',
  density: 'compact',
  reducedMotion: false,
})

/**
 * Nobody is signed in. The note names the device, not the account, and there is no save-outcome
 * region below the controls — the same panel as `Default`, named for what it demonstrates.
 */
export const Anonymous: Story = withPreferences({
  theme: 'system',
  textSize: '100',
  density: 'comfortable',
  reducedMotion: false,
})

/** Renders every visible word from the pseudo-locale catalogue, at the 40% growth it tolerates. */
export const PseudoLocaleStory: Story = {
  render: function PseudoLocaleStoryRender() {
    return (
      <AppIntlProvider locale={PSEUDO_LOCALE}>
        <DisplayPreferencesProvider store={createInMemoryDisplayPreferencesStore()}>
          <DisplayPreferencesPanel />
        </DisplayPreferencesProvider>
      </AppIntlProvider>
    )
  },
}

/* Signed-in save outcomes ------------------------------------------------------------------------
 * Each of these supplies the context value directly rather than going through a real session and a
 * real request: what is under review is the panel's rendering of one fixed outcome, and the
 * integration tests are what actually drive the network to produce it. */

const ACCOUNT_PREFERENCES: DisplayPreferences = {
  theme: 'dark',
  textSize: '125',
  density: 'comfortable',
  reducedMotion: false,
}

function fixedValue(overrides: Partial<DisplayPreferencesValue>): DisplayPreferencesValue {
  return {
    preferences: ACCOUNT_PREFERENCES,
    loaded: true,
    accountBacked: true,
    saveStatus: 'idle',
    saveError: undefined,
    retrySave: () => undefined,
    setTheme: () => undefined,
    setTextSize: () => undefined,
    setDensity: () => undefined,
    setReducedMotion: () => undefined,
    ...overrides,
  }
}

function withFixedValue(value: DisplayPreferencesValue): Story {
  return {
    render: function FixedValueStory() {
      return (
        <DisplayPreferencesContext.Provider value={value}>
          <DisplayPreferencesPanel />
        </DisplayPreferencesContext.Provider>
      )
    },
  }
}

/** The ordinary case: the change landed, and the panel says so quietly. */
export const Saved: Story = withFixedValue(fixedValue({ saveStatus: 'saved' }))

/** The write is in flight. Nothing about the controls changes — the choice is already applied. */
export const Saving: Story = withFixedValue(fixedValue({ saveStatus: 'saving' }))

/** The save did not land. The choice stays applied for this session, and Retry resends it. */
export const Failed: Story = withFixedValue(
  fixedValue({
    saveStatus: 'failed',
    saveError: new ApiError('PUT /api/v1/me/preferences returned 500.', { status: 500 }),
  }),
)

/** The device has no connection, so nothing was even sent. */
export const Offline: Story = withFixedValue(fixedValue({ saveStatus: 'offline' }))
