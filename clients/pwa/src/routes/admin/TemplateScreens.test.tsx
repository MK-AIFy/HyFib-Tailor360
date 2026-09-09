import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { RequireSession } from '../../auth/RequireSession'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import {
  aMeasurementTemplate,
  aTemplateVersion,
  versionedResponse,
} from '../../admin/testing/fixtures'
import { RequirePermission } from '../../admin/RequirePermission'
import { ADMIN_PERMISSIONS } from '../../admin/adminPermissions'
import { TemplateDetailRoute } from './TemplateDetailRoute'
import { TemplateListRoute } from './TemplateListRoute'

let transport: FetchStub

const TEMPLATES = '/api/v1/customers/measurement-templates'

const DRAFT = aTemplateVersion()

const PUBLISHED = aTemplateVersion({
  templateVersionId: '0199bb00-0000-7000-8000-0000000000e2',
  versionNumber: 2,
  name: 'Version 2',
  status: 'Published',
  isApproved: true,
  publishedAt: '2026-09-05T09:15:00.000Z',
})

const IN_REVIEW = aTemplateVersion({
  templateVersionId: '0199bb00-0000-7000-8000-0000000000e3',
  versionNumber: 3,
  name: 'Version 3',
  status: 'InReview',
})

const TEMPLATE = aMeasurementTemplate()

const LIVE = aMeasurementTemplate({
  publishedVersionId: PUBLISHED.templateVersionId,
  versions: [DRAFT, PUBLISHED],
})

const DETAIL = `${TEMPLATES}/${TEMPLATE.measurementTemplateId}`

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
  transport.route(`GET ${TEMPLATES}`, () => jsonResponse([TEMPLATE]))
  transport.route(`GET ${DETAIL}`, () => versionedResponse(TEMPLATE, 'W/"1"'))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderList() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/admin/templates']}>
          <TemplateListRoute />
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

function renderDetail() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[`/admin/templates/${TEMPLATE.measurementTemplateId}`]}>
          <Routes>
            <Route path="/admin/templates/:templateId" element={<TemplateDetailRoute />} />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/** Opens an action's confirmation and answers it, typing a reason when the tier asks for one. */
async function confirm(
  user: ReturnType<typeof userEvent.setup>,
  action: string,
  reason: string | null,
) {
  await user.click(screen.getByRole('button', { name: action }))

  if (reason !== null) {
    await user.type(await screen.findByRole('textbox'), reason)
  }

  const dialog = await screen.findByRole('dialog')
  const confirmButton = Array.from(dialog.querySelectorAll('button')).find(
    (candidate) => candidate.textContent === action,
  )

  await user.click(confirmButton as HTMLButtonElement)
}

/* The list ------------------------------------------------------------------------------------ */

it('says which version each template is capturing against, in words', async () => {
  transport.route(`GET ${TEMPLATES}`, () => jsonResponse([LIVE]))

  renderList()

  expect(await screen.findByRole('link', { name: 'Open Blouse, pattern work' })).toBeInTheDocument()
  expect(screen.getByText('Version 2')).toBeInTheDocument()
})

it('says plainly when a template can measure nothing yet', async () => {
  renderList()

  expect(await screen.findByRole('link', { name: 'Open Blouse, pattern work' })).toBeInTheDocument()
  expect(screen.getByText('Nothing published')).toBeInTheDocument()
})

it('says so when there is no template at all, rather than showing an empty table', async () => {
  transport.route(`GET ${TEMPLATES}`, () => jsonResponse([]))

  renderList()

  expect(await screen.findByText(/No measurement template has been set up yet/)).toBeInTheDocument()
  expect(screen.queryByRole('table')).not.toBeInTheDocument()
})

it('explains a refusal without showing the server its own words', async () => {
  transport.route(`GET ${TEMPLATES}`, () => problemResponse(503, 'platform.unavailable'))

  renderList()

  expect(await screen.findByRole('alert')).toBeInTheDocument()
  expect(screen.queryByText(/platform.unavailable/)).not.toBeInTheDocument()
})

it('tells a caller who holds neither permission what is missing', async () => {
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions: [] })))

  // Through RequireSession, because RequirePermission reads the current user and that boundary is
  // what puts one there — the same nesting the router and the story transport use.
  render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/admin/templates']}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                path="/admin/templates"
                element={
                  <RequirePermission permission={ADMIN_PERMISSIONS.templatesEdit}>
                    <TemplateListRoute />
                  </RequirePermission>
                }
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )

  expect(await screen.findByText(/You do not have access to this/)).toBeInTheDocument()
  expect(screen.getByText(/ask the shop owner/)).toBeInTheDocument()
  expect(screen.queryByRole('table')).not.toBeInTheDocument()
})

it('has no accessibility violations on the list', async () => {
  const { container } = renderList()

  await screen.findByRole('link', { name: 'Open Blouse, pattern work' })

  await expectNoAccessibilityViolations(container)
})

/* One template ------------------------------------------------------------------------------- */

it('offers a draft only the act it admits, and no other', async () => {
  renderDetail()

  expect(await screen.findByRole('button', { name: 'Submit for review' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Publish' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Retire' })).not.toBeInTheDocument()
})

it('offers a version in review a return and an approval, and publication only once approved', async () => {
  transport.route(`GET ${DETAIL}`, () =>
    versionedResponse(aMeasurementTemplate({ versions: [IN_REVIEW] }), 'W/"1"'),
  )

  renderDetail()

  expect(await screen.findByRole('button', { name: 'Approve' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Send back' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Publish' })).not.toBeInTheDocument()

  transport.route(`GET ${DETAIL}`, () =>
    versionedResponse(
      aMeasurementTemplate({ versions: [{ ...IN_REVIEW, isApproved: true }] }),
      'W/"2"',
    ),
  )

  renderDetail()

  expect((await screen.findAllByRole('button', { name: 'Publish' }))[0]).toBeInTheDocument()
})

it('offers a published version a draft rather than an edit that would be refused', async () => {
  transport.route(`GET ${DETAIL}`, () => versionedResponse(LIVE, 'W/"1"'))

  renderDetail()

  expect(
    await screen.findByRole('button', { name: 'Start a draft from this version' }),
  ).toBeInTheDocument()
  expect(screen.getByText(/cannot be edited/)).toBeInTheDocument()
})

it('sends the version it was showing and a retry key, and asks again before it does', async () => {
  const user = userEvent.setup()

  transport.route(`POST ${DETAIL}/versions/${DRAFT.templateVersionId}/submit`, () =>
    versionedResponse(TEMPLATE, 'W/"2"'),
  )

  renderDetail()

  await screen.findByRole('button', { name: 'Submit for review' })
  await confirm(user, 'Submit for review', null)

  const sent = transport.callsTo(`POST ${DETAIL}/versions/${DRAFT.templateVersionId}/submit`)[0]
  if (sent === undefined) {
    throw new Error('The submit command was never sent.')
  }

  expect(sent.headers.get('If-Match')).toBe('W/"1"')
  expect(sent.headers.get('Idempotency-Key')).not.toBeNull()
  // Two reads: the one that rendered the screen, and the one taken immediately before the command
  // so that the precondition is against what is true now.
  expect(transport.callsTo(`GET ${DETAIL}`).length).toBeGreaterThanOrEqual(2)
})

it('carries the reason the server refuses publication without', async () => {
  const user = userEvent.setup()

  transport.route(`GET ${DETAIL}`, () =>
    versionedResponse(
      aMeasurementTemplate({ versions: [{ ...IN_REVIEW, isApproved: true }] }),
      'W/"1"',
    ),
  )
  transport.route(`POST ${DETAIL}/versions/${IN_REVIEW.templateVersionId}/publish`, () =>
    versionedResponse(TEMPLATE, 'W/"2"'),
  )

  renderDetail()

  await screen.findByRole('button', { name: 'Publish' })
  await confirm(user, 'Publish', 'Reviewed with the Tailor Master.')

  const sent = transport.callsTo(
    `POST ${DETAIL}/versions/${IN_REVIEW.templateVersionId}/publish`,
  )[0]
  if (sent === undefined) {
    throw new Error('The publish command was never sent.')
  }

  expect(sent.body).toEqual({ reason: 'Reviewed with the Tailor Master.' })
})

it('explains the separation of duties rather than only refusing', async () => {
  const user = userEvent.setup()

  transport.route(`GET ${DETAIL}`, () =>
    versionedResponse(aMeasurementTemplate({ versions: [IN_REVIEW] }), 'W/"1"'),
  )
  transport.route(`POST ${DETAIL}/versions/${IN_REVIEW.templateVersionId}/approve`, () =>
    problemResponse(403, 'measurements.submitter-cannot-publish'),
  )

  renderDetail()

  await screen.findByRole('button', { name: 'Approve' })
  await confirm(user, 'Approve', null)

  expect(await screen.findByText(/does not also approve it/)).toBeInTheDocument()
})

it('offers a re-read when somebody else changed the template first', async () => {
  const user = userEvent.setup()

  transport.route(`POST ${DETAIL}/versions/${DRAFT.templateVersionId}/submit`, () =>
    problemResponse(409, 'measurements.version-changed'),
  )

  renderDetail()

  await screen.findByRole('button', { name: 'Submit for review' })
  await confirm(user, 'Submit for review', null)

  expect(await screen.findByRole('button', { name: 'Reload' })).toBeInTheDocument()
})

it('has no accessibility violations on one template', async () => {
  transport.route(`GET ${DETAIL}`, () => versionedResponse(LIVE, 'W/"1"'))

  const { container } = renderDetail()

  await screen.findByRole('button', { name: 'Start a draft from this version' })

  await expectNoAccessibilityViolations(container)
})
