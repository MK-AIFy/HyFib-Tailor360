import type { Meta, StoryObj } from '@storybook/react-vite'
import { TextLink } from './TextLink'
import './storybook.css'

/**
 * Text links.
 *
 * Underlined at rest, always. Inside a paragraph the underline is the only non-colour signal a link
 * has, and 1.4.1 Use of Colour does not allow it to be removed — which is also what makes a link
 * findable at the counter in the afternoon sun.
 */
const meta = {
  title: 'Primitives/Text link',
  component: TextLink,
  args: { href: '/help/scanning', children: 'the scanning procedure' },
  parameters: { layout: 'padded' },
} satisfies Meta<typeof TextLink>

export default meta
type Story = StoryObj<typeof meta>

export const InAParagraph: Story = {
  render: () => (
    <p>
      A manual entry needs a reason. Read{' '}
      <TextLink href="/help/scanning">the scanning procedure</TextLink> before overriding a scan.
    </p>
  ),
}

/** Announced as a change of context, and hardened with `noopener` for a shared counter device. */
export const External: Story = {
  args: { href: 'https://example.invalid/supplier', external: true, children: 'Supplier portal' },
}

export const Quiet: Story = {
  render: () => (
    <p>
      Customer:{' '}
      <TextLink href="/customers/1" quiet>
        Lakshmi Narayanan
      </TextLink>
    </p>
  ),
}

export const Telephone: Story = {
  args: { href: 'tel:+914220000000', children: '+91 422 000 0000' },
}

/** At 40% growth the link wraps inside its sentence rather than pushing the paragraph sideways. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () => (
    <p className="storybook-phone">
      A manual entry needs a reason. Read{' '}
      <TextLink href="https://example.invalid" external>
        the scanning procedure
      </TextLink>{' '}
      before overriding a scan.
    </p>
  ),
}
