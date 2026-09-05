import { describe, expect, it } from 'vitest'
import { INSTALL_PLATFORMS, detectInstallPlatform, isInstalledDisplayMode } from './installPlatform'
import type { InstallPlatform } from './installPlatform'

/**
 * Real user-agent strings, because a paraphrased one proves nothing: the whole reason this module
 * exists is that the strings are irregular. Every device class in docs/nfr/support-matrix.md
 * section 3 appears here, plus the three that are easy to get wrong — iPadOS pretending to be a
 * Macintosh, an Android WebView pretending to be Chrome, and Chrome on iOS pretending to be able to
 * install.
 */
const AGENTS: Readonly<Record<string, { ua: string; touch: number; expected: InstallPlatform }>> = {
  'Chrome on an Android phone': {
    ua: 'Mozilla/5.0 (Linux; Android 10; SM-A105F) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36',
    touch: 5,
    expected: 'android',
  },
  'Samsung Internet': {
    ua: 'Mozilla/5.0 (Linux; Android 13; SM-A536E) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/23.0 Chrome/115.0.0.0 Mobile Safari/537.36',
    touch: 5,
    expected: 'android',
  },
  'Firefox on Android — no install event': {
    ua: 'Mozilla/5.0 (Android 13; Mobile; rv:133.0) Gecko/133.0 Firefox/133.0',
    touch: 5,
    expected: 'other',
  },
  'an Android WebView inside another application': {
    ua: 'Mozilla/5.0 (Linux; Android 12; Pixel 6 Build/SQ3A; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/120.0.0.0 Mobile Safari/537.36',
    touch: 5,
    expected: 'other',
  },
  'Safari on an iPhone': {
    ua: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1',
    touch: 5,
    expected: 'ios-safari',
  },
  'Chrome on an iPhone': {
    ua: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/131.0.0.0 Mobile/15E148 Safari/604.1',
    touch: 5,
    expected: 'ios-other',
  },
  'Firefox on an iPhone': {
    ua: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) FxiOS/133.0 Mobile/15E148 Safari/605.1.15',
    touch: 5,
    expected: 'ios-other',
  },
  'Safari on an iPad, which reports itself as a Macintosh': {
    ua: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15',
    touch: 5,
    expected: 'ios-safari',
  },
  'Safari on a Mac, which is the same string with no touch': {
    ua: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15',
    touch: 0,
    expected: 'other',
  },
  'Chrome on Windows': {
    ua: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
    touch: 0,
    expected: 'desktop',
  },
  'Edge on Windows': {
    ua: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0',
    touch: 0,
    expected: 'desktop',
  },
  'Firefox on Windows': {
    ua: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0',
    touch: 0,
    expected: 'other',
  },
}

describe('detectInstallPlatform', () => {
  for (const [description, { ua, touch, expected }] of Object.entries(AGENTS)) {
    it(`recognises ${description}`, () => {
      expect(detectInstallPlatform({ userAgent: ua, maxTouchPoints: touch })).toBe(expected)
    })
  }

  it('falls back to the no-install branch for an unrecognised agent', () => {
    // Not a defensive nicety: the fallback is the branch that says "everything works in a tab",
    // which is the correct thing to tell somebody whose browser this module has never heard of.
    expect(detectInstallPlatform({ userAgent: 'Tailor360TestAgent/1.0', maxTouchPoints: 0 })).toBe(
      'other',
    )
  })

  it('only ever returns a platform the Install screen has copy for', () => {
    for (const { ua, touch } of Object.values(AGENTS)) {
      expect(INSTALL_PLATFORMS).toContain(
        detectInstallPlatform({ userAgent: ua, maxTouchPoints: touch }),
      )
    }
  })
})

/** A stand-in for `window` carrying only what the function reads. */
function viewWith(options: {
  standalone?: boolean
  displayMode?: string
  throws?: boolean
}): Window {
  const view = {
    navigator: { standalone: options.standalone },
    matchMedia: (query: string) => {
      if (options.throws === true) {
        throw new SyntaxError(`unsupported media feature in ${query}`)
      }
      return {
        matches: options.displayMode !== undefined && query.includes(options.displayMode),
      }
    },
  }

  return view as unknown as Window
}

describe('isInstalledDisplayMode', () => {
  it('is false in an ordinary browser tab', () => {
    expect(isInstalledDisplayMode(viewWith({}))).toBe(false)
  })

  it('is true for the standalone display mode', () => {
    expect(isInstalledDisplayMode(viewWith({ displayMode: 'standalone' }))).toBe(true)
  })

  it('is true for the minimal-ui fallback the manifest also allows', () => {
    // display_override lists minimal-ui, so a device that refused standalone still installed the
    // application. Telling that person to install it again would be wrong.
    expect(isInstalledDisplayMode(viewWith({ displayMode: 'minimal-ui' }))).toBe(true)
  })

  it("reads Safari's non-standard navigator.standalone", () => {
    expect(isInstalledDisplayMode(viewWith({ standalone: true }))).toBe(true)
  })

  it('answers "browser tab" when the engine refuses the query', () => {
    // The harmless answer: the Install screen shows its instructions, which is never damaging, and
    // an exception here would take down a page whose only job is to help somebody who is stuck.
    expect(isInstalledDisplayMode(viewWith({ throws: true }))).toBe(false)
  })
})
