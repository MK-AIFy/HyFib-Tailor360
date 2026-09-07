import { enIN } from './en-IN'
import type { MessageCatalogue } from './en-IN'

/**
 * The pseudo-locale.
 *
 * docs/nfr/accessibility-localisation.md section 4.2 requires layouts to tolerate **40% text
 * growth**, and criterion 5 of the Tamil enablement gate makes "no layout breaks in the pseudo-locale
 * and at 40% growth" a condition of switching Tamil on at all. The #50 blueprint therefore requires
 * a pseudo-locale story for every component.
 *
 * What the transformation does, and why each part earns its place:
 *
 *  - **Accents every letter.** `Confirm order` becomes `Çöñƒïrm örðér`. Still readable to a
 *    reviewer, and instantly obvious when a string was not taken from the catalogue: unaccented text
 *    on a pseudo-locale screen is hard-coded English, which is the defect the story is looking for.
 *  - **Grows the text to 140%.** Vowels are doubled and the remainder is padded, so a label that
 *    only just fits in English visibly overflows here — which is what Tamil will do in production.
 *  - **Brackets the whole string.** `⟦…⟧` makes truncation visible: a missing `⟧` means the label
 *    was clipped, not merely wrapped.
 *  - **Leaves ICU arguments alone.** Everything from a `{` to its matching `}` is copied verbatim,
 *    nesting included, so `{count, plural, one {# job} other {# jobs}}` still compiles.
 *
 * The locale is `en-XA`, the conventional private-use tag for a pseudo-locale. It is deliberately
 * not in `SUPPORTED_LOCALES`: it is a development and review tool, never offered to staff.
 */
export const PSEUDO_LOCALE = 'en-XA'

/** How much longer the pseudo text is than the English it came from. */
export const PSEUDO_GROWTH_FACTOR = 1.4

const ACCENTS: Readonly<Record<string, string>> = {
  a: 'á',
  b: 'ƀ',
  c: 'ç',
  d: 'ð',
  e: 'é',
  f: 'ƒ',
  g: 'ĝ',
  h: 'ĥ',
  i: 'ï',
  j: 'ĵ',
  k: 'ķ',
  l: 'ļ',
  m: 'm',
  n: 'ñ',
  o: 'ö',
  p: 'þ',
  q: 'q',
  r: 'r',
  s: 'š',
  t: 'ţ',
  u: 'ü',
  v: 'v',
  w: 'ŵ',
  x: 'x',
  y: 'ý',
  z: 'ž',
  A: 'Á',
  B: 'Ɓ',
  C: 'Ç',
  D: 'Ð',
  E: 'É',
  F: 'Ƒ',
  G: 'Ĝ',
  H: 'Ĥ',
  I: 'Ï',
  J: 'Ĵ',
  K: 'Ķ',
  L: 'Ļ',
  M: 'M',
  N: 'Ñ',
  O: 'Ö',
  P: 'Þ',
  Q: 'Q',
  R: 'R',
  S: 'Š',
  T: 'Ţ',
  U: 'Ü',
  V: 'V',
  W: 'Ŵ',
  X: 'X',
  Y: 'Ý',
  Z: 'Ž',
}

const VOWELS = new Set(['a', 'e', 'i', 'o', 'u', 'A', 'E', 'I', 'O', 'U'])

/**
 * Pseudo-localises one message, leaving every ICU argument — braces and all their nesting —
 * untouched.
 */
export function pseudoLocaliseMessage(message: string): string {
  let transformed = ''
  let depth = 0
  let translatableLength = 0

  for (const character of message) {
    if (character === '{') {
      depth += 1
    }
    if (depth > 0) {
      transformed += character
      if (character === '}') {
        depth -= 1
      }
      continue
    }

    translatableLength += 1
    const accented = ACCENTS[character]
    if (accented === undefined) {
      transformed += character
      continue
    }
    // Doubling the vowels grows the text where a real translation grows it — in the middle of a
    // word — rather than only at the end.
    transformed += VOWELS.has(character) ? `${accented}${accented}` : accented
  }

  const target = Math.ceil(translatableLength * PSEUDO_GROWTH_FACTOR)
  const padding = Math.max(0, target - transformed.length)
  return `⟦${transformed}${'·'.repeat(padding)}⟧`
}

/** Pseudo-localises a whole catalogue. */
export function buildPseudoCatalogue(catalogue: MessageCatalogue): MessageCatalogue {
  const entries = Object.entries(catalogue).map(
    ([key, value]) => [key, pseudoLocaliseMessage(value)] as const,
  )
  return Object.fromEntries(entries) as MessageCatalogue
}

let cached: MessageCatalogue | undefined

/**
 * The pseudo catalogue, built on first use.
 *
 * Lazily, so that a production bundle that never opens a pseudo-locale story never spends the time
 * or the memory building one.
 */
export function pseudoCatalogue(): MessageCatalogue {
  cached ??= buildPseudoCatalogue(enIN)
  return cached
}
