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
  GST_REGISTRATION_ID,
  aGstRegistration,
  aTaxConfigurationSummary,
} from '../../billing/testing/pricingConfigFixtures'
import { GstRegistrationsRoute } from './GstRegistrationsRoute'
import { TaxConfigurationListRoute } from './TaxConfigurationListRoute'

let transport: FetchStub

const GST_REGISTRATIONS = '/api/v1/billing/gst-registrations'
const BRANCHES = '/api/v1/admin/branches/'
const TAX_VERSIONS = '/api/v1/billing/tax-configuration/versions'

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route(`GET ${BRANCHES}`, () =>
    jsonResponse([aBranch({ branchId: BRANCH_ID, name: 'Coimbatore counter' })]),
  )
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderGstAt(permissions: readonly string[] = [BILLING_PERMISSIONS.managePriceLists]) {
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions })))

  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/admin/gst-registrations']}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                element={
                  <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
                    <GstRegistrationsRoute />
                  </RequirePermission>
                }
                path="/admin/gst-registrations"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

function renderTaxAt(permissions: readonly string[] = [BILLING_PERMISSIONS.managePriceLists]) {
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions })))

  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/admin/tax-configuration']}>
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
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

describe('the GST registration register', () => {
  it('lists registrations with the branch name resolved and a still-in-force last day', async () => {
    transport.route(`GET ${GST_REGISTRATIONS}`, () => jsonResponse([aGstRegistration()]))

    const { container } = renderGstAt()

    expect(await screen.findByText('Coimbatore counter')).toBeInTheDocument()
    expect(screen.getByText('33AAACH7409R1Z8')).toBeInTheDocument()
    expect(screen.getByText('Example Tailors Private Limited')).toBeInTheDocument()
    expect(screen.getByText('Still in force')).toBeInTheDocument()

    await expectNoAccessibilityViolations(container)
  })

  it('shows the empty state with the record control beside it', async () => {
    transport.route(`GET ${GST_REGISTRATIONS}`, () => jsonResponse([]))

    renderGstAt()

    expect(await screen.findByText('No registration recorded yet')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Record a registration' })).toBeInTheDocument()
  })

  it('has no accessibility violations on the record form', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${GST_REGISTRATIONS}`, () => jsonResponse([]))

    const { container } = renderGstAt()
    await screen.findByText('No registration recorded yet')
    await user.click(screen.getByRole('button', { name: 'Record a registration' }))
    await screen.findByRole('form', { name: 'Record a GST registration' })

    await expectNoAccessibilityViolations(container)
  })

  it('records a registration, sending the branch, the GSTIN and an Idempotency-Key', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${GST_REGISTRATIONS}`, () => jsonResponse([]))
    transport.route(`POST ${GST_REGISTRATIONS}`, () => jsonResponse(aGstRegistration(), 201))

    renderGstAt()
    await screen.findByText('No registration recorded yet')
    await user.click(screen.getByRole('button', { name: 'Record a registration' }))

    const form = within(await screen.findByRole('form', { name: 'Record a GST registration' }))
    await user.selectOptions(form.getByLabelText('Branch'), 'Coimbatore counter (CBE01)')
    await user.type(form.getByLabelText('GSTIN'), '33AAACH7409R1Z8')
    await user.type(form.getByLabelText('State code'), '33')
    await user.type(form.getByLabelText('Legal name'), 'Example Tailors Private Limited')
    await user.type(form.getByLabelText('First day'), '2026-04-01')
    await user.click(form.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(transport.callsTo(`POST ${GST_REGISTRATIONS}`)).toHaveLength(1)
    })

    const sent = transport.callsTo(`POST ${GST_REGISTRATIONS}`)[0]
    expect(sent?.body).toEqual({
      branchId: BRANCH_ID,
      gstin: '33AAACH7409R1Z8',
      stateCode: '33',
      legalName: 'Example Tailors Private Limited',
      tradeName: null,
      effectiveFrom: '2026-04-01',
      effectiveTo: null,
      reason: null,
    })
    expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
    expect(await screen.findByText('Recorded the registration.')).toBeInTheDocument()
  })

  it('refuses an overlapping registration in words and reuses the same Idempotency-Key on retry', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${GST_REGISTRATIONS}`, () => jsonResponse([]))
    transport.route(`POST ${GST_REGISTRATIONS}`, () =>
      problemResponse(409, 'billing.registration-overlaps'),
    )

    renderGstAt()
    await screen.findByText('No registration recorded yet')
    await user.click(screen.getByRole('button', { name: 'Record a registration' }))

    const form = within(await screen.findByRole('form', { name: 'Record a GST registration' }))
    await user.selectOptions(form.getByLabelText('Branch'), 'Coimbatore counter (CBE01)')
    await user.type(form.getByLabelText('GSTIN'), '33AAACH7409R1Z8')
    await user.type(form.getByLabelText('State code'), '33')
    await user.type(form.getByLabelText('Legal name'), 'Example Tailors Private Limited')
    await user.type(form.getByLabelText('First day'), '2026-04-01')
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'Another registration is already in force for this branch on this day. End it first, or choose a later first day.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText('billing.registration-overlaps')).not.toBeInTheDocument()

    // The typed values survive the refusal.
    expect(form.getByLabelText('GSTIN')).toHaveValue('33AAACH7409R1Z8')

    await user.click(form.getByRole('button', { name: 'Save' }))
    await waitFor(() => {
      expect(transport.callsTo(`POST ${GST_REGISTRATIONS}`)).toHaveLength(2)
    })

    const sent = transport.callsTo(`POST ${GST_REGISTRATIONS}`)
    expect(sent[1]?.headers.get('Idempotency-Key')).toBe(sent[0]?.headers.get('Idempotency-Key'))
  })

  it('shows a malformed GSTIN as a field error beside the control, not as a page alert', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${GST_REGISTRATIONS}`, () => jsonResponse([]))
    transport.route(`POST ${GST_REGISTRATIONS}`, () =>
      problemResponse(400, 'billing.gstin-not-well-formed', {
        errors: { gstin: ['Not well-formed.'] },
      }),
    )

    renderGstAt()
    await screen.findByText('No registration recorded yet')
    await user.click(screen.getByRole('button', { name: 'Record a registration' }))

    const form = within(await screen.findByRole('form', { name: 'Record a GST registration' }))
    await user.selectOptions(form.getByLabelText('Branch'), 'Coimbatore counter (CBE01)')
    await user.type(form.getByLabelText('GSTIN'), 'NOT-A-GSTIN')
    await user.type(form.getByLabelText('State code'), '33')
    await user.type(form.getByLabelText('Legal name'), 'Example Tailors Private Limited')
    await user.type(form.getByLabelText('First day'), '2026-04-01')
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'This does not look like a GSTIN. It is fifteen characters, ending with a check character the server verifies.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(form.getByLabelText('GSTIN')).toHaveAttribute('aria-invalid', 'true')
  })

  it('shows a GSTIN/state mismatch as a field error beside the control', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${GST_REGISTRATIONS}`, () => jsonResponse([]))
    transport.route(`POST ${GST_REGISTRATIONS}`, () =>
      problemResponse(400, 'billing.gstin-state-mismatch'),
    )

    renderGstAt()
    await screen.findByText('No registration recorded yet')
    await user.click(screen.getByRole('button', { name: 'Record a registration' }))

    const form = within(await screen.findByRole('form', { name: 'Record a GST registration' }))
    await user.selectOptions(form.getByLabelText('Branch'), 'Coimbatore counter (CBE01)')
    await user.type(form.getByLabelText('GSTIN'), '33AAACH7409R1Z8')
    await user.type(form.getByLabelText('State code'), '07')
    await user.type(form.getByLabelText('Legal name'), 'Example Tailors Private Limited')
    await user.type(form.getByLabelText('First day'), '2026-04-01')
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText('This GSTIN’s state does not match the state code entered.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('amends a registration against a freshly read tag, sending every field', async () => {
    const user = userEvent.setup()
    const existing = aGstRegistration()
    transport.route(`GET ${GST_REGISTRATIONS}`, () => jsonResponse([existing]))
    transport.route(
      `GET ${GST_REGISTRATIONS}/${GST_REGISTRATION_ID}`,
      () =>
        new Response(JSON.stringify(existing), {
          status: 200,
          headers: { 'Content-Type': 'application/json', ETag: 'W/"1"' },
        }),
    )
    transport.route(`PUT ${GST_REGISTRATIONS}/${GST_REGISTRATION_ID}`, () =>
      jsonResponse({ ...existing, legalName: 'Example Tailors Limited' }),
    )

    renderGstAt()
    await screen.findByText('33AAACH7409R1Z8')
    await user.click(screen.getByRole('button', { name: 'Amend the registration 33AAACH7409R1Z8' }))

    await waitFor(() => {
      expect(transport.callsTo(`GET ${GST_REGISTRATIONS}/${GST_REGISTRATION_ID}`)).toHaveLength(1)
    })

    const form = within(
      await screen.findByRole('form', { name: 'Amend the registration 33AAACH7409R1Z8' }),
    )
    expect(form.getByLabelText('Branch')).toHaveValue('Coimbatore counter')
    expect(form.getByLabelText('Branch')).toHaveAttribute('readonly')

    await user.clear(form.getByLabelText('Legal name'))
    await user.type(form.getByLabelText('Legal name'), 'Example Tailors Limited')
    await user.click(form.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(transport.callsTo(`PUT ${GST_REGISTRATIONS}/${GST_REGISTRATION_ID}`)).toHaveLength(1)
    })

    const sent = transport.callsTo(`PUT ${GST_REGISTRATIONS}/${GST_REGISTRATION_ID}`)[0]
    expect(sent?.body).toEqual({
      branchId: BRANCH_ID,
      gstin: '33AAACH7409R1Z8',
      stateCode: '33',
      legalName: 'Example Tailors Limited',
      tradeName: 'Example Tailors',
      effectiveFrom: '2026-04-01',
      effectiveTo: null,
      reason: null,
    })
    expect(sent?.headers.get('If-Match')).toBe('W/"1"')
    expect(await screen.findByText('Amended the registration.')).toBeInTheDocument()
  })

  it('offers a re-read after a stale tag, rather than overwriting silently', async () => {
    const user = userEvent.setup()
    const existing = aGstRegistration()
    transport.route(`GET ${GST_REGISTRATIONS}`, () => jsonResponse([existing]))
    transport.route(
      `GET ${GST_REGISTRATIONS}/${GST_REGISTRATION_ID}`,
      () =>
        new Response(JSON.stringify(existing), {
          status: 200,
          headers: { 'Content-Type': 'application/json', ETag: 'W/"1"' },
        }),
    )
    transport.route(`PUT ${GST_REGISTRATIONS}/${GST_REGISTRATION_ID}`, () =>
      problemResponse(412, 'billing.registration-changed'),
    )

    renderGstAt()
    await screen.findByText('33AAACH7409R1Z8')
    await user.click(screen.getByRole('button', { name: 'Amend the registration 33AAACH7409R1Z8' }))
    await waitFor(() => {
      expect(transport.callsTo(`GET ${GST_REGISTRATIONS}/${GST_REGISTRATION_ID}`)).toHaveLength(1)
    })

    const form = within(
      await screen.findByRole('form', { name: 'Amend the registration 33AAACH7409R1Z8' }),
    )
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'Someone else changed this registration while it was open here. Read it again to see what changed.',
      ),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Read it again' }))

    await waitFor(() => {
      expect(transport.callsTo(`GET ${GST_REGISTRATIONS}/${GST_REGISTRATION_ID}`)).toHaveLength(2)
    })
    expect(
      screen.queryByText(
        'Someone else changed this registration while it was open here. Read it again to see what changed.',
      ),
    ).not.toBeInTheDocument()
  })

  it('shows branch identifiers, and a typed identifier field, when branch names cannot be read', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${BRANCHES}`, () => problemResponse(403, 'security.forbidden'))
    transport.route(`GET ${GST_REGISTRATIONS}`, () => jsonResponse([aGstRegistration()]))

    renderGstAt()

    expect(await screen.findByText(BRANCH_ID)).toBeInTheDocument()
    expect(screen.queryByText('Coimbatore counter')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Record a registration' }))
    const form = within(await screen.findByRole('form', { name: 'Record a GST registration' }))
    expect(form.getByLabelText('Branch identifier')).toBeInTheDocument()
    expect(form.queryByLabelText('Branch')).not.toBeInTheDocument()
  })

  it('refuses a caller holding no billing permission before any request is made', async () => {
    renderGstAt([])

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(transport.callsTo(`GET ${GST_REGISTRATIONS}`)).toHaveLength(0)
  })

  it('blocks the record control while offline, without queuing anything', async () => {
    transport.route(`GET ${GST_REGISTRATIONS}`, () => jsonResponse([aGstRegistration()]))

    renderGstAt()
    await screen.findByText('33AAACH7409R1Z8')

    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findByText('Needs connection — this will not be queued'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Record a registration' })).not.toBeInTheDocument()

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })
})

describe('the tax configuration version register', () => {
  it('lists versions newest first with status and first day', async () => {
    transport.route(`GET ${TAX_VERSIONS}`, () =>
      jsonResponse([
        aTaxConfigurationSummary({ versionNumber: 2, name: 'Version 2', status: 'Draft' }),
        aTaxConfigurationSummary(),
      ]),
    )

    const { container } = renderTaxAt()

    await screen.findByText('Version 2')
    const rows = screen.getAllByRole('row')
    const draftRow = rows.find((row) => row.textContent?.includes('Version 2'))
    const publishedRow = rows.find((row) => row.textContent?.includes('Version 1'))
    const statusOf = (row: HTMLElement | undefined): string | null =>
      row?.querySelector('[data-column="status"] .data-table__cell-value')?.textContent ?? null
    expect(statusOf(draftRow)).toBe('Draft')
    expect(statusOf(publishedRow)).toBe('Published')

    await expectNoAccessibilityViolations(container)
  })

  it('states an organisation with no version as a fact, not an error', async () => {
    transport.route(`GET ${TAX_VERSIONS}`, () => jsonResponse([]))

    renderTaxAt()

    expect(await screen.findByText('No tax configuration version yet')).toBeInTheDocument()
  })

  it('refuses a caller holding no billing permission before any request is made', async () => {
    renderTaxAt([])

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(transport.callsTo(`GET ${TAX_VERSIONS}`)).toHaveLength(0)
  })
})
