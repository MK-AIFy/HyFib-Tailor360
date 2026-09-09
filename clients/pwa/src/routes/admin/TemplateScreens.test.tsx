import { render, screen, waitFor, within } from '@testing-library/react'
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

/**
 * Both template keys, which is what the Owner and Admin system roles carry.
 *
 * The screen needs the caller's permissions now, because four of the five lifecycle acts are gated
 * on `catalog.templates.publish` rather than on the key that let somebody reach the screen.
 */
const BOTH_KEYS = [ADMIN_PERMISSIONS.templatesEdit, ADMIN_PERMISSIONS.templatesPublish]

const LIVE = aMeasurementTemplate({
  publishedVersionId: PUBLISHED.templateVersionId,
  versions: [DRAFT, PUBLISHED],
})

const DETAIL = `${TEMPLATES}/${TEMPLATE.measurementTemplateId}`

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions: BOTH_KEYS })))
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
            <Route element={<RequireSession />}>
              <Route path="/admin/templates/:templateId" element={<TemplateDetailRoute />} />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/**
 * Opens an action's confirmation and answers it, typing a reason when the tier asks for one.
 *
 * Everything is found by accessible name, including inside the dialog: the row control and the
 * confirming control share a name on purpose — 2.5.3 Label in Name is what makes "tap Publish" work
 * on voice control — so the dialog is the scope that tells them apart, not the DOM order.
 */
async function confirm(
  user: ReturnType<typeof userEvent.setup>,
  action: string,
  reason: string | null,
) {
  await user.click(screen.getByRole('button', { name: action }))

  const dialog = await screen.findByRole('dialog')

  if (reason !== null) {
    await user.type(within(dialog).getByRole('textbox'), reason)
  }

  await user.click(within(dialog).getByRole('button', { name: action }))
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
})

it('acts on the version it rendered, not on one somebody else has since changed', async () => {
  const user = userEvent.setup()

  renderDetail()

  await screen.findByRole('button', { name: 'Submit for review' })

  // Somebody else changes the template while this page is open. Re-reading before the command would
  // pick their revision up and submit against changes this administrator never saw, which is the one
  // thing the precondition exists to refuse.
  transport.route(`GET ${DETAIL}`, () => versionedResponse(TEMPLATE, 'W/"9"'))
  transport.route(`POST ${DETAIL}/versions/${DRAFT.templateVersionId}/submit`, () =>
    problemResponse(409, 'measurements.version-changed'),
  )

  await confirm(user, 'Submit for review', null)

  const sent = transport.callsTo(`POST ${DETAIL}/versions/${DRAFT.templateVersionId}/submit`)[0]
  if (sent === undefined) {
    throw new Error('The submit command was never sent.')
  }

  expect(sent.headers.get('If-Match')).toBe('W/"1"')
  expect(await screen.findByRole('button', { name: 'Reload' })).toBeInTheDocument()
})

it('sends one command however many times the confirmation is pressed', async () => {
  const user = userEvent.setup()

  // Never answers, so the command is still in flight when the second press lands.
  transport.route(
    `POST ${DETAIL}/versions/${DRAFT.templateVersionId}/submit`,
    () => new Promise<Response>(() => undefined),
  )

  renderDetail()

  await screen.findByRole('button', { name: 'Submit for review' })
  await user.click(screen.getByRole('button', { name: 'Submit for review' }))

  const dialog = await screen.findByRole('dialog')
  const confirmButton = within(dialog).getByRole('button', { name: 'Submit for review' })

  await user.click(confirmButton)
  await user.click(confirmButton)

  expect(
    transport.callsTo(`POST ${DETAIL}/versions/${DRAFT.templateVersionId}/submit`),
  ).toHaveLength(1)
})

it('withholds the reviewing acts from somebody who may not carry them out, and says why', async () => {
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [ADMIN_PERMISSIONS.templatesEdit] })),
  )
  transport.route(`GET ${DETAIL}`, () =>
    versionedResponse(aMeasurementTemplate({ versions: [IN_REVIEW] }), 'W/"1"'),
  )

  renderDetail()

  expect(await screen.findByText(/need the template publishing permission/)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Send back' })).not.toBeInTheDocument()
})

it('names a cloned draft after the template’s next version, not after the one copied', async () => {
  const user = userEvent.setup()

  const retired = aTemplateVersion({
    templateVersionId: '0199bb00-0000-7000-8000-0000000000e9',
    versionNumber: 1,
    name: 'Version 1',
    status: 'Retired',
  })

  transport.route(`GET ${DETAIL}`, () =>
    versionedResponse(
      aMeasurementTemplate({
        publishedVersionId: PUBLISHED.templateVersionId,
        versions: [retired, PUBLISHED, IN_REVIEW],
      }),
      'W/"1"',
    ),
  )
  transport.route(`POST ${DETAIL}/versions`, () => versionedResponse(TEMPLATE, 'W/"2"'))

  renderDetail()

  const clones = await screen.findAllByRole('button', { name: 'Start a draft from this version' })
  // The first row is version 3, newest first; the last is the retired version 1.
  await user.click(clones[clones.length - 1] as HTMLButtonElement)

  const sent = transport.callsTo(`POST ${DETAIL}/versions`)[0]
  if (sent === undefined) {
    throw new Error('The draft was never started.')
  }

  expect(sent.body).toMatchObject({
    name: 'Version 4',
    cloneFromVersionId: retired.templateVersionId,
  })
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

it('asks for a fresh proof of identity when the server demands one, rather than reporting a refusal', async () => {
  const user = userEvent.setup()

  transport.route(`GET ${DETAIL}`, () =>
    versionedResponse(
      aMeasurementTemplate({ versions: [{ ...IN_REVIEW, isApproved: true }] }),
      'W/"1"',
    ),
  )
  transport.route(`POST ${DETAIL}/versions/${IN_REVIEW.templateVersionId}/publish`, () =>
    problemResponse(403, 'security.step-up-required'),
  )

  renderDetail()

  await screen.findByRole('button', { name: 'Publish' })
  await confirm(user, 'Publish', 'Reviewed with the Tailor Master.')

  // The question, not "this did not go through and the reason is not clear".
  expect(await screen.findByText('Confirm it is you')).toBeInTheDocument()
})

it('reuses the retry key when a refused command is attempted again', async () => {
  const user = userEvent.setup()

  transport.route(`POST ${DETAIL}/versions/${DRAFT.templateVersionId}/submit`, () =>
    problemResponse(409, 'measurements.version-changed'),
  )

  renderDetail()

  await screen.findByRole('button', { name: 'Submit for review' })
  await confirm(user, 'Submit for review', null)
  await screen.findByRole('button', { name: 'Reload' })
  await confirm(user, 'Submit for review', null)

  const sent = transport.callsTo(`POST ${DETAIL}/versions/${DRAFT.templateVersionId}/submit`)

  expect(sent).toHaveLength(2)
  // conventions section 4.3: a retry after a conflict reuses the same key. A fresh one would let a
  // command whose answer was lost rather than refused happen twice.
  expect(sent[0]?.headers.get('Idempotency-Key')).toBe(sent[1]?.headers.get('Idempotency-Key'))
})

it('re-reads and clears the conflict when the administrator presses Reload', async () => {
  const user = userEvent.setup()

  transport.route(`POST ${DETAIL}/versions/${DRAFT.templateVersionId}/submit`, () =>
    problemResponse(409, 'measurements.version-changed'),
  )

  renderDetail()

  await screen.findByRole('button', { name: 'Submit for review' })
  await confirm(user, 'Submit for review', null)

  const reads = transport.callsTo(`GET ${DETAIL}`).length

  await user.click(await screen.findByRole('button', { name: 'Reload' }))

  await waitFor(() => {
    expect(transport.callsTo(`GET ${DETAIL}`).length).toBeGreaterThan(reads)
  })
  expect(screen.queryByRole('button', { name: 'Reload' })).not.toBeInTheDocument()
})

it('acts on the version its own last command produced, not the one already superseded', async () => {
  const user = userEvent.setup()

  let reads = 0
  transport.route(`GET ${DETAIL}`, () => {
    reads += 1

    // The re-read after a command never lands, which is the window the screen has to survive.
    return reads === 1 ? versionedResponse(LIVE, 'W/"1"') : new Promise<Response>(() => undefined)
  })
  transport.route(`POST ${DETAIL}/versions/${DRAFT.templateVersionId}/submit`, () =>
    versionedResponse(LIVE, 'W/"2"'),
  )
  transport.route(`POST ${DETAIL}/versions/${PUBLISHED.templateVersionId}/retire`, () =>
    versionedResponse(LIVE, 'W/"3"'),
  )

  renderDetail()

  await screen.findByRole('button', { name: 'Submit for review' })
  await confirm(user, 'Submit for review', null)
  await screen.findByText('Version 1 — done.')

  await confirm(user, 'Retire', 'Superseded by the new pattern.')

  const sent = transport.callsTo(`POST ${DETAIL}/versions/${PUBLISHED.templateVersionId}/retire`)[0]

  expect(sent?.headers.get('If-Match')).toBe('W/"2"')
})

it('says which refusal it was when publication fails its checks', async () => {
  const user = userEvent.setup()

  transport.route(`GET ${DETAIL}`, () =>
    versionedResponse(
      aMeasurementTemplate({ versions: [{ ...IN_REVIEW, isApproved: true }] }),
      'W/"1"',
    ),
  )
  transport.route(`POST ${DETAIL}/versions/${IN_REVIEW.templateVersionId}/publish`, () =>
    problemResponse(400, 'measurements.publish-validation-failed'),
  )

  renderDetail()

  await screen.findByRole('button', { name: 'Publish' })
  await confirm(user, 'Publish', 'Reviewed with the Tailor Master.')

  expect(await screen.findByText(/checks that run before publication/)).toBeInTheDocument()
})

it('says so plainly when a version is in a state this release does not know', async () => {
  transport.route(`GET ${DETAIL}`, () =>
    versionedResponse(
      aMeasurementTemplate({ versions: [aTemplateVersion({ status: 'Superseded' })] }),
      'W/"1"',
    ),
  )

  renderDetail()

  expect(
    await screen.findByText('A state this version of the application does not know'),
  ).toBeInTheDocument()
  expect(screen.queryByText(/admin\.template\.status/)).not.toBeInTheDocument()
})

it('explains a read that could not reach the server at all', async () => {
  transport.route(`GET ${DETAIL}`, () => {
    throw new TypeError('Failed to fetch')
  })

  renderDetail()

  expect(await screen.findByRole('alert')).toBeInTheDocument()
  expect(screen.queryByText(/Failed to fetch/)).not.toBeInTheDocument()
})

it('has no accessibility violations on one template', async () => {
  transport.route(`GET ${DETAIL}`, () => versionedResponse(LIVE, 'W/"1"'))

  const { container } = renderDetail()

  await screen.findByRole('button', { name: 'Start a draft from this version' })

  await expectNoAccessibilityViolations(container)
})
