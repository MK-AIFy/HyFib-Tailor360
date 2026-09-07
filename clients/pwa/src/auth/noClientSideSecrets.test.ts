import { describe, expect, it } from 'vitest'

/**
 * The rule this whole family exists to keep: **no token material reaches anywhere a script can read
 * it back.**
 *
 * The session is an `httpOnly`, `__Host-`-prefixed cookie the browser will not show to script. The
 * only piece of security material this application ever holds is the anti-forgery request token,
 * and it lives in a module variable that dies with the page — because it is bound to the signed-in
 * account and a stored copy would outlive the pair it belongs to.
 *
 * This file asserts that at the source level, over every file in the authentication family and every
 * authentication screen. A test that only checked behaviour would pass for a screen that wrote a
 * recovery code to `localStorage` on a path the test did not happen to walk; a test that reads the
 * source cannot be walked around, and it fails on the day the line is added rather than on the day
 * somebody audits a shared counter tablet.
 *
 * The companion runtime assertion — that a whole sign-in, challenge and enrolment writes nothing —
 * is in `SessionProvider.test.tsx` and in the screen tests, which spy on the storage APIs.
 */

/** Everything under the authentication family and the authentication screens, as raw text. */
const SOURCES: Readonly<Record<string, string>> = {
  ...import.meta.glob('./**/*.{ts,tsx}', { query: '?raw', import: 'default', eager: true }),
  ...import.meta.glob('../routes/auth/**/*.{ts,tsx}', {
    query: '?raw',
    import: 'default',
    eager: true,
  }),
}

/**
 * The places a value can be put where another script, another tab, or the next person to use a
 * shared device could read it back.
 *
 * `document.cookie` is on the list even though writing one is not how this application works,
 * because a cookie written by script is by definition not `httpOnly`, which is the one property the
 * session cookie's safety rests on.
 */
const FORBIDDEN = [
  'localStorage',
  'sessionStorage',
  'indexedDB',
  'document.cookie',
  'window.name',
  'openDatabase',
] as const

/** Files that are test scaffolding rather than shipped code. */
function isProduction(path: string): boolean {
  return !path.includes('.test.') && !path.includes('/testing/')
}

describe('the authentication family never writes anything a script can read back', () => {
  it('covers every source file, so an empty glob cannot pass as a clean result', () => {
    const covered = Object.keys(SOURCES).filter(isProduction)
    expect(covered.length).toBeGreaterThan(15)
    expect(covered).toContain('./antiforgery.ts')
    expect(covered).toContain('./SessionProvider.tsx')
    expect(covered).toContain('../routes/auth/LoginRoute.tsx')
  })

  for (const forbidden of FORBIDDEN) {
    it(`never touches ${forbidden}`, () => {
      const offenders = Object.entries(SOURCES)
        .filter(([path]) => isProduction(path))
        // A mention inside a comment is the explanation of why it is absent, and the whole family is
        // heavily commented; the code itself is what is being asserted about.
        .filter(([, source]) => withoutComments(source).includes(forbidden))
        .map(([path]) => path)

      expect(offenders).toEqual([])
    })
  }

  it('would fail if any of them were used, comments notwithstanding', () => {
    // A negative control. Without it this file passes for the wrong reason the day the glob pattern
    // stops matching, or the day the comment stripper eats the code as well as the prose.
    const breach = 'export function keep(code: string) {\n  localStorage.setItem("codes", code)\n}'
    const prose =
      '/* This never reaches localStorage. */\n// nor sessionStorage\nexport const x = 1'

    expect(withoutComments(breach)).toContain('localStorage')
    expect(withoutComments(prose)).not.toContain('localStorage')
    expect(withoutComments(prose)).not.toContain('sessionStorage')
  })

  it('holds the anti-forgery token in a module variable and exposes only whether there is one', async () => {
    const module = await import('./antiforgery')
    // There is deliberately no exported way to read the value: a screen that could read it is a
    // screen that could store it, and nothing outside this family has any business holding one.
    expect(Object.keys(module)).not.toContain('token')
    expect(typeof module.hasAntiforgeryToken).toBe('function')
  })
})

/** Strips line and block comments, so prose about a rule is not mistaken for a breach of it. */
function withoutComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1')
}
