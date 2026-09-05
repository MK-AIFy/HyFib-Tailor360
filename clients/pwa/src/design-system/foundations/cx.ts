/**
 * Joins class names, dropping anything falsy.
 *
 * Deliberately tiny and dependency-free: the design system's variants are expressed as data
 * attributes and CSS custom properties, so a component rarely needs more than this. There is no
 * class-name-per-variant explosion to manage, and therefore no need for a class-variance library.
 */
export function cx(...parts: readonly (string | false | null | undefined)[]): string {
  return parts
    .filter((part): part is string => typeof part === 'string' && part.length > 0)
    .join(' ')
}
