import { render, screen, waitFor } from '@testing-library/react'
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
import { ShellStatusProvider } from '../../components/layout/ShellStatusProvider'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import {
  INVOICE_ID,
  aBarcodeResolution,
  anInvoice,
  anInvoicePage,
  anInvoiceSummary,
} from '../../billing/testing/fixtures'
import { InvoiceRegisterRoute } from './InvoiceRegisterRoute'
import { InvoiceDetailRoute } from './InvoiceDetailRoute'

let transport: FetchStub

const INVOICES = '/api/v1/billing/invoices'
const barcodeUrl = (payload: string) => `/api/v1/billing/barcodes/${payload}`

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [BILLING_PERMISSIONS.createInvoice] })),
  )
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderAt(
  path: string,
  permissions: readonly string[] = [BILLING_PERMISSIONS.createInvoice],
) {
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions })))

  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <ShellStatusProvider>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route element={<RequireSession />}>
                <Route
                  element={
                    <RequirePermission permission={BILLING_PERMISSIONS.createInvoice}>
                      <InvoiceRegisterRoute />
                    </RequirePermission>
                  }
                  path="/billing/invoices"
                />
                <Route
                  element={
                    <RequirePermission permission={BILLING_PERMISSIONS.createInvoice}>
                      <InvoiceDetailRoute />
                    </RequirePermission>
                  }
                  path="/billing/invoices/:invoiceId"
                />
              </Route>
            </Routes>
          </MemoryRouter>
        </ShellStatusProvider>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

describe('the invoice register', () => {
  it('shows a page of invoices, filters to Posted, and appends the second page on Show more', async () => {
    const user = userEvent.setup()
    const first = anInvoiceSummary({ orderNumber: 'O-CBE01-2627-000512' })
    const second = anInvoiceSummary({
      invoiceId: '0199dd00-0000-7000-8000-000000006001',
      orderNumber: 'O-CBE01-2627-000900',
      invoiceNumber: 'INV-CBE01-2627-000900',
    })

    transport.route(`GET ${INVOICES}?limit=20`, () =>
      jsonResponse(anInvoicePage({ invoices: [first], nextCursor: 'cursor-2' })),
    )
    transport.route(`GET ${INVOICES}?status=Posted&limit=20`, () =>
      jsonResponse(anInvoicePage({ invoices: [first], nextCursor: 'cursor-2' })),
    )
    transport.route(`GET ${INVOICES}?status=Posted&cursor=cursor-2&limit=20`, () =>
      jsonResponse(anInvoicePage({ invoices: [second], nextCursor: null })),
    )

    renderAt('/billing/invoices')

    await screen.findByText('I-CBE01-2627-000731')

    await user.selectOptions(screen.getByLabelText('Status'), 'Posted')
    await screen.findByText('Status: Posted')

    await user.click(await screen.findByRole('button', { name: 'Show more' }))

    await waitFor(() => {
      expect(screen.getByText('INV-CBE01-2627-000900')).toBeInTheDocument()
    })
    // The first page's row is still there: Show more appends rather than replaces.
    expect(screen.getByText('I-CBE01-2627-000731')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
  })

  it('shows the empty state naming a filter when a status matches nothing', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${INVOICES}?limit=20`, () => jsonResponse(anInvoicePage({ invoices: [] })))
    transport.route(`GET ${INVOICES}?status=Discarded&limit=20`, () =>
      jsonResponse(anInvoicePage({ invoices: [] })),
    )

    renderAt('/billing/invoices')

    await screen.findByText('No invoices at this branch yet')

    await user.selectOptions(screen.getByLabelText('Status'), 'Discarded')

    expect(await screen.findByText('No invoices match this filter')).toBeInTheDocument()
  })

  it('navigates to the resolved invoice when a valid barcode is entered', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${INVOICES}?limit=20`, () => jsonResponse(anInvoicePage({ invoices: [] })))
    transport.route(`GET ${barcodeUrl('I-7K3M9QW2XZ4B')}`, () => jsonResponse(aBarcodeResolution()))
    transport.route(`GET /api/v1/billing/invoices/${INVOICE_ID}`, () => jsonResponse(anInvoice()))

    renderAt('/billing/invoices')

    await screen.findByText('No invoices at this branch yet')
    await user.type(screen.getByLabelText('Barcode'), 'I-7K3M9QW2XZ4B')
    await user.click(screen.getByRole('button', { name: 'Find' }))

    expect(
      await screen.findByRole('heading', { name: 'INV-CBE01-2627-000731' }),
    ).toBeInTheDocument()
  })

  it('renders the identical sentence for every case the barcode route answers as document-not-found', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${INVOICES}?limit=20`, () => jsonResponse(anInvoicePage({ invoices: [] })))
    transport.route(`GET ${barcodeUrl('I-NOTHING000000')}`, () =>
      problemResponse(404, 'billing.document-not-found'),
    )

    renderAt('/billing/invoices')

    await screen.findByText('No invoices at this branch yet')
    await user.type(screen.getByLabelText('Barcode'), 'I-NOTHING000000')
    await user.click(screen.getByRole('button', { name: 'Find' }))

    expect(await screen.findByText('No invoice matches that barcode.')).toBeInTheDocument()
    expect(screen.queryByText('billing.document-not-found')).not.toBeInTheDocument()
  })

  it('renders a distinct sentence when the session carries no branch, not the not-found sentence', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${INVOICES}?limit=20`, () => jsonResponse(anInvoicePage({ invoices: [] })))
    transport.route(`GET ${barcodeUrl('I-7K3M9QW2XZ4B')}`, () =>
      problemResponse(400, 'billing.value-required', {
        errors: { branch: ['A required value was not supplied.'] },
      }),
    )

    renderAt('/billing/invoices')

    await screen.findByText('No invoices at this branch yet')
    await user.type(screen.getByLabelText('Barcode'), 'I-7K3M9QW2XZ4B')
    await user.click(screen.getByRole('button', { name: 'Find' }))

    expect(
      await screen.findByText('Your session needs a branch before a barcode can be looked up.'),
    ).toBeInTheDocument()
    expect(screen.queryByText('No invoice matches that barcode.')).not.toBeInTheDocument()
  })

  it('refuses a caller holding no billing permission before any request is made', async () => {
    renderAt('/billing/invoices', [])

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(transport.callsTo(`GET ${INVOICES}?limit=20`)).toHaveLength(0)
  })

  it('blocks Show more and the lookup while offline, without queuing anything', async () => {
    transport.route(`GET ${INVOICES}?limit=20`, () =>
      jsonResponse(anInvoicePage({ invoices: [anInvoiceSummary()], nextCursor: 'cursor-2' })),
    )

    renderAt('/billing/invoices')
    await screen.findByText('I-CBE01-2627-000731')

    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    expect(await screen.findAllByText('Needs connection — this will not be queued')).toHaveLength(2)
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Find' })).not.toBeInTheDocument()

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })

  it('has no accessibility violations', async () => {
    transport.route(`GET ${INVOICES}?limit=20`, () =>
      jsonResponse(anInvoicePage({ invoices: [anInvoiceSummary()] })),
    )
    const { container } = renderAt('/billing/invoices')
    await screen.findByText('I-CBE01-2627-000731')
    await expectNoAccessibilityViolations(container)
  })
})
