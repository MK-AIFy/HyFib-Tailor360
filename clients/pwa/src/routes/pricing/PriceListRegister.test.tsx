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
import { BRANCH_ID, versionedResponse } from '../../billing/testing/fixtures'
import {
  PRICE_LIST_ID,
  PRICE_LIST_VERSION_ID,
  aPriceList,
  aPriceListVersionSummary,
} from '../../billing/testing/priceListFixtures'
import { PriceListsRoute } from './PriceListsRoute'
import { PriceListVersionsRoute } from './PriceListVersionsRoute'

let transport: FetchStub

const PRICE_LISTS = '/api/v1/billing/price-lists'
const BRANCHES = '/api/v1/admin/branches/'
const PRICE_LIST_VERSIONS = `${PRICE_LISTS}/${PRICE_LIST_ID}/versions`

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route(`GET ${BRANCHES}`, () =>
    jsonResponse([aBranch({ branchId: BRANCH_ID, name: 'Coimbatore counter', code: 'CBE01' })]),
  )
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderPriceListsAt(
  permissions: readonly string[] = [BILLING_PERMISSIONS.managePriceLists],
) {
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions })))

  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/admin/price-lists']}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                element={
                  <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
                    <PriceListsRoute />
                  </RequirePermission>
                }
                path="/admin/price-lists"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

function renderVersionsAt(permissions: readonly string[] = [BILLING_PERMISSIONS.managePriceLists]) {
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions })))

  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[`/admin/price-lists/${PRICE_LIST_ID}`]}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                element={
                  <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
                    <PriceListVersionsRoute />
                  </RequirePermission>
                }
                path="/admin/price-lists/:priceListId"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/** A row's cell text by column id, the same lookup the tax-configuration register's test uses. */
function cellText(row: HTMLElement | undefined, column: string): string | null {
  return (
    row?.querySelector(`[data-column="${column}"] .data-table__cell-value`)?.textContent ?? null
  )
}

describe('the price-list register', () => {
  it('creates a price list, sending the code, the name and an Idempotency-Key', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${PRICE_LISTS}`, () => jsonResponse([]))
    transport.route(`POST ${PRICE_LISTS}`, () => jsonResponse(aPriceList(), 201))

    const { container } = renderPriceListsAt()
    await screen.findByText('No price list yet')
    await user.click(screen.getByRole('button', { name: 'Create a price list' }))

    const form = within(await screen.findByRole('form', { name: 'Create a price list' }))
    await expectNoAccessibilityViolations(container)

    await user.type(form.getByLabelText('Code'), 'pl_cbe01')
    await user.type(form.getByLabelText('Name'), 'Coimbatore price list')
    await user.click(form.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(transport.callsTo(`POST ${PRICE_LISTS}`)).toHaveLength(1)
    })
    const sent = transport.callsTo(`POST ${PRICE_LISTS}`)[0]
    expect(sent?.body).toEqual({ code: 'PL_CBE01', name: 'Coimbatore price list', reason: null })
    expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
    expect(await screen.findByText('Created the price list.')).toBeInTheDocument()
  })

  it('renames a price list against a freshly read tag, sending only the name', async () => {
    const user = userEvent.setup()
    const existing = aPriceList()
    transport.route(`GET ${PRICE_LISTS}`, () => jsonResponse([existing]))
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(existing, 'W/"1"'),
    )
    transport.route(`PUT ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      jsonResponse({ ...existing, name: 'Coimbatore and Tiruppur price list' }),
    )

    renderPriceListsAt()
    await screen.findByText('PL_CBE01')
    await user.click(screen.getByRole('button', { name: 'Rename the price list PL_CBE01' }))

    await waitFor(() => {
      expect(transport.callsTo(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`)).toHaveLength(1)
    })

    const form = within(await screen.findByRole('form', { name: 'Rename PL_CBE01' }))
    expect(form.queryByLabelText('Code')).not.toBeInTheDocument()
    await user.clear(form.getByLabelText('Name'))
    await user.type(form.getByLabelText('Name'), 'Coimbatore and Tiruppur price list')
    await user.click(form.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(transport.callsTo(`PUT ${PRICE_LISTS}/${PRICE_LIST_ID}`)).toHaveLength(1)
    })
    const sent = transport.callsTo(`PUT ${PRICE_LISTS}/${PRICE_LIST_ID}`)[0]
    expect(sent?.body).toEqual({ name: 'Coimbatore and Tiruppur price list', reason: null })
    expect(sent?.headers.get('If-Match')).toBe('W/"1"')
    expect(await screen.findByText('Renamed the price list.')).toBeInTheDocument()
  })

  it('RefusesADuplicateListCodeInWords', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${PRICE_LISTS}`, () => jsonResponse([]))
    transport.route(`POST ${PRICE_LISTS}`, () => problemResponse(409, 'billing.code-not-unique'))

    renderPriceListsAt()
    await screen.findByText('No price list yet')
    await user.click(screen.getByRole('button', { name: 'Create a price list' }))
    const form = within(await screen.findByRole('form', { name: 'Create a price list' }))
    await user.type(form.getByLabelText('Code'), 'PL_CBE01')
    await user.type(form.getByLabelText('Name'), 'Coimbatore price list')
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText('That code is already used. Choose a different one.'),
    ).toBeInTheDocument()
    expect(screen.queryByText('billing.code-not-unique')).not.toBeInTheDocument()

    // The typed values survive the refusal.
    expect(form.getByLabelText('Code')).toHaveValue('PL_CBE01')
    expect(form.getByLabelText('Name')).toHaveValue('Coimbatore price list')
  })

  it('ReusesTheSameIdempotencyKeyOnRetry', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${PRICE_LISTS}`, () => jsonResponse([]))
    transport.route(`POST ${PRICE_LISTS}`, () => problemResponse(409, 'billing.code-not-unique'))

    renderPriceListsAt()
    await screen.findByText('No price list yet')
    await user.click(screen.getByRole('button', { name: 'Create a price list' }))
    const form = within(await screen.findByRole('form', { name: 'Create a price list' }))
    await user.type(form.getByLabelText('Code'), 'PL_CBE01')
    await user.type(form.getByLabelText('Name'), 'Coimbatore price list')
    await user.click(form.getByRole('button', { name: 'Save' }))
    await screen.findByText(/already used/)

    await user.click(form.getByRole('button', { name: 'Save' }))
    await waitFor(() => {
      expect(transport.callsTo(`POST ${PRICE_LISTS}`)).toHaveLength(2)
    })

    const sent = transport.callsTo(`POST ${PRICE_LISTS}`)
    expect(sent[1]?.headers.get('Idempotency-Key')).toBe(sent[0]?.headers.get('Idempotency-Key'))
  })

  it('OffersARereadAfterAStalePriceListTag', async () => {
    const user = userEvent.setup()
    const existing = aPriceList()
    transport.route(`GET ${PRICE_LISTS}`, () => jsonResponse([existing]))
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(existing, 'W/"1"'),
    )
    transport.route(`PUT ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      problemResponse(412, 'billing.price-list-changed'),
    )

    renderPriceListsAt()
    await screen.findByText('PL_CBE01')
    await user.click(screen.getByRole('button', { name: 'Rename the price list PL_CBE01' }))
    await waitFor(() => {
      expect(transport.callsTo(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`)).toHaveLength(1)
    })

    const form = within(await screen.findByRole('form', { name: 'Rename PL_CBE01' }))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'Someone else changed this price list while it was open here. Read it again to see what changed.',
      ),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Read it again' }))

    await waitFor(() => {
      expect(transport.callsTo(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`)).toHaveLength(2)
    })
    expect(
      screen.queryByText(
        'Someone else changed this price list while it was open here. Read it again to see what changed.',
      ),
    ).not.toBeInTheDocument()
  })

  it('ShowsASentenceWithoutTheManageKey', async () => {
    renderPriceListsAt([])

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(transport.callsTo(`GET ${PRICE_LISTS}`)).toHaveLength(0)
  })

  it('HandlesTheServersRefusalWhenTheClaimListIsStale', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${PRICE_LISTS}`, () => jsonResponse([]))
    transport.route(`POST ${PRICE_LISTS}`, () => problemResponse(403, 'security.forbidden'))

    // The client's own claim list says this caller may act — `renderPriceListsAt` grants the key —
    // but the server's own copy is what actually decides, and it refuses the write anyway.
    renderPriceListsAt()
    await screen.findByText('No price list yet')
    await user.click(screen.getByRole('button', { name: 'Create a price list' }))
    const form = within(await screen.findByRole('form', { name: 'Create a price list' }))
    await user.type(form.getByLabelText('Code'), 'PL_CBE01')
    await user.type(form.getByLabelText('Name'), 'Coimbatore price list')
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(form.getByLabelText('Code')).toHaveValue('PL_CBE01')
  })

  it('BlocksEveryWriteControlWhileOffline', async () => {
    transport.route(`GET ${PRICE_LISTS}`, () => jsonResponse([aPriceList()]))

    renderPriceListsAt()
    await screen.findByText('PL_CBE01')

    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findByText('Needs connection — this will not be queued'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Create a price list' })).not.toBeInTheDocument()

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })
})

describe("a price list's versions", () => {
  it('lists versions newest first with their conventions read back', async () => {
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(aPriceList(), 'W/"1"'),
    )
    transport.route(`GET ${PRICE_LIST_VERSIONS}`, () =>
      jsonResponse([
        aPriceListVersionSummary({
          priceListVersionId: 'other-version-id',
          versionNumber: 2,
          name: 'Second version',
          status: 'Draft',
          taxInclusive: true,
          roundOff: 'None',
          overrideThresholdPercent: 5,
          branchIds: [],
        }),
        aPriceListVersionSummary(),
      ]),
    )

    const { container } = renderVersionsAt()

    await screen.findByText('Second version')
    expect(screen.getByText('Rates from 1 April 2026')).toBeInTheDocument()

    const rows = screen.getAllByRole('row')
    const draftRow = rows.find((row) => row.textContent?.includes('Second version'))
    const publishedRow = rows.find((row) => row.textContent?.includes('Rates from 1 April 2026'))

    expect(cellText(draftRow, 'status')).toBe('Draft')
    expect(cellText(publishedRow, 'status')).toBe('Published')
    expect(cellText(draftRow, 'tax')).toBe('Inclusive')
    expect(cellText(publishedRow, 'tax')).toBe('Exclusive')
    expect(cellText(draftRow, 'roundOff')).toBe('No rounding')
    expect(cellText(publishedRow, 'roundOff')).toBe('Nearest rupee')
    expect(cellText(draftRow, 'threshold')).toBe('5%')
    expect(cellText(publishedRow, 'threshold')).toBe('10%')
    expect(cellText(draftRow, 'branches')).toBe('—')
    expect(cellText(publishedRow, 'branches')).toBe('Coimbatore counter')
    expect(cellText(publishedRow, 'clonedFrom')).toBe('—')

    await expectNoAccessibilityViolations(container)
  })

  it('shows the empty state with the start-a-draft control', async () => {
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(aPriceList(), 'W/"1"'),
    )
    transport.route(`GET ${PRICE_LIST_VERSIONS}`, () => jsonResponse([]))

    renderVersionsAt()

    expect(await screen.findByText('No version yet')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Start a draft' })).toBeInTheDocument()
  })

  it('starts an empty draft with every convention, sending an Idempotency-Key', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(aPriceList(), 'W/"1"'),
    )
    transport.route(`GET ${PRICE_LIST_VERSIONS}`, () => jsonResponse([]))
    transport.route(`POST ${PRICE_LIST_VERSIONS}`, () =>
      versionedResponse(
        { version: aPriceListVersionSummary(), items: [], discountRules: [] },
        'W/"1"',
        201,
      ),
    )

    const { container } = renderVersionsAt()
    await screen.findByText('No version yet')
    await user.click(screen.getByRole('button', { name: 'Start a draft' }))

    const form = within(await screen.findByRole('form', { name: 'Start a draft' }))
    await expectNoAccessibilityViolations(container)

    await user.type(form.getByLabelText('Name'), 'Rates from 1 April 2026')
    await user.type(form.getByLabelText('First day'), '2026-04-01')
    await user.click(form.getByRole('radio', { name: 'Tax exclusive' }))
    await user.click(form.getByRole('radio', { name: 'Nearest rupee' }))
    await user.type(form.getByLabelText('Override threshold'), '10')
    await user.click(form.getByLabelText('Coimbatore counter (CBE01)'))
    await user.click(form.getByRole('button', { name: 'Start the draft' }))

    await waitFor(() => {
      expect(transport.callsTo(`POST ${PRICE_LIST_VERSIONS}`)).toHaveLength(1)
    })
    const sent = transport.callsTo(`POST ${PRICE_LIST_VERSIONS}`)[0]
    expect(sent?.body).toEqual({
      name: 'Rates from 1 April 2026',
      notes: null,
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
    expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
    expect(await screen.findByText('Started the draft.')).toBeInTheDocument()
  })

  it('starts a cloned draft that reports the version it was cloned from', async () => {
    const user = userEvent.setup()
    const source = aPriceListVersionSummary({ versionNumber: 1 })
    let versionReads = 0

    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(aPriceList(), 'W/"1"'),
    )
    transport.route(`GET ${PRICE_LIST_VERSIONS}`, () => {
      versionReads += 1
      return versionReads === 1
        ? jsonResponse([source])
        : jsonResponse([
            aPriceListVersionSummary({
              priceListVersionId: 'cloned-version-id',
              versionNumber: 2,
              clonedFromVersionId: PRICE_LIST_VERSION_ID,
            }),
            source,
          ])
    })
    transport.route(`POST ${PRICE_LIST_VERSIONS}`, () =>
      versionedResponse(
        {
          version: aPriceListVersionSummary({
            priceListVersionId: 'cloned-version-id',
            versionNumber: 2,
            clonedFromVersionId: PRICE_LIST_VERSION_ID,
          }),
          items: [],
          discountRules: [],
        },
        'W/"1"',
        201,
      ),
    )

    renderVersionsAt()
    await screen.findByText('Rates from 1 April 2026')
    await user.click(screen.getByRole('button', { name: 'Clone version 1 into a new draft' }))

    const form = within(
      await screen.findByRole('form', { name: 'Clone version 1 into a new draft' }),
    )
    expect(form.getByLabelText('Name')).toHaveValue('Rates from 1 April 2026')
    await user.click(form.getByRole('button', { name: 'Start the draft' }))

    await waitFor(() => {
      expect(transport.callsTo(`POST ${PRICE_LIST_VERSIONS}`)).toHaveLength(1)
    })
    const sent = transport.callsTo(`POST ${PRICE_LIST_VERSIONS}`)[0]
    expect((sent?.body as { cloneFromVersionId?: string })?.cloneFromVersionId).toBe(
      PRICE_LIST_VERSION_ID,
    )

    await screen.findByText('Started the draft.')
    const rows = screen.getAllByRole('row')
    const clonedRow = rows.find((row) => cellText(row, 'version') === '2')
    expect(cellText(clonedRow, 'clonedFrom')).toBe('Version 1')
  })

  it('RefusesADraftWithNoInclusiveChoice', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(aPriceList(), 'W/"1"'),
    )
    transport.route(`GET ${PRICE_LIST_VERSIONS}`, () => jsonResponse([]))
    transport.route(`POST ${PRICE_LIST_VERSIONS}`, () =>
      problemResponse(400, 'billing.value-required', { errors: { taxInclusive: ['Choose one.'] } }),
    )

    renderVersionsAt()
    await screen.findByText('No version yet')
    await user.click(screen.getByRole('button', { name: 'Start a draft' }))
    const form = within(await screen.findByRole('form', { name: 'Start a draft' }))
    await user.type(form.getByLabelText('Name'), 'Rates from 1 April 2026')
    await user.type(form.getByLabelText('First day'), '2026-04-01')
    await user.click(form.getByRole('radio', { name: 'Nearest rupee' }))
    await user.type(form.getByLabelText('Override threshold'), '10')
    await user.click(form.getByLabelText('Coimbatore counter (CBE01)'))
    await user.click(form.getByRole('button', { name: 'Start the draft' }))

    expect(
      await screen.findByText('This is required.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('RefusesADraftWithNoRoundOffRule', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(aPriceList(), 'W/"1"'),
    )
    transport.route(`GET ${PRICE_LIST_VERSIONS}`, () => jsonResponse([]))
    transport.route(`POST ${PRICE_LIST_VERSIONS}`, () =>
      problemResponse(400, 'billing.value-required', { errors: { roundOff: ['Choose one.'] } }),
    )

    renderVersionsAt()
    await screen.findByText('No version yet')
    await user.click(screen.getByRole('button', { name: 'Start a draft' }))
    const form = within(await screen.findByRole('form', { name: 'Start a draft' }))
    await user.type(form.getByLabelText('Name'), 'Rates from 1 April 2026')
    await user.type(form.getByLabelText('First day'), '2026-04-01')
    await user.click(form.getByRole('radio', { name: 'Tax exclusive' }))
    await user.type(form.getByLabelText('Override threshold'), '10')
    await user.click(form.getByLabelText('Coimbatore counter (CBE01)'))
    await user.click(form.getByRole('button', { name: 'Start the draft' }))

    expect(
      await screen.findByText('This is required.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('RefusesADraftWithNoBranch', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(aPriceList(), 'W/"1"'),
    )
    transport.route(`GET ${PRICE_LIST_VERSIONS}`, () => jsonResponse([]))
    transport.route(`POST ${PRICE_LIST_VERSIONS}`, () =>
      problemResponse(400, 'billing.value-required', {
        errors: { branchIds: ['Choose at least one branch.'] },
      }),
    )

    renderVersionsAt()
    await screen.findByText('No version yet')
    await user.click(screen.getByRole('button', { name: 'Start a draft' }))
    const form = within(await screen.findByRole('form', { name: 'Start a draft' }))
    await user.type(form.getByLabelText('Name'), 'Rates from 1 April 2026')
    await user.type(form.getByLabelText('First day'), '2026-04-01')
    await user.click(form.getByRole('radio', { name: 'Tax exclusive' }))
    await user.click(form.getByRole('radio', { name: 'Nearest rupee' }))
    await user.type(form.getByLabelText('Override threshold'), '10')
    await user.click(form.getByRole('button', { name: 'Start the draft' }))

    expect(
      await screen.findByText('This is required.'),
    ).toBeInTheDocument()

    const sent = transport.callsTo(`POST ${PRICE_LIST_VERSIONS}`)[0]
    const body = sent?.body as { branchIds: unknown; saysBranchIds: unknown }
    expect(body.branchIds).toBeNull()
    expect(body.saysBranchIds).toBe(false)
  })

  it('SendsNoValueThePersonDidNotType', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(aPriceList(), 'W/"1"'),
    )
    transport.route(`GET ${PRICE_LIST_VERSIONS}`, () => jsonResponse([]))
    transport.route(`POST ${PRICE_LIST_VERSIONS}`, () =>
      problemResponse(400, 'billing.value-required', { errors: { taxInclusive: ['Choose one.'] } }),
    )

    renderVersionsAt()
    await screen.findByText('No version yet')
    await user.click(screen.getByRole('button', { name: 'Start a draft' }))
    const form = within(await screen.findByRole('form', { name: 'Start a draft' }))
    await user.type(form.getByLabelText('Name'), 'Rates from 1 April 2026')
    await user.click(form.getByRole('button', { name: 'Start the draft' }))

    await waitFor(() => {
      expect(transport.callsTo(`POST ${PRICE_LIST_VERSIONS}`)).toHaveLength(1)
    })
    const sent = transport.callsTo(`POST ${PRICE_LIST_VERSIONS}`)[0]
    expect(sent?.body).toEqual({
      name: 'Rates from 1 April 2026',
      notes: null,
      effectiveFrom: null,
      taxInclusive: null,
      roundOff: null,
      overrideThresholdPercent: null,
      branchIds: null,
      cloneFromVersionId: null,
      reason: null,
      saysTaxInclusive: false,
      saysBranchIds: false,
    })
  })

  it('RefusesAnotherOrganisationsBranchInWords', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(aPriceList(), 'W/"1"'),
    )
    transport.route(`GET ${PRICE_LIST_VERSIONS}`, () => jsonResponse([]))
    transport.route(`POST ${PRICE_LIST_VERSIONS}`, () =>
      problemResponse(422, 'billing.branch-not-known'),
    )

    renderVersionsAt()
    await screen.findByText('No version yet')
    await user.click(screen.getByRole('button', { name: 'Start a draft' }))
    const form = within(await screen.findByRole('form', { name: 'Start a draft' }))
    await user.type(form.getByLabelText('Name'), 'Rates from 1 April 2026')
    await user.type(form.getByLabelText('First day'), '2026-04-01')
    await user.click(form.getByRole('radio', { name: 'Tax exclusive' }))
    await user.click(form.getByRole('radio', { name: 'Nearest rupee' }))
    await user.type(form.getByLabelText('Override threshold'), '10')
    await user.click(form.getByLabelText('Coimbatore counter (CBE01)'))
    await user.click(form.getByRole('button', { name: 'Start the draft' }))

    // The organisation's own branch picker never offers a branch it does not own — this is the
    // server settling an attempt the picker itself could not have produced honestly.
    expect(
      await screen.findByText('This branch is not set up to draw invoice numbers yet.'),
    ).toBeInTheDocument()
  })

  it('OffersNoConventionControlOnAPublishedVersion', async () => {
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(aPriceList(), 'W/"1"'),
    )
    transport.route(`GET ${PRICE_LIST_VERSIONS}`, () =>
      jsonResponse([aPriceListVersionSummary({ status: 'Published' })]),
    )

    renderVersionsAt()
    await screen.findByText('Rates from 1 April 2026')

    expect(screen.queryByRole('button', { name: /edit/i })).not.toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Clone version 1 into a new draft' }),
    ).toBeInTheDocument()
  })

  it('ShowsBranchIdentifiersWithoutTheBranchKey', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${BRANCHES}`, () => problemResponse(403, 'security.forbidden'))
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(aPriceList(), 'W/"1"'),
    )
    transport.route(`GET ${PRICE_LIST_VERSIONS}`, () => jsonResponse([aPriceListVersionSummary()]))

    renderVersionsAt()

    expect(await screen.findByText(BRANCH_ID)).toBeInTheDocument()
    expect(screen.queryByText('Coimbatore counter')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Start a draft' }))
    expect(
      await screen.findByText(
        'choosing which branches this version prices is not part of what your role can do.',
      ),
    ).toBeInTheDocument()
  })

  it('refuses a caller holding no billing permission before any request is made', async () => {
    renderVersionsAt([])

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(transport.callsTo(`GET ${PRICE_LIST_VERSIONS}`)).toHaveLength(0)
  })

  it('BlocksEveryWriteControlWhileOffline', async () => {
    transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}`, () =>
      versionedResponse(aPriceList(), 'W/"1"'),
    )
    transport.route(`GET ${PRICE_LIST_VERSIONS}`, () => jsonResponse([aPriceListVersionSummary()]))

    renderVersionsAt()
    await screen.findByText('Rates from 1 April 2026')

    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findByText('Needs connection — this will not be queued'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start a draft' })).not.toBeInTheDocument()

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })
})
