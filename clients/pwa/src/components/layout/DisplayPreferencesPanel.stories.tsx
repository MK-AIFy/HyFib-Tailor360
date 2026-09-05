import type { Meta, StoryObj } from '@storybook/react-vite'
import { DisplayPreferencesProvider } from '../../app/DisplayPreferencesProvider'
import { createInMemoryDisplayPreferencesStore } from '../../app/preferences'
import type { DisplayPreferences } from '../../design-system/foundations/displayPreferences'
import { DisplayPreferencesPanel } from './DisplayPreferencesPanel'

/**
 * Theme, text size and row density — the three settings the product owes a person.
 *
 * Each of them answers something specific. The high-contrast theme is for reading the screen in
 * afternoon sunlight at the counter, which is what checklist item A11Y-54 asks a person to judge
 * outdoors at arm's length. The 100 / 125 / 150% text size is the product's own, separate from the
 * browser zoom, and it is the one a person with presbyopia actually finds (checklist item A11Y-72).
 * Compact density is the desktop affordance, and it collapses back to the 44 px standard target on a
 * coarse pointer, so it cannot reach a phone however it is set.
 *
 * The change applies on the next frame and there is no Save button: the only way to judge whether a
 * setting is the one you wanted is to see it, and the sample line is there so that judgement can be
 * made without leaving the screen.
 *
 * **Where the choice is kept.** In a local stub, on this device, which is exactly what the #50
 * blueprint says must not be the end state: these belong in `identity.user_preferences` on the
 * server and are applied at login, so that a shared counter device neither leaks one person's
 * settings to the next nor loses them at sign-out. The store is a two-method interface for that
 * reason — the server-backed implementation arrives with #25 and nothing else changes.
 *
 * The stories drive the panel from an in-memory store, so reviewing them never writes to the
 * browser's storage. Note that the panel writes its attributes onto the document, so a story leaves
 * the whole Storybook frame in the theme it was set to — which is the effect, not a defect.
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
})

/** The counter in sunlight: the high-contrast theme, chosen and stored. */
export const HighContrast: Story = withPreferences({
  theme: 'contrast',
  textSize: '100',
  density: 'comfortable',
})

/** Reading glasses left at home, which is the normal case after forty. */
export const LargestText: Story = withPreferences({
  theme: 'system',
  textSize: '150',
  density: 'comfortable',
})

/** A back-office desktop with the rows tightened up. */
export const CompactDesktop: Story = withPreferences({
  theme: 'light',
  textSize: '100',
  density: 'compact',
})
