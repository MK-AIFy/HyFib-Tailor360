import { useEffect, useRef } from 'react'
import { useIntl } from 'react-intl'
import { useLocation } from 'react-router'

/**
 * The attribute a screen puts on the heading it wants focus and the document title taken from.
 *
 * Optional. Without it the announcer takes the first `h1` inside the main landmark, which is what
 * every screen in this application has, so a screen only needs the attribute when its `h1` is not
 * the heading a person should land on.
 */
export const PAGE_HEADING_ATTRIBUTE = 'data-page-heading'

/**
 * Two queries rather than one selector list. `querySelector` returns the first match in *document*
 * order, not in selector order, so a single list would hand back whichever came first in the markup
 * — and a screen that nominated a heading below its `h1` would silently not get it.
 */
function findPageHeading(scope: ParentNode): HTMLElement | null {
  return (
    scope.querySelector<HTMLElement>(`[${PAGE_HEADING_ATTRIBUTE}]`) ??
    scope.querySelector<HTMLElement>('h1')
  )
}

export interface RouteAnnouncerProps {
  /**
   * The main landmark's id, used to scope the heading search. Defaults to the shell's own
   * `main-content`.
   */
  readonly mainId?: string
}

/**
 * Says where the person has just arrived, once, on every client-side navigation.
 *
 * ## The problem
 *
 * A single-page application changes the screen without a page load. Nothing is announced, the
 * document title never changes, and focus stays on the link that was activated — which is now
 * somewhere in a shell that no longer describes what is on screen. Three checklist items are failing
 * at once: A11Y-02 (the announced title does not change), A11Y-45 (the reader does not say where it
 * now is) and A11Y-14 (the next `Tab` continues from a place that is no longer meaningful).
 *
 * ## What this does
 *
 * On every navigation after the first it finds the screen's heading, moves focus to it and sets the
 * document title from its text. Moving focus is the stronger of the two available fixes: it
 * announces the heading, it puts the next `Tab` at the start of the new screen's content, and it
 * scrolls the heading into view — clear of the sticky header, because `html` carries
 * `scroll-padding-top`.
 *
 * ## Announced once, never twice
 *
 * When there is a heading, the live region below stays empty. A focus move already announces, and a
 * live region firing at the same moment is the doubled announcement checklist item A11Y-43 fails a
 * screen for. The region is used only when a screen has no heading to focus — which is a defect in
 * that screen, and the announcement is the graceful degradation, not the design.
 *
 * The first render is deliberately silent: the browser has just loaded a document and the screen
 * reader is already reading it. Only the title is set.
 */
export function RouteAnnouncer({ mainId = 'main-content' }: RouteAnnouncerProps) {
  const intl = useIntl()
  const location = useLocation()
  const announcerRef = useRef<HTMLDivElement>(null)
  const isFirstRender = useRef(true)

  useEffect(() => {
    const main = document.getElementById(mainId)
    const heading = findPageHeading(main ?? document)
    const pageName = heading?.textContent?.trim() ?? ''

    document.title =
      pageName === ''
        ? intl.formatMessage({ id: 'app.name' })
        : intl.formatMessage({ id: 'layout.documentTitle' }, { page: pageName })

    if (isFirstRender.current) {
      isFirstRender.current = false
      return
    }

    if (heading === null) {
      // Written straight into the region rather than through React state. The region is an external
      // system this effect is synchronising — the text is never read back, never rendered from, and
      // a state update here would only schedule a second render to produce the same DOM.
      if (announcerRef.current !== null) {
        announcerRef.current.textContent = intl.formatMessage(
          { id: 'layout.route.announcement' },
          { page: pageName === '' ? intl.formatMessage({ id: 'app.name' }) : pageName },
        )
      }
      return
    }

    // A heading is not focusable by default. tabIndex -1 makes it a focus target without adding a
    // tab stop, which is the same device the skip link uses on the main landmark.
    if (!heading.hasAttribute('tabindex')) {
      heading.tabIndex = -1
    }
    heading.focus()
    if (announcerRef.current !== null) {
      announcerRef.current.textContent = ''
    }
  }, [location.key, mainId, intl])

  return (
    <div
      className="visually-hidden"
      role="status"
      data-testid="route-announcer"
      ref={announcerRef}
    />
  )
}
