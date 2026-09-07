import type { Meta, StoryObj } from '@storybook/react-vite'
import { Button } from './Button'
import { ButtonGroup } from './ButtonGroup'
import './storybook.css'

/**
 * Button groups, which is where the spacing half of the sizing rule is spent.
 *
 * A size token on a button sets the target; a gap on the group sets the spacing — 12 px between
 * primary shop-floor actions and 8 px between standard controls. Checklist item A11Y-68 measures
 * both, and A11Y-69 measures the 24 px that separates a destructive action from the frequent one
 * beside it.
 */
const meta = {
  title: 'Primitives/Button group',
  component: ButtonGroup,
  args: { children: null },
  parameters: { layout: 'padded' },
} satisfies Meta<typeof ButtonGroup>

export default meta
type Story = StoryObj<typeof meta>

export const FormActions: Story = {
  render: () => (
    <ButtonGroup>
      <Button variant="primary">Save measurements</Button>
      <Button variant="subtle">Cancel</Button>
    </ButtonGroup>
  ),
}

/**
 * The separation rule, visible.
 *
 * Dispatch does not sit beside Cancel order. Where they must share a screen they are at least 24 px
 * apart and differ in weight and colour, and the destructive one carries a confirmation — the
 * confirmation itself is the overlays family's three-tier `ConfirmDialog`.
 */
export const WithDestructiveAction: Story = {
  render: () => (
    <ButtonGroup destructiveAction={<Button variant="danger">Cancel order</Button>}>
      <Button variant="primary">Confirm order</Button>
      <Button>Save as draft</Button>
    </ButtonGroup>
  ),
}

export const PrimarySpacing: Story = {
  render: () => (
    <ButtonGroup size="primary">
      <Button size="primary" variant="primary" iconName="scan">
        Scan in
      </Button>
      <Button size="primary" iconName="check">
        Complete phase
      </Button>
    </ButtonGroup>
  ),
}

/** Stacked, as a phone bottom sheet renders it. */
export const Vertical: Story = {
  render: () => (
    <div className="storybook-phone">
      <ButtonGroup orientation="vertical" size="primary">
        <Button size="primary" variant="primary" fullWidth>
          Take payment
        </Button>
        <Button size="primary" fullWidth>
          Print the estimate
        </Button>
      </ButtonGroup>
    </div>
  ),
}

export const Named: Story = {
  render: () => (
    <ButtonGroup label="Job card actions">
      <Button iconName="receipt">Print label</Button>
      <Button iconName="external-link">Open job card</Button>
    </ButtonGroup>
  ),
}

/** At 40% growth the row wraps rather than clipping, and the 24 px separation still holds. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () => (
    <ButtonGroup destructiveAction={<Button variant="danger">Cancel order</Button>}>
      <Button variant="primary">Confirm order</Button>
      <Button busy>Save as draft</Button>
    </ButtonGroup>
  ),
}
