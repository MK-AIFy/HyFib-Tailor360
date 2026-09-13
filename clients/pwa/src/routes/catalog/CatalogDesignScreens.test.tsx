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
import { RequirePermission } from '../../admin/RequirePermission'
import {
  COIMBATORE,
  ERODE,
  aCatalogVersion,
  aCategory,
  aDesignGroup,
  aDesignOption,
  aDesignRule,
  anOperand,
} from '../../catalog/testing/fixtures'
import { CatalogDesignRoute } from './CatalogDesignRoute'

let transport: FetchStub

const CATALOG = '/api/v1/catalog'
const BLOUSE = aCategory()
const NECKLINE = aDesignGroup({
  designOptionGroupId: 'group-neckline',
  categoryId: BLOUSE.categoryId,
  code: 'neckline',
  name: 'Neckline',
  displayOrder: 0,
  options: [
    aDesignOption({
      designOptionId: 'option-round',
      designOptionGroupId: 'group-neckline',
      code: 'ROUND',
      name: 'Round',
      displayOrder: 0,
    }),
  ],
})
const SLEEVE = aDesignGroup({
  designOptionGroupId: 'group-sleeve',
  categoryId: BLOUSE.categoryId,
  code: 'sleeve',
  name: 'Sleeve length',
  displayOrder: 1,
  options: [
    aDesignOption({
      designOptionId: 'option-short',
      designOptionGroupId: 'group-sleeve',
      code: 'SHORT',
      name: 'Short',
      displayOrder: 0,
    }),
  ],
})
const RULE = aDesignRule({
  designRuleId: 'rule-1',
  categoryId: BLOUSE.categoryId,
  identifier: 'DR-01',
  antecedent: anOperand({ groupCode: 'neckline', form: 'Equals', optionCodes: ['ROUND'] }),
  consequent: anOperand({ groupCode: 'sleeve', form: 'Equals', optionCodes: ['SHORT'] }),
})
const DRAFT = aCatalogVersion({
  categories: [BLOUSE],
  serviceTypes: [],
  designGroups: [NECKLINE, SLEEVE],
  designRules: [RULE],
})
const VERSION_ID = DRAFT.version.catalogVersionId
const VERSION = `${CATALOG}/versions/${VERSION_ID}`

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
  transport.route(`GET ${VERSION}`, () => versionedResponse(DRAFT, 'W/"1"'))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderDesign() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter
          initialEntries={[`/admin/catalog/${VERSION_ID}/categories/${BLOUSE.categoryId}/design`]}
        >
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                element={<CatalogDesignRoute />}
                path="/admin/catalog/:versionId/categories/:categoryId/design"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

it('lists the category’s design groups, their options, and the rules between them', async () => {
  const { container } = renderDesign()

  const groupTable = within((await screen.findAllByRole('table'))[0] ?? document.body)
  expect(groupTable.getByText('neckline')).toBeInTheDocument()
  expect(groupTable.getByText('— ROUND')).toBeInTheDocument()
  expect(screen.getByText('DR-01')).toBeInTheDocument()

  await expectNoAccessibilityViolations(container)
})

it('shows the alternative text when an option has no illustration', async () => {
  renderDesign()

  await screen.findByText('neckline')
  // Neither ROUND nor SHORT carries an illustration key in the fixture, so the picker's own
  // fallback is shown for both.
  expect(screen.getAllByText(/No illustration yet/)).toHaveLength(2)
})

it('adds a design group with the rendered tag and a retry key', async () => {
  const user = userEvent.setup()
  transport.route(`POST ${VERSION}/categories/${BLOUSE.categoryId}/design-groups`, () =>
    versionedResponse(NECKLINE, 'W/"2"'),
  )

  renderDesign()
  await screen.findByText('neckline')
  await user.click(screen.getByRole('button', { name: 'Add a group' }))

  const form = within(await screen.findByRole('form', { name: 'A new design group' }))
  await user.type(form.getByLabelText('Code'), 'sleeve_style')
  await user.type(form.getByLabelText('Name'), 'Sleeve style')
  await user.click(form.getByRole('button', { name: 'Save' }))

  await waitFor(() => {
    expect(
      transport.callsTo(`POST ${VERSION}/categories/${BLOUSE.categoryId}/design-groups`),
    ).toHaveLength(1)
  })

  const sent = transport.callsTo(`POST ${VERSION}/categories/${BLOUSE.categoryId}/design-groups`)[0]
  const body = sent?.body as Record<string, unknown>

  expect(sent?.headers.get('If-Match')).toBe('W/"1"')
  expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
  expect(body.code).toBe('sleeve_style')
  expect(body.selectionMode).toBe('SingleChoice')
})

it('adds an option to a group', async () => {
  const user = userEvent.setup()
  transport.route(`POST ${VERSION}/design-groups/${NECKLINE.designOptionGroupId}/options`, () =>
    versionedResponse(NECKLINE.options[0], 'W/"2"'),
  )

  renderDesign()
  await screen.findByText('neckline')
  await user.click(screen.getByRole('button', { name: 'Add an option to Neckline' }))

  const form = within(await screen.findByRole('form', { name: 'A new option' }))
  await user.type(form.getByLabelText('Code'), 'boat')
  await user.type(form.getByLabelText('Name'), 'Boat neck')
  await user.type(form.getByLabelText('Alternative text'), 'A wide boat-shaped neckline.')
  await user.click(form.getByRole('button', { name: 'Save' }))

  await waitFor(() => {
    expect(
      transport.callsTo(`POST ${VERSION}/design-groups/${NECKLINE.designOptionGroupId}/options`),
    ).toHaveLength(1)
  })

  const sent = transport.callsTo(
    `POST ${VERSION}/design-groups/${NECKLINE.designOptionGroupId}/options`,
  )[0]
  const body = sent?.body as Record<string, unknown>
  expect(body.code).toBe('BOAT')
})

it('composes a rule from the operand grammar and shows the sentence before saving', async () => {
  const user = userEvent.setup()
  transport.route(`POST ${VERSION}/categories/${BLOUSE.categoryId}/design-rules`, () =>
    versionedResponse(RULE, 'W/"2"'),
  )

  renderDesign()
  await screen.findByText('neckline')
  await user.click(screen.getByRole('button', { name: 'Add a rule' }))

  const form = within(await screen.findByRole('form', { name: 'A design rule' }))
  await user.selectOptions(
    form.getByLabelText('Condition', { selector: '#catalog-design-antecedent-form' }),
    'Is exactly',
  )
  await user.selectOptions(
    form.getByLabelText('Group', { selector: '#catalog-design-antecedent-group' }),
    'Neckline',
  )
  await user.click(form.getByLabelText('Round'))

  // The rule reads back in words before it is ever sent.
  expect(screen.getByText(/Neckline is Round/)).toBeInTheDocument()

  await user.click(form.getByRole('button', { name: 'Save' }))

  await waitFor(() => {
    expect(
      transport.callsTo(`POST ${VERSION}/categories/${BLOUSE.categoryId}/design-rules`),
    ).toHaveLength(1)
  })

  const sent = transport.callsTo(`POST ${VERSION}/categories/${BLOUSE.categoryId}/design-rules`)[0]
  const body = sent?.body as { antecedent: Record<string, unknown> }
  expect(body.antecedent).toEqual({ groupCode: 'neckline', form: 'Equals', optionCodes: ['ROUND'] })
})

it('removes a design group with a reason, and refuses to submit without one', async () => {
  const user = userEvent.setup()
  transport.route(
    `POST ${VERSION}/design-groups/${NECKLINE.designOptionGroupId}/delete`,
    () => new Response(null, { status: 204 }),
  )

  renderDesign()
  await screen.findByText('neckline')
  await user.click(screen.getByRole('button', { name: 'Remove Neckline' }))

  const dialog = await screen.findByRole('dialog')
  await user.click(within(dialog).getByRole('button', { name: 'Remove Neckline' }))
  expect(within(dialog).getByText('Type the reason before you confirm.')).toBeInTheDocument()
  expect(
    transport.callsTo(`POST ${VERSION}/design-groups/${NECKLINE.designOptionGroupId}/delete`),
  ).toHaveLength(0)

  await user.type(within(dialog).getByRole('textbox'), 'The shop stopped offering this style.')
  await user.click(within(dialog).getByRole('button', { name: 'Remove Neckline' }))

  await waitFor(() => {
    expect(
      transport.callsTo(`POST ${VERSION}/design-groups/${NECKLINE.designOptionGroupId}/delete`),
    ).toHaveLength(1)
  })
})

it('shows a reload prompt on a conflict, rather than a bare refusal', async () => {
  const user = userEvent.setup()
  transport.route(`POST ${VERSION}/categories/${BLOUSE.categoryId}/design-groups`, () =>
    problemResponse(409, 'platform.version-conflict'),
  )

  renderDesign()
  await screen.findByText('neckline')
  await user.click(screen.getByRole('button', { name: 'Add a group' }))

  const form = within(await screen.findByRole('form', { name: 'A new design group' }))
  await user.type(form.getByLabelText('Code'), 'sleeve_style')
  await user.type(form.getByLabelText('Name'), 'Sleeve style')
  await user.click(form.getByRole('button', { name: 'Save' }))

  expect(await screen.findByRole('button', { name: 'Reload' })).toBeInTheDocument()
})

it('offers no editing controls to somebody without catalog.edit', async () => {
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions: [] })))

  render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter
          initialEntries={[`/admin/catalog/${VERSION_ID}/categories/${BLOUSE.categoryId}/design`]}
        >
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                element={
                  <RequirePermission permission={ADMIN_PERMISSIONS.catalogEdit}>
                    <CatalogDesignRoute />
                  </RequirePermission>
                }
                path="/admin/catalog/:versionId/categories/:categoryId/design"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )

  expect(await screen.findByText(/ask/i)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Add a group' })).not.toBeInTheDocument()
})
