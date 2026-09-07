import { useCallback, useEffect, useRef } from 'react'
import type { KeyboardEvent, RefObject } from 'react'
import { focusableElements } from './focusable'

export interface DialogBehaviourOptions {
  /** Called by Escape, and by anything else that closes the dialog. */
  readonly onClose: () => void
  /**
   * Where focus should land when the dialog opens. Defaults to the first focusable element, which
   * is right for almost everything; a destructive confirmation may prefer to point it at Cancel.
   * Never at the confirming control: a dialog that opens with Confirm focused is a dialog that a
   * held Enter key answers.
   */
  readonly initialFocusRef?: RefObject<HTMLElement | null>
}

export interface DialogBehaviour {
  /** Attach to the dialog surface — the element carrying `role="dialog"`. */
  readonly surfaceRef: RefObject<HTMLDivElement | null>
  /** Attach to the same element. Handles Escape and the tab cycle. */
  readonly onKeyDown: (event: KeyboardEvent<HTMLElement>) => void
}

/** Attribute marking a dialog's outermost node, so the rest of the document can be made inert. */
export const DIALOG_ROOT_ATTRIBUTE = 'data-dialog-root'

/** The class that stops the page behind a modal scrolling. Defined in dialogs.css. */
const SCROLL_LOCK_CLASS = 'has-open-dialog'

/**
 * How many modals are open.
 *
 * Module state, because the lock belongs to the document rather than to any one dialog: closing the
 * inner of two stacked dialogs must not release the page behind both of them. The lock is a class
 * rather than a written `overflow` because the house rule is that a component may set a custom
 * property and may never set a CSS property — the value belongs in the stylesheet, where a theme and
 * a text-size preference can reach it.
 */
let openModalCount = 0

/**
 * Everything a modal owes the keyboard, in one place.
 *
 * The four obligations are checklist items A11Y-61 to A11Y-64, and they are the four things a
 * hand-rolled dialog gets wrong one at a time:
 *
 *  1. **Focus moves in** when it opens, so the dialog is not invisible and the screen behind it does
 *     not merely appear to have stopped working.
 *  2. **Tab cycles only within it**, and the rest of the document is `inert`, so a confirmation
 *     cannot be answered for the record behind it.
 *  3. **Escape closes it** — always, without exception. 2.1.2 No Keyboard Trap is absolute, and this
 *     hook is what makes it true of the camera overlay as well as of a confirmation.
 *  4. **Focus returns** to the control that opened it. Focus dumped on the document body means
 *     re-tabbing the whole screen, one-handed, with a garment in the other hand.
 *
 * ## Why `inert` and not `aria-hidden`
 *
 * `inert` removes an element from the tab order *and* from the accessibility tree, which is exactly
 * what is wanted and which `aria-hidden` does only half of. `aria-hidden` over content that is still
 * focusable is itself an accessibility violation — axe reports it as `aria-hidden-focus` — because a
 * keyboard user can then land on a control no screen reader will describe. Every browser in
 * docs/nfr/support-matrix.md supports `inert`; the attribute is set rather than the property so that
 * the state is visible in the DOM to a test and to anybody inspecting the page.
 *
 * ## Why this is a hook and not a `<dialog>` element
 *
 * The native `<dialog showModal>` gives most of this for free, and would be the right answer if it
 * were available everywhere it has to run. It is not: `showModal` is unimplemented in the test
 * environment, so every dialog test would be asserting against a fallback path that never ships.
 * A component whose behaviour under test differs from its behaviour in production is worth less than
 * the sixty lines it saves.
 */
export function useDialogBehaviour({
  onClose,
  initialFocusRef,
}: DialogBehaviourOptions): DialogBehaviour {
  const surfaceRef = useRef<HTMLDivElement | null>(null)
  const openerRef = useRef<Element | null>(null)
  // The close callback changes identity on every render of most callers. Held in a ref, and written
  // in an effect rather than during render, so the handler below can stay mounted for the life of
  // the dialog instead of being torn down and rebuilt on every keystroke.
  const closeRef = useRef(onClose)
  useEffect(() => {
    closeRef.current = onClose
  })

  // Obligation 1 and 4: remember what opened this, and give focus back to it on the way out.
  useEffect(() => {
    openerRef.current = document.activeElement

    return () => {
      const opener = openerRef.current
      if (opener instanceof HTMLElement && opener.isConnected) {
        opener.focus()
      }
    }
  }, [])

  useEffect(() => {
    const surface = surfaceRef.current
    if (surface === null) {
      return
    }
    const preferred = initialFocusRef?.current
    const target = preferred ?? focusableElements(surface)[0] ?? surface
    target.focus()
  }, [initialFocusRef])

  // The page behind a modal does not scroll under it — on a phone, a sheet over a scrolling list is
  // how a person loses their place in a queue they were halfway down.
  useEffect(() => {
    openModalCount += 1
    document.documentElement.classList.add(SCROLL_LOCK_CLASS)

    return () => {
      openModalCount = Math.max(0, openModalCount - 1)
      if (openModalCount === 0) {
        document.documentElement.classList.remove(SCROLL_LOCK_CLASS)
      }
    }
  }, [])

  // Obligation 2: everything else in the document becomes inert while this is open.
  useEffect(() => {
    const surface = surfaceRef.current
    if (surface === null) {
      return undefined
    }
    const root = surface.closest(`[${DIALOG_ROOT_ATTRIBUTE}]`) ?? surface
    const madeInert = Array.from(document.body.children).filter(
      (element) => element !== root && !element.contains(root) && !element.hasAttribute('inert'),
    )
    for (const element of madeInert) {
      element.setAttribute('inert', '')
    }

    return () => {
      // Only what this dialog made inert is released, so closing the inner one of two stacked
      // dialogs leaves the outer one's own inert background alone.
      for (const element of madeInert) {
        element.removeAttribute('inert')
      }
    }
  }, [])

  const onKeyDown = useCallback((event: KeyboardEvent<HTMLElement>) => {
    if (event.key === 'Escape') {
      event.stopPropagation()
      closeRef.current()
      return
    }

    if (event.key !== 'Tab') {
      return
    }

    const surface = surfaceRef.current
    if (surface === null) {
      return
    }

    const focusable = focusableElements(surface)
    const first = focusable[0]
    const last = focusable[focusable.length - 1]
    if (first === undefined || last === undefined) {
      // A dialog with nothing focusable in it: keep the tab stop on the surface rather than letting
      // focus escape to the inert document behind, where nothing can be reached at all.
      event.preventDefault()
      return
    }

    const active = document.activeElement
    if (event.shiftKey && (active === first || active === surface)) {
      event.preventDefault()
      last.focus()
      return
    }
    if (!event.shiftKey && active === last) {
      event.preventDefault()
      first.focus()
    }
  }, [])

  return { surfaceRef, onKeyDown }
}
