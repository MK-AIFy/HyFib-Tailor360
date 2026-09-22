import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { aCustomer, anExportReceipt, versionedResponse } from '../../customers/testing/fixtures'
import { CUSTOMER_EXPORT_EXPIRED_CODE } from '../../customers/types'
import { CustomerExportRoute } from './CustomerExportRoute'

const CUSTOMER_ID = '0199cc00-0000-7000-8000-000000000001'
const EXPORT_ID = '0199cc00-0000-7000-8000-00000000e501'
const READ = `GET /api/v1/customers/${CUSTOMER_ID}`
const GENERATE = `POST /api/v1/customers/${CUSTOMER_ID}/export`
const DOWNLOAD = `GET /api/v1/customers/${CUSTOMER_ID}/exports/${EXPORT_ID}`

let transport: FetchStub
let saved: { readonly name: string; readonly type: string } | null

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  saved = null
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: ['customers.read', 'customers.export'] })),
  )
  transport.route(READ, () => versionedResponse(aCustomer(), 'W/"1"'))

  // jsdom has neither an object-URL factory nor a real download, so the anchor `saveBlob` clicks is
  // captured instead. What matters here is the *name* it would have written to disk.
  vi.stubGlobal('URL', {
    ...URL,
    createObjectURL: () => 'blob:stub',
    revokeObjectURL: () => undefined,
  })
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
    this: HTMLAnchorElement,
  ) {
    saved = { name: this.download, type: 'anchor' }
  })
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderExport() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[`/customers/${CUSTOMER_ID}/export`]}>
          <Routes>
            <Route path="/customers/:customerId" element={<p>the record</p>} />
            <Route path="/customers/:customerId/export" element={<CustomerExportRoute />} />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/**
 * Opens the confirmation, gives the reason the endpoint requires, and generates.
 *
 * Scoped to the dialog, because the page's own control carries the same words: `ConfirmDialog`
 * requires a confirming label that says what it does rather than "OK", and what it does here is the
 * same thing the page said it would. The page is inert behind the backdrop, so only one of the two
 * is reachable — but a query that is not scoped would still see both.
 */
async function generate(reason = 'Subject access request received 21 September') {
  await userEvent.click(await screen.findByRole('button', { name: 'Generate the copy' }))
  const dialog = await screen.findByRole('dialog')
  await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), reason)
  await userEvent.click(within(dialog).getByRole('button', { name: 'Generate the copy' }))
}

it('says what the copy holds and, just as plainly, what it does not', async () => {
  renderExport()

  await screen.findByRole('heading', { name: 'Export Priya Selvam’s data' })

  expect(screen.getByText(/Every answer she has given about consent/)).toBeInTheDocument()
  // Somebody handing this to a customer is answering for its completeness.
  expect(
    screen.getByText(/does not hold images, duplicate scores or merge reasons/),
  ).toBeInTheDocument()
  expect(screen.getByText(/holds no measurements/)).toBeInTheDocument()
})

it('warns that generating stops an earlier copy working, before it does it', async () => {
  renderExport()
  await userEvent.click(await screen.findByRole('button', { name: 'Generate the copy' }))

  expect(
    await screen.findByText(/a download you have already given somebody will stop working/),
  ).toBeInTheDocument()
})

it('sends the reason the audit trail keeps, and a retry key', async () => {
  transport.route(GENERATE, () => jsonResponse(anExportReceipt(), 201))
  renderExport()
  await generate()

  await waitFor(() => {
    expect(transport.callsTo(GENERATE)).toHaveLength(1)
  })
  const [sent] = transport.callsTo(GENERATE)
  expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
  expect(sent?.body).toEqual({ reason: 'Subject access request received 21 September' })
})

it('will not generate without a reason', async () => {
  renderExport()
  await userEvent.click(await screen.findByRole('button', { name: 'Generate the copy' }))
  const dialog = await screen.findByRole('dialog')
  await userEvent.click(within(dialog).getByRole('button', { name: 'Generate the copy' }))

  expect(transport.callsTo(GENERATE)).toHaveLength(0)
})

it('reports when the copy stops working, and how many it replaced', async () => {
  transport.route(GENERATE, () => jsonResponse(anExportReceipt({ supersededCount: 1 }), 201))
  renderExport()
  await generate()

  expect(await screen.findByText('The copy is ready')).toBeInTheDocument()
  expect(screen.getByText('Stops working at')).toBeInTheDocument()
  expect(
    screen.getByText('1 earlier copy stopped working when this one was made.'),
  ).toBeInTheDocument()
})

// Rule 9: the document is streamed by an endpoint that re-authorises and audits the read. A link
// somebody could copy out of the address bar would do neither.
it('fetches the document through the transport rather than linking to it', async () => {
  transport.route(GENERATE, () => jsonResponse(anExportReceipt(), 201))
  transport.route(DOWNLOAD, () => jsonResponse({ profile: {} }))
  renderExport()
  await generate()

  await userEvent.click(await screen.findByRole('button', { name: 'Download the copy' }))

  await waitFor(() => {
    expect(transport.callsTo(DOWNLOAD)).toHaveLength(1)
  })
  // No anchor pointing at the API: the only href on the screen goes back to the record.
  const hrefs = screen.queryAllByRole('link').map((link) => link.getAttribute('href'))
  expect(hrefs.some((href) => href?.includes('/exports/') === true)).toBe(false)
})

// Rule 8: personal data is never an identifier — not a filename. A customer's name in a downloads
// folder is personal data in a place nobody is auditing.
it('saves the file under the export’s identity, never under the customer’s name', async () => {
  transport.route(GENERATE, () => jsonResponse(anExportReceipt(), 201))
  transport.route(DOWNLOAD, () => jsonResponse({ profile: {} }))
  renderExport()
  await generate()
  await userEvent.click(await screen.findByRole('button', { name: 'Download the copy' }))

  await waitFor(() => {
    expect(saved).not.toBeNull()
  })
  expect(saved?.name).toBe(`CUSTOMER-EXPORT-${EXPORT_ID}.json`)
  expect(saved?.name).not.toMatch(/priya/i)
  expect(saved?.name).not.toContain('C-000123')
})

it('says the copy has gone when the download finds it expired or replaced', async () => {
  transport.route(GENERATE, () => jsonResponse(anExportReceipt(), 201))
  transport.route(DOWNLOAD, () => problemResponse(404, CUSTOMER_EXPORT_EXPIRED_CODE))
  renderExport()
  await generate()
  await userEvent.click(await screen.findByRole('button', { name: 'Download the copy' }))

  expect(await screen.findByText('That copy has gone')).toBeInTheDocument()
  // And it says what survives: the record of the export, not the data.
  expect(screen.getByText(/only the copy of the data is destroyed/)).toBeInTheDocument()
})

it('shows an empty state for a record that cannot be reached', async () => {
  transport.route(READ, () => problemResponse(404, 'customers.customer-not-found'))
  renderExport()

  expect(
    await screen.findByText('This record could not be found, or is not one you can reach.'),
  ).toBeInTheDocument()
})

it('has no accessibility violations', async () => {
  transport.route(GENERATE, () => jsonResponse(anExportReceipt(), 201))
  const { container } = renderExport()
  await generate()
  await screen.findByText('The copy is ready')

  await expectNoAccessibilityViolations(container)
})
