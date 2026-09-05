import type { Meta, StoryObj } from '@storybook/react-vite'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { DisplayPreferencesProvider } from '../../app/DisplayPreferencesProvider'
import { createInMemoryDisplayPreferencesStore } from '../../app/preferences'
import type { JourneyRole, ShellKind } from '../../design-system/foundations/types'
import { useDemoText } from '../primitives/demoText'
import { AppShell } from './AppShell'
import { PhoneShell } from './PhoneShell'
import { ShellStatusProvider } from './ShellStatusProvider'
import { useRoleNavigation } from './roleNavigation'
import './layoutStories.css'

/**
 * The application shell and the three role-optimised layouts.
 *
 * The layout is chosen by measuring the shell's own container, never by asking what the device is
 * (docs/nfr/support-matrix.md section 5). The stories force it with `shellKind`, because a Storybook
 * frame is a container of whatever width the story gives it and reviewing all three side by side is
 * the point — but nothing in the application ever passes that prop.
 *
 * Walk them in this order and the design reads itself:
 *
 *   Phone     bottom navigation in thumb reach, five destinations at most, each with a word, and one
 *             floating primary action — Scan for the four shop-floor roles.
 *   Tablet    a rail rather than a bar, because a counter tablet in landscape has its bottom edge
 *             furthest from either thumb; master-detail is the working area.
 *   Desktop   a grouped rail with real headings, help and support at its foot, dense rows.
 *
 * `PhoneWithKeyboard` is the one to compare against `Phone`: while the on-screen keyboard is up the
 * bar and the floating action are gone outright. That is 2.4.11 Focus Not Obscured, and it is the
 * half `scroll-padding-bottom` cannot deliver on a screen the keyboard has taken half of.
 *
 * Every story runs in the toolbar's locale, theme, text size and density — including the
 * pseudo-locale, which is where 40% text growth either fits or does not.
 */
const meta = {
  title: 'Layout/Application shell',
  component: AppShell,
  // Every story renders its own shell inside a frame, so the args here exist only to satisfy the
  // component's required props; nothing reads them.
  args: { children: null },
  parameters: { layout: 'centered' },
} satisfies Meta<typeof AppShell>

export default meta
type Story = StoryObj<typeof meta>

/** Sample work for the main landmark: a heading, some prose and a control at the very bottom. */
function SampleScreen() {
  const demo = useDemoText()

  return (
    <section className="page">
      <h1>{demo('Orders due today')}</h1>
      <p>
        {demo(
          'Synthetic data only. The point of this screen is the chrome around it: where navigation sits, where help sits, and what happens to both when the keyboard opens.',
        )}
      </p>
      <ul className="layout-story-list">
        {[
          'J-CBE01-2627-000512-01',
          'J-CBE01-2627-000514-02',
          'J-CBE01-2627-000521-01',
          'J-CBE01-2627-000533-01',
        ].map((job) => (
          <li key={job}>
            <button type="button" className="layout-story-list__button">
              {job}
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}

function Frame({ width, children }: { readonly width: string; readonly children: ReactNode }) {
  return (
    <div className="layout-story" data-width={width}>
      <div className="layout-story__scroll">{children}</div>
    </div>
  )
}

function shellStory(shellKind: ShellKind, width: string, role: JourneyRole): Story {
  return {
    render: function ShellStory() {
      return (
        <MemoryRouter initialEntries={['/orders']}>
          <DisplayPreferencesProvider store={createInMemoryDisplayPreferencesStore()}>
            <Frame width={width}>
              <AppShell shellKind={shellKind} role={role}>
                <SampleScreen />
              </AppShell>
            </Frame>
          </DisplayPreferencesProvider>
        </MemoryRouter>
      )
    },
  }
}

/** Reception on a phone: five destinations, and New order as the floating action. */
export const Phone: Story = shellStory('phone', '360', 'reception')

/** A Tailor on a phone: the scanner-first layout the #50 blueprint asks for. */
export const PhoneTailor: Story = shellStory('phone', '360', 'tailor')

/**
 * The same phone layout at the 1.4.10 Reflow floor.
 *
 * Nothing may scroll the page sideways here, and nothing may sit in the bottom 8 px where the system
 * gesture bar takes the touch (checklist items A11Y-70 and A11Y-71).
 */
export const PhoneAtReflowFloor: Story = shellStory('phone', '320', 'tailor')

/** A counter tablet: a rail, and the working area beside it. */
export const Tablet: Story = shellStory('tablet', '768', 'reception')

/** A back-office desktop: the grouped rail, with help and support at its foot. */
export const Desktop: Story = shellStory('desktop', '1280', 'cashier')

/** An Owner's desktop: eight destinations, and no floating action anywhere. */
export const DesktopOwner: Story = shellStory('desktop', '1280', 'owner')

/**
 * The phone layout while the on-screen keyboard is open.
 *
 * `PhoneShell` is rendered directly, because the keyboard state is measured from `visualViewport`
 * and a story has no keyboard to open. Compare it with `Phone`: the bar and the floating action are
 * `hidden` — out of the layout and out of the accessibility tree together, so nothing is announced
 * that cannot be reached.
 */
export const PhoneWithKeyboard: Story = {
  render: function KeyboardStory() {
    return (
      <MemoryRouter initialEntries={['/orders']}>
        <DisplayPreferencesProvider store={createInMemoryDisplayPreferencesStore()}>
          <Frame width="360">
            <ShellStatusProvider>
              <KeyboardOpenPhone />
            </ShellStatusProvider>
          </Frame>
        </DisplayPreferencesProvider>
      </MemoryRouter>
    )
  },
}

function KeyboardOpenPhone() {
  const navigation = useRoleNavigation('tailor')

  return (
    <div className="app-shell" data-shell="phone">
      <PhoneShell navigation={navigation} keyboardOpen>
        <SampleScreen />
      </PhoneShell>
    </div>
  )
}
