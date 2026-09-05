import { describe, expect, it } from 'vitest'
import { focusableElements } from './focusable'

function build(html: string): HTMLElement {
  const root = document.createElement('div')
  root.innerHTML = html
  return root
}

describe('focusableElements', () => {
  it('finds the ordinary controls, in document order', () => {
    const root = build(`
      <a href="/help">Help</a>
      <button>One</button>
      <input />
      <select></select>
      <textarea></textarea>
    `)

    expect(focusableElements(root).map((element) => element.tagName)).toEqual([
      'A',
      'BUTTON',
      'INPUT',
      'SELECT',
      'TEXTAREA',
    ])
  })

  it('skips what the browser skips', () => {
    const root = build(`
      <button disabled>Gone</button>
      <input type="hidden" />
      <a>No href</a>
      <button hidden>Hidden</button>
      <button aria-hidden="true">Hidden from the tree</button>
    `)

    expect(focusableElements(root)).toHaveLength(0)
  })

  it('keeps an aria-disabled control in the cycle', () => {
    // This design system disables a control by making it aria-disabled rather than disabled,
    // precisely so it keeps its place and nobody loses their position mid-task (A11Y-66). Dropping
    // them here would undo that inside every dialog.
    const root = build('<button aria-disabled="true">Busy</button>')

    expect(focusableElements(root)).toHaveLength(1)
  })

  it('ignores a programmatic-only tab stop', () => {
    // The dialog surface itself carries tabindex="-1"; a trap that cycled through it would put an
    // invisible stop at the top of every dialog.
    const root = build('<div tabindex="-1">Surface</div><div tabindex="0">Stop</div>')

    expect(focusableElements(root)).toHaveLength(1)
  })
})
