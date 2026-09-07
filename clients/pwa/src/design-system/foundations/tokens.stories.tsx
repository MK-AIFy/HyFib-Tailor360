import type { Meta, StoryObj } from '@storybook/react-vite'
import { useCallback } from 'react'
import type { ReactNode } from 'react'
import { setCssVariable } from './setCssVariable'
import {
  COLOUR_GROUPS,
  ELEVATION_TOKENS,
  SPACING_TOKENS,
  TARGET_TOKENS,
  TYPE_TOKENS,
} from './tokenGallery'
import './tokenGallery.css'

/**
 * The token gallery.
 *
 * Its job is not decoration. Switch the toolbar to the high-contrast theme and every pair here is
 * the pair a component will use in sunlight; switch to the pseudo-locale and 150% text and the
 * type scale shows what a Tamil label will do to a row. A token that looks wrong here looks wrong
 * on every screen in the product, which is the cheapest place to find that out.
 *
 * The contrast of each pair is asserted, not eyeballed: src/design-system/testing/tokenContrast.test.ts
 * reads the same token files and fails the build on a ratio below the floor.
 */

/** Sets a token's value onto an element as `--token-value`, without an inline style attribute. */
function useTokenRef(
  token: string,
  property: '--token-value' | '--token-size' | '--token-shadow',
): (element: HTMLElement | null) => void {
  return useCallback(
    (element: HTMLElement | null) => {
      if (element !== null) {
        setCssVariable(element, property, `var(${token})`)
      }
    },
    [token, property],
  )
}

function Swatch({ token }: { readonly token: string }) {
  return <span className="token-gallery__swatch" ref={useTokenRef(token, '--token-value')} />
}

function Bar({ token }: { readonly token: string }) {
  return <span className="token-gallery__bar" ref={useTokenRef(token, '--token-size')} />
}

function TypeSample({ token }: { readonly token: string }) {
  return (
    <span className="token-gallery__sample" ref={useTokenRef(token, '--token-size')}>
      Job J-CBE01-2627-000512-01
    </span>
  )
}

function Target({ token }: { readonly token: string }) {
  return <span className="token-gallery__target" ref={useTokenRef(token, '--token-size')} />
}

function Raised({ token }: { readonly token: string }) {
  return (
    <div className="token-gallery__raised" ref={useTokenRef(token, '--token-shadow')}>
      {token}
    </div>
  )
}

function Group({
  title,
  note,
  children,
}: {
  readonly title: string
  readonly note: string
  readonly children: ReactNode
}) {
  return (
    <section className="token-gallery__group">
      <h2 className="token-gallery__heading">{title}</h2>
      <p className="token-gallery__note">{note}</p>
      {children}
    </section>
  )
}

function TokenGallery() {
  return (
    <div className="token-gallery">
      {COLOUR_GROUPS.map((group) => (
        <Group key={group.title} title={group.title} note={group.note}>
          <ul className="token-gallery__list">
            {group.tokens.map((token) => (
              <li className="token-gallery__item" key={token}>
                <Swatch token={token} />
                <span className="token-gallery__name">{token}</span>
              </li>
            ))}
          </ul>
        </Group>
      ))}

      <Group title="Spacing" note="A 4 px base, so a phone layout can be dense without cramping.">
        <ul className="token-gallery__list">
          {SPACING_TOKENS.map((token) => (
            <li className="token-gallery__item" key={token}>
              <Bar token={token} />
              <span className="token-gallery__name">{token}</span>
            </li>
          ))}
        </ul>
      </Group>

      <Group
        title="Type scale"
        note="Every step is multiplied by the text-size preference, so the toolbar's 125% and 150% move all of it together."
      >
        <ul className="token-gallery__list">
          {TYPE_TOKENS.map((token) => (
            <li className="token-gallery__item" key={token}>
              <TypeSample token={token} />
              <span className="token-gallery__name">{token}</span>
            </li>
          ))}
        </ul>
      </Group>

      <Group
        title="Target sizes"
        note="56 px primary, 44 px standard, 32 px dense — desktop only — and the 24 px WCAG floor nothing on a phone should reach."
      >
        <ul className="token-gallery__list">
          {TARGET_TOKENS.map((token) => (
            <li className="token-gallery__item" key={token}>
              <Target token={token} />
              <span className="token-gallery__name">{token}</span>
            </li>
          ))}
        </ul>
      </Group>

      <Group
        title="Elevation"
        note="Every shadow disappears in the high-contrast theme, which is why a raised surface always carries a border as well."
      >
        <ul className="token-gallery__list">
          {ELEVATION_TOKENS.map((token) => (
            <li className="token-gallery__item" key={token}>
              <Raised token={token} />
            </li>
          ))}
        </ul>
      </Group>
    </div>
  )
}

const meta = {
  title: 'Foundations/Design tokens',
  component: TokenGallery,
  parameters: { layout: 'fullscreen' },
} satisfies Meta<typeof TokenGallery>

export default meta

type Story = StoryObj<typeof meta>

export const AllTokens: Story = {}

export const HighContrastSunlight: Story = {
  globals: { theme: 'contrast' },
}

export const Dark: Story = {
  globals: { theme: 'dark' },
}

export const LargestTextSize: Story = {
  globals: { textSize: '150' },
}
