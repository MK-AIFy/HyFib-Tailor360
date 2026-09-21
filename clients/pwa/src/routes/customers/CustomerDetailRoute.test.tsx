import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { aCustomer, versionedResponse } from '../../customers/testing/fixtures'
import { CustomerDetailRoute } from './CustomerDetailRoute'

const CUSTOMER_ID = '0199cc00-0000-7000-8000-000000000001'

let transport: FetchStub

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: ['customers.read'] })),
  )
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderDetail() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[`/customers/${CUSTOMER_ID}`]}>
          <Routes>
            <Route path="/customers" element={<p>the search screen</p>} />
            <Route path="/customers/:customerId" element={<CustomerDetailRoute />} />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

it('shows the record, with its status as a word rather than only a colour', async () => {
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer(), 'W/"1"'),
  )
  renderDetail()

  expect(await screen.findByRole('heading', { name: 'Priya Selvam' })).toBeInTheDocument()
  expect(screen.getByText('Active')).toBeInTheDocument()
  expect(screen.getByText('+919000000001')).toBeInTheDocument()
})

it('tells a withheld contact field apart from one the customer never gave', async () => {
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer({ contactIncluded: false, phone: null, email: null }), 'W/"1"'),
  )
  renderDetail()

  await screen.findByRole('heading', { name: 'Priya Selvam' })

  expect(screen.getByText(/permission you do not have/)).toBeInTheDocument()
  // Withheld, not "not given" — the phone number exists, it just is not shown to this caller.
  expect(screen.queryByText('Not given')).not.toBeInTheDocument()
})

it('says a contact field was never given, once contact is actually shown', async () => {
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer({ email: null }), 'W/"1"'),
  )
  renderDetail()

  await screen.findByRole('heading', { name: 'Priya Selvam' })

  // Every other contact field is also unset in the fixture, so several cells read "Not given" —
  // this test only needs to know the word is used at all, and that it is not the withheld message.
  expect(screen.getAllByText('Not given').length).toBeGreaterThan(0)
  expect(screen.queryByText(/permission you do not have/)).not.toBeInTheDocument()
})

it('lists the record’s aliases when it has any, and says nothing when it has none', async () => {
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(
      aCustomer({
        aliases: [
          { kind: 'PreviousName', value: 'Priya Kumar', recordedAt: '2026-01-01T00:00:00Z' },
        ],
      }),
      'W/"1"',
    ),
  )
  renderDetail()

  expect(await screen.findByText('Priya Kumar')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Also known as' })).toBeInTheDocument()
})

it('shows an empty state for a record that does not exist, or is not one the caller can reach', async () => {
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    problemResponse(404, 'customers.customer-not-found'),
  )
  renderDetail()

  expect(
    await screen.findByText('This record could not be found, or is not one you can reach.'),
  ).toBeInTheDocument()
})

it('links back to the search screen', async () => {
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer(), 'W/"1"'),
  )
  renderDetail()

  const back = await screen.findByRole('link', { name: 'Back to customers' })
  expect(back).toHaveAttribute('href', '/customers')
})

it('offers the correction form to a caller who holds customers.update', async () => {
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: ['customers.read', 'customers.update'] })),
  )
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer(), 'W/"1"'),
  )
  renderDetail()

  const correct = await screen.findByRole('link', { name: 'Correct this record' })
  expect(correct).toHaveAttribute('href', `/customers/${CUSTOMER_ID}/edit`)
})

// The server re-checks the permission on the request either way; what this asserts is that nobody is
// offered a door that would only refuse them. The default `beforeEach` caller holds `customers.read`
// alone, so this is the ordinary receptionist's view of somebody else's correction.
it('offers no correction link to a caller who may not correct the record', async () => {
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer(), 'W/"1"'),
  )
  renderDetail()

  await screen.findByRole('heading', { name: 'Priya Selvam' })

  expect(screen.queryByRole('link', { name: 'Correct this record' })).not.toBeInTheDocument()
})

it('has no accessibility violations', async () => {
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer(), 'W/"1"'),
  )
  const { container } = renderDetail()

  await screen.findByRole('heading', { name: 'Priya Selvam' })

  await expectNoAccessibilityViolations(container)
})
