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
import { aBranch, versionedResponse } from '../../admin/testing/fixtures'
import { ADMIN_PERMISSIONS } from '../../admin/adminPermissions'
import {
  COIMBATORE,
  ERODE,
  aCatalogFinding,
  aCatalogVersion,
  aCatalogVersionSummary,
  aCategory,
  aServiceType,
  aValidationReport,
} from '../../catalog/testing/fixtures'
import { CatalogVersionEditorRoute } from './CatalogVersionEditorRoute'
import { CatalogVersionListRoute } from './CatalogVersionListRoute'

let transport: FetchStub

const CATALOG = '/api/v1/catalog'
const DRAFT = aCatalogVersion()
const VERSION_ID = DRAFT.version.catalogVersionId
const VERSION = `${CATALOG}/versions/${VERSION_ID}`

/** Both keys, which is what an Owner or Admin carries. */
const BOTH_KEYS = [ADMIN_PERMISSIONS.catalogEdit, ADMIN_PERMISSIONS.catalogPublish]

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions: BOTH_KEYS })))
  transport.route('GET /api/v1/admin/branches/', () =>
    jsonResponse([
      aBranch({ branchId: COIMBATORE, name: 'Coimbatore counter' }),
      aBranch({ branchId: ERODE, code: 'ERD01', name: 'Erode counter' }),
    ]),
  )
  transport.route(`GET ${CATALOG}/versions`, () => jsonResponse([DRAFT.version]))
  transport.route(`GET ${VERSION}`, () => versionedResponse(DRAFT, 'W/"1"'))
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
        <MemoryRouter initialEntries={['/admin/catalog']}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route element={<CatalogVersionListRoute />} path="/admin/catalog" />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

function renderEditor(versionId = VERSION_ID) {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[`/admin/catalog/${versionId}`]}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route element={<CatalogVersionEditorRoute />} path="/admin/catalog/:versionId" />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/* The versions ---------------------------------------------------------------------------------- */

it('lists the versions and says loudly when nothing is published', async () => {
  const { container } = renderList()

  const table = within(await screen.findByRole('table'))
  expect(table.getByText('Draft')).toBeInTheDocument()

  // A shop with no published catalogue cannot take an order at all. That is where every
  // installation begins, so it is stated as a fact with the act that ends it beside it.
  expect(screen.getByText(/no counter can take an order yet/)).toBeInTheDocument()

  await expectNoAccessibilityViolations(container)
})

it('says what an empty catalogue means for the shop, not that a list is empty', async () => {
  transport.route(`GET ${CATALOG}/versions`, () => jsonResponse([]))
  renderList()

  expect(await screen.findByText(/no counter can take an order/)).toBeInTheDocument()
})

it('starts a draft with a reason, on a retry key it keeps', async () => {
  const user = userEvent.setup()
  transport.route(`POST ${CATALOG}/versions`, () => versionedResponse(DRAFT, 'W/"1"'))

  renderList()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Start a draft' }))

  const dialog = await screen.findByRole('dialog')
  await user.type(within(dialog).getByRole('textbox'), 'Add the Aari work category.')
  await user.click(within(dialog).getByRole('button', { name: 'Start a draft' }))

  await waitFor(() => {
    expect(transport.callsTo(`POST ${CATALOG}/versions`)).toHaveLength(1)
  })

  const sent = transport.callsTo(`POST ${CATALOG}/versions`)[0]
  expect(sent?.body).toEqual({
    name: 'Add the Aari work category.',
    notes: null,
    cloneFromVersionId: null,
  })
  expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
})

it('offers a clone of a published version, and never of a draft', async () => {
  const published = aCatalogVersionSummary({
    catalogVersionId: '0199bb00-0000-7000-8000-0000000000c2',
    versionNumber: 2,
    status: 'Published',
    publishedAt: '2026-09-05T09:15:00.000Z',
  })
  transport.route(`GET ${CATALOG}/versions`, () => jsonResponse([DRAFT.version, published]))

  renderList()
  await screen.findByRole('table')

  // A draft is already a working copy; cloning one would be a second copy of the same change.
  expect(screen.getAllByRole('button', { name: 'Start a draft from this version' })).toHaveLength(1)
})

/* One version ------------------------------------------------------------------------------------ */

it('shows the hierarchy with its nesting said, not only drawn', async () => {
  const child = aCategory({
    categoryId: 'id-aari',
    parentCategoryId: DRAFT.categories[0]?.categoryId ?? null,
    code: 'AARI',
    name: 'Aari work',
  })
  transport.route(`GET ${VERSION}`, () =>
    versionedResponse({ ...DRAFT, categories: [...DRAFT.categories, child] }, 'W/"1"'),
  )

  const { container } = renderEditor()
  const table = within(await screen.findByRole('table'))

  // Indentation alone is not available to a screen-reader user reading a table cell.
  expect(table.getByText('BLOUSE')).toBeInTheDocument()
  expect(table.getByText('— AARI')).toBeInTheDocument()
  expect(table.getByText('— PATTERN')).toBeInTheDocument()

  await expectNoAccessibilityViolations(container)
})

it('says which branch breaks the subset rule, where it is broken', async () => {
  // A sub-category cannot be offered where its parent is not, and finding out at publication means
  // working back from a finding to which of twenty categories it was about.
  const parent = aCategory({ categoryId: 'p', code: 'BLOUSE', branchIds: [COIMBATORE] })
  const child = aCategory({
    categoryId: 'c',
    parentCategoryId: 'p',
    code: 'AARI',
    name: 'Aari work',
    branchIds: [COIMBATORE, ERODE],
  })
  transport.route(`GET ${VERSION}`, () =>
    versionedResponse({ ...DRAFT, categories: [parent, child], serviceTypes: [] }, 'W/"1"'),
  )

  renderEditor()
  await screen.findByRole('table')

  expect(screen.getByText(/where its parent is not/)).toBeInTheDocument()
})

it('says a category is offered nowhere, rather than leaving the cell blank', async () => {
  // An empty branch list is not "offered everywhere". A blank cell would read as the second.
  transport.route(`GET ${VERSION}`, () =>
    versionedResponse(
      { ...DRAFT, categories: [aCategory({ branchIds: [] })], serviceTypes: [] },
      'W/"1"',
    ),
  )

  renderEditor()
  await screen.findByRole('table')

  expect(screen.getByText('Nowhere')).toBeInTheDocument()
})

it('names which links are missing rather than only that it is not orderable', async () => {
  transport.route(`GET ${VERSION}`, () =>
    versionedResponse(
      {
        ...DRAFT,
        serviceTypes: [
          aServiceType({
            priceListItemCode: null,
            qcChecklistTemplateId: null,
            notOrderable: true,
          }),
        ],
      },
      'W/"1"',
    ),
  )

  renderEditor()
  await screen.findByRole('table')

  // The server's `notOrderable` says *that*; only the screen can say which of the five links is why.
  expect(screen.getByText(/2 links still missing/)).toBeInTheDocument()
  expect(screen.getByText(/what it costs/)).toBeInTheDocument()
  expect(screen.getByText(/how it is checked/)).toBeInTheDocument()
})

it('adds a category with the rendered tag and a retry key', async () => {
  const user = userEvent.setup()
  transport.route(`POST ${VERSION}/categories`, () => versionedResponse(DRAFT, 'W/"2"'))

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Add a category' }))

  const form = within(await screen.findByRole('form', { name: 'A new category' }))
  await user.type(form.getByLabelText('Code'), 'salwar')
  await user.type(form.getByLabelText('Name'), 'Salwar kameez')
  await user.click(form.getByLabelText('Coimbatore counter'))
  await user.click(form.getByRole('button', { name: 'Save' }))

  await waitFor(() => {
    expect(transport.callsTo(`POST ${VERSION}/categories`)).toHaveLength(1)
  })

  const sent = transport.callsTo(`POST ${VERSION}/categories`)[0]
  const body = sent?.body as Record<string, unknown>

  expect(sent?.headers.get('If-Match')).toBe('W/"1"')
  expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
  // Upper-cased as it is typed: a code appears in every order, invoice and report, so a person who
  // types `salwar` has to see that `SALWAR` is what they are creating.
  expect(body.code).toBe('SALWAR')
  expect(body.branchIds).toEqual([COIMBATORE])
})

it('does not offer to change a code that already exists', async () => {
  const user = userEvent.setup()
  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Edit Blouse' }))

  const form = within(await screen.findByRole('form', { name: 'Editing Blouse' }))

  // A code is what every order files the thing under; the server does not accept a change to it,
  // so an editable box would appear to work and change nothing.
  expect(form.getByLabelText('Code')).toHaveAttribute('readonly')
  expect(form.getByText(/Orders already placed are filed under it/)).toBeInTheDocument()
})

it('offers no editing at all on a published version, and says where the change is made', async () => {
  transport.route(`GET ${VERSION}`, () =>
    versionedResponse(
      {
        ...DRAFT,
        version: aCatalogVersionSummary({
          status: 'Published',
          publishedAt: '2026-09-05T09:15:00.000Z',
        }),
      },
      'W/"1"',
    ),
  )

  renderEditor()
  await screen.findByRole('table')

  // A published version's structure is what orders were placed against; changing it would change
  // what an order already placed meant.
  expect(screen.getByText(/its structure cannot be changed/)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Add a category' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Edit Blouse' })).not.toBeInTheDocument()
})

it('offers the label correction on a published version, and demands a reason with it', async () => {
  const user = userEvent.setup()
  const category = DRAFT.categories[0]
  const published = {
    ...DRAFT,
    version: aCatalogVersionSummary({
      status: 'Published',
      publishedAt: '2026-09-05T09:15:00.000Z',
    }),
  }
  const route = `${VERSION}/categories/${category?.categoryId ?? ''}/presentation`

  transport.route(`GET ${VERSION}`, () => versionedResponse(published, 'W/"1"'))
  transport.route(`POST ${route}`, () => versionedResponse(published, 'W/"2"'))

  const { container } = renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Correct the labels of Blouse' }))

  const form = within(screen.getByRole('form', { name: 'Correct the labels of Blouse' }))
  await expectNoAccessibilityViolations(container)

  // The other controls are absent rather than disabled. The boundary between a label and a
  // behaviour is the whole safety argument: a form one flag away from sending a code is not it.
  expect(form.queryByLabelText('Code')).not.toBeInTheDocument()
  expect(form.queryByLabelText(/price-list item/i)).not.toBeInTheDocument()
  expect(form.getByLabelText('Why are you making this change?')).toBeRequired()

  await user.clear(form.getByLabelText('Name'))
  await user.type(form.getByLabelText('Name'), 'Blouse (all styles)')
  await user.type(
    form.getByLabelText('Why are you making this change?'),
    'Counter staff asked for the plural.',
  )
  await user.click(form.getByRole('button', { name: 'Correct the labels' }))

  await waitFor(() => {
    expect(transport.callsTo(`POST ${route}`)).toHaveLength(1)
  })

  const sent = transport.callsTo(`POST ${route}`)[0]
  expect(sent?.body).toEqual({
    name: 'Blouse (all styles)',
    nameTamil: null,
    description: 'Everything stitched as a blouse.',
    displayOrder: 0,
    reason: 'Counter staff asked for the plural.',
  })
  expect(sent?.headers.get('If-Match')).toBe('W/"1"')
})

it('does not offer the correction to somebody who may draft but not publish', async () => {
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [ADMIN_PERMISSIONS.catalogEdit] })),
  )
  transport.route(`GET ${VERSION}`, () =>
    versionedResponse(
      {
        ...DRAFT,
        version: aCatalogVersionSummary({
          status: 'Published',
          publishedAt: '2026-09-05T09:15:00.000Z',
        }),
      },
      'W/"1"',
    ),
  )

  renderEditor()
  await screen.findByRole('table')

  // The correction endpoint asks for `catalog.publish`, for the same reason publishing does.
  expect(
    screen.queryByRole('button', { name: 'Correct the labels of Blouse' }),
  ).not.toBeInTheDocument()
  expect(screen.getByText(/Correcting a label on a published version/)).toBeInTheDocument()
})

it('hides publishing and retiring from somebody who holds only the drafting key', async () => {
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [ADMIN_PERMISSIONS.catalogEdit] })),
  )

  renderEditor()
  await screen.findByRole('table')

  // Two keys, not one: a screen offering these to somebody holding only the first would offer
  // controls that each end in a 403.
  expect(screen.queryByRole('button', { name: 'Publish this version' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Retire this version' })).not.toBeInTheDocument()
  expect(screen.getByText(/need the catalogue publishing permission/)).toBeInTheDocument()
})

it('reports every finding beside the thing it is about', async () => {
  const user = userEvent.setup()
  transport.route(`GET ${VERSION}/validation`, () =>
    jsonResponse(
      aValidationReport({
        publishable: false,
        errorCount: 1,
        warningCount: 1,
        findings: [
          aCatalogFinding(),
          aCatalogFinding({
            severity: 'Warning',
            code: 'catalog.category-not-translated',
            message: "'BLOUSE' has no Tamil name.",
            target: 'categories[BLOUSE].nameTamil',
          }),
        ],
      }),
    ),
  )

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Check this version' }))

  const rows = await screen.findAllByRole('row')
  const blouse = rows.find((row) => row.textContent?.includes('BLOUSE'))
  const pattern = rows.find((row) => row.textContent?.includes('PATTERN'))

  expect(within(blouse as HTMLElement).getByText(/has no Tamil name/)).toBeInTheDocument()
  expect(within(pattern as HTMLElement).getByText(/has no price-list item/)).toBeInTheDocument()
  expect(screen.getByText(/Publication is refused/)).toBeInTheDocument()
})

it('keeps a finding it cannot anchor rather than losing a refusal', async () => {
  const user = userEvent.setup()
  transport.route(`GET ${VERSION}/validation`, () =>
    jsonResponse(
      aValidationReport({
        publishable: false,
        errorCount: 1,
        findings: [
          aCatalogFinding({
            message: 'This catalogue version has no categories at all.',
            target: null,
          }),
        ],
      }),
    ),
  )

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Check this version' }))

  expect(
    await screen.findByText(/This catalogue version has no categories at all\./),
  ).toBeInTheDocument()
  expect(screen.getByText(/reported about something not on this screen/)).toBeInTheDocument()
})

it('does not let a warning read as something that blocks publication', async () => {
  const user = userEvent.setup()
  transport.route(`GET ${VERSION}/validation`, () =>
    jsonResponse(
      aValidationReport({
        warningCount: 1,
        findings: [
          aCatalogFinding({
            severity: 'Warning',
            message: "'BLOUSE' has no Tamil name.",
            target: 'categories[BLOUSE].nameTamil',
          }),
        ],
      }),
    ),
  )

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Check this version' }))

  expect(await screen.findByText(/Nothing here refuses publication/)).toBeInTheDocument()
  expect(screen.queryByText(/Publication is refused/)).not.toBeInTheDocument()
})

it('says a report is stale once the draft has been written since it ran', async () => {
  const user = userEvent.setup()
  transport.route(`GET ${VERSION}/validation`, () => jsonResponse(aValidationReport()))
  transport.route(
    `POST ${VERSION}/categories/${DRAFT.categories[0]?.categoryId ?? ''}/delete`,
    () => versionedResponse(DRAFT, 'W/"2"'),
  )

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Check this version' }))
  await screen.findByText('Every check passed. This version can be published.')

  await user.click(screen.getByRole('button', { name: 'Remove Blouse' }))
  const dialog = await screen.findByRole('dialog')
  await user.type(within(dialog).getByRole('textbox'), 'Replaced by two narrower categories.')
  await user.click(within(dialog).getByRole('button', { name: 'Remove Blouse' }))

  expect(await screen.findByText(/ran against an earlier state/)).toBeInTheDocument()
})

it('says what publishing does to the shop before it is confirmed', async () => {
  const user = userEvent.setup()
  transport.route(`POST ${VERSION}/publish`, () =>
    versionedResponse({ version: DRAFT.version, supersededVersionId: null, findings: [] }, 'W/"2"'),
  )

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Publish this version' }))

  const dialog = await screen.findByRole('dialog')
  expect(
    within(dialog).getByText(/Every counter offers this from the moment you publish/),
  ).toBeInTheDocument()

  await user.type(within(dialog).getByRole('textbox'), 'Approved with the Tailor Master.')
  await user.click(within(dialog).getByRole('button', { name: 'Publish this version' }))

  await waitFor(() => {
    expect(transport.callsTo(`POST ${VERSION}/publish`)).toHaveLength(1)
  })

  const sent = transport.callsTo(`POST ${VERSION}/publish`)[0]
  expect(sent?.body).toEqual({ reason: 'Approved with the Tailor Master.' })
  expect(sent?.headers.get('If-Match')).toBe('W/"1"')
})

it('keeps the refusal, the reason and the retry key in the dialog that caused it', async () => {
  const user = userEvent.setup()
  transport.route(`POST ${VERSION}/publish`, () => problemResponse(409, 'catalog.version-changed'))

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Publish this version' }))

  const dialog = await screen.findByRole('dialog')
  const reason = within(dialog).getByRole('textbox')
  await user.type(reason, 'Approved with the Tailor Master.')
  await user.click(within(dialog).getByRole('button', { name: 'Publish this version' }))

  // The dialog is modal, so an alert rendered behind it is under the backdrop and outside the
  // focus trap: a person who confirmed would see a dialog that appeared to do nothing.
  await within(dialog).findByRole('alert')
  expect(reason).toHaveValue('Approved with the Tailor Master.')

  await user.click(within(dialog).getByRole('button', { name: 'Publish this version' }))
  await waitFor(() => {
    expect(transport.callsTo(`POST ${VERSION}/publish`)).toHaveLength(2)
  })

  // conventions section 4.3: a retry after a conflict reuses the same key. A fresh one would let a
  // publication whose answer was lost rather than refused happen twice.
  const sent = transport.callsTo(`POST ${VERSION}/publish`)
  expect(sent[1]?.headers.get('Idempotency-Key')).toBe(sent[0]?.headers.get('Idempotency-Key'))
})
