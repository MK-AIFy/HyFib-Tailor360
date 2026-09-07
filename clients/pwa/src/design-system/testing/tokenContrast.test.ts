import { describe, expect, it } from 'vitest'
import { CONTRAST_FLOORS, contrastRatio, formatRatio } from './contrast'
import {
  RESOLVABLE_THEMES,
  isUnconditional,
  ownSelector,
  parseCssCustomProperties,
  readDesignTokens,
  readTokenSource,
} from './cssTokens'
import type { ResolvableTheme } from './cssTokens'

/**
 * The token-pair contrast test that docs/nfr/accessibility-localisation.md section 4.1 asks the
 * design system to carry.
 *
 * It reads src/styles/tokens.css and src/styles/themes.css, resolves each theme the way a browser
 * would, and asserts the ratio of every pair a component is allowed to put together. Changing a
 * colour without running this is how a shop floor loses its screens in the afternoon sun.
 */

function colour(tokens: ReadonlyMap<string, string>, name: string): string {
  const value = tokens.get(name)
  if (value === undefined) {
    throw new Error(`Token ${name} is not defined`)
  }
  return value
}

interface Pair {
  readonly foreground: string
  readonly background: string
  readonly floor: keyof typeof CONTRAST_FLOORS
}

/**
 * Primary shop-floor text — job number, due date, phase name, scan result, amount due — read at
 * arm's length in sunlight. AL-02 adopts 1.4.6 Contrast (Enhanced) for exactly this text.
 */
const SHOP_FLOOR_PAIRS: readonly Pair[] = [
  { foreground: '--colour-ink', background: '--colour-surface', floor: 'shopFloor' },
  { foreground: '--colour-ink', background: '--colour-surface-sunken', floor: 'shopFloor' },
  { foreground: '--colour-ink', background: '--colour-surface-raised', floor: 'shopFloor' },
  { foreground: '--colour-ink-on-brand', background: '--colour-brand', floor: 'shopFloor' },
  { foreground: '--colour-brand-contrast', background: '--colour-brand', floor: 'shopFloor' },
]

/** Body text and every status pair. 1.4.3, the 4.5:1 hard floor. */
const TEXT_PAIRS: readonly Pair[] = [
  { foreground: '--colour-ink-muted', background: '--colour-surface', floor: 'text' },
  { foreground: '--colour-ink-muted', background: '--colour-surface-sunken', floor: 'text' },
  { foreground: '--colour-ink-muted', background: '--colour-surface-raised', floor: 'text' },
  { foreground: '--colour-ink-muted', background: '--colour-surface-selected', floor: 'text' },
  { foreground: '--colour-ink-muted', background: '--colour-surface-hover', floor: 'text' },
  { foreground: '--colour-ink', background: '--colour-surface-selected', floor: 'text' },
  { foreground: '--colour-link', background: '--colour-surface', floor: 'text' },
  { foreground: '--colour-link', background: '--colour-surface-sunken', floor: 'text' },
  { foreground: '--colour-link-visited', background: '--colour-surface', floor: 'text' },
  { foreground: '--colour-danger-ink', background: '--colour-surface', floor: 'text' },
  { foreground: '--colour-danger-ink', background: '--colour-danger-surface', floor: 'text' },
  { foreground: '--colour-success-ink', background: '--colour-surface', floor: 'text' },
  { foreground: '--colour-success-ink', background: '--colour-success-surface', floor: 'text' },
  { foreground: '--colour-warning-ink', background: '--colour-warning-surface', floor: 'text' },
  { foreground: '--colour-info-ink', background: '--colour-surface', floor: 'text' },
  { foreground: '--colour-info-ink', background: '--colour-info-surface', floor: 'text' },
  { foreground: '--colour-accent-ink', background: '--colour-accent', floor: 'text' },
]

/**
 * Control borders and status borders. 1.4.11 Non-text Contrast, 3:1.
 *
 * `--colour-border-subtle` is deliberately absent: it draws dividers, which are decoration and are
 * exempt. That is the whole reason the scaffold's single `--colour-border` became two tokens.
 */
const NON_TEXT_PAIRS: readonly Pair[] = [
  { foreground: '--colour-border', background: '--colour-surface', floor: 'nonText' },
  { foreground: '--colour-border', background: '--colour-surface-sunken', floor: 'nonText' },
  { foreground: '--colour-border', background: '--colour-surface-raised', floor: 'nonText' },
  { foreground: '--colour-border-strong', background: '--colour-surface', floor: 'nonText' },
  { foreground: '--colour-danger-border', background: '--colour-surface', floor: 'nonText' },
  { foreground: '--colour-success-border', background: '--colour-surface', floor: 'nonText' },
  { foreground: '--colour-info-border', background: '--colour-surface', floor: 'nonText' },
  // Disabled text is exempt from 1.4.3, but it still has to be seen to be recognised as disabled.
  { foreground: '--colour-ink-disabled', background: '--colour-surface', floor: 'nonText' },
]

const ALL_PAIRS = [...SHOP_FLOOR_PAIRS, ...TEXT_PAIRS, ...NON_TEXT_PAIRS]

describe.each(RESOLVABLE_THEMES)('%s theme token contrast', (theme: ResolvableTheme) => {
  const tokens = readDesignTokens(theme)

  it.each(ALL_PAIRS)(
    '$foreground on $background meets the $floor floor',
    ({ foreground, background, floor }) => {
      const ratio = contrastRatio(colour(tokens, foreground), colour(tokens, background))
      const required = CONTRAST_FLOORS[floor]
      expect(
        ratio,
        `${foreground} (${colour(tokens, foreground)}) on ${background} ` +
          `(${colour(tokens, background)}) is ${formatRatio(ratio)}:1, below ${String(required)}:1`,
      ).toBeGreaterThanOrEqual(required)
    },
  )

  /**
   * WCAG 2.4.13 Focus Appearance, adopted as AAA. One colour cannot contrast with both a white
   * field and the dark brand header, so the ring is two: a coloured inner outline and a contrasting
   * outer halo. The property that makes that work is asserted here rather than assumed — the two
   * rings must contrast with each other, and on every surface at least one of them must reach 3:1.
   */
  describe('the two-ring focus indicator', () => {
    const ring = colour(tokens, '--colour-focus-ring')
    const halo = colour(tokens, '--colour-focus-ring-contrast')

    it('has two rings that contrast with each other', () => {
      expect(contrastRatio(ring, halo)).toBeGreaterThanOrEqual(CONTRAST_FLOORS.nonText)
    })

    it.each([
      '--colour-surface',
      '--colour-surface-sunken',
      '--colour-surface-raised',
      '--colour-brand',
      '--colour-warning-surface',
      '--colour-accent',
    ])('is visible against %s', (surfaceToken) => {
      const surface = colour(tokens, surfaceToken)
      const best = Math.max(contrastRatio(ring, surface), contrastRatio(halo, surface))
      expect(
        best,
        `neither the focus ring (${ring}) nor its halo (${halo}) reaches 3:1 on ` +
          `${surfaceToken} (${surface}); the best is ${formatRatio(best)}:1`,
      ).toBeGreaterThanOrEqual(CONTRAST_FLOORS.nonText)
    })
  })

  it('sets a target size no smaller than the WCAG 2.5.8 floor', () => {
    // AL-03: 56 primary, 44 standard, 32 dense-desktop-only, 24 absolute floor.
    expect(tokens.get('--target-primary')).toBe('56px')
    expect(tokens.get('--target-standard')).toBe('44px')
    expect(tokens.get('--target-dense')).toBe('32px')
    expect(tokens.get('--target-floor')).toBe('24px')
  })

  it('keeps the focus indicator at least 2 px thick', () => {
    const width = Number.parseInt(tokens.get('--focus-ring-width') ?? '0', 10)
    expect(width).toBeGreaterThanOrEqual(2)
  })
})

/**
 * The dark and high-contrast palettes are written twice — once inside a media query guarded so an
 * explicit choice still wins, once under the [data-theme] attribute. That duplication is required
 * by CSS and is exactly the kind of thing that drifts, so it is asserted instead of trusted.
 */
describe('the guarded media block and the explicit attribute block agree', () => {
  const declarations = parseCssCustomProperties(readTokenSource())

  /** Values declared inside the media query whose condition contains the given feature. */
  function fromMediaBlock(feature: string): Map<string, string> {
    const values = new Map<string, string>()
    for (const declaration of declarations) {
      const media = declaration.selectors.find((selector) => selector.startsWith('@media'))
      if (media === undefined || !media.includes(feature)) {
        continue
      }
      if (ownSelector(declaration).startsWith(':root:not(')) {
        values.set(declaration.name, declaration.value)
      }
    }
    return values
  }

  /** Values declared under the explicit [data-theme] attribute, outside any media query. */
  function fromAttributeBlock(theme: string): Map<string, string> {
    const values = new Map<string, string>()
    for (const declaration of declarations) {
      if (!isUnconditional(declaration)) {
        continue
      }
      if (ownSelector(declaration) === `:root[data-theme='${theme}']`) {
        values.set(declaration.name, declaration.value)
      }
    }
    return values
  }

  it.each([
    ['dark', 'prefers-color-scheme'],
    ['contrast', 'prefers-contrast'],
  ])('%s', (theme, feature) => {
    const media = fromMediaBlock(feature)
    const attribute = fromAttributeBlock(theme)

    expect(attribute.size, `the ${theme} attribute block declares nothing`).toBeGreaterThan(0)
    expect(media.size, `the ${theme} media block declares nothing`).toBe(attribute.size)

    for (const [name, value] of attribute) {
      expect(media.has(name), `${name} is missing from the ${theme} media block`).toBe(true)
      expect(media.get(name), `${name} differs between the two ${theme} blocks`).toBe(value)
    }
  })
})
