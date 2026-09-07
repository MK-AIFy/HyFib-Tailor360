import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

/**
 * Reads the token files and resolves a theme's custom properties to literal values.
 *
 * Written by hand rather than with a CSS parser dependency, because the job is small and precisely
 * bounded: the token files contain nothing but custom-property declarations, and the resolver only
 * has to follow `var()` chains between them. A test that depended on a parser would be a test that
 * could fail for a reason having nothing to do with the tokens.
 *
 * Node-only: this module reads files from disk and belongs to the test tree, never to a bundle.
 */

export interface CssDeclaration {
  /** The prelude stack, outermost first: `@layer tokens`, `@media ...`, `:root[data-theme='dark']`. */
  readonly selectors: readonly string[]
  readonly name: string
  readonly value: string
}

/**
 * Extracts every custom-property declaration with the nesting it sits in.
 *
 * A hand-rolled scan: comments are stripped, then the text is walked once, pushing a frame on `{`
 * and popping on `}`, so a declaration always knows the at-rules and selectors above it.
 */
export function parseCssCustomProperties(css: string): readonly CssDeclaration[] {
  const withoutComments = css.replace(/\/\*[\s\S]*?\*\//g, '')
  const declarations: CssDeclaration[] = []
  const stack: string[] = []
  let buffer = ''

  for (const character of withoutComments) {
    if (character === '{') {
      stack.push(buffer.trim().replace(/\s+/g, ' '))
      buffer = ''
      continue
    }
    if (character === '}') {
      recordDeclaration(declarations, stack, buffer)
      buffer = ''
      stack.pop()
      continue
    }
    if (character === ';') {
      recordDeclaration(declarations, stack, buffer)
      buffer = ''
      continue
    }
    buffer += character
  }

  return declarations
}

function recordDeclaration(into: CssDeclaration[], stack: readonly string[], raw: string): void {
  const text = raw.trim()
  if (!text.startsWith('--')) {
    return
  }
  const separator = text.indexOf(':')
  if (separator < 0) {
    return
  }
  into.push({
    selectors: [...stack],
    name: text.slice(0, separator).trim(),
    value: text
      .slice(separator + 1)
      .trim()
      .replace(/\s+/g, ' '),
  })
}

/** True when no prelude in the stack is an at-rule with a condition, such as a media query. */
export function isUnconditional(declaration: CssDeclaration): boolean {
  return !declaration.selectors.some((selector) => selector.startsWith('@media'))
}

/** The innermost selector — the one that actually matches an element. */
export function ownSelector(declaration: CssDeclaration): string {
  return declaration.selectors[declaration.selectors.length - 1] ?? ''
}

const VAR_PATTERN = /var\(\s*(--[a-z0-9-]+)\s*(?:,\s*([^)]*))?\)/i

/**
 * Resolves `var()` references against a map of raw values.
 *
 * Follows chains — a semantic token pointing at a primitive pointing at a literal — and gives up
 * with a clear error rather than looping if the tokens ever reference each other in a cycle.
 */
export function resolveValue(
  value: string,
  values: ReadonlyMap<string, string>,
  depth = 0,
): string {
  if (depth > 16) {
    throw new Error(`Custom property reference is too deep or circular: ${value}`)
  }
  const match = VAR_PATTERN.exec(value)
  if (match === null) {
    return value.trim()
  }
  const [whole, name, fallback] = match
  const referenced = name === undefined ? undefined : values.get(name)
  const replacement = referenced ?? fallback ?? ''
  return resolveValue(value.replace(whole, replacement), values, depth + 1)
}

/**
 * The themes a token file can be read in. `light` is the bare `:root` palette; the other two are
 * that palette with the explicit `[data-theme]` block applied over it, which is exactly what a
 * browser does when the shell writes the stored preference onto `<html>`.
 */
export const RESOLVABLE_THEMES = ['light', 'dark', 'contrast'] as const

export type ResolvableTheme = (typeof RESOLVABLE_THEMES)[number]

function themeSelector(theme: ResolvableTheme): string {
  return theme === 'light' ? ':root' : `:root[data-theme='${theme}']`
}

/**
 * Resolves every custom property for one theme, following the same order the cascade would:
 * unconditional `:root` declarations first, then the explicit `[data-theme]` block.
 *
 * Media-query blocks are skipped on purpose. They carry the same values as the attribute blocks —
 * `tokenContrast.test.ts` asserts that the pair cannot drift — and including them here would make
 * the result depend on which media conditions a headless run happens to report.
 */
export function resolveTheme(
  declarations: readonly CssDeclaration[],
  theme: ResolvableTheme,
): ReadonlyMap<string, string> {
  const raw = new Map<string, string>()
  const wanted = themeSelector(theme)

  for (const declaration of declarations) {
    if (!isUnconditional(declaration)) {
      continue
    }
    const selector = ownSelector(declaration)
    if (selector === ':root' || selector === wanted) {
      raw.set(declaration.name, declaration.value)
    }
  }

  const resolved = new Map<string, string>()
  for (const [name, value] of raw) {
    resolved.set(name, resolveValue(value, raw))
  }
  return resolved
}

const TOKEN_FILES = ['../../styles/tokens.css', '../../styles/themes.css'] as const

/** Reads the design system's own token files and resolves them for one theme. */
export function readDesignTokens(theme: ResolvableTheme): ReadonlyMap<string, string> {
  const css = TOKEN_FILES.map((relative) =>
    readFileSync(fileURLToPath(new URL(relative, import.meta.url)), 'utf8'),
  ).join('\n')
  return resolveTheme(parseCssCustomProperties(css), theme)
}

/** Reads both token files as one string, for tests that inspect the declarations directly. */
export function readTokenSource(): string {
  return TOKEN_FILES.map((relative) =>
    readFileSync(fileURLToPath(new URL(relative, import.meta.url)), 'utf8'),
  ).join('\n')
}
