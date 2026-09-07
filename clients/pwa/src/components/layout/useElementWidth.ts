import { useEffect, useState } from 'react'
import type { RefObject } from 'react'

/**
 * The width of an element, in CSS pixels, kept up to date as it changes.
 *
 * ## Why a measured width rather than a media query
 *
 * docs/nfr/support-matrix.md section 5 is explicit: layouts are chosen by container queries, not by
 * orientation or user agent. A media query asks how wide the *window* is, which is the wrong
 * question the moment the shell is inside anything — a split-screen Android phone, an iPad in Slide
 * Over, a desktop window dragged narrow, a Storybook preview frame, or a printed page. The right
 * question is how wide the container is, and only a measurement answers it.
 *
 * CSS container queries answer it for *styling*. They cannot answer it for *structure*: a bottom
 * navigation bar and a desktop rail are different elements, and rendering both and hiding one would
 * put two "Main navigation" landmarks in the document, which is precisely what checklist item
 * A11Y-06 fails a screen for. So the shell measures once, in JavaScript, and renders one.
 *
 * ## Fallback
 *
 * `ResizeObserver` is on every browser in the support matrix. It is not in jsdom, and it is not in a
 * server render, so this falls back to the viewport width and a `resize` listener. That fallback is
 * not a second-class path: it produces the same answer for a shell that fills the window, which is
 * every shell the application ships.
 *
 * @param ref      the element to measure
 * @param fallback the width to assume before the first measurement — the viewport width by default
 */
export function useElementWidth(
  ref: RefObject<HTMLElement | null>,
  fallback = readViewportWidth(),
): number {
  const [width, setWidth] = useState(fallback)

  useEffect(() => {
    const element = ref.current

    if (typeof ResizeObserver === 'undefined' || element === null) {
      // No observer, or nothing mounted to observe: track the viewport instead. `resize` fires on
      // rotation as well, which is what keeps a tablet correct when it is turned over.
      const update = () => {
        setWidth(readViewportWidth())
      }
      update()
      window.addEventListener('resize', update)
      return () => {
        window.removeEventListener('resize', update)
      }
    }

    const observer = new ResizeObserver((entries) => {
      const entry = entries[0]
      if (entry === undefined) {
        return
      }
      // borderBoxSize is the box the layout actually occupies. contentRect would exclude padding,
      // which would make a padded shell report itself narrower than it is and switch layout early.
      const boxWidth = entry.borderBoxSize[0]?.inlineSize ?? entry.contentRect.width
      setWidth(boxWidth)
    })

    observer.observe(element)
    setWidth(element.getBoundingClientRect().width || readViewportWidth())

    return () => {
      observer.disconnect()
    }
  }, [ref])

  return width
}

/** The viewport width, or the reference device's 360 px where there is no window at all. */
export function readViewportWidth(): number {
  if (typeof window === 'undefined') {
    return 360
  }
  return window.innerWidth
}
