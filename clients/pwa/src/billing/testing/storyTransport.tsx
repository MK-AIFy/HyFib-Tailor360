import { MemoryRouter, Route, Routes } from 'react-router'
import type { ReactElement } from 'react'
import { RequireSession } from '../../auth/RequireSession'
import { SessionProvider } from '../../auth/SessionProvider'

/**
 * A stand-in for the API, so a story can show a real billing screen rather than a mock of one.
 *
 * The same shape `admin/testing/storyTransport.tsx` established, and for the same reason: Definition
 * of Done item 7 wants a story per state of a new screen, and the value is entirely in the story
 * being the screen — its own branches, not a state component with billing-shaped words typed into it.
 * `auth/testing/fixtures.ts`'s `vi.stubGlobal` stub does not exist outside Vitest, and Storybook has
 * no reliable per-story teardown, so this swaps `fetch` directly on render and the last story to
 * render wins, which is what a person clicking between stories wants.
 */
export type StoryRoutes = Readonly<Record<string, () => Response | Promise<Response>>>

/** A JSON body, with an optional `ETag` for the reads an edit is made against. */
export function storyJson(body: unknown, version?: string): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers:
      version === undefined
        ? { 'Content-Type': 'application/json' }
        : { 'Content-Type': 'application/json', ETag: version },
  })
}

/** An RFC 9457 problem, in the shape the API sends. */
export function storyProblem(
  status: number,
  code: string,
  extra: Readonly<Record<string, unknown>> = {},
): Response {
  return new Response(
    JSON.stringify({ status, code, title: code, correlationId: 'story', ...extra }),
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  )
}

/** A request that never answers, for the loading state. */
export function storyPending(): Promise<Response> {
  return new Promise<Response>(() => {
    // Deliberately never settles: the loading state is the story.
  })
}

/**
 * Installs the routes and renders the screen inside the providers the router gives it.
 *
 * @param element The screen.
 * @param routes Keyed `"METHOD /path"`, matching the transport's own signature form.
 * @param options The route pattern and the address to render at, for a screen with a parameter.
 */
export function withBillingApi(
  element: ReactElement,
  routes: StoryRoutes,
  options: {
    readonly path?: string
    readonly at?: string
    readonly permissions?: readonly string[]
  } = {},
): ReactElement {
  const answers: StoryRoutes = {
    'GET /api/v1/antiforgery': () =>
      storyJson({ token: 'story-request-token', headerName: 'X-CSRF-Token' }),
    'GET /api/v1/me': () => storyJson(storyUser(options.permissions)),
    ...routes,
  }

  globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    const method = (init?.method ?? 'GET').toUpperCase()
    const answer = answers[`${method} ${path}`]

    return answer === undefined ? storyProblem(404, 'story.route-not-stubbed') : await answer()
  }

  const path = options.path ?? '/billing/invoices'
  const at = options.at ?? '/billing/invoices'

  return (
    <SessionProvider>
      <MemoryRouter initialEntries={[at]}>
        <Routes>
          <Route element={<RequireSession />}>
            <Route element={element} path={path} />
          </Route>
        </Routes>
      </MemoryRouter>
    </SessionProvider>
  )
}

function storyUser(permissions: readonly string[] = ['billing.create_invoice']) {
  return {
    userId: '0199bb00-0000-7000-8000-0000000000f1',
    userName: 'cashier.story',
    displayName: 'Anitha (counter)',
    email: 'cashier.story@example.invalid',
    status: 'Active',
    organisationId: '0199bb00-0000-7000-8000-0000000000ff',
    branchId: '0199bb00-0000-7000-8000-0000000000aa',
    permissions,
    security: {
      mfaEnrolment: 'Enrolled',
      mustChangePassword: false,
      mfaSatisfied: true,
      lastStrongAuthenticationAt: '2026-09-12T09:00:00.000Z',
      factors: { authenticator: true, recoveryCode: true, passkey: false },
      unusedRecoveryCodes: 8,
    },
    preferences: {
      locale: 'en-IN',
      timeZoneId: 'Asia/Kolkata',
      theme: 'System',
      density: 'Comfortable',
      reducedMotion: false,
      landingRoute: null,
    },
    // Relative to the moment a story asks, not a fixed instant — see STORY_USER's own note in
    // admin/testing/storyTransport.tsx for why: a fixed date eventually falls far enough into the
    // past that every story opens under a "Your session ended" dialog.
    get session() {
      return {
        idleExpiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
        absoluteExpiresAt: new Date(Date.now() + 11 * 60 * 60 * 1000).toISOString(),
        warningLeadSeconds: 120,
        mfaSatisfied: true,
      }
    },
  }
}
