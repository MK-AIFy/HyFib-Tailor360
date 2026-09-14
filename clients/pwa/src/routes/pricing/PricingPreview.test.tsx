import { render, screen, within } from '@testing-library/react'
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
import type { FetchStub, RecordedCall } from '../../auth/testing/fixtures'
import { RequirePermission } from '../../admin/RequirePermission'
import { aBranch, versionedResponse } from '../../admin/testing/fixtures'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import { BRANCH_ID } from '../../billing/testing/fixtures'
import {
  PRICE_LIST_ID,
  PRICE_LIST_VERSION_ID,
  aDiscountRule,
  aPriceList,
  aPriceListVersion,
  aPriceListVersionSummary,
} from '../../billing/testing/priceListFixtures'
import { aTaxConfigurationSummary } from '../../billing/testing/pricingConfigFixtures'
import { aPricingResult } from '../../billing/testing/pricingPreviewFixtures'
import { PricingPreviewRoute } from './PricingPreviewRoute'

let transport: FetchStub

const PRICE_LISTS = '/api/v1/billing/price-lists'
const PRICE_LIST_VERSION_BASE = `${PRICE_LISTS}/versions`
const BRANCHES = '/api/v1/admin/branches/'
const TAX_VERSIONS = '/api/v1/billing/tax-configuration/versions'
const PREVIEW = '/api/v1/billing/pricing/preview'

/** The line key the client minted for the request it just sent, read from the recorded body. */
function sentLineKey(call: RecordedCall, index = 0): string {
  const body = call.body as { readonly lines?: readonly { readonly lineKey?: string }[] }
  return body.lines?.[index]?.lineKey ?? ''
}

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [BILLING_PERMISSIONS.managePriceLists] })),
  )
  transport.route(`GET ${BRANCHES}`, () =>
    jsonResponse([aBranch({ branchId: BRANCH_ID, name: 'Coimbatore counter', code: 'CBE01' })]),
  )
  transport.route(`GET ${TAX_VERSIONS}`, () => jsonResponse([aTaxConfigurationSummary()]))
  transport.route(`GET ${PRICE_LISTS}`, () => jsonResponse([aPriceList()]))
  transport.route(`GET ${PRICE_LISTS}/${PRICE_LIST_ID}/versions`, () =>
    jsonResponse([aPriceListVersionSummary()]),
  )
  transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${PRICE_LIST_VERSION_ID}`, () =>
    versionedResponse(aPriceListVersion(), 'W/"1"'),
  )
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderPreview(path = `/admin/pricing/preview?versionId=${PRICE_LIST_VERSION_ID}`) {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                element={
                  <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
                    <PricingPreviewRoute />
                  </RequirePermission>
                }
                path="/admin/pricing/preview"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/** Adds a line naming the version's own first item, through the visible form. */
async function addLine(user: ReturnType<typeof userEvent.setup>, itemCode = 'STITCH_BLOUSE') {
  await user.click(await screen.findByRole('button', { name: 'Add a line' }))
  const form = within(await screen.findByRole('form', { name: 'Add a line' }))
  await user.selectOptions(form.getByLabelText('Item'), itemCode)
  await user.click(form.getByRole('button', { name: 'Save this line' }))
}

describe('the pricing preview', () => {
  it('prices a line and shows exactly the figures the server sent, round-off on its own line', async () => {
    const user = userEvent.setup()
    const result = aPricingResult()

    transport.route(`POST ${PREVIEW}`, () => jsonResponse(result))

    renderPreview()
    await addLine(user)
    await user.click(screen.getByRole('button', { name: 'Run the preview' }))

    await screen.findByRole('heading', { name: 'What the engine priced' })
    expect(screen.getByText('Intra-state')).toBeInTheDocument()
    expect(screen.getAllByText('₹450.00').length).toBeGreaterThan(0)
    expect(screen.getAllByText('₹11.25').length).toBeGreaterThan(0)
    // The document round-off, shown as its own row rather than folded into the grand total.
    const roundOffRow = screen.getByText('Round-off').nextElementSibling
    expect(roundOffRow).toHaveTextContent('₹0.00')
    expect(screen.getAllByText('₹472.50').length).toBeGreaterThan(0)
  })

  it('sends no Idempotency-Key, unlike every other write in this family', async () => {
    const user = userEvent.setup()
    transport.route(`POST ${PREVIEW}`, () => jsonResponse(aPricingResult()))

    renderPreview()
    await addLine(user)
    await user.click(screen.getByRole('button', { name: 'Run the preview' }))

    await screen.findByRole('heading', { name: 'What the engine priced' })
    const [call] = transport.callsTo(`POST ${PREVIEW}`)
    expect(call?.headers.get('Idempotency-Key')).toBeNull()
  })

  it('mints a distinct key for a second line', async () => {
    const user = userEvent.setup()
    transport.route(`POST ${PREVIEW}`, () => jsonResponse(aPricingResult()))

    renderPreview()
    await addLine(user)
    await addLine(user)
    await user.click(screen.getByRole('button', { name: 'Run the preview' }))

    await screen.findByRole('heading', { name: 'What the engine priced' })
    const [call] = transport.callsTo(`POST ${PREVIEW}`)
    const body = call?.body as { readonly lines: readonly { readonly lineKey: string }[] }
    expect(body.lines).toHaveLength(2)
    expect(body.lines[0]?.lineKey).not.toBe(body.lines[1]?.lineKey)
  })

  it('refuses an override beyond the threshold without the permission, naming who may authorise it', async () => {
    const user = userEvent.setup()
    transport.route(`POST ${PREVIEW}`, (call) =>
      problemResponse(403, 'billing.approval-required', {
        errors: { [`lines[${sentLineKey(call)}].override.rate`]: ['Needs authorisation.'] },
      }),
    )

    renderPreview()
    await user.click(await screen.findByRole('button', { name: 'Add a line' }))
    const form = within(await screen.findByRole('form', { name: 'Add a line' }))
    await user.selectOptions(form.getByLabelText('Item'), 'STITCH_BLOUSE')
    await user.click(form.getByLabelText(/Price this line at a different rate/))
    await user.type(form.getByLabelText(/Override rate/), '580')
    await user.click(form.getByRole('button', { name: 'Save this line' }))

    await user.click(screen.getByRole('button', { name: 'Run the preview' }))

    expect(await screen.findByText(/Owner or a Branch Manager/)).toBeInTheDocument()
    // The sentence explains in words; it never repeats the raw permission key.
    expect(screen.queryByText(/billing\.override_price/)).not.toBeInTheDocument()
  })

  it('refuses a discount above the rule’s maximum', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${PRICE_LIST_VERSION_ID}`, () =>
      versionedResponse(aPriceListVersion({ discountRules: [aDiscountRule()] }), 'W/"1"'),
    )
    transport.route(`POST ${PREVIEW}`, (call) =>
      problemResponse(422, 'billing.discount-above-maximum', {
        errors: { [`lines[${sentLineKey(call)}].discount.value`]: ['Beyond the rule’s maximum.'] },
      }),
    )

    renderPreview()
    await user.click(await screen.findByRole('button', { name: 'Add a line' }))
    const form = within(await screen.findByRole('form', { name: 'Add a line' }))
    await user.selectOptions(form.getByLabelText('Item'), 'STITCH_BLOUSE')
    await user.selectOptions(form.getByLabelText('Discount'), 'FESTIVE10')
    await user.type(form.getByLabelText(/Discount value/), '25')
    await user.click(form.getByRole('button', { name: 'Save this line' }))

    await user.click(screen.getByRole('button', { name: 'Run the preview' }))

    expect(await screen.findByText(/beyond the rule’s own maximum/)).toBeInTheDocument()
  })

  it('says what is missing and links to where it is recorded, on a missing configuration', async () => {
    const user = userEvent.setup()
    transport.route(`POST ${PREVIEW}`, () => problemResponse(422, 'billing.configuration-missing'))

    renderPreview()
    await addLine(user)
    await user.click(screen.getByRole('button', { name: 'Run the preview' }))

    await screen.findByRole('link', { name: 'Check the branch’s GST registrations' })
    expect(screen.getByRole('link', { name: 'Check this version’s branches' })).toHaveAttribute(
      'href',
      `/admin/price-lists/versions/${PRICE_LIST_VERSION_ID}`,
    )
  })

  it('fills the form from a case shape and names which one was run', async () => {
    const user = userEvent.setup()
    transport.route(`POST ${PREVIEW}`, () => jsonResponse(aPricingResult()))

    renderPreview()
    // Waits for the version's own detail to have loaded — only then is the case shape's button
    // genuinely clickable rather than merely present and `aria-disabled`.
    await screen.findByRole('button', { name: 'Add a line' })
    await user.click(screen.getByRole('button', { name: 'A quantity above one' }))

    // The case seeded a line from the version's own first item — nothing was on the form before.
    await screen.findByText('STITCH_BLOUSE')

    await user.click(screen.getByRole('button', { name: 'Run the preview' }))

    expect(await screen.findByText('Case shown: A quantity above one.')).toBeInTheDocument()
  })

  it('blocks the preview control while offline, and queues nothing', async () => {
    renderPreview()
    await screen.findByRole('heading', { name: 'Pricing preview' })

    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findByText('Needs connection — this will not be queued'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Run the preview' })).not.toBeInTheDocument()

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })

  it('has no accessibility violations on the loaded form', async () => {
    const { container } = renderPreview()
    await screen.findByRole('button', { name: 'Add a line' })

    await expectNoAccessibilityViolations(container)
  })
})
