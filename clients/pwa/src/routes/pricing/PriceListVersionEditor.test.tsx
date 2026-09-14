import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { RequireSession } from '../../auth/RequireSession'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { RequirePermission } from '../../admin/RequirePermission'
import { aBranch } from '../../admin/testing/fixtures'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import { BRANCH_ID } from '../../billing/testing/fixtures'
import {
  PRICE_LIST_ITEM_ID,
  PRICE_LIST_VERSION_ID,
  aDiscountRule,
  aPriceListItem,
  aPriceListVersion,
  aPriceListVersionSummary,
} from '../../billing/testing/priceListFixtures'
import {
  TAX_CONFIGURATION_VERSION_ID,
  aTaxCode,
  aTaxConfiguration,
  aTaxConfigurationSummary,
} from '../../billing/testing/pricingConfigFixtures'
import type { PriceListItem } from '../../billing/priceListTypes'
import { PriceListVersionEditorRoute } from './PriceListVersionEditorRoute'

let transport: FetchStub

const PRICE_LISTS = '/api/v1/billing/price-lists'
const PRICE_LIST_VERSION_BASE = `${PRICE_LISTS}/versions`
const BRANCHES = '/api/v1/admin/branches/'
const TAX_VERSIONS = '/api/v1/billing/tax-configuration/versions'
const DRAFT_ID = PRICE_LIST_VERSION_ID
const SECOND_ITEM_ID = '0199dd00-0000-7000-8000-000000007099'

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route(`GET ${BRANCHES}`, () =>
    jsonResponse([aBranch({ branchId: BRANCH_ID, name: 'Coimbatore counter', code: 'CBE01' })]),
  )
  // Nothing published by default: most tests exercise the typed tax-code path. The tests that need
  // an active, published code override this before rendering.
  transport.route(`GET ${TAX_VERSIONS}`, () => jsonResponse([]))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

/**
 * A JSON body carrying the `ETag` a write is made against — `jsonResponse` alone cannot.
 *
 * A `204` (the item removal route's own answer) may not carry a body at all, so `null` is passed to
 * the `Response` constructor as-is rather than as the four-character string `"null"`.
 */
function versionedJson(body: unknown, etag: string, status = 200): Response {
  return new Response(body === null ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ETag: etag },
  })
}

function renderApp(
  path: string,
  permissions: readonly string[] = [BILLING_PERMISSIONS.managePriceLists],
) {
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions })))

  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                element={
                  <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
                    <PriceListVersionEditorRoute />
                  </RequirePermission>
                }
                path="/admin/price-lists/versions/:versionId"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

function renderEditorAt(
  versionId: string,
  permissions: readonly string[] = [BILLING_PERMISSIONS.managePriceLists],
) {
  return renderApp(`/admin/price-lists/versions/${versionId}`, permissions)
}

describe('the price-list version editor', () => {
  it('adds an item with a tax code from the published configuration, edits it, removes it, and changes the conventions', async () => {
    const user = userEvent.setup()
    const NEW_ITEM_ID = '0199dd00-0000-7000-8000-000000007003'
    let items: readonly PriceListItem[] = []
    let version = aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' })
    let tag = 'W/"1"'
    const versionBody = () => ({ version, items, discountRules: [] })

    transport.route(`GET ${TAX_VERSIONS}`, () =>
      jsonResponse([aTaxConfigurationSummary({ status: 'Published' })]),
    )
    transport.route(`GET ${TAX_VERSIONS}/${TAX_CONFIGURATION_VERSION_ID}`, () =>
      versionedJson(
        aTaxConfiguration({ taxCodes: [aTaxCode({ code: 'STITCHING_5', active: true })] }),
        'W/"9"',
      ),
    )
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(versionBody(), tag),
    )

    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items`, (call) => {
      const body = call.body as {
        code: string
        description: string
        kind: string
        baseRate: string
        unit: string
        taxCode: string
        active: boolean
      }
      const created = aPriceListItem({ ...body, priceListItemId: NEW_ITEM_ID })
      items = [...items, created]
      tag = 'W/"2"'
      return versionedJson(created, tag, 201)
    })

    transport.route(`PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items/${NEW_ITEM_ID}`, (call) => {
      const body = call.body as { description: string }
      const edited = { ...items[0], description: body.description } as PriceListItem
      items = [edited]
      tag = 'W/"3"'
      return versionedJson(edited, tag)
    })

    transport.route(
      `POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items/${NEW_ITEM_ID}/delete`,
      () => {
        items = []
        tag = 'W/"4"'
        return versionedJson(null, tag, 204)
      },
    )

    transport.route(`PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, (call) => {
      const body = call.body as { name: string }
      version = { ...version, name: body.name }
      tag = 'W/"5"'
      return versionedJson(versionBody(), tag)
    })

    renderEditorAt(DRAFT_ID)
    expect(await screen.findByText('Price-list version 1')).toBeInTheDocument()

    // Adds an item, typing the tax code the published configuration offers.
    await user.click(screen.getByRole('button', { name: 'Add an item' }))
    const addForm = within(await screen.findByRole('form', { name: 'Add an item' }))
    await user.type(addForm.getByLabelText('Code'), 'STITCH_BLOUSE')
    await user.type(addForm.getByLabelText('Description'), 'Blouse stitching')
    await user.click(addForm.getByLabelText('A service’s base charge'))
    await user.type(addForm.getByLabelText('Base rate'), '505')
    await user.type(addForm.getByLabelText('Unit'), 'each')
    await user.type(addForm.getByLabelText('Tax code'), 'STITCHING_5')
    await user.click(addForm.getByLabelText('Active'))
    await user.click(addForm.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Saved the item STITCH_BLOUSE.')).toBeInTheDocument()
    expect(screen.getByText('STITCH_BLOUSE')).toBeInTheDocument()

    const added = transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items`)[0]
    expect(added?.body).toEqual({
      code: 'STITCH_BLOUSE',
      description: 'Blouse stitching',
      kind: 'Service',
      baseRate: '505',
      unit: 'each',
      taxCode: 'STITCHING_5',
      active: true,
      reason: null,
    })
    expect(added?.headers.get('If-Match')).toBe('W/"1"')

    // Edits it.
    await user.click(screen.getByRole('button', { name: 'Edit STITCH_BLOUSE' }))
    const editForm = within(
      await screen.findByRole('form', { name: 'Edit the item STITCH_BLOUSE' }),
    )
    await user.clear(editForm.getByLabelText('Description'))
    await user.type(editForm.getByLabelText('Description'), 'Blouse stitching, standard')
    await user.click(editForm.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Blouse stitching, standard')).toBeInTheDocument()
    const edited = transport.callsTo(
      `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items/${NEW_ITEM_ID}`,
    )[0]
    expect(edited?.headers.get('If-Match')).toBe('W/"2"')

    // Removes it.
    await user.click(screen.getByRole('button', { name: 'Remove STITCH_BLOUSE' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'Duplicated by a later item.')
    await user.click(dialog.getByRole('button', { name: 'Remove STITCH_BLOUSE' }))

    expect(await screen.findByText('Removed the item STITCH_BLOUSE.')).toBeInTheDocument()
    expect(screen.getByText('No item yet. Add the first one below.')).toBeInTheDocument()
    const removed = transport.callsTo(
      `POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items/${NEW_ITEM_ID}/delete`,
    )[0]
    expect(removed?.headers.get('If-Match')).toBe('W/"3"')

    // Changes the version's conventions.
    await user.click(screen.getByRole('button', { name: 'Change the version’s conventions' }))
    const conventionsForm = within(
      await screen.findByRole('form', { name: 'Version 1 conventions' }),
    )
    await user.clear(conventionsForm.getByLabelText('Name'))
    await user.type(conventionsForm.getByLabelText('Name'), 'Rates from 1 May 2026')
    await user.click(conventionsForm.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Saved the version’s conventions.')).toBeInTheDocument()
    expect(await screen.findByText('Rates from 1 May 2026')).toBeInTheDocument()
    const versionSaved = transport.callsTo(`PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`)[0]
    expect(versionSaved?.headers.get('If-Match')).toBe('W/"4"')
  }, 30000)

  it('sends every convention field on a whole-value write, not only the one that changed', async () => {
    const user = userEvent.setup()
    const version = aPriceListVersionSummary({
      priceListVersionId: DRAFT_ID,
      status: 'Draft',
      name: 'Rates from 1 April 2026',
      notes: 'Interim rates.',
      effectiveFrom: '2026-04-01',
      taxInclusive: false,
      roundOff: 'NearestRupee',
      overrideThresholdPercent: 10,
      branchIds: [BRANCH_ID],
    })
    const body = { version, items: [], discountRules: [] }
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(body, 'W/"1"'),
    )
    transport.route(`PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(body, 'W/"2"'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(
      await screen.findByRole('button', { name: 'Change the version’s conventions' }),
    )
    const form = within(await screen.findByRole('form', { name: 'Version 1 conventions' }))
    await user.click(form.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(transport.callsTo(`PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`)).toHaveLength(1)
    })
    const sent = transport.callsTo(`PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`)[0]
    expect(sent?.body).toEqual({
      name: 'Rates from 1 April 2026',
      notes: 'Interim rates.',
      effectiveFrom: '2026-04-01',
      taxInclusive: false,
      roundOff: 'NearestRupee',
      overrideThresholdPercent: '10',
      branchIds: [BRANCH_ID],
      cloneFromVersionId: null,
      reason: null,
      saysTaxInclusive: true,
      saysBranchIds: true,
    })
  })

  it('says so when no tax configuration is published', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' }),
      items: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add an item' }))
    const form = within(await screen.findByRole('form', { name: 'Add an item' }))

    expect(
      form.getByText(
        'No tax configuration is published yet, so no code can be suggested. The code is checked when this version is published, not now — the item still saves with a typed code.',
      ),
    ).toBeInTheDocument()
    expect(form.getByRole('link', { name: 'Open the tax configuration' })).toHaveAttribute(
      'href',
      '/admin/tax-configuration',
    )
  })

  it('accepts a typed tax code with no published configuration, and the item saves', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' }),
      items: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items`, () =>
      versionedJson(aPriceListItem({ taxCode: 'GST5' }), 'W/"2"', 201),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add an item' }))
    const form = within(await screen.findByRole('form', { name: 'Add an item' }))
    await user.type(form.getByLabelText('Code'), 'STITCH_BLOUSE')
    await user.type(form.getByLabelText('Description'), 'Blouse stitching')
    await user.click(form.getByLabelText('A service’s base charge'))
    await user.type(form.getByLabelText('Base rate'), '505')
    await user.type(form.getByLabelText('Unit'), 'each')
    await user.type(form.getByLabelText('Tax code'), 'GST5')
    await user.click(form.getByLabelText('Active'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Saved the item STITCH_BLOUSE.')).toBeInTheDocument()
    const sent = transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items`)[0]
    expect((sent?.body as { taxCode: string }).taxCode).toBe('GST5')
  })

  it('shows a malformed unit as a field error', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' }),
      items: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items`, () =>
      problemResponse(400, 'billing.unit-not-well-formed', {
        errors: { unit: ['A unit is a short lower-case word such as each, metre or hour.'] },
      }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add an item' }))
    const form = within(await screen.findByRole('form', { name: 'Add an item' }))
    await user.type(form.getByLabelText('Code'), 'STITCH_BLOUSE')
    await user.type(form.getByLabelText('Description'), 'Blouse stitching')
    await user.click(form.getByLabelText('A service’s base charge'))
    await user.type(form.getByLabelText('Base rate'), '505')
    await user.type(form.getByLabelText('Unit'), 'EACH!!')
    await user.type(form.getByLabelText('Tax code'), 'STITCHING_5')
    await user.click(form.getByLabelText('Active'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText('A unit is a short lower-case word such as each, metre or hour.'),
    ).toBeInTheDocument()
    expect(form.getByLabelText('Unit')).toHaveAttribute('aria-invalid', 'true')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  /**
   * The server's own check for a malformed base rate answers `billing.rate-not-well-formed` on the
   * `baseRate` field (`PriceListItemDetails.Validate`), not `billing.amount-not-well-formed` — the
   * latter is still mapped in `billingProblems.ts` per the issue's own scope, but no route this issue
   * calls actually answers it for a base rate.
   */
  it('shows a malformed rate as a field error', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' }),
      items: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items`, () =>
      problemResponse(400, 'billing.rate-not-well-formed', {
        errors: { baseRate: ['A rate is a non-negative amount with at most four decimal places.'] },
      }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add an item' }))
    const form = within(await screen.findByRole('form', { name: 'Add an item' }))
    await user.type(form.getByLabelText('Code'), 'STITCH_BLOUSE')
    await user.type(form.getByLabelText('Description'), 'Blouse stitching')
    await user.click(form.getByLabelText('A service’s base charge'))
    await user.type(form.getByLabelText('Base rate'), 'abc')
    await user.type(form.getByLabelText('Unit'), 'each')
    await user.type(form.getByLabelText('Tax code'), 'STITCHING_5')
    await user.click(form.getByLabelText('Active'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText('A rate is a non-negative amount with at most four decimal places.'),
    ).toBeInTheDocument()
    expect(form.getByLabelText('Base rate')).toHaveAttribute('aria-invalid', 'true')
  })

  it('refuses a duplicate item code in words, keeping typed values and the retry key', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' }),
      items: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items`, () =>
      problemResponse(400, 'billing.code-not-unique', { errors: { code: ['Already used.'] } }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add an item' }))
    const form = within(await screen.findByRole('form', { name: 'Add an item' }))
    await user.type(form.getByLabelText('Code'), 'STITCH_BLOUSE')
    await user.type(form.getByLabelText('Description'), 'Blouse stitching')
    await user.click(form.getByLabelText('A service’s base charge'))
    await user.type(form.getByLabelText('Base rate'), '505')
    await user.type(form.getByLabelText('Unit'), 'each')
    await user.type(form.getByLabelText('Tax code'), 'STITCHING_5')
    await user.click(form.getByLabelText('Active'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText('That code is already used. Choose a different one.'),
    ).toBeInTheDocument()
    expect(form.getByLabelText('Code')).toHaveValue('STITCH_BLOUSE')

    await user.click(form.getByRole('button', { name: 'Save' }))
    await waitFor(() => {
      expect(transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items`)).toHaveLength(2)
    })

    const sent = transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items`)
    expect(sent[1]?.headers.get('Idempotency-Key')).toBe(sent[0]?.headers.get('Idempotency-Key'))
  })

  it('refuses an item with no active choice, sending no request the person did not author', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' }),
      items: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add an item' }))
    const form = within(await screen.findByRole('form', { name: 'Add an item' }))
    await user.type(form.getByLabelText('Code'), 'STITCH_BLOUSE')
    await user.type(form.getByLabelText('Description'), 'Blouse stitching')
    await user.click(form.getByLabelText('A service’s base charge'))
    await user.type(form.getByLabelText('Base rate'), '505')
    await user.type(form.getByLabelText('Unit'), 'each')
    await user.type(form.getByLabelText('Tax code'), 'STITCHING_5')
    // Active is deliberately left unanswered.
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('This is required.')).toBeInTheDocument()
    expect(transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items`)).toHaveLength(0)
  })

  it('offers a re-read after a stale tag, rather than overwriting silently', async () => {
    const user = userEvent.setup()
    const existing = aPriceListItem()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' }),
      items: [existing],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(
      `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items/${existing.priceListItemId}`,
      () => problemResponse(412, 'billing.version-changed'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: `Edit ${existing.code}` }))
    const form = within(await screen.findByRole('form', { name: `Edit the item ${existing.code}` }))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'Someone else changed this version while it was open here. Read it again to see what changed.',
      ),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Read it again' }))

    await waitFor(() => {
      expect(transport.callsTo(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`)).toHaveLength(2)
    })
  })

  it('carries the tag from a 204 removal into the next write', async () => {
    const user = userEvent.setup()
    const first = aPriceListItem({ priceListItemId: PRICE_LIST_ITEM_ID, code: 'STITCH_BLOUSE' })
    const second = aPriceListItem({ priceListItemId: SECOND_ITEM_ID, code: 'ALTER_SLEEVE' })
    let items: readonly PriceListItem[] = [first, second]
    const version = aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' })

    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson({ version, items, discountRules: [] }, 'W/"1"'),
    )
    transport.route(
      `POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items/${first.priceListItemId}/delete`,
      () => {
        items = [second]
        return versionedJson(null, 'W/"2"', 204)
      },
    )
    transport.route(
      `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items/${second.priceListItemId}`,
      () => versionedJson({ ...second, active: false }, 'W/"3"'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: `Remove ${first.code}` }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'No longer offered.')
    await user.click(dialog.getByRole('button', { name: `Remove ${first.code}` }))

    await waitFor(() => {
      expect(
        transport.callsTo(
          `POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items/${first.priceListItemId}/delete`,
        ),
      ).toHaveLength(1)
    })

    await user.click(await screen.findByRole('button', { name: `Edit ${second.code}` }))
    const form = within(await screen.findByRole('form', { name: `Edit the item ${second.code}` }))
    await user.click(form.getByLabelText('Inactive'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(
        transport.callsTo(
          `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items/${second.priceListItemId}`,
        ),
      ).toHaveLength(1)
    })
    const sent = transport.callsTo(
      `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items/${second.priceListItemId}`,
    )[0]
    expect(sent?.headers.get('If-Match')).toBe('W/"2"')
  })

  it('offers no item or conventions control on a published version, and points at the clone instead', async () => {
    const published = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: PRICE_LIST_VERSION_ID,
        status: 'Published',
      }),
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${PRICE_LIST_VERSION_ID}`, () =>
      versionedJson(published, 'W/"1"'),
    )

    renderEditorAt(PRICE_LIST_VERSION_ID)

    expect(
      await screen.findByText(
        'This version is published. Every invoice since was calculated on it — clone it to a new draft to change what it says.',
      ),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Change the version’s conventions' }),
    ).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add an item' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Edit /i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Remove /i })).not.toBeInTheDocument()
  })

  it('still shows discount rules on a published version, read-only', async () => {
    const published = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: PRICE_LIST_VERSION_ID,
        status: 'Published',
      }),
      discountRules: [aDiscountRule()],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${PRICE_LIST_VERSION_ID}`, () =>
      versionedJson(published, 'W/"1"'),
    )

    renderEditorAt(PRICE_LIST_VERSION_ID)

    expect(await screen.findByText('FESTIVE10')).toBeInTheDocument()
    expect(
      screen.getByText('Adding, editing and removing a discount rule are not in this screen yet.'),
    ).toBeInTheDocument()
  })

  it('shows a sentence without the manage key, and makes no request', async () => {
    renderEditorAt(PRICE_LIST_VERSION_ID, [])

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(
      transport.callsTo(`GET ${PRICE_LIST_VERSION_BASE}/${PRICE_LIST_VERSION_ID}`),
    ).toHaveLength(0)
  })

  it("handles the server's own refusal when the claim list is stale", async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' }),
      items: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items`, () =>
      problemResponse(403, 'security.forbidden'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add an item' }))
    const form = within(await screen.findByRole('form', { name: 'Add an item' }))
    await user.type(form.getByLabelText('Code'), 'STITCH_BLOUSE')
    await user.type(form.getByLabelText('Description'), 'Blouse stitching')
    await user.click(form.getByLabelText('A service’s base charge'))
    await user.type(form.getByLabelText('Base rate'), '505')
    await user.type(form.getByLabelText('Unit'), 'each')
    await user.type(form.getByLabelText('Tax code'), 'STITCHING_5')
    await user.click(form.getByLabelText('Active'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText('This did not go through, and the reason is not clear.'),
    ).toBeInTheDocument()
  })

  it('blocks every write control while offline, without queuing anything', async () => {
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' }),
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )

    renderEditorAt(DRAFT_ID)
    await screen.findByText(draft.items[0]?.code ?? '')

    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findAllByText('Needs connection — this will not be queued'),
    ).not.toHaveLength(0)
    expect(screen.queryByRole('button', { name: 'Add an item' })).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Change the version’s conventions' }),
    ).not.toBeInTheDocument()

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })

  it('pre-fills no rate and no unit when adding an item', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' }),
      items: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )

    const { container } = renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add an item' }))
    const form = within(await screen.findByRole('form', { name: 'Add an item' }))

    expect(form.getByLabelText('Base rate')).toHaveValue('')
    expect(form.getByLabelText('Unit')).toHaveValue('')
    expect(form.getByLabelText('A service’s base charge')).not.toBeChecked()
    expect(form.getByLabelText('A surcharge')).not.toBeChecked()
    expect(form.getByLabelText('A material')).not.toBeChecked()
    expect(form.getByLabelText('Active')).not.toBeChecked()
    expect(form.getByLabelText('Inactive')).not.toBeChecked()

    await expectNoAccessibilityViolations(container)
  })

  it('has no accessibility violations on a working editor', async () => {
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({ priceListVersionId: DRAFT_ID, status: 'Draft' }),
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )

    const { container } = renderEditorAt(DRAFT_ID)
    await screen.findByText('Price-list version 1')

    await expectNoAccessibilityViolations(container)
  })
})
