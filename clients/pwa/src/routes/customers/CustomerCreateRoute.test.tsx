import { render, screen } from '@testing-library/react'
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
import { aCustomer, aDuplicateCandidate, versionedResponse } from '../../customers/testing/fixtures'
import { CUSTOMER_DUPLICATES_CODE } from '../../customers/types'
import { CustomerCreateRoute } from './CustomerCreateRoute'

let transport: FetchStub

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: ['customers.read', 'customers.create'] })),
  )
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderCreate() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/customers/new']}>
          <Routes>
            <Route path="/customers/new" element={<CustomerCreateRoute />} />
            <Route path="/customers/:customerId" element={<p>the customer's own record</p>} />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

// Positioned first: useNetworkState reads navigator.onLine into a snapshot shared by the rest of
// this file, and spying on it only takes effect before anything has read it yet.
it('offline, blocks registration and sends no request', async () => {
  vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)

  renderCreate()

  expect(await screen.findByText(/needs a connection/)).toBeInTheDocument()
  expect(transport.callsTo('POST /api/v1/customers/')).toHaveLength(0)

  vi.restoreAllMocks()
  window.dispatchEvent(new Event('online'))
})

it('refuses to submit with no name, and asks the server nothing', async () => {
  const user = userEvent.setup()
  renderCreate()

  await user.click(await screen.findByRole('button', { name: 'Register' }))

  expect(await screen.findByRole('alert')).toBeInTheDocument()
  expect(transport.callsTo('POST /api/v1/customers/')).toHaveLength(0)
})

it('registers a customer and opens the record it created', async () => {
  const user = userEvent.setup()
  transport.route('POST /api/v1/customers/', () => versionedResponse(aCustomer(), 'W/"1"', 201))
  renderCreate()

  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Priya Selvam')
  await user.click(screen.getByRole('button', { name: 'Register' }))

  expect(await screen.findByText("the customer's own record")).toBeInTheDocument()

  const [call] = transport.callsTo('POST /api/v1/customers/')
  expect(call?.body).toMatchObject({ displayName: 'Priya Selvam', duplicatesReviewed: false })
  expect(call?.headers.get('Idempotency-Key')).not.toBeNull()
})

it('shows why each candidate matched, and lets the person register anyway with a new decision', async () => {
  const user = userEvent.setup()
  const candidate = aDuplicateCandidate({ reasons: ['Same telephone number', 'Same name'] })

  transport.route('POST /api/v1/customers/', (call) => {
    const body = call.body as { readonly duplicatesReviewed?: boolean }
    return body.duplicatesReviewed === true
      ? versionedResponse(aCustomer(), 'W/"1"', 201)
      : problemResponse(409, CUSTOMER_DUPLICATES_CODE, {
          candidates: [candidate],
        })
  })
  const { container } = renderCreate()

  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Priya Selvam')
  await user.type(screen.getByRole('textbox', { name: 'Telephone number' }), '+919000000001')
  await user.click(screen.getByRole('button', { name: 'Register' }))

  expect(await screen.findByText('This may be somebody we already know')).toBeInTheDocument()
  expect(screen.getByText('Same telephone number')).toBeInTheDocument()
  expect(screen.getByText('Same name')).toBeInTheDocument()
  expect(screen.getByText('Strong match')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Open this record instead' })).toHaveAttribute(
    'href',
    `/customers/${candidate.customer.customerId}`,
  )

  // The warning alert's own state, not just the blank form the file's other axe test covers — a
  // real 1.27:1 contrast failure and a skipped heading level were both found only by checking here.
  await expectNoAccessibilityViolations(container)

  const firstAttempt = transport.callsTo('POST /api/v1/customers/')[0]

  await user.click(screen.getByRole('button', { name: "I've checked — this is somebody new" }))

  expect(await screen.findByText("the customer's own record")).toBeInTheDocument()

  const [, secondAttempt] = transport.callsTo('POST /api/v1/customers/')
  expect(secondAttempt?.body).toMatchObject({ duplicatesReviewed: true })
  // A different decision, not a retry of the refused one: reusing the first key here would ask the
  // server to treat "create despite the match" as the same request as the one it just refused.
  expect(secondAttempt?.headers.get('Idempotency-Key')).not.toBe(
    firstAttempt?.headers.get('Idempotency-Key'),
  )
})

it('drops the shown candidates once a field changes, because they no longer match what would be sent', async () => {
  const user = userEvent.setup()
  transport.route('POST /api/v1/customers/', () =>
    problemResponse(409, CUSTOMER_DUPLICATES_CODE, {
      candidates: [aDuplicateCandidate()],
    }),
  )
  renderCreate()

  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Priya Selvam')
  await user.click(screen.getByRole('button', { name: 'Register' }))

  expect(await screen.findByText('This may be somebody we already know')).toBeInTheDocument()

  await user.type(screen.getByRole('textbox', { name: 'Name' }), ' Junior')

  expect(screen.queryByText('This may be somebody we already know')).not.toBeInTheDocument()
})

it('explains a server refusal that is not the duplicate question', async () => {
  const user = userEvent.setup()
  transport.route('POST /api/v1/customers/', () =>
    problemResponse(403, 'security.permission-denied'),
  )
  renderCreate()

  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Priya Selvam')
  await user.click(screen.getByRole('button', { name: 'Register' }))

  expect(await screen.findByRole('alert')).toBeInTheDocument()
  expect(screen.queryByText('This may be somebody we already know')).not.toBeInTheDocument()
})

it('has no accessibility violations', async () => {
  const { container } = renderCreate()

  await screen.findByRole('textbox', { name: 'Name' })

  await expectNoAccessibilityViolations(container)
})
