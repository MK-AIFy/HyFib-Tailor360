import type { Meta, StoryObj } from '@storybook/react-vite'
import { Button } from './Button'
import { BUTTON_VARIANTS } from './variants'
import './storybook.css'

/**
 * Buttons.
 *
 * Switch the toolbar to the high-contrast sunlight theme and to 150% text before signing anything
 * off here: those are the settings a Tailor at the window in the afternoon actually has on, and a
 * button that only looks right at 100% in the light theme has a colour or a size hard-coded
 * somewhere.
 */
const meta = {
  title: 'Primitives/Button',
  component: Button,
  args: { children: 'Confirm order' },
  parameters: {
    layout: 'centered',
    docs: {
      description: {
        component:
          'Sizes follow docs/nfr/accessibility-localisation.md section 5: 56 px for a primary shop-floor action, 44 px for everything else, 32 px for a desktop toolbar — and the 32 px class collapses back to 44 px on a coarse pointer, so it cannot reach a phone.',
      },
    },
  },
} satisfies Meta<typeof Button>

export default meta
type Story = StoryObj<typeof meta>

export const Primary: Story = { args: { variant: 'primary' } }

export const Secondary: Story = {}

export const Subtle: Story = { args: { variant: 'subtle', children: 'Save as draft' } }

/** Different in weight and colour from its neighbours; `ButtonGroup` supplies the 24 px separation. */
export const Danger: Story = { args: { variant: 'danger', children: 'Cancel order' } }

/** Every emphasis at once, which is the fastest way to see one of them drift out of the palette. */
export const AllVariants: Story = {
  render: () => (
    <div className="storybook-row">
      {BUTTON_VARIANTS.map((variant) => (
        <Button key={variant} variant={variant}>
          {variant}
        </Button>
      ))}
    </div>
  ),
}

/**
 * The three target classes side by side. Measure them with the remote-debug method of the
 * accessibility checklist section 3.6 — this is what item A11Y-68 records numbers for.
 */
export const Sizes: Story = {
  render: () => (
    <div className="storybook-row">
      <Button size="primary" variant="primary" iconName="scan">
        Scan (56 px)
      </Button>
      <Button size="standard">Standard (44 px)</Button>
      <Button size="dense">Dense (32 px, desktop)</Button>
    </div>
  ),
}

export const WithLeadingIcon: Story = {
  args: { variant: 'primary', size: 'primary', iconName: 'scan', children: 'Scan the label' },
}

export const WithTrailingIcon: Story = {
  args: { iconName: 'chevron-right', iconPosition: 'trailing', children: 'Next step' },
}

/** Focusable, announced, and it swallows the second tap. Never `disabled` — see the component note. */
export const Busy: Story = {
  args: { variant: 'primary', busy: true, children: 'Post the invoice' },
}

export const Unavailable: Story = {
  args: { unavailable: true, children: 'Approve the variance' },
}

export const FullWidth: Story = {
  args: { variant: 'primary', size: 'primary', fullWidth: true, children: 'Take payment' },
  parameters: { layout: 'padded' },
}

/**
 * The pseudo-locale, at 40% text growth.
 *
 * Required for every component by the #50 blueprint and by criterion 5 of the Tamil enablement gate.
 * Two things to look for: does the label wrap rather than clip — a missing `⟧` means it was cut —
 * and is any text on the screen unaccented, which is how hard-coded English gives itself away.
 */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () => (
    <div className="storybook-row">
      <Button variant="primary" size="primary" iconName="check">
        Complete the phase
      </Button>
      <Button busy>Post the invoice</Button>
      <Button variant="danger">Cancel order</Button>
    </div>
  ),
}
