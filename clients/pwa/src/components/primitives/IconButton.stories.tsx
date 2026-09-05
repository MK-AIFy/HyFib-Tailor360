import type { Meta, StoryObj } from '@storybook/react-vite'
import { IconButton } from './IconButton'
import './storybook.css'

/**
 * Icon buttons.
 *
 * The `label` prop is required and there is no way round it. An unlabelled icon button is the single
 * commonest screen-reader defect a design system produces, and the type checker is a cheaper place
 * to catch it than an audit three months from now.
 */
const meta = {
  title: 'Primitives/Icon button',
  component: IconButton,
  args: { name: 'close', label: 'Dismiss this message' },
  parameters: { layout: 'centered' },
} satisfies Meta<typeof IconButton>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const Secondary: Story = {
  args: { variant: 'secondary', name: 'filter', label: 'Show filters' },
}

/**
 * A row action that names its row.
 *
 * Checklist item A11Y-60: eleven identical "Print" buttons in a delivery queue is a custody error
 * waiting to happen. Turn the screen reader on and tab across this story to hear the difference.
 */
export const NamedForItsRow: Story = {
  render: () => (
    <div className="storybook-row">
      <IconButton name="receipt" label="Print label, job J-CBE01-2627-000512-01" />
      <IconButton name="chevron-right" label="Open job J-CBE01-2627-000512-01" />
    </div>
  ),
}

export const Sizes: Story = {
  render: () => (
    <div className="storybook-row">
      <IconButton size="primary" name="scan" label="Scan a label" variant="primary" />
      <IconButton size="standard" name="search" label="Search customers" variant="secondary" />
      <IconButton size="dense" name="settings" label="Table settings" />
    </div>
  ),
}

export const Unavailable: Story = {
  args: { unavailable: true, name: 'plus', label: 'Add a garment job' },
}

/** The pseudo-locale, at 40% growth: the name is hidden text, so listen rather than look. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  args: { name: 'close', label: 'Dismiss this message' },
}
