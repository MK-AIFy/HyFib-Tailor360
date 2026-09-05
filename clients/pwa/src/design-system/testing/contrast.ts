/**
 * WCAG contrast arithmetic.
 *
 * This is the one accessibility check axe-core genuinely cannot make in jsdom: jsdom has no layout
 * and no compositing, so it cannot tell what colour a pixel ends up. Computing the ratio straight
 * from the token values is both possible and stronger — it proves the pair before any component has
 * had the chance to put them together.
 *
 * The formula is WCAG 2.x's: relative luminance from sRGB with the piecewise gamma expansion, then
 * (lighter + 0.05) / (darker + 0.05).
 */

/** A colour with each channel in the 0..1 range. */
export interface Srgb {
  readonly red: number
  readonly green: number
  readonly blue: number
}

const HEX_PATTERN = /^#?(?:[0-9a-f]{3}|[0-9a-f]{6})$/i

/**
 * Parses `#rgb` or `#rrggbb`, with or without the hash. Throws on anything else, because a token
 * value this function cannot read is a defect in the token file, not a case to skip quietly.
 */
export function parseHexColour(value: string): Srgb {
  const trimmed = value.trim()
  if (!HEX_PATTERN.test(trimmed)) {
    throw new Error(`Not a hex colour: ${JSON.stringify(value)}`)
  }

  const digits = trimmed.replace('#', '')
  const expanded =
    digits.length === 3
      ? digits
          .split('')
          .map((digit) => `${digit}${digit}`)
          .join('')
      : digits

  const channel = (start: number): number =>
    Number.parseInt(expanded.slice(start, start + 2), 16) / 255

  return { red: channel(0), green: channel(2), blue: channel(4) }
}

function expandGamma(channel: number): number {
  return channel <= 0.03928 ? channel / 12.92 : Math.pow((channel + 0.055) / 1.055, 2.4)
}

/** Relative luminance, 0 for black and 1 for white. */
export function relativeLuminance(colour: Srgb): number {
  return (
    0.2126 * expandGamma(colour.red) +
    0.7152 * expandGamma(colour.green) +
    0.0722 * expandGamma(colour.blue)
  )
}

/** The contrast ratio between two colours, from 1 (identical) to 21 (black on white). */
export function contrastRatio(foreground: string, background: string): number {
  const first = relativeLuminance(parseHexColour(foreground))
  const second = relativeLuminance(parseHexColour(background))
  const lighter = Math.max(first, second)
  const darker = Math.min(first, second)
  return (lighter + 0.05) / (darker + 0.05)
}

/**
 * The floors this product holds itself to.
 *
 *   text       4.5:1 — WCAG 1.4.3, the hard floor everywhere (AL-02)
 *   largeText  3:1   — 1.4.3 for 18.66 px bold or 24 px
 *   nonText    3:1   — 1.4.11 for control borders, focus rings, meaningful icons
 *   shopFloor  7:1   — 1.4.6 Contrast (Enhanced), adopted for the primary shop-floor text a Tailor
 *                      reads in sunlight at arm's length: job number, due date, phase name, scan
 *                      result, amount due
 */
export const CONTRAST_FLOORS = {
  text: 4.5,
  largeText: 3,
  nonText: 3,
  shopFloor: 7,
} as const

export type ContrastFloor = keyof typeof CONTRAST_FLOORS

/** Rounds to two decimals for a readable assertion message. */
export function formatRatio(ratio: number): string {
  return ratio.toFixed(2)
}
