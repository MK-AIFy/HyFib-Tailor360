import type { Meta, StoryObj } from '@storybook/react-vite'
import { MemoryRouter } from 'react-router'
import { HelpEntry } from './HelpEntry'
import '../primitives/storybook.css'
import './navigationStories.css'

/**
 * The help entry point.
 *
 * WCAG 3.2.6 Consistent Help asks that help occur in the same relative order on every page that has
 * it. Checklist items A11Y-10 and A11Y-85 turn that into two questions somebody walking a journey
 * must be able to answer yes to on every screen: is it here, and is it called the same thing as on
 * the last one?
 *
 * The only way to guarantee both is one component and one string, which is why this has no `label`
 * prop. The three stories below are the same control in the three shells; the appearance differs
 * because a rail, a header bar and a page have different room, and nothing else does.
 */
const meta = {
  title: 'Navigation/Help entry',
  component: HelpEntry,
  parameters: { layout: 'padded' },
  decorators: [
    (Story) => (
      <MemoryRouter initialEntries={['/orders']}>
        <Story />
      </MemoryRouter>
    ),
  ],
} satisfies Meta<typeof HelpEntry>

export default meta
type Story = StoryObj<typeof meta>

/** On a page, beside the content it explains. */
export const Inline: Story = { args: { placement: 'inline' } }

/** In a header bar, which is where a phone and a tablet shell put it — never in the bottom bar. */
export const InAHeaderBar: Story = {
  render: () => (
    <div className="nav-story-bar">
      <strong>HyFib Tailor360</strong>
      <HelpEntry placement="bar" />
    </div>
  ),
}

/** At the foot of the desktop rail, with the support contact beside it. */
export const InTheRail: Story = { args: { placement: 'side', showSupport: true } }

/** Help and the support contact together, which is how section 4.5 asks for them. */
export const WithSupport: Story = { args: { showSupport: true } }

/**
 * All three placements together.
 *
 * The word is identical in each, which is the assertion `HelpEntry.test.tsx` makes and the thing a
 * reviewer should be checking here.
 */
export const EveryPlacement: Story = {
  render: () => (
    <div className="storybook-stack">
      <HelpEntry placement="inline" />
      <div className="nav-story-bar">
        <strong>HyFib Tailor360</strong>
        <HelpEntry placement="bar" />
      </div>
      <HelpEntry placement="side" showSupport />
    </div>
  ),
}

/** The pseudo-locale, at 40% growth: "Help" grows, and the target stays 44 px. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  args: { placement: 'side', showSupport: true },
}
