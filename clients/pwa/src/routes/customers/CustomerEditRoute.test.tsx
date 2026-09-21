import { render, screen, waitFor } from '@testing-library/react'
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
import { aCustomer, versionedResponse } from '../../customers/testing/fixtures'
import { CUSTOMER_VERSION_CONFLICT_CODE } from '../../customers/types'
import { CustomerEditRoute } from './CustomerEditRoute'

const CUSTOMER_ID = '0199cc00-0000-7000-8000-000000000001'
const READ = `GET /api/v1/customers/${CUSTOMER_ID}`
const CORRECT = `PUT /api/v1/customers/${CUSTOMER_ID}`

let transport: FetchStub

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: ['customers.read', 'customers.update'] })),
  )
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderEdit() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[`/customers/${CUSTOMER_ID}/edit`]}>
          <Routes>
            <Route path="/customers/:customerId" element={<p>the record</p>} />
            <Route path="/customers/:customerId/edit" element={<CustomerEditRoute />} />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/** The form, once it has the record. Every test starts here. */
async function aLoadedForm() {
  renderEdit()
  return await screen.findByRole('textbox', { name: 'Name' })
}

it('starts from what the record says, so a correction is an edit and not a retyping', async () => {
  transport.route(READ, () =>
    versionedResponse(aCustomer({ nativeName: 'ப்ரியா', email: 'priya@example.invalid' }), 'W/"7"'),
  )

  const name = await aLoadedForm()

  expect(name).toHaveValue('Priya Selvam')
  expect(screen.getByRole('textbox', { name: 'Name (native script)' })).toHaveValue('ப்ரியா')
  expect(screen.getByRole('textbox', { name: 'Email address' })).toHaveValue(
    'priya@example.invalid',
  )
  expect(screen.getByRole('textbox', { name: 'Telephone number' })).toHaveValue('+919000000001')
})

it('sends the whole record against the version it was read at, with the reason', async () => {
  transport.route(READ, () => versionedResponse(aCustomer(), 'W/"7"'))
  transport.route(CORRECT, () => versionedResponse(aCustomer({ displayName: 'Priya S' }), 'W/"8"'))

  const name = await aLoadedForm()
  await userEvent.clear(name)
  await userEvent.type(name, 'Priya S')
  await userEvent.type(
    screen.getByRole('textbox', { name: 'Why this correction' }),
    'Spelling on her identity document',
  )
  await userEvent.click(screen.getByRole('button', { name: 'Save the correction' }))

  await screen.findByText('The correction was saved.')

  const [sent] = transport.callsTo(CORRECT)
  expect(sent?.headers.get('If-Match')).toBe('W/"7"')
  expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
  expect(sent?.body).toMatchObject({
    displayName: 'Priya S',
    // Unchanged fields travel too: the endpoint is whole-record, so omitting one clears it.
    phone: '+919000000001',
    language: 'en-IN',
    reason: 'Spelling on her identity document',
  })
})

// Both validation tests assert the *field* is marked, not just that the save was withheld. A screen
// that silently does nothing on a failed submit is the failure mode these replaced: the endpoint
// records a reason against every correction, so an empty one has to say which box is empty.
it('will not save without a reason, and says so on the reason field', async () => {
  transport.route(READ, () => versionedResponse(aCustomer(), 'W/"7"'))

  const name = await aLoadedForm()
  await userEvent.clear(name)
  await userEvent.type(name, 'Priya S')
  await userEvent.click(screen.getByRole('button', { name: 'Save the correction' }))

  expect(transport.callsTo(CORRECT)).toHaveLength(0)

  const reason = screen.getByRole('textbox', { name: 'Why this correction' })
  expect(reason).toHaveAttribute('aria-invalid', 'true')
  expect(reason).toHaveAccessibleDescription(/Say why this record is being corrected/)
  // And the same sentence is listed at the top of the form, as the control that takes focus — the
  // summary's entries are buttons, because they move focus rather than navigate.
  expect(
    screen.getByRole('button', { name: 'Say why this record is being corrected.' }),
  ).toBeInTheDocument()
})

it('will not save a record with no name at all, and says so on the name field', async () => {
  transport.route(READ, () => versionedResponse(aCustomer(), 'W/"7"'))

  const name = await aLoadedForm()
  await userEvent.clear(name)
  await userEvent.type(screen.getByRole('textbox', { name: 'Why this correction' }), 'A reason')
  await userEvent.click(screen.getByRole('button', { name: 'Save the correction' }))

  expect(transport.callsTo(CORRECT)).toHaveLength(0)
  expect(screen.getByRole('textbox', { name: 'Name' })).toHaveAttribute('aria-invalid', 'true')
  expect(
    screen.getByRole('button', { name: 'A customer record must have a name.' }),
  ).toBeInTheDocument()
})

it('offers a reload on a stale version, keeps what was typed, and saves against the new one', async () => {
  let version = 'W/"7"'
  transport.route(READ, () => versionedResponse(aCustomer(), version))
  transport.route(CORRECT, (call) =>
    call.headers.get('If-Match') === 'W/"9"'
      ? versionedResponse(aCustomer({ displayName: 'Priya S' }), 'W/"10"')
      : problemResponse(409, CUSTOMER_VERSION_CONFLICT_CODE),
  )

  const name = await aLoadedForm()
  await userEvent.clear(name)
  await userEvent.type(name, 'Priya S')
  await userEvent.type(screen.getByRole('textbox', { name: 'Why this correction' }), 'A reason')
  await userEvent.click(screen.getByRole('button', { name: 'Save the correction' }))

  expect(await screen.findByText('Somebody else changed this record')).toBeInTheDocument()

  // A colleague's save is what the next read answers with.
  version = 'W/"9"'
  await userEvent.click(screen.getByRole('button', { name: 'Reload the record' }))

  // The name being corrected is still the one that was typed — the reload replaces the version, not
  // the person's work. This is the assertion the "never discards typed input" rule comes down to.
  await waitFor(() => {
    expect(transport.callsTo(READ).length).toBeGreaterThan(1)
  })
  expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('Priya S')

  await userEvent.type(screen.getByRole('textbox', { name: 'Why this correction' }), 'A reason')
  await userEvent.click(screen.getByRole('button', { name: 'Save the correction' }))

  await screen.findByText('The correction was saved.')
  expect(transport.callsTo(CORRECT)).toHaveLength(2)
  expect(transport.callsTo(CORRECT)[1]?.headers.get('If-Match')).toBe('W/"9"')
})

it('renders a refusal that is not a version conflict as itself, not as the conflict', async () => {
  transport.route(READ, () => versionedResponse(aCustomer(), 'W/"7"'))
  transport.route(CORRECT, () => problemResponse(403, 'security.permission-denied'))

  await aLoadedForm()
  await userEvent.type(screen.getByRole('textbox', { name: 'Why this correction' }), 'A reason')
  await userEvent.click(screen.getByRole('button', { name: 'Save the correction' }))

  await waitFor(() => {
    expect(transport.callsTo(CORRECT)).toHaveLength(1)
  })
  expect(screen.queryByText('Somebody else changed this record')).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Reload the record' })).not.toBeInTheDocument()
})

it('offers no form at all when the contact fields were withheld from the caller', async () => {
  transport.route(READ, () =>
    versionedResponse(aCustomer({ contactIncluded: false, phone: null }), 'W/"7"'),
  )
  renderEdit()

  // A whole-record PUT built from a record with its contact withheld would ask the server to clear
  // the customer's telephone number, so the screen explains instead of offering a doomed save.
  expect(await screen.findByText(/permission you do not have/)).toBeInTheDocument()
  expect(screen.queryByRole('textbox', { name: 'Name' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Save the correction' })).not.toBeInTheDocument()
})

it('shows an empty state for a record that does not exist, or is not one the caller can reach', async () => {
  transport.route(READ, () => problemResponse(404, 'customers.customer-not-found'))
  renderEdit()

  expect(
    await screen.findByText('This record could not be found, or is not one you can reach.'),
  ).toBeInTheDocument()
})

it('links back to the record it is correcting', async () => {
  transport.route(READ, () => versionedResponse(aCustomer(), 'W/"7"'))
  await aLoadedForm()

  expect(screen.getByRole('link', { name: 'Back to the record' })).toHaveAttribute(
    'href',
    `/customers/${CUSTOMER_ID}`,
  )
})

it('has no accessibility violations', async () => {
  transport.route(READ, () => versionedResponse(aCustomer(), 'W/"7"'))
  const { container } = renderEdit()
  await screen.findByRole('textbox', { name: 'Name' })

  await expectNoAccessibilityViolations(container)
})
