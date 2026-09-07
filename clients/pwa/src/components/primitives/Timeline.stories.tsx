import type { Meta, StoryObj } from '@storybook/react-vite'
import { Timeline } from './Timeline'
import type { TimelineEntry } from './Timeline'
import { useDemoText } from './demoText'
import './storybook.css'

/**
 * The history rail.
 *
 * An ordered list, because the sequence is the meaning: "item 3 of 11" is what somebody walking a
 * custody chain needs while looking for the handover that went wrong. The absolute time is always
 * shown, even beside a relative cue — section 12 does not allow "3 hours ago" to stand alone.
 */
const meta = {
  title: 'Primitives/Timeline',
  component: Timeline,
  args: { entries: [] },
  parameters: { layout: 'padded' },
} satisfies Meta<typeof Timeline>

export default meta
type Story = StoryObj<typeof meta>

function useEntries(): readonly TimelineEntry[] {
  const demo = useDemoText()

  return [
    {
      id: '1',
      title: demo('Order confirmed'),
      status: 'draft',
      absoluteTime: '02-09-2026 11:05 AM',
      dateTime: '2026-09-02T05:35:00Z',
      actor: demo('Reception — Devi S'),
    },
    {
      id: '2',
      title: demo('Scanned in at Cutting'),
      status: 'in-progress',
      absoluteTime: '03-09-2026 09:12 AM',
      dateTime: '2026-09-03T03:42:00Z',
      relativeTime: demo('2 days ago'),
      actor: demo('Kavitha R'),
    },
    {
      id: '3',
      title: demo('Held — awaiting customer approval on the neck depth'),
      status: 'held',
      absoluteTime: '03-09-2026 04:20 PM',
      dateTime: '2026-09-03T10:50:00Z',
      actor: demo('Tailor Master'),
      detail: <p>{demo('The customer was called; a decision is expected on Friday.')}</p>,
    },
    {
      id: '4',
      title: demo('QC failed — seam finish'),
      status: 'qc-failed',
      absoluteTime: '05-09-2026 02:40 PM',
      dateTime: '2026-09-05T09:10:00Z',
      actor: demo('QC — Ramesh K'),
      detail: (
        <p>
          {demo('Rework opened. The garment stays in production and the ready gate stays closed.')}
        </p>
      ),
    },
    {
      id: '5',
      title: demo('Ready for delivery'),
      status: 'ready',
      absoluteTime: '06-09-2026 10:02 AM',
      dateTime: '2026-09-06T04:32:00Z',
      relativeTime: demo('in 2 hours'),
    },
  ]
}

/** The custody history of one garment job. */
export const JobHistory: Story = {
  render: function JobHistoryStory() {
    return <Timeline entries={useEntries()} />
  },
}

export const SingleEntry: Story = {
  render: function SingleEntryStory() {
    const demo = useDemoText()
    return (
      <Timeline
        entries={[
          {
            id: '1',
            title: demo('Order confirmed'),
            icon: 'check',
            absoluteTime: '02-09-2026 11:05 AM',
          },
        ]}
      />
    )
  },
}

/** In a pane the width of a phone: the rail keeps its marker column and the text wraps beside it. */
export const OnAPhone: Story = {
  render: function OnAPhoneStory() {
    return (
      <div className="storybook-phone">
        <Timeline entries={useEntries()} />
      </div>
    )
  },
}

/** The pseudo-locale, at 40% growth. Watch the rail hold its column while every line grows. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: function PseudoLocaleStory() {
    return (
      <div className="storybook-phone">
        <Timeline entries={useEntries()} />
      </div>
    )
  },
}
