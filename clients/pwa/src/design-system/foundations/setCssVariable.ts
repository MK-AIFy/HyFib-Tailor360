/**
 * The one sanctioned way to set a style value from TypeScript.
 *
 * The application runs under a Content Security Policy with a nonce, and the house rule is stricter
 * than the policy: there are no inline styles anywhere, and a `style` attribute in JSX is an inline
 * style. Everything that can be static is a class or a data attribute resolved in a CSS file.
 *
 * A few values genuinely cannot be static — the measured height of a bottom bar, the height of the
 * virtual keyboard, a progress fraction, the width of a column a user has dragged. Those are
 * written here, through the CSSOM, which the policy does not restrict because no markup is parsed:
 * `element.style.setProperty` sets a property on a live style declaration rather than injecting a
 * style attribute the browser has to re-parse.
 *
 * The rule that follows: a component may write a custom property and may never write a CSS
 * property. `setCssVariable(el, '--bottom-bar-height', '72px')` is allowed; setting `el.style.height`
 * is not, because the value would then live in TypeScript instead of in the stylesheet where a
 * reviewer, a theme and a text-size preference can all reach it.
 */

/** A CSS custom property name. The template type is what stops a plain property being passed. */
export type CssVariableName = `--${string}`

/**
 * Sets a custom property on an element, or removes it when the value is null.
 *
 * @param element the element to set the property on, usually a shell root or `document.documentElement`
 * @param name    a custom property name, which must begin with `--`
 * @param value   the value, or null to remove the property and fall back to the inherited one
 */
export function setCssVariable(
  element: HTMLElement,
  name: CssVariableName,
  value: string | number | null,
): void {
  if (value === null) {
    element.style.removeProperty(name)
    return
  }
  element.style.setProperty(name, String(value))
}

/**
 * Reads a custom property as the browser has resolved it, trimmed.
 *
 * Used to read a token back — the shell reads `--bottom-bar-height` to size a scroll container, and
 * tests read a colour token to assert a contrast pair. Returns an empty string when the property is
 * not set, which is what `getPropertyValue` does.
 */
export function readCssVariable(element: Element, name: CssVariableName): string {
  return window.getComputedStyle(element).getPropertyValue(name).trim()
}
