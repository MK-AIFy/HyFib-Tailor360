import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { useDemoText } from '../primitives/demoText'
import { MasterDetail } from './MasterDetail'
import './layoutStories.css'

/**
 * A list beside the thing it selects, or one at a time when there is not room for both.
 *
 * This is the counter tablet's working pattern — measurement capture, the job queue, a job card,
 * stock, a bill. The arrangement is decided by the component's own container width, which is what
 * makes docs/nfr/support-matrix.md section 5 true without an orientation check anywhere: a tablet in
 * portrait gives a container under 768 px and stacks, the same tablet in landscape splits, and
 * 1.3.4 Orientation cannot be broken by a layout that never asks.
 *
 * `Stacked` is the one to walk with a keyboard. Opening a job replaces the list, so focus moves into
 * the detail pane; Back returns it to the list. Without that, a person who has just opened a job
 * card is focused on a control that is no longer on the screen.
 */
const meta = {
  title: 'Layout/Master and detail',
  component: MasterDetail,
  // Each story renders its own pair; these args exist only to satisfy the required prop.
  args: { list: null },
  parameters: { layout: 'centered' },
} satisfies Meta<typeof MasterDetail>

export default meta
type Story = StoryObj<typeof meta>

const JOBS = [
  'J-CBE01-2627-000512-01',
  'J-CBE01-2627-000514-02',
  'J-CBE01-2627-000521-01',
  'J-CBE01-2627-000533-01',
] as const

function Example({ arrangement }: { readonly arrangement: 'split' | 'stacked' }) {
  const demo = useDemoText()
  const [selected, setSelected] = useState<string | null>(null)

  return (
    <MasterDetail
      arrangement={arrangement}
      listLabel={demo('Jobs due today')}
      detailLabel={demo('Job card')}
      detailOpen={selected !== null}
      onCloseDetail={() => {
        setSelected(null)
      }}
      list={
        <ul className="layout-story-list">
          {JOBS.map((job) => (
            <li key={job}>
              <button
                type="button"
                className="layout-story-list__button"
                aria-pressed={selected === job}
                onClick={() => {
                  setSelected(job)
                }}
              >
                {job}
              </button>
            </li>
          ))}
        </ul>
      }
      detail={
        selected === null ? undefined : (
          <div className="layout-story-card">
            <h2>{selected}</h2>
            <p>{demo('Cutting, due 14-09-2026. Synthetic data.')}</p>
          </div>
        )
      }
    />
  )
}

/** Landscape on a counter tablet, and every desktop: both panes at once. */
export const Split: Story = {
  render: function SplitStory() {
    return (
      <div className="layout-story" data-width="1280">
        <div className="layout-story__scroll">
          <Example arrangement="split" />
        </div>
      </div>
    )
  },
}

/** Portrait, and every phone: one pane at a time, with a way back that a keyboard can reach. */
export const Stacked: Story = {
  render: function StackedStory() {
    return (
      <div className="layout-story" data-width="360">
        <div className="layout-story__scroll">
          <Example arrangement="stacked" />
        </div>
      </div>
    )
  },
}

/** Nothing selected. An empty pane says what it is waiting for rather than looking unfinished. */
export const NothingSelected: Story = {
  render: function EmptyStory() {
    return (
      <div className="layout-story" data-width="768">
        <div className="layout-story__scroll">
          <MasterDetail
            arrangement="split"
            list={
              <ul className="layout-story-list">
                {JOBS.map((job) => (
                  <li key={job}>
                    <button type="button" className="layout-story-list__button">
                      {job}
                    </button>
                  </li>
                ))}
              </ul>
            }
          />
        </div>
      </div>
    )
  },
}
