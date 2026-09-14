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
  TAX_CODE_ID,
  TAX_CONFIGURATION_VERSION_ID,
  aTaxCode,
  aTaxConfiguration,
  aTaxConfigurationSummary,
} from '../../billing/testing/pricingConfigFixtures'
import type { TaxCode, TaxConfiguration } from '../../billing/pricingAdminTypes'
import { TaxConfigurationEditorRoute } from './TaxConfigurationEditorRoute'
import { TaxConfigurationListRoute } from './TaxConfigurationListRoute'

let transport: FetchStub

const TAX_VERSIONS = '/api/v1/billing/tax-configuration/versions'
const DRAFT_ID = '0199dd00-0000-7000-8000-000000006050'
const SECOND_CODE_ID = '0199dd00-0000-7000-8000-000000006051'

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

/**
 * A JSON body carrying the `ETag` a write is made against — `jsonResponse` alone cannot.
 *
 * A `204` (the removal route's own answer) may not carry a body at all, so `null` is passed to the
 * `Response` constructor as-is rather than as the four-character string `"null"`.
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
                    <TaxConfigurationListRoute />
                  </RequirePermission>
                }
                path="/admin/tax-configuration"
              />
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

/** Renders the editor directly, for the tests that do not need the register in front of it. */
function renderEditorAt(
  versionId: string,
  permissions: readonly string[] = [BILLING_PERMISSIONS.managePriceLists],
) {
  return renderApp(`/admin/tax-configuration/${versionId}`, permissions)
}

describe('the tax configuration editor', () => {
  it('starts a draft, changes its details, adds a tax code, edits it, and removes it', async () => {
    const user = userEvent.setup()

    let version = aTaxConfigurationSummary({
      taxConfigurationVersionId: DRAFT_ID,
      versionNumber: 2,
      name: 'Untitled',
      notes: null,
      status: 'Draft',
      effectiveFrom: '2026-04-01',
      publishedAt: null,
      clonedFromVersionId: null,
    })
    let taxCodes: readonly TaxCode[] = []
    let tag = 'W/"1"'
    const configuration = (): TaxConfiguration => ({ version, taxCodes })

    transport.route(`GET ${TAX_VERSIONS}`, () => jsonResponse([aTaxConfigurationSummary()]))
    transport.route(`POST ${TAX_VERSIONS}`, () => jsonResponse(configuration(), 201))
    transport.route(`GET ${TAX_VERSIONS}/${DRAFT_ID}`, () => versionedJson(configuration(), tag))

    transport.route(`PUT ${TAX_VERSIONS}/${DRAFT_ID}`, (call) => {
      const body = call.body as { name: string; notes: string | null; effectiveFrom: string }
      version = {
        ...version,
        name: body.name,
        notes: body.notes,
        effectiveFrom: body.effectiveFrom,
      }
      tag = 'W/"2"'
      return versionedJson(configuration(), tag)
    })

    transport.route(`POST ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes`, (call) => {
      const body = call.body as { code: string }
      const created = aTaxCode({
        taxCodeId: TAX_CODE_ID,
        code: body.code,
        rates: [
          { kind: 'Cgst', ratePercent: '2.5' },
          { kind: 'Sgst', ratePercent: '2.5' },
        ],
      })
      taxCodes = [...taxCodes, created]
      tag = 'W/"3"'
      return versionedJson(created, tag, 201)
    })

    transport.route(`PUT ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes/${TAX_CODE_ID}`, (call) => {
      const body = call.body as { description: string }
      const edited = { ...taxCodes[0], description: body.description } as TaxCode
      taxCodes = [edited]
      tag = 'W/"4"'
      return versionedJson(edited, tag)
    })

    transport.route(`POST ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes/${TAX_CODE_ID}/delete`, () => {
      taxCodes = []
      tag = 'W/"5"'
      return versionedJson(null, tag, 204)
    })

    renderApp('/admin/tax-configuration')
    await user.click(await screen.findByRole('button', { name: 'Start a draft' }))

    const startForm = within(await screen.findByRole('form', { name: 'Start a draft' }))
    await user.type(startForm.getByLabelText('Name'), 'Untitled')
    await user.type(startForm.getByLabelText('First day'), '2026-04-01')
    await user.click(startForm.getByRole('button', { name: 'Start draft' }))

    expect(await screen.findByText('Tax configuration version 2')).toBeInTheDocument()

    // Changes the version's own details.
    await user.click(screen.getByRole('button', { name: 'Change the version’s details' }))
    const detailsForm = within(await screen.findByRole('form', { name: 'Version details' }))
    await user.clear(detailsForm.getByLabelText('Name'))
    await user.type(detailsForm.getByLabelText('Name'), 'Financial year 2026-27')
    await user.click(detailsForm.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Saved the version’s details.')).toBeInTheDocument()
    expect(await screen.findByText('Financial year 2026-27')).toBeInTheDocument()

    // Adds a tax code with a CGST + SGST pair.
    await user.click(screen.getByRole('button', { name: 'Add a tax code' }))
    const codeForm = within(await screen.findByRole('form', { name: 'Add a tax code' }))
    await user.type(codeForm.getByLabelText('Code'), 'STITCHING_5')
    await user.type(codeForm.getByLabelText('Description'), 'Tailoring services')
    await user.type(codeForm.getByLabelText('HSN or SAC classification'), '998822')
    await user.click(codeForm.getByLabelText('Services'))
    await user.click(codeForm.getByLabelText('Active'))
    await user.type(codeForm.getByLabelText('CGST'), '2.5')
    await user.type(codeForm.getByLabelText('SGST'), '2.5')
    await user.click(codeForm.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Saved the tax code STITCHING_5.')).toBeInTheDocument()
    expect(screen.getByText('STITCHING_5')).toBeInTheDocument()

    const added = transport.callsTo(`POST ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes`)[0]
    expect(added?.body).toEqual({
      code: 'STITCHING_5',
      description: 'Tailoring services',
      classification: '998822',
      kind: 'Services',
      active: true,
      rates: [
        { kind: 'Cgst', ratePercent: '2.5' },
        { kind: 'Sgst', ratePercent: '2.5' },
      ],
      reason: null,
    })
    expect(added?.headers.get('If-Match')).toBe('W/"2"')

    // Edits it.
    await user.click(screen.getByRole('button', { name: 'Edit STITCHING_5' }))
    const editForm = within(
      await screen.findByRole('form', { name: 'Edit the tax code STITCHING_5' }),
    )
    await user.clear(editForm.getByLabelText('Description'))
    await user.type(editForm.getByLabelText('Description'), 'Alteration and stitching services')
    await user.click(editForm.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Alteration and stitching services')).toBeInTheDocument()

    const edited = transport.callsTo(`PUT ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes/${TAX_CODE_ID}`)[0]
    expect(edited?.headers.get('If-Match')).toBe('W/"3"')

    // Removes it.
    await user.click(screen.getByRole('button', { name: 'Remove STITCHING_5' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'Duplicated by a later code.')
    await user.click(dialog.getByRole('button', { name: 'Remove STITCHING_5' }))

    expect(await screen.findByText('Removed the tax code STITCHING_5.')).toBeInTheDocument()
    expect(screen.getByText('No tax code yet. Add the first one below.')).toBeInTheDocument()

    const removed = transport.callsTo(
      `POST ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes/${TAX_CODE_ID}/delete`,
    )[0]
    expect(removed?.headers.get('If-Match')).toBe('W/"4"')
  }, 30000)

  it('shows a refused rate against its own component row, not as a page-level alert', async () => {
    const user = userEvent.setup()
    const draft = aTaxConfiguration({
      version: aTaxConfigurationSummary({ taxConfigurationVersionId: DRAFT_ID, status: 'Draft' }),
      taxCodes: [],
    })

    transport.route(`GET ${TAX_VERSIONS}/${DRAFT_ID}`, () => versionedJson(draft, 'W/"1"'))
    transport.route(`POST ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes`, () =>
      problemResponse(400, 'billing.rate-out-of-range', {
        errors: { 'rates[1].ratePercent': ['A rate is a percentage between 0 and 100.'] },
      }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a tax code' }))
    const form = within(await screen.findByRole('form', { name: 'Add a tax code' }))
    await user.type(form.getByLabelText('Code'), 'STITCHING_5')
    await user.type(form.getByLabelText('Description'), 'Tailoring services')
    await user.type(form.getByLabelText('HSN or SAC classification'), '998822')
    await user.click(form.getByLabelText('Services'))
    await user.click(form.getByLabelText('Active'))
    await user.type(form.getByLabelText('CGST'), '2.5')
    await user.type(form.getByLabelText('SGST'), '150')
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'A rate is a percentage between 0 and 100, to at most three decimal places.',
      ),
    ).toBeInTheDocument()
    expect(form.getByLabelText('SGST')).toHaveAttribute('aria-invalid', 'true')
    expect(form.getByLabelText('CGST')).not.toHaveAttribute('aria-invalid', 'true')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows a duplicate component in words', async () => {
    const user = userEvent.setup()
    const draft = aTaxConfiguration({
      version: aTaxConfigurationSummary({ taxConfigurationVersionId: DRAFT_ID, status: 'Draft' }),
      taxCodes: [],
    })

    transport.route(`GET ${TAX_VERSIONS}/${DRAFT_ID}`, () => versionedJson(draft, 'W/"1"'))
    transport.route(`POST ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes`, () =>
      problemResponse(400, 'billing.component-duplicated', {
        errors: { 'rates[0].kind': ['Duplicated.'] },
      }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a tax code' }))
    const form = within(await screen.findByRole('form', { name: 'Add a tax code' }))
    await user.type(form.getByLabelText('Code'), 'STITCHING_5')
    await user.type(form.getByLabelText('Description'), 'Tailoring services')
    await user.type(form.getByLabelText('HSN or SAC classification'), '998822')
    await user.click(form.getByLabelText('Services'))
    await user.click(form.getByLabelText('Active'))
    await user.type(form.getByLabelText('CGST'), '2.5')
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'A tax code carries each component — CGST, SGST, IGST, cess — at most once.',
      ),
    ).toBeInTheDocument()
    expect(form.getByLabelText('CGST')).toHaveAttribute('aria-invalid', 'true')
  })

  it('refuses a duplicate code in words, keeping typed values and the retry key', async () => {
    const user = userEvent.setup()
    const draft = aTaxConfiguration({
      version: aTaxConfigurationSummary({ taxConfigurationVersionId: DRAFT_ID, status: 'Draft' }),
      taxCodes: [],
    })

    transport.route(`GET ${TAX_VERSIONS}/${DRAFT_ID}`, () => versionedJson(draft, 'W/"1"'))
    transport.route(`POST ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes`, () =>
      problemResponse(400, 'billing.code-not-unique', { errors: { code: ['Already used.'] } }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a tax code' }))
    const form = within(await screen.findByRole('form', { name: 'Add a tax code' }))
    await user.type(form.getByLabelText('Code'), 'STITCHING_5')
    await user.type(form.getByLabelText('Description'), 'Tailoring services')
    await user.type(form.getByLabelText('HSN or SAC classification'), '998822')
    await user.click(form.getByLabelText('Services'))
    await user.click(form.getByLabelText('Active'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText('That code is already used. Choose a different one.'),
    ).toBeInTheDocument()
    expect(form.getByLabelText('Code')).toHaveValue('STITCHING_5')

    await user.click(form.getByRole('button', { name: 'Save' }))
    await waitFor(() => {
      expect(transport.callsTo(`POST ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes`)).toHaveLength(2)
    })

    const sent = transport.callsTo(`POST ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes`)
    expect(sent[1]?.headers.get('Idempotency-Key')).toBe(sent[0]?.headers.get('Idempotency-Key'))
  })

  it('offers a re-read after a stale tag, rather than overwriting silently', async () => {
    const user = userEvent.setup()
    const existing = aTaxCode({ taxCodeId: TAX_CODE_ID })
    const draft = aTaxConfiguration({
      version: aTaxConfigurationSummary({ taxConfigurationVersionId: DRAFT_ID, status: 'Draft' }),
      taxCodes: [existing],
    })

    transport.route(`GET ${TAX_VERSIONS}/${DRAFT_ID}`, () => versionedJson(draft, 'W/"1"'))
    transport.route(`PUT ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes/${TAX_CODE_ID}`, () =>
      problemResponse(412, 'billing.version-changed'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: `Edit ${existing.code}` }))
    const form = within(
      await screen.findByRole('form', { name: `Edit the tax code ${existing.code}` }),
    )
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'Someone else changed this version while it was open here. Read it again to see what changed.',
      ),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Read it again' }))

    await waitFor(() => {
      expect(transport.callsTo(`GET ${TAX_VERSIONS}/${DRAFT_ID}`)).toHaveLength(2)
    })
  })

  it('carries the tag from a 204 removal into the next write', async () => {
    const user = userEvent.setup()
    const first = aTaxCode({ taxCodeId: TAX_CODE_ID, code: 'STITCHING_5' })
    const second = aTaxCode({ taxCodeId: SECOND_CODE_ID, code: 'ALTER_5' })
    let taxCodes: readonly TaxCode[] = [first, second]
    const version = aTaxConfigurationSummary({
      taxConfigurationVersionId: DRAFT_ID,
      status: 'Draft',
    })

    transport.route(`GET ${TAX_VERSIONS}/${DRAFT_ID}`, () =>
      versionedJson({ version, taxCodes }, 'W/"1"'),
    )
    transport.route(`POST ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes/${TAX_CODE_ID}/delete`, () => {
      taxCodes = [second]
      return versionedJson(null, 'W/"2"', 204)
    })
    transport.route(`PUT ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes/${SECOND_CODE_ID}`, () =>
      versionedJson({ ...second, active: false }, 'W/"3"'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: `Remove ${first.code}` }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'No longer offered.')
    await user.click(dialog.getByRole('button', { name: `Remove ${first.code}` }))

    await waitFor(() => {
      expect(
        transport.callsTo(`POST ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes/${TAX_CODE_ID}/delete`),
      ).toHaveLength(1)
    })

    await user.click(await screen.findByRole('button', { name: `Edit ${second.code}` }))
    const form = within(
      await screen.findByRole('form', { name: `Edit the tax code ${second.code}` }),
    )
    await user.click(form.getByLabelText('Inactive'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(
        transport.callsTo(`PUT ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes/${SECOND_CODE_ID}`),
      ).toHaveLength(1)
    })
    const sent = transport.callsTo(`PUT ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes/${SECOND_CODE_ID}`)[0]
    expect(sent?.headers.get('If-Match')).toBe('W/"2"')
  })

  it('offers no editing control on a published version, and points at the clone instead', async () => {
    const published = aTaxConfiguration({
      version: aTaxConfigurationSummary({
        taxConfigurationVersionId: TAX_CONFIGURATION_VERSION_ID,
        status: 'Published',
      }),
    })
    transport.route(`GET ${TAX_VERSIONS}/${TAX_CONFIGURATION_VERSION_ID}`, () =>
      versionedJson(published, 'W/"1"'),
    )

    renderEditorAt(TAX_CONFIGURATION_VERSION_ID)

    expect(
      await screen.findByText(
        'This version is published. Every invoice since was calculated on it — clone it to a new draft to change what it says.',
      ),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Change the version’s details' }),
    ).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add a tax code' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Edit /i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Remove /i })).not.toBeInTheDocument()
  })

  it('shows a sentence without the manage key, and makes no request', async () => {
    renderEditorAt(TAX_CONFIGURATION_VERSION_ID, [])

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(transport.callsTo(`GET ${TAX_VERSIONS}/${TAX_CONFIGURATION_VERSION_ID}`)).toHaveLength(0)
  })

  it("handles the server's own refusal when the claim list is stale", async () => {
    const user = userEvent.setup()
    const draft = aTaxConfiguration({
      version: aTaxConfigurationSummary({ taxConfigurationVersionId: DRAFT_ID, status: 'Draft' }),
      taxCodes: [],
    })

    transport.route(`GET ${TAX_VERSIONS}/${DRAFT_ID}`, () => versionedJson(draft, 'W/"1"'))
    transport.route(`POST ${TAX_VERSIONS}/${DRAFT_ID}/tax-codes`, () =>
      problemResponse(403, 'security.forbidden'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a tax code' }))
    const form = within(await screen.findByRole('form', { name: 'Add a tax code' }))
    await user.type(form.getByLabelText('Code'), 'STITCHING_5')
    await user.type(form.getByLabelText('Description'), 'Tailoring services')
    await user.type(form.getByLabelText('HSN or SAC classification'), '998822')
    await user.click(form.getByLabelText('Services'))
    await user.click(form.getByLabelText('Active'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText('This did not go through, and the reason is not clear.'),
    ).toBeInTheDocument()
  })

  it('blocks every write control while offline, without queuing anything', async () => {
    const draft = aTaxConfiguration({
      version: aTaxConfigurationSummary({ taxConfigurationVersionId: DRAFT_ID, status: 'Draft' }),
    })
    transport.route(`GET ${TAX_VERSIONS}/${DRAFT_ID}`, () => versionedJson(draft, 'W/"1"'))

    renderEditorAt(DRAFT_ID)
    await screen.findByText(draft.taxCodes[0]?.code ?? '')

    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findAllByText('Needs connection — this will not be queued'),
    ).not.toHaveLength(0)
    expect(screen.queryByRole('button', { name: 'Add a tax code' })).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Change the version’s details' }),
    ).not.toBeInTheDocument()

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })

  it('pre-fills no rate when adding a code', async () => {
    const user = userEvent.setup()
    const draft = aTaxConfiguration({
      version: aTaxConfigurationSummary({ taxConfigurationVersionId: DRAFT_ID, status: 'Draft' }),
      taxCodes: [],
    })
    transport.route(`GET ${TAX_VERSIONS}/${DRAFT_ID}`, () => versionedJson(draft, 'W/"1"'))

    const { container } = renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a tax code' }))
    const form = within(await screen.findByRole('form', { name: 'Add a tax code' }))

    expect(form.getByLabelText('CGST')).toHaveValue('')
    expect(form.getByLabelText('SGST')).toHaveValue('')
    expect(form.getByLabelText('IGST')).toHaveValue('')
    expect(form.getByLabelText('Cess')).toHaveValue('')
    expect(form.getByLabelText('Goods')).not.toBeChecked()
    expect(form.getByLabelText('Services')).not.toBeChecked()
    expect(form.getByLabelText('Active')).not.toBeChecked()
    expect(form.getByLabelText('Inactive')).not.toBeChecked()

    await expectNoAccessibilityViolations(container)
  })

  it('has no accessibility violations on a working editor', async () => {
    const draft = aTaxConfiguration({
      version: aTaxConfigurationSummary({ taxConfigurationVersionId: DRAFT_ID, status: 'Draft' }),
    })
    transport.route(`GET ${TAX_VERSIONS}/${DRAFT_ID}`, () => versionedJson(draft, 'W/"1"'))

    const { container } = renderEditorAt(DRAFT_ID)
    await screen.findByText('Tax configuration version 1')

    await expectNoAccessibilityViolations(container)
  })
})
