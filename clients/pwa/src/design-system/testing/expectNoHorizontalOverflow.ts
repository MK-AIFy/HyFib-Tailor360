import { LAYOUT_PROOF_WIDTHS } from '../foundations/breakpoints'

/**
 * The shared layout-integrity helper.
 *
 * Definition of Done item 7 requires every later user-interface pull request to run this, and
 * docs/nfr/support-matrix.md section 10 lists it as a per-pull-request gate. It proves two things
 * that automated accessibility scanning cannot:
 *
 *  - **1.4.10 Reflow and 1.4.4 Resize Text.** No horizontal scrolling of the page at 320, 360, 768,
 *    1024 and 1280 CSS px, or at 200% zoom. A wide table may scroll — inside its own container,
 *    never by moving the body.
 *  - **2.4.11 Focus Not Obscured (Minimum).** No focused control is hidden behind the bottom
 *    navigation, a sticky action bar, a banner or a toast. On a phone form this is the failure that
 *    happens first and is noticed last, which is why the blueprint gives this helper the assertion.
 *
 * ## Why there is no Playwright import here
 *
 * The helper takes a structural `PageLike`, which Playwright's `Page` satisfies without any
 * declaration. That keeps the client package free of a browser-automation dependency it does not
 * otherwise need, lets the helper be unit-tested in the ordinary Vitest run, and still lets #52 —
 * which owns `tests/e2e/playwright.config.ts` — import it and pass a real page:
 *
 * ```ts
 * import { expectNoHorizontalOverflow } from '@tailor360/pwa/src/design-system/testing/expectNoHorizontalOverflow'
 *
 * test('measurement wizard reflows', async ({ page }) => {
 *   await page.goto('/measurements/new')
 *   await expectNoHorizontalOverflow(page)
 * })
 * ```
 */

export interface ViewportSize {
  readonly width: number
  readonly height: number
}

/**
 * The part of a browser-automation page this helper uses. Playwright's `Page` matches it, and so
 * does any stub in a unit test.
 */
export interface PageLike {
  setViewportSize(size: ViewportSize): Promise<void>
  evaluate<TResult>(pageFunction: () => TResult): Promise<TResult>
}

/** One element sticking out of the viewport. */
export interface OverflowOffender {
  readonly description: string
  readonly left: number
  readonly right: number
}

export interface OverflowMeasurement {
  readonly documentScrollWidth: number
  readonly documentClientWidth: number
  readonly offenders: readonly OverflowOffender[]
}

/** One focusable control that nothing could see once it had focus. */
export interface ObscuredControl {
  readonly description: string
  readonly obscuredBy: string
  readonly top: number
  readonly bottom: number
}

export interface ObscuredFocusMeasurement {
  readonly checked: number
  readonly obscured: readonly ObscuredControl[]
}

/**
 * Runs in the page. Reports the document's own overflow and the elements causing it.
 *
 * An element inside an ancestor that scrolls horizontally is not an offender: that is the pattern
 * the accessibility policy prescribes for wide tables, ledgers and workboards.
 *
 * Everything it needs is declared inside the function, because the body is serialised into the
 * browser and cannot close over anything in this module.
 */
export function measureHorizontalOverflow(): OverflowMeasurement {
  function describeElement(element: Element): string {
    const id = element.id === '' ? '' : `#${element.id}`
    const classes =
      element.className === '' || typeof element.className !== 'string'
        ? ''
        : `.${element.className.trim().split(/\s+/).join('.')}`
    const text = (element.textContent ?? '').trim().slice(0, 40)
    return `${element.tagName.toLowerCase()}${id}${classes}${text === '' ? '' : ` — "${text}"`}`
  }

  const documentElement = document.documentElement
  const clientWidth = documentElement.clientWidth
  const offenders: OverflowOffender[] = []

  for (const element of Array.from(document.body.querySelectorAll('*'))) {
    const rect = element.getBoundingClientRect()
    if (rect.width === 0 && rect.height === 0) {
      continue
    }
    // One pixel of tolerance: sub-pixel layout rounding is not a reflow failure.
    if (rect.right <= clientWidth + 1 && rect.left >= -1) {
      continue
    }

    let scrollsHorizontally = false
    for (
      let ancestor = element.parentElement;
      ancestor !== null;
      ancestor = ancestor.parentElement
    ) {
      const overflowX = window.getComputedStyle(ancestor).overflowX
      if (overflowX === 'auto' || overflowX === 'scroll') {
        scrollsHorizontally = true
        break
      }
    }
    if (scrollsHorizontally) {
      continue
    }

    offenders.push({ description: describeElement(element), left: rect.left, right: rect.right })
    if (offenders.length >= 10) {
      break
    }
  }

  return {
    documentScrollWidth: documentElement.scrollWidth,
    documentClientWidth: clientWidth,
    offenders,
  }
}

/**
 * Runs in the page. Focuses every focusable control in turn and reports the ones nothing can see.
 *
 * The test is the one WCAG 2.4.11 describes: after focus moves, at least part of the control must
 * still be visible. Hit-testing the control's centre and its four inset corners answers that
 * without needing a screenshot — if every one of those points belongs to something else, a sticky
 * bar, a banner or the virtual keyboard is sitting on top of it.
 *
 * Focus is restored to whatever held it before, so a page can be checked mid-journey.
 */
export function measureObscuredFocus(): ObscuredFocusMeasurement {
  function describeElement(element: Element): string {
    const id = element.id === '' ? '' : `#${element.id}`
    const name =
      element.getAttribute('aria-label') ?? (element.textContent ?? '').trim().slice(0, 40)
    return `${element.tagName.toLowerCase()}${id}${name === '' ? '' : ` — "${name}"`}`
  }

  const focusableSelector = [
    'a[href]',
    'button',
    'input',
    'select',
    'textarea',
    'summary',
    '[tabindex]:not([tabindex="-1"])',
  ].join(', ')

  const previouslyFocused = document.activeElement
  const obscured: ObscuredControl[] = []
  let checked = 0

  for (const candidate of Array.from(document.querySelectorAll(focusableSelector))) {
    if (!(candidate instanceof HTMLElement) || candidate.hasAttribute('disabled')) {
      continue
    }
    const rect = candidate.getBoundingClientRect()
    if (rect.width === 0 || rect.height === 0) {
      continue
    }

    candidate.focus()
    if (document.activeElement !== candidate) {
      continue
    }
    checked += 1

    const inset = 2
    const points = [
      { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 },
      { x: rect.left + inset, y: rect.top + inset },
      { x: rect.right - inset, y: rect.top + inset },
      { x: rect.left + inset, y: rect.bottom - inset },
      { x: rect.right - inset, y: rect.bottom - inset },
    ]

    let covering: Element | null = null
    let visible = false
    for (const point of points) {
      if (
        point.x < 0 ||
        point.y < 0 ||
        point.x > document.documentElement.clientWidth ||
        point.y > document.documentElement.clientHeight
      ) {
        continue
      }
      const hit = document.elementFromPoint(point.x, point.y)
      if (
        hit !== null &&
        (hit === candidate || candidate.contains(hit) || hit.contains(candidate))
      ) {
        visible = true
        break
      }
      if (hit !== null) {
        covering = hit
      }
    }

    if (!visible) {
      obscured.push({
        description: describeElement(candidate),
        obscuredBy: covering === null ? 'the viewport edge' : describeElement(covering),
        top: rect.top,
        bottom: rect.bottom,
      })
    }
  }

  if (previouslyFocused instanceof HTMLElement) {
    previouslyFocused.focus()
  }

  return { checked, obscured }
}

export interface LayoutCheckOptions {
  /** CSS widths to check. Defaults to 320, 360, 768, 1024 and 1280. */
  readonly widths?: readonly number[]
  /** Viewport height at 100%. Defaults to 800. */
  readonly height?: number
  /**
   * Zoom levels to check at each width. Defaults to 1 and 2 — 100% and the 200% of 1.4.4.
   *
   * Zoom is emulated as the CSS viewport it produces: a 1280 px window at 200% presents 640 CSS px
   * of width and half the height. That is the same equivalence WCAG 1.4.10 itself relies on when it
   * calls 320 CSS px the reflow floor, and it is the only form of zoom that behaves identically in
   * Chromium, Firefox and WebKit, none of which expose the same zoom control to automation.
   *
   * A width that would fall below the 320 px reflow floor once zoomed is skipped, because no layout
   * is required to work there — 320 px at 200% is 160 px, which nothing in the standard asks for.
   *
   * What this emulation does **not** reproduce is text growing inside an unchanged box. The product
   * has its own answer to that: set `data-text-size="150"` on the document element and call this
   * helper again. Real browser zoom on a real device stays a manual item (A11Y-71).
   */
  readonly zoomLevels?: readonly number[]
  /** Also assert 2.4.11 Focus Not Obscured. Defaults to true. */
  readonly checkObscuredFocus?: boolean
  /** Awaited after each resize, for a layout that settles asynchronously. */
  readonly settle?: () => Promise<void>
}

/** The reflow floor of WCAG 1.4.10, below which no layout is required to work. */
export const MINIMUM_REFLOW_WIDTH = 320

export interface LayoutFailure {
  readonly width: number
  readonly zoom: number
  readonly message: string
}

/** "at 360 CSS px", or "at a 1280 px window at 200% zoom — 640 CSS px". */
export function describeViewport(width: number, zoom: number): string {
  if (zoom === 1) {
    return `at ${String(width)} CSS px`
  }
  return `at a ${String(width)} px window at ${String(zoom * 100)}% zoom — ${String(Math.round(width / zoom))} CSS px`
}

/** Formats one width's overflow measurement, or null when it passed. */
export function describeOverflowFailure(
  width: number,
  zoom: number,
  measurement: OverflowMeasurement,
): string | null {
  const overflows =
    measurement.documentScrollWidth > measurement.documentClientWidth + 1 ||
    measurement.offenders.length > 0

  if (!overflows) {
    return null
  }

  const heading =
    `The page scrolls horizontally ${describeViewport(width, zoom)}: ` +
    `document scroll width ${String(measurement.documentScrollWidth)} px against a client width of ` +
    `${String(measurement.documentClientWidth)} px.`

  if (measurement.offenders.length === 0) {
    return `${heading} No single element was wider than the viewport, so the cause is a margin, a gap or a fixed width on the body.`
  }

  const lines = measurement.offenders.map(
    (offender) =>
      `  - ${offender.description} spans ${String(Math.round(offender.left))}..${String(Math.round(offender.right))} px`,
  )
  return `${heading}\nWiden the container or let it scroll inside itself:\n${lines.join('\n')}`
}

/** Formats one width's obscured-focus measurement, or null when it passed. */
export function describeObscuredFocusFailure(
  width: number,
  zoom: number,
  measurement: ObscuredFocusMeasurement,
): string | null {
  if (measurement.obscured.length === 0) {
    return null
  }

  const lines = measurement.obscured.map(
    (control) => `  - ${control.description} is covered by ${control.obscuredBy}`,
  )
  return (
    `Focus is obscured ${describeViewport(width, zoom)} (WCAG 2.4.11), ` +
    `on ${String(measurement.obscured.length)} of ${String(measurement.checked)} focusable controls:\n` +
    `${lines.join('\n')}`
  )
}

/**
 * Checks a page for horizontal overflow and obscured focus at every required width and zoom level.
 *
 * Every width is checked before anything is reported, so one run tells the author about all of the
 * breakpoints that fail rather than only the narrowest.
 *
 * @throws Error listing every failing width, when any check fails.
 */
export async function expectNoHorizontalOverflow(
  page: PageLike,
  options: LayoutCheckOptions = {},
): Promise<void> {
  const widths = options.widths ?? LAYOUT_PROOF_WIDTHS
  const height = options.height ?? 800
  const zoomLevels = options.zoomLevels ?? [1, 2]
  const checkObscuredFocus = options.checkObscuredFocus ?? true
  const failures: LayoutFailure[] = []
  let checksRun = 0

  for (const zoom of zoomLevels) {
    for (const width of widths) {
      const cssWidth = Math.round(width / zoom)
      if (cssWidth < MINIMUM_REFLOW_WIDTH) {
        continue
      }
      checksRun += 1

      await page.setViewportSize({ width: cssWidth, height: Math.round(height / zoom) })
      if (options.settle !== undefined) {
        await options.settle()
      }

      const overflow = await page.evaluate(measureHorizontalOverflow)
      const overflowMessage = describeOverflowFailure(width, zoom, overflow)
      if (overflowMessage !== null) {
        failures.push({ width, zoom, message: overflowMessage })
      }

      if (checkObscuredFocus) {
        const focus = await page.evaluate(measureObscuredFocus)
        const focusMessage = describeObscuredFocusFailure(width, zoom, focus)
        if (focusMessage !== null) {
          failures.push({ width, zoom, message: focusMessage })
        }
      }
    }
  }

  if (failures.length > 0) {
    throw new Error(
      `Layout integrity failed ${String(failures.length)} of ${String(checksRun)} checks.\n\n` +
        failures.map((failure) => failure.message).join('\n\n'),
    )
  }
}
