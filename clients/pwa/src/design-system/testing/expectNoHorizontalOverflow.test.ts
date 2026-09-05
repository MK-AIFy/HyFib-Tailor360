import { describe, expect, it } from 'vitest'
import {
  describeObscuredFocusFailure,
  describeOverflowFailure,
  describeViewport,
  expectNoHorizontalOverflow,
  measureHorizontalOverflow,
  measureObscuredFocus,
} from './expectNoHorizontalOverflow'
import type {
  ObscuredFocusMeasurement,
  OverflowMeasurement,
  PageLike,
  ViewportSize,
} from './expectNoHorizontalOverflow'

const CLEAN_OVERFLOW: OverflowMeasurement = {
  documentScrollWidth: 360,
  documentClientWidth: 360,
  offenders: [],
}

const CLEAN_FOCUS: ObscuredFocusMeasurement = { checked: 12, obscured: [] }

/**
 * A stand-in for a Playwright page. Building one by hand is the point of the structural `PageLike`
 * type: the helper's orchestration — which widths it visits, which it skips, how it reports — is
 * proved in the ordinary test run, without a browser.
 */
function stubPage(
  results: {
    overflowFor?: (size: ViewportSize) => OverflowMeasurement
    focusFor?: (size: ViewportSize) => ObscuredFocusMeasurement
  } = {},
): PageLike & { readonly visited: ViewportSize[] } {
  const visited: ViewportSize[] = []
  let current: ViewportSize = { width: 0, height: 0 }

  return {
    visited,
    setViewportSize(size: ViewportSize): Promise<void> {
      current = size
      visited.push(size)
      return Promise.resolve()
    },
    evaluate<TResult>(pageFunction: () => TResult): Promise<TResult> {
      const measurement =
        pageFunction === measureHorizontalOverflow
          ? ((results.overflowFor ?? (() => CLEAN_OVERFLOW))(current) as unknown)
          : ((results.focusFor ?? (() => CLEAN_FOCUS))(current) as unknown)
      return Promise.resolve(measurement as TResult)
    },
  }
}

describe('expectNoHorizontalOverflow', () => {
  it('visits every required width, and the zoomed widths that stay above the reflow floor', async () => {
    const page = stubPage()

    await expectNoHorizontalOverflow(page)

    expect(page.visited.map((size) => size.width)).toEqual([
      // 100%: the five widths of docs/nfr/support-matrix.md section 2.
      320, 360, 768, 1024, 1280,
      // 200%: the CSS width each window presents. 320 and 360 would fall to 160 and 180, below the
      // 320 px reflow floor, and are skipped rather than failed.
      384, 512, 640,
    ])
    expect(page.visited.map((size) => size.height)).toEqual([
      800, 800, 800, 800, 800, 400, 400, 400,
    ])
  })

  it('accepts a page that reflows cleanly', async () => {
    await expect(expectNoHorizontalOverflow(stubPage())).resolves.toBeUndefined()
  })

  it('reports every failing width, not only the first', async () => {
    const page = stubPage({
      overflowFor: (size) =>
        size.width === 320 || size.width === 384
          ? {
              documentScrollWidth: size.width + 90,
              documentClientWidth: size.width,
              offenders: [{ description: 'table.queue', left: 0, right: size.width + 90 }],
            }
          : CLEAN_OVERFLOW,
    })

    await expect(expectNoHorizontalOverflow(page)).rejects.toThrow(
      /failed 2 of 8 checks[\s\S]*at 320 CSS px[\s\S]*table\.queue/,
    )
  })

  it('reports a control hidden behind a bottom bar', async () => {
    const page = stubPage({
      focusFor: (size) =>
        size.width === 360
          ? {
              checked: 9,
              obscured: [
                {
                  description: 'button#save — "Save measurements"',
                  obscuredBy: 'nav.bottom-nav',
                  top: 740,
                  bottom: 796,
                },
              ],
            }
          : CLEAN_FOCUS,
    })

    await expect(expectNoHorizontalOverflow(page)).rejects.toThrow(
      /Focus is obscured at 360 CSS px \(WCAG 2\.4\.11\)[\s\S]*covered by nav\.bottom-nav/,
    )
  })

  it('can be asked to check reflow only', async () => {
    const page = stubPage({
      focusFor: () => ({
        checked: 1,
        obscured: [{ description: 'button', obscuredBy: 'div', top: 0, bottom: 10 }],
      }),
    })

    await expect(
      expectNoHorizontalOverflow(page, { checkObscuredFocus: false }),
    ).resolves.toBeUndefined()
  })

  it('honours an explicit width and zoom list', async () => {
    const page = stubPage()

    await expectNoHorizontalOverflow(page, { widths: [768], zoomLevels: [1], height: 600 })

    expect(page.visited).toEqual([{ width: 768, height: 600 }])
  })
})

describe('describeViewport', () => {
  it('names a plain width plainly', () => {
    expect(describeViewport(360, 1)).toBe('at 360 CSS px')
  })

  it('says what a zoomed window actually presents to the layout', () => {
    expect(describeViewport(1280, 2)).toBe('at a 1280 px window at 200% zoom — 640 CSS px')
  })
})

describe('describeOverflowFailure', () => {
  it('passes a document that fits', () => {
    expect(describeOverflowFailure(360, 1, CLEAN_OVERFLOW)).toBeNull()
  })

  it('names the offending elements and their extent', () => {
    const message = describeOverflowFailure(360, 1, {
      documentScrollWidth: 520,
      documentClientWidth: 360,
      offenders: [{ description: 'table.ledger', left: 0, right: 520 }],
    })

    expect(message).toContain('table.ledger spans 0..520 px')
    expect(message).toContain('let it scroll inside itself')
  })

  it('says so when no single element is to blame', () => {
    const message = describeOverflowFailure(320, 1, {
      documentScrollWidth: 340,
      documentClientWidth: 320,
      offenders: [],
    })

    expect(message).toContain('a margin, a gap or a fixed width on the body')
  })
})

describe('describeObscuredFocusFailure', () => {
  it('passes when nothing is covered', () => {
    expect(describeObscuredFocusFailure(360, 1, CLEAN_FOCUS)).toBeNull()
  })

  it('counts how many of the checked controls were covered', () => {
    const message = describeObscuredFocusFailure(360, 1, {
      checked: 9,
      obscured: [{ description: 'input#waist', obscuredBy: 'div.keyboard', top: 700, bottom: 744 }],
    })

    expect(message).toContain('on 1 of 9 focusable controls')
  })
})

/**
 * jsdom has no layout engine, so every rectangle is zero-sized and both probes should decide there
 * is nothing to report. That is worth asserting: it proves the probe bodies are self-contained —
 * they close over nothing in this module, which is what lets Playwright serialise them into a real
 * browser — and that neither one throws on a document it does not understand.
 */
describe('the page-side probes', () => {
  it('run in a document without layout and report nothing', () => {
    document.body.innerHTML = '<main><button type="button">Scan</button></main>'

    expect(measureHorizontalOverflow().offenders).toEqual([])
    expect(measureObscuredFocus().obscured).toEqual([])
  })
})
