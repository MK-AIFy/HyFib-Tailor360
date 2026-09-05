import type { Meta, StoryObj } from '@storybook/react-vite'
import { Icon } from './Icon'
import { ICON_NAMES } from './icons'
import './storybook.css'
import './iconGallery.css'

/**
 * The icon set.
 *
 * Every glyph is `aria-hidden` and every glyph follows `currentColor`, which is what lets an icon
 * inherit the contrast of the word beside it in all three themes without a single value being kept
 * in step by hand. Switch to the high-contrast theme: the stroke thickens, because a 1.75 px
 * hairline is the first thing sunlight erases.
 */
const meta = {
  title: 'Primitives/Icon',
  component: Icon,
  args: { name: 'scan' },
  parameters: { layout: 'centered' },
} satisfies Meta<typeof Icon>

export default meta
type Story = StoryObj<typeof meta>

export const Single: Story = {}

/** The whole set, named. A glyph nobody can name is a glyph nobody should ship. */
export const Gallery: Story = {
  parameters: { layout: 'padded' },
  render: () => (
    <ul className="icon-gallery">
      {ICON_NAMES.map((name) => (
        <li key={name} className="icon-gallery__item">
          <Icon name={name} className="icon-gallery__glyph" />
          <code className="icon-gallery__name">{name}</code>
        </li>
      ))}
    </ul>
  ),
}

/**
 * Statuses that appear in the same list, side by side.
 *
 * Read this row in greyscale. If two of these shapes were the same, the word beside them would be
 * doing all the work and the icon would be decoration pretending to be information.
 */
export const ShapesThatMustDiffer: Story = {
  parameters: { layout: 'padded' },
  render: () => (
    <div className="storybook-row">
      <Icon name="check-circle" className="icon-gallery__glyph" />
      <Icon name="rupee" className="icon-gallery__glyph" />
      <Icon name="check" className="icon-gallery__glyph" />
      <Icon name="x-circle" className="icon-gallery__glyph" />
      <Icon name="close" className="icon-gallery__glyph" />
      <Icon name="clock" className="icon-gallery__glyph" />
      <Icon name="alert-triangle" className="icon-gallery__glyph" />
    </div>
  ),
}

/**
 * The pseudo-locale story for the icon set is a story about what is *not* here: an icon carries no
 * text, so nothing on this screen grows. That is the point — the growth happens in the label beside
 * it, which is why no component in this system ships a glyph without one.
 */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  args: { name: 'help' },
}
