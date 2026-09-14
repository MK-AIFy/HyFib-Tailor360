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
  PRICE_LIST_VERSION_ID,
  aPriceListPublication,
  aPriceListVersion,
  aPriceListVersionSummary,
} from '../../billing/testing/priceListFixtures'
import { aBillingFinding, findingsProblem } from '../../billing/testing/pricingConfigFixtures'
import type { PriceListVersionSummary } from '../../billing/priceListTypes'
import { PriceListVersionEditorRoute } from './PriceListVersionEditorRoute'

let transport: FetchStub

const PRICE_LIST_VERSION_BASE = '/api/v1/billing/price-lists/versions'
const BRANCHES = '/api/v1/admin/branches/'
const TAX_VERSIONS = '/api/v1/billing/tax-configuration/versions'
const DRAFT_ID = PRICE_LIST_VERSION_ID
const VALIDATE = (id: string) => `GET ${PRICE_LIST_VERSION_BASE}/${id}/validation`
const PUBLISH = (id: string) => `POST ${PRICE_LIST_VERSION_BASE}/${id}/publish`
const READ = (id: string) => `GET ${PRICE_LIST_VERSION_BASE}/${id}`

const BOTH_KEYS = [BILLING_PERMISSIONS.managePriceLists, BILLING_PERMISSIONS.publishPriceList]

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route(`GET ${BRANCHES}`, () =>
    jsonResponse([aBranch({ branchId: BRANCH_ID, name: 'Coimbatore counter', code: 'CBE01' })]),
  )
  transport.route(`GET ${TAX_VERSIONS}`, () => jsonResponse([]))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function versionedJson(body: unknown, etag: string, status = 200): Response {
  return new Response(body === null ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ETag: etag },
  })
}

function renderEditorAt(versionId: string, permissions: readonly string[] = BOTH_KEYS) {
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions })))

  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[`/admin/price-lists/versions/${versionId}`]}>
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

function aDraft(overrides: Partial<PriceListVersionSummary> = {}) {
  return aPriceListVersion({
    version: aPriceListVersionSummary({
      priceListVersionId: DRAFT_ID,
      status: 'Draft',
      publishedAt: null,
      ...overrides,
    }),
  })
}

describe('validating and publishing a price-list version', () => {
  it('validates clean, publishes with a reason, and shows the version it superseded', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    let published = false

    transport.route(READ(DRAFT_ID), () =>
      versionedJson(
        published ? aDraft({ status: 'Published' }) : draft,
        published ? 'W/"2"' : 'W/"1"',
      ),
    )
    transport.route(VALIDATE(DRAFT_ID), () =>
      jsonResponse({ versionId: DRAFT_ID, canPublish: true, findings: [] }),
    )
    transport.route(PUBLISH(DRAFT_ID), (call) => {
      published = true
      expect(call.body).toMatchObject({ reason: 'Approved by the accountant on 12 September.' })
      return versionedJson(
        aPriceListPublication({
          published: aDraft({ status: 'Published' }),
          supersededVersionId: PRICE_LIST_VERSION_ID,
        }),
        'W/"2"',
      )
    })

    renderEditorAt(DRAFT_ID)

    await user.click(await screen.findByRole('button', { name: 'Check this version' }))
    expect(
      await screen.findByText('Every check passed. There is nothing to fix or note.'),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Publish this version' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'Approved by the accountant on 12 September.')
    await user.click(dialog.getByRole('button', { name: 'Publish this version' }))

    expect(await screen.findByText('Published.')).toBeInTheDocument()
    expect(
      screen.getByText('It supersedes the version that was published before it.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('lists every publication finding against its target, not just the first', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(PUBLISH(DRAFT_ID), () =>
      findingsProblem([
        aBillingFinding({
          severity: 'Error',
          code: 'billing.tax-code-unknown',
          message:
            "'STITCH_BLOUSE' names tax code 'STITCHING_5', which the published tax configuration does not hold.",
          target: 'items[STITCH_BLOUSE].taxCode',
        }),
        aBillingFinding({
          severity: 'Error',
          code: 'billing.no-branches',
          message: 'The version prices for no branch, so nothing could ever be priced on it.',
          target: 'branchIds',
        }),
      ]),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Publish this version' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'Approved.')
    await user.click(dialog.getByRole('button', { name: 'Publish this version' }))

    expect(
      await screen.findByText(
        "'STITCH_BLOUSE' names tax code 'STITCHING_5', which the published tax configuration does not hold.",
      ),
    ).toBeInTheDocument()
    expect(
      screen.getByText('The version prices for no branch, so nothing could ever be priced on it.'),
    ).toBeInTheDocument()
    expect(screen.queryByText('billing.publish-validation-failed')).not.toBeInTheDocument()
  })

  it('shows an item whose tax code nobody published as an error, refusing the publish', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(VALIDATE(DRAFT_ID), () =>
      jsonResponse({
        versionId: DRAFT_ID,
        canPublish: false,
        findings: [
          aBillingFinding({
            severity: 'Error',
            code: 'billing.tax-code-unknown',
            message:
              "'STITCH_BLOUSE' names tax code 'STITCHING_5', which the published tax configuration does not hold.",
            target: 'items[STITCH_BLOUSE].taxCode',
          }),
        ],
      }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Check this version' }))

    expect(
      await screen.findByText(
        "'STITCH_BLOUSE' names tax code 'STITCHING_5', which the published tax configuration does not hold.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByText('Error')).toBeInTheDocument()
  })

  it('says so and links to the tax configuration when none is published at all', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(VALIDATE(DRAFT_ID), () =>
      jsonResponse({
        versionId: DRAFT_ID,
        canPublish: false,
        findings: [
          aBillingFinding({
            severity: 'Error',
            code: 'billing.tax-configuration-missing',
            message:
              "No tax configuration version is published, so no item's tax code can be resolved. Publish one first.",
            target: 'items',
          }),
        ],
      }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Check this version' }))

    expect(
      await screen.findByText(
        "No tax configuration version is published, so no item's tax code can be resolved. Publish one first.",
      ),
    ).toBeInTheDocument()
    const link = screen.getByRole('link', { name: 'Open the tax configuration' })
    expect(link).toHaveAttribute('href', '/admin/tax-configuration')
  })

  it('shows no items as a warning, not as a failure — an empty version may still publish', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () =>
      versionedJson(aPriceListVersion({ version: draft.version, items: [] }), 'W/"1"'),
    )
    transport.route(VALIDATE(DRAFT_ID), () =>
      jsonResponse({
        versionId: DRAFT_ID,
        canPublish: true,
        findings: [
          aBillingFinding({
            severity: 'Warning',
            code: 'billing.no-items',
            message: 'The version holds no item, so no service could be priced on it.',
            target: 'items',
          }),
        ],
      }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Check this version' }))

    expect(await screen.findByText('Warning')).toBeInTheDocument()
    expect(
      screen.getByText('The version holds no item, so no service could be priced on it.'),
    ).toBeInTheDocument()
    expect(screen.queryByText('Error')).not.toBeInTheDocument()
  })

  it('shows an unpriced branch as a warning naming the branch, rather than a failure', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(VALIDATE(DRAFT_ID), () =>
      jsonResponse({
        versionId: DRAFT_ID,
        canPublish: true,
        findings: [
          aBillingFinding({
            severity: 'Warning',
            code: 'billing.branch-left-unpriced',
            message:
              '1 branch(es) the published version prices are dropped by this one, and the published catalogue offers priced services there. Keep the branch, or publish another list’s version for it first.',
            target: 'branchIds',
          }),
        ],
      }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Check this version' }))

    expect(await screen.findByText('Warning')).toBeInTheDocument()
    expect(
      screen.getByText(
        '1 branch(es) the published version prices are dropped by this one, and the published catalogue offers priced services there. Keep the branch, or publish another list’s version for it first.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText('Error')).not.toBeInTheDocument()
  })

  it('shows a warning from a successful publish as a warning, not as a failure or silently', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(PUBLISH(DRAFT_ID), () =>
      versionedJson(
        aPriceListPublication({
          published: aDraft({ status: 'Published' }),
          findings: [
            aBillingFinding({
              severity: 'Warning',
              code: 'billing.no-items',
              message: 'The version holds no item, so no service could be priced on it.',
              target: 'items',
            }),
          ],
        }),
        'W/"2"',
      ),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Publish this version' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'Approved.')
    await user.click(dialog.getByRole('button', { name: 'Publish this version' }))

    expect(await screen.findByText('Warning')).toBeInTheDocument()
    expect(
      screen.getByText('The version holds no item, so no service could be priced on it.'),
    ).toBeInTheDocument()
    expect(screen.queryByText('Error')).not.toBeInTheDocument()
  })

  it('hides the publish control without the publish key, and names who may publish instead', async () => {
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))

    renderEditorAt(DRAFT_ID, [BILLING_PERMISSIONS.managePriceLists])

    expect(await screen.findByText(draft.items[0]?.code ?? '')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Publish this version' })).not.toBeInTheDocument()
    expect(
      screen.getByText('publishing a price-list version is not part of what your role can do.'),
    ).toBeInTheDocument()
    expect(screen.getByText('This is done by: Owner.')).toBeInTheDocument()
  })

  it("handles the server's own refusal of a publish, because the claim list can be stale", async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(PUBLISH(DRAFT_ID), () => problemResponse(403, 'security.forbidden'))

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Publish this version' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'Approved.')
    await user.click(dialog.getByRole('button', { name: 'Publish this version' }))

    expect(
      await screen.findByText('This did not go through, and the reason is not clear.'),
    ).toBeInTheDocument()
  })

  it('replays the publish with the same retry key after a step-up, without a second publication', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    let attempts = 0
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(PUBLISH(DRAFT_ID), () => {
      attempts += 1
      return attempts === 1
        ? problemResponse(403, 'security.step-up-required')
        : versionedJson(
            aPriceListPublication({ published: aDraft({ status: 'Published' }) }),
            'W/"2"',
          )
    })

    renderEditorAt(DRAFT_ID)
    await screen.findByRole('button', { name: 'Publish this version' })
    // `SessionProvider` registers its own handler on mount, after this file's `beforeEach` cleared
    // it — so the stub is installed here, once the provider has mounted, rather than before render.
    setSessionChallengeHandler(() => Promise.resolve(true))

    await user.click(screen.getByRole('button', { name: 'Publish this version' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'Approved.')
    await user.click(dialog.getByRole('button', { name: 'Publish this version' }))

    await waitFor(() => expect(transport.callsTo(PUBLISH(DRAFT_ID))).toHaveLength(2))
    const [first, second] = transport.callsTo(PUBLISH(DRAFT_ID))
    expect(second?.headers.get('Idempotency-Key')).toBe(first?.headers.get('Idempotency-Key'))
    expect(await screen.findByText('Published.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('shows a missing reason as a field error, keeping the dialog open with the typed reason', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(PUBLISH(DRAFT_ID), () =>
      problemResponse(400, 'billing.reason-required', { errors: { reason: ['Say why.'] } }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Publish this version' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'Approved by the accountant.')
    await user.click(dialog.getByRole('button', { name: 'Publish this version' }))

    expect(await screen.findByText('Say why.')).toBeInTheDocument()
    expect(dialog.getByLabelText('Reason')).toHaveValue('Approved by the accountant.')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('re-reads the version after a lost race with another version of this list', async () => {
    const user = userEvent.setup()
    let reads = 0
    transport.route(READ(DRAFT_ID), () => {
      reads += 1
      return reads === 1
        ? versionedJson(aDraft(), 'W/"1"')
        : versionedJson(aDraft({ status: 'Published' }), 'W/"2"')
    })
    transport.route(PUBLISH(DRAFT_ID), () => problemResponse(409, 'billing.publish-conflict'))

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Publish this version' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'Approved.')
    await user.click(dialog.getByRole('button', { name: 'Publish this version' }))

    expect(
      await screen.findByText(
        'Someone else published a version at the same moment. Here is where this version now stands.',
      ),
    ).toBeInTheDocument()
    await waitFor(() => expect(transport.callsTo(READ(DRAFT_ID))).toHaveLength(2))

    await user.click(dialog.getByRole('button', { name: 'Cancel' }))
    expect(
      await screen.findByText(
        'This version is published. Every invoice since was calculated on it — clone it to a new draft to change what it says.',
      ),
    ).toBeInTheDocument()
  })

  it('answers with a different sentence, naming a branch, when another list conflicts on one', async () => {
    const user = userEvent.setup()
    let reads = 0
    transport.route(READ(DRAFT_ID), () => {
      reads += 1
      return reads === 1
        ? versionedJson(aDraft(), 'W/"1"')
        : versionedJson(aDraft({ status: 'Published' }), 'W/"2"')
    })
    transport.route(PUBLISH(DRAFT_ID), () =>
      problemResponse(409, 'billing.branch-publish-conflict'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Publish this version' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'Approved.')
    await user.click(dialog.getByRole('button', { name: 'Publish this version' }))

    expect(
      await screen.findByText(
        'Another price list’s version already prices one of this version’s branches, published at the same moment. A branch is priced by one published version at a time; read what is published now before deciding whether this draft is still wanted.',
      ),
    ).toBeInTheDocument()
    expect(
      screen.queryByText(
        'Someone else published a version at the same moment. Here is where this version now stands.',
      ),
    ).not.toBeInTheDocument()
    await waitFor(() => expect(transport.callsTo(READ(DRAFT_ID))).toHaveLength(2))
  })

  it('marks the report stale after a write, so the publish control does not read it as still current', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(VALIDATE(DRAFT_ID), () =>
      jsonResponse({ versionId: DRAFT_ID, canPublish: true, findings: [] }),
    )
    transport.route(`PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(aDraft({ name: 'Rates from 1 May 2026' }), 'W/"2"'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Check this version' }))
    expect(
      await screen.findByText('Every check passed. There is nothing to fix or note.'),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Change the version’s conventions' }))
    const form = within(await screen.findByRole('form', { name: 'Version 1 conventions' }))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'This version has changed since the last check. Check it again before publishing.',
      ),
    ).toBeInTheDocument()
  })

  it('blocks the validate and publish controls while offline, without queuing anything', async () => {
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))

    renderEditorAt(DRAFT_ID)
    await screen.findByText(draft.items[0]?.code ?? '')

    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findAllByText('Needs connection — this will not be queued'),
    ).not.toHaveLength(0)
    expect(screen.queryByRole('button', { name: 'Check this version' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Publish this version' })).not.toBeInTheDocument()

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })

  it('has no accessibility violations with a report on screen and the publish dialog open', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(VALIDATE(DRAFT_ID), () =>
      jsonResponse({
        versionId: DRAFT_ID,
        canPublish: false,
        findings: [aBillingFinding({ severity: 'Error', target: 'items' })],
      }),
    )

    const { container } = renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Check this version' }))
    await screen.findByText("The 'GST5' tax code has no CGST component configured.")
    await expectNoAccessibilityViolations(container)

    await user.click(screen.getByRole('button', { name: 'Publish this version' }))
    const dialog = await screen.findByRole('dialog')
    // The dialog is portalled to `document.body`, outside the render `container`, so it is checked
    // on its own rather than being silently skipped by a check scoped to `container`.
    await expectNoAccessibilityViolations(dialog)
  })
})
