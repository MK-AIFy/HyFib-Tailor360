import { MemoryRouter, Route, Routes } from 'react-router'
import type { ReactElement } from 'react'
import { RequireSession } from '../../auth/RequireSession'
import { SessionProvider } from '../../auth/SessionProvider'

/**
 * A stand-in for the API, so a story can show a real administration screen rather than a mock of
 * one.
 *
 * ## Why the stories drive the actual screens
 *
 * Definition of Done item 7 wants a story for the loading, empty, error, offline and forbidden state
 * of every new screen, and the value of that is entirely in it being the screen. A story that
 * rendered `<EmptyState>` with the right words would pass the letter of the requirement and prove
 * nothing: the question a reviewer is asking is whether *this screen* shows a usable empty state,
 * which is a fact about the component's branches and not about the state component it happens to
 * call.
 *
 * ## Why this is not the test stub
 *
 * `auth/testing/fixtures.ts` stubs `fetch` through `vi.stubGlobal`, which does not exist outside
 * Vitest. This swaps the global directly and hands back a restore function; Storybook has no
 * per-story teardown hook that runs reliably on navigation, so each story installs its own routes on
 * render and the last one to render wins — which is exactly the behaviour wanted when a person is
 * clicking between stories.
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
export function storyProblem(status: number, code: string): Response {
  return new Response(JSON.stringify({ status, code, title: code, correlationId: 'story' }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
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
export function withAdminApi(
  element: ReactElement,
  routes: StoryRoutes,
  options: { readonly path?: string; readonly at?: string } = {},
): ReactElement {
  const answers: StoryRoutes = {
    'GET /api/v1/antiforgery': () =>
      storyJson({ token: 'story-request-token', headerName: 'X-CSRF-Token' }),
    'GET /api/v1/me': () => storyJson(STORY_USER),
    ...routes,
  }

  globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    const method = (init?.method ?? 'GET').toUpperCase()
    const answer = answers[`${method} ${path}`]

    return answer === undefined ? storyProblem(404, 'story.route-not-stubbed') : await answer()
  }

  const path = options.path ?? '/admin'
  const at = options.at ?? '/admin'

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

/**
 * The signed-in Owner every administration story runs as.
 *
 * Synthetic throughout, and holding every administrative permission — because a story of the
 * forbidden state sets its own narrower list, and a story of a working screen should not be one
 * permission away from showing the wrong thing.
 */
export const STORY_USER = {
  userId: '0199bb00-0000-7000-8000-00000000000f',
  userName: 'owner.story',
  displayName: 'Devi (owner)',
  email: 'owner.story@example.invalid',
  status: 'Active',
  organisationId: '0199bb00-0000-7000-8000-0000000000ff',
  branchId: '0199bb00-0000-7000-8000-0000000000aa',
  permissions: [
    'admin.users',
    'admin.branches',
    'admin.roles',
    'admin.feature_flags',
    'admin.audit.read',
    'admin.outbox.replay',
    'catalog.templates.edit',
    'catalog.templates.publish',
  ],
  security: {
    mfaEnrolment: 'Enrolled',
    mustChangePassword: false,
    mfaSatisfied: true,
    lastStrongAuthenticationAt: '2026-09-07T09:00:00.000Z',
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
  session: {
    idleExpiresAt: '2026-09-07T18:00:00.000Z',
    absoluteExpiresAt: '2026-09-07T21:00:00.000Z',
  },
} as const
