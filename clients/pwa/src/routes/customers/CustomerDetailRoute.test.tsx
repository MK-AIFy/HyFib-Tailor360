import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import {
  aCustomer,
  aTimelineEntry,
  aTimelinePage,
  versionedResponse,
} from '../../customers/testing/fixtures'
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

/**
 * A control that moves to another customer, so a test can do what a person does: open one record and
 * then another. `MemoryRouter` reads `initialEntries` on mount alone, so re-rendering it with a new
 * address changes nothing — the navigation has to happen inside the router that is already there,
 * which is also the only version of this that exercises what React Router actually does to the route
 * element when the parameter changes: it re-renders it in place rather than remounting it.
 */
function GoToCustomer({ customerId }: { readonly customerId: string }) {
  const navigate = useNavigate()
  return (
    <button
      onClick={() => {
        void navigate(`/customers/${customerId}`)
      }}
      type="button"
    >
      open the other customer
    </button>
  )
}

function renderDetail(alsoRender?: React.ReactNode) {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[`/customers/${CUSTOMER_ID}`]}>
          {alsoRender}
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

// The record is a pane as of #616, and the pane's own layout owns going back: `MasterDetail` offers
// "Back to the list" when the two cannot both be on screen, and nothing when they can. A second back
// control here put two of them on top of each other on a phone.
it('has no back link of its own, because the layout owns going back', async () => {
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer(), 'W/"1"'),
  )
  renderDetail()

  await screen.findByRole('heading', { name: 'Priya Selvam' })

  expect(screen.queryByRole('link', { name: 'Back to customers' })).not.toBeInTheDocument()
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

it('offers the record and its history as two tabs, with the record first', async () => {
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer(), 'W/"1"'),
  )
  renderDetail()

  await screen.findByRole('heading', { name: 'Priya Selvam' })

  expect(screen.getByRole('tab', { name: 'Details', selected: true })).toBeInTheDocument()
  expect(screen.getByRole('tab', { name: 'History', selected: false })).toBeInTheDocument()
})

// `Tabs` mounts only the selected panel, and that is load-bearing here rather than incidental: the
// history is a separate request across every module that holds part of it, and making it on a screen
// somebody opened to check a telephone number would be a cost paid by everybody for a few people.
it('does not ask for the history until the history tab is opened', async () => {
  const timeline = `GET /api/v1/customers/${CUSTOMER_ID}/timeline`
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer(), 'W/"1"'),
  )
  transport.route(timeline, () => jsonResponse(aTimelinePage()))
  renderDetail()

  await screen.findByRole('heading', { name: 'Priya Selvam' })
  expect(transport.callsTo(timeline)).toHaveLength(0)

  await userEvent.click(screen.getByRole('tab', { name: 'History' }))

  expect(await screen.findByText('Customer record corrected')).toBeInTheDocument()
  expect(transport.callsTo(timeline)).toHaveLength(1)
  // And the person's name stays above the tabs, because it is what the screen is about.
  expect(screen.getByRole('heading', { name: 'Priya Selvam' })).toBeInTheDocument()
})

// React Router re-renders this route in place when the identifier changes rather than remounting it,
// so without a key on the panel the previous customer's followed pages would still be on screen
// under this customer's name — which is the worst kind of wrong answer an audit trail can give.
it('starts a fresh history when the record changes underneath it', async () => {
  const SECOND = '0199cc00-0000-7000-8000-000000000002'
  const firstTimeline = `GET /api/v1/customers/${CUSTOMER_ID}/timeline`
  const secondTimeline = `GET /api/v1/customers/${SECOND}/timeline`

  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer(), 'W/"1"'),
  )
  transport.route(`GET /api/v1/customers/${SECOND}`, () =>
    versionedResponse(aCustomer({ customerId: SECOND, displayName: 'Anitha K' }), 'W/"1"'),
  )
  // The first customer's history must have a page *followed* before the move, because the state that
  // would leak is the followed pages — a test that only opens the tab proves nothing.
  transport.route(firstTimeline, () => jsonResponse(aTimelinePage({ nextCursor: 'cursor-2' })))
  transport.route(`${firstTimeline}?cursor=cursor-2`, () =>
    jsonResponse(
      aTimelinePage({
        entries: [
          aTimelineEntry({
            entryId: '0199cc00-0000-7000-8000-00000000f009',
            title: 'Priya was registered',
          }),
        ],
      }),
    ),
  )
  transport.route(secondTimeline, () =>
    jsonResponse(
      aTimelinePage({
        entries: [
          aTimelineEntry({
            entryId: '0199cc00-0000-7000-8000-00000000f001',
            title: 'Customer registered',
          }),
        ],
      }),
    ),
  )

  renderDetail(<GoToCustomer customerId={SECOND} />)
  await screen.findByRole('heading', { name: 'Priya Selvam' })
  await userEvent.click(screen.getByRole('tab', { name: 'History' }))
  await screen.findByText('Customer record corrected')
  await userEvent.click(screen.getByRole('button', { name: 'Show older' }))
  await screen.findByText('Priya was registered')

  await userEvent.click(screen.getByRole('button', { name: 'open the other customer' }))

  expect(await screen.findByRole('heading', { name: 'Anitha K' })).toBeInTheDocument()
  await userEvent.click(screen.getByRole('tab', { name: 'History' }))

  expect(await screen.findByText('Customer registered')).toBeInTheDocument()
  expect(screen.queryByText('Customer record corrected')).not.toBeInTheDocument()
  // The followed page is what would leak, so it is what this asserts is gone.
  expect(screen.queryByText('Priya was registered')).not.toBeInTheDocument()
})

/*
 * The same in-place re-render, for the command surface rather than the history.
 *
 * What would leak here is the retry key. It is held so that pressing confirm again after a timeout
 * replays the request rather than issuing a second one — correct within one record, and wrong the
 * moment the record underneath changes: the next person deactivated with the same reason text would
 * be sent under the previous person's key, and a server honouring that key is entitled to answer
 * with the outcome it already stored for somebody else.
 */
it("does not carry one record's retry key over to the next record", async () => {
  const SECOND = '0199cc00-0000-7000-8000-000000000002'
  const REASON = 'Created in error'
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: ['customers.read', 'customers.deactivate'] })),
  )
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer(), 'W/"1"'),
  )
  transport.route(`GET /api/v1/customers/${SECOND}`, () =>
    versionedResponse(aCustomer({ customerId: SECOND, displayName: 'Anitha K' }), 'W/"1"'),
  )
  // The first command has to *fail*, because a succeeded one clears the key on its own. A key only
  // survives when there is something left to retry.
  const first = `POST /api/v1/customers/${CUSTOMER_ID}/deactivate`
  const second = `POST /api/v1/customers/${SECOND}/deactivate`
  transport.route(first, () => problemResponse(503, 'platform.unavailable'))
  transport.route(second, () => versionedResponse(aCustomer({ customerId: SECOND }), 'W/"2"'))

  const deactivate = async (reason: string) => {
    await userEvent.click(screen.getByRole('button', { name: 'Deactivate this record' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), reason)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Deactivate this record' }))
  }

  renderDetail(<GoToCustomer customerId={SECOND} />)
  await screen.findByRole('heading', { name: 'Priya Selvam' })
  await deactivate(REASON)
  await waitFor(() => {
    expect(transport.callsTo(first)).toHaveLength(1)
  })

  await userEvent.click(screen.getByRole('button', { name: 'open the other customer' }))
  await screen.findByRole('heading', { name: 'Anitha K' })
  // The same words, which is exactly the case that used to collide.
  await deactivate(REASON)
  await waitFor(() => {
    expect(transport.callsTo(second)).toHaveLength(1)
  })

  expect(transport.callsTo(second)[0]?.headers.get('Idempotency-Key')).not.toBe(
    transport.callsTo(first)[0]?.headers.get('Idempotency-Key'),
  )
})

it('has no accessibility violations', async () => {
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer(), 'W/"1"'),
  )
  const { container } = renderDetail()

  await screen.findByRole('heading', { name: 'Priya Selvam' })

  await expectNoAccessibilityViolations(container)
})
