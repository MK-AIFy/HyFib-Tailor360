/**
 * Finding the things inside a dialog that a keyboard can reach.
 *
 * A `.ts` sibling rather than part of the hook, because "what counts as focusable" is the part of a
 * focus trap that is worth asserting on its own: the selector is short, the exclusions are the whole
 * story, and a test that renders a dialog to prove a selector is testing the wrong thing.
 */

/**
 * Everything that takes focus by default, plus anything given an explicit tab stop.
 *
 * `[tabindex="-1"]` is excluded because it is programmatically focusable but not tabbable — the
 * dialog surface itself carries one, and a trap that cycled through the surface would put an
 * invisible stop at the top of every dialog.
 */
const FOCUSABLE_SELECTOR = [
  'a[href]',
  'area[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  'summary',
  'audio[controls]',
  'video[controls]',
  '[contenteditable]:not([contenteditable="false"])',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

/**
 * The focusable elements inside `root`, in document order.
 *
 * Two deliberate non-exclusions:
 *
 *  - **`aria-disabled` elements stay in.** This design system disables a control by making it
 *    `aria-disabled` rather than `disabled`, precisely so that it keeps its place in the tab order
 *    and nobody loses their position mid-task (checklist item A11Y-66). Dropping them here would
 *    undo that inside every dialog.
 *  - **Visibility is not consulted.** Anything genuinely hidden is `hidden` or `disabled` and is
 *    already excluded; testing computed visibility would additionally mean that in a test
 *    environment with no layout — which is every test in this repository — the trap would find
 *    nothing at all and silently stop trapping.
 */
export function focusableElements(root: ParentNode): readonly HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)).filter(
    (element) => !element.hasAttribute('hidden') && element.getAttribute('aria-hidden') !== 'true',
  )
}
