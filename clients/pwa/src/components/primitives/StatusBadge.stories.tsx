import type { Meta, StoryObj } from '@storybook/react-vite'
import { StatusBadge } from './StatusBadge'
import { STATUS_KINDS } from './statuses'
import './storybook.css'

/**
 * Status badges.
 *
 * docs/nfr/accessibility-localisation.md section 4.1 asks for "a Storybook story per status" as the
 * verification for 1.4.1 Use of Colour, and `AllStatuses` below is it. The test to apply: put the
 * browser into greyscale, or into the high-contrast theme, and check that every badge still says
 * which status it is. It will, because every one of them carries a word and a distinct shape.
 */
const meta = {
  title: 'Primitives/Status badge',
  component: StatusBadge,
  args: { status: 'ready' },
  parameters: { layout: 'centered' },
} satisfies Meta<typeof StatusBadge>

export default meta
type Story = StoryObj<typeof meta>

export const Ready: Story = {}

export const Overdue: Story = { args: { status: 'overdue', detail: '4 days' } }

/** Every status the product has. The one story 1.4.1's verification row asks for. */
export const AllStatuses: Story = {
  parameters: { layout: 'padded' },
  render: () => (
    <div className="storybook-row">
      {STATUS_KINDS.map((status) => (
        <StatusBadge key={status} status={status} />
      ))}
    </div>
  ),
}

/**
 * The pairs that share a list and must not share a shape: paid beside unpaid, QC passed beside QC
 * failed, ready beside delivered, due soon beside overdue.
 */
export const PairsThatMustBeToldApart: Story = {
  parameters: { layout: 'padded' },
  render: () => (
    <div className="storybook-stack">
      <div className="storybook-row">
        <StatusBadge status="paid" />
        <StatusBadge status="unpaid" />
      </div>
      <div className="storybook-row">
        <StatusBadge status="qc-passed" />
        <StatusBadge status="qc-failed" />
        <StatusBadge status="cancelled" />
      </div>
      <div className="storybook-row">
        <StatusBadge status="due-soon" detail="tomorrow" />
        <StatusBadge status="overdue" detail="4 days" />
      </div>
    </div>
  ),
}

/** The one status a shop-floor screen is really about, at the size it deserves. 1.4.6 wants 7:1 here. */
export const Prominent: Story = {
  args: { status: 'rework', prominent: true, detail: 'seam finish' },
}

export const WithDetail: Story = { args: { status: 'held', detail: 'awaiting customer approval' } }

/**
 * The pseudo-locale, at 40% growth.
 *
 * Watch the badges wrap as a whole rather than breaking "On hold" across two lines: the word and its
 * qualifier are held together, because a status split down the middle is a status misread.
 */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  parameters: { layout: 'padded' },
  render: () => (
    <div className="storybook-phone">
      <div className="storybook-row">
        {STATUS_KINDS.map((status) => (
          <StatusBadge key={status} status={status} />
        ))}
      </div>
    </div>
  ),
}

/** Tamil, where it exists. The untranslated entries deliberately still read in English. */
export const Tamil: Story = {
  globals: { locale: 'ta-IN' },
  parameters: { layout: 'padded' },
  render: () => (
    <div className="storybook-row">
      <StatusBadge status="held" />
      <StatusBadge status="rework" />
      <StatusBadge status="ready" />
    </div>
  ),
}
