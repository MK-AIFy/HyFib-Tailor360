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
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import {
  TAX_CONFIGURATION_VERSION_ID,
  aBillingFinding,
  aTaxConfiguration,
  aTaxConfigurationPublication,
  aTaxConfigurationSummary,
  findingsProblem,
} from '../../billing/testing/pricingConfigFixtures'
import type { TaxConfigurationSummary } from '../../billing/pricingAdminTypes'
import { TaxConfigurationEditorRoute } from './TaxConfigurationEditorRoute'

let transport: FetchStub

const TAX_VERSIONS = '/api/v1/billing/tax-configuration/versions'
const DRAFT_ID = '0199dd00-0000-7000-8000-000000006060'
const VALIDATE = (id: string) => `GET ${TAX_VERSIONS}/${id}/validation`
const PUBLISH = (id: string) => `POST ${TAX_VERSIONS}/${id}/publish`
const READ = (id: string) => `GET ${TAX_VERSIONS}/${id}`

const BOTH_KEYS = [BILLING_PERMISSIONS.managePriceLists, BILLING_PERMISSIONS.publishPriceList]

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
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
        <MemoryRouter initialEntries={[`/admin/tax-configuration/${versionId}`]}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                element={
                  <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
                    <TaxConfigurationEditorRoute />
                  </RequirePermission>
                }
                path="/admin/tax-configuration/:versionId"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

function aDraft(overrides: Partial<TaxConfigurationSummary> = {}) {
  return aTaxConfiguration({
    version: aTaxConfigurationSummary({
      taxConfigurationVersionId: DRAFT_ID,
      status: 'Draft',
      ...overrides,
    }),
  })
}

describe('validating and publishing a tax configuration version', () => {
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
        aTaxConfigurationPublication({
          published: aDraft({ status: 'Published' }),
          supersededVersionId: TAX_CONFIGURATION_VERSION_ID,
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

  it('lists every publication finding against the code it concerns, not just the first', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(PUBLISH(DRAFT_ID), () =>
      findingsProblem([
        aBillingFinding({
          severity: 'Error',
          code: 'billing.intra-state-pair-incomplete',
          message: 'The tax code STITCHING_5 carries a CGST without its matching SGST.',
          target: 'taxCodes[STITCHING_5]',
        }),
        aBillingFinding({
          severity: 'Error',
          code: 'billing.no-tax-codes',
          message: 'This version carries no active tax code.',
          target: null,
        }),
      ]),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Publish this version' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'Approved.')
    await user.click(dialog.getByRole('button', { name: 'Publish this version' }))

    expect(
      await screen.findByText('The tax code STITCHING_5 carries a CGST without its matching SGST.'),
    ).toBeInTheDocument()
    expect(screen.getByText('This version carries no active tax code.')).toBeInTheDocument()
    expect(screen.queryByText('billing.publish-validation-failed')).not.toBeInTheDocument()
  })

  it('shows a warning from a successful publish as a warning, not as a failure or silently', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(PUBLISH(DRAFT_ID), () =>
      versionedJson(
        aTaxConfigurationPublication({
          published: aDraft({ status: 'Published' }),
          findings: [
            aBillingFinding({
              severity: 'Warning',
              code: 'billing.nil-rated-code',
              message: 'The tax code ALTER_0 carries no rate at all.',
              target: 'taxCodes[ALTER_0]',
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
    expect(screen.getByText('The tax code ALTER_0 carries no rate at all.')).toBeInTheDocument()
    expect(screen.queryByText('Error')).not.toBeInTheDocument()
  })

  it('hides the publish control without the publish key, and names who may publish instead', async () => {
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))

    renderEditorAt(DRAFT_ID, [BILLING_PERMISSIONS.managePriceLists])

    expect(await screen.findByText(draft.taxCodes[0]?.code ?? '')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Publish this version' })).not.toBeInTheDocument()
    expect(
      screen.getByText(
        'publishing a tax configuration version is not part of what your role can do.',
      ),
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
            aTaxConfigurationPublication({ published: aDraft({ status: 'Published' }) }),
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

  it('re-reads the version after a lost publish race, rather than leaving a stale Draft on screen', async () => {
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

  it('marks the report stale after a write, so the publish control does not read it as still current', async () => {
    const user = userEvent.setup()
    const draft = aDraft()
    transport.route(READ(DRAFT_ID), () => versionedJson(draft, 'W/"1"'))
    transport.route(VALIDATE(DRAFT_ID), () =>
      jsonResponse({ versionId: DRAFT_ID, canPublish: true, findings: [] }),
    )
    transport.route(`PUT ${TAX_VERSIONS}/${DRAFT_ID}`, () =>
      versionedJson(aDraft({ name: 'Financial year 2026-27' }), 'W/"2"'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Check this version' }))
    expect(
      await screen.findByText('Every check passed. There is nothing to fix or note.'),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Change the version’s details' }))
    const form = within(await screen.findByRole('form', { name: 'Version details' }))
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
    await screen.findByText(draft.taxCodes[0]?.code ?? '')

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
        findings: [aBillingFinding({ severity: 'Error' })],
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
