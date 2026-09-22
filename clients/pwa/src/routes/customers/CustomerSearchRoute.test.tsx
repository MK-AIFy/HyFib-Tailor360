import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { aCustomerCard } from '../../customers/testing/fixtures'
import { CustomerSearchRoute } from './CustomerSearchRoute'

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

function renderSearch(at = '/customers') {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[at]}>
          <CustomerSearchRoute />
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

it('refuses a search shorter than the server accepts, without asking the server', async () => {
  const user = userEvent.setup()
  renderSearch()

  await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'ab')
  await user.click(screen.getByRole('button', { name: 'Search' }))

  expect(await screen.findByText(/Type at least 3 characters/)).toBeInTheDocument()
  expect(transport.callsTo('GET /api/v1/customers/?term=ab')).toHaveLength(0)
})

/*
 * The case #182's criterion A is about: the refusal reads the same whether the field worked it out
 * or the server said it.
 *
 * Reachable in one realistic way — the client's minimum and the server's having drifted apart, which
 * is what the contract test in the .NET tier exists to prevent — and in one ordinary one: a pasted
 * or bookmarked address carrying a term the form itself would never have submitted. Either way it
 * belongs at the field the person has to change, not floating above the form as an unexplained
 * failure.
 */
it("shows the server's too-short refusal at the field, in the same words", async () => {
  transport.route('GET /api/v1/customers/?term=abc', () =>
    problemResponse(400, 'customers.search-term-too-short'),
  )
  renderSearch('/customers?term=abc')

  expect(await screen.findByText(/Type at least 3 characters/)).toBeInTheDocument()
  // Not also as a problem alert: one refusal, said once, where it can be acted on.
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('finds a customer and opens their record', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya', () =>
    jsonResponse({ customers: [aCustomerCard()], nextCursor: null }),
  )
  renderSearch()

  await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'priya')
  await user.click(screen.getByRole('button', { name: 'Search' }))

  const link = await screen.findByRole('link', { name: /Priya Selvam, C-000123/ })
  // The search travels with the link: this screen is the list pane of a master-detail layout, and a
  // bare address would empty the list beside the record it just opened (#616).
  expect(link).toHaveAttribute('href', '/customers/0199cc00-0000-7000-8000-000000000001?term=priya')
})

it('says a masked card is at another branch, and still offers to open it', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya', () =>
    jsonResponse({
      customers: [aCustomerCard({ visibleToCaller: false })],
      nextCursor: null,
    }),
  )
  renderSearch()

  await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'priya')
  await user.click(screen.getByRole('button', { name: 'Search' }))

  expect(await screen.findByText(/at another branch/)).toBeInTheDocument()
  expect(screen.getByText(/add your branch to its visibility/)).toBeInTheDocument()
})

it('shows an empty state, with the way out, when nobody matches', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=nobody', () =>
    jsonResponse({ customers: [], nextCursor: null }),
  )
  renderSearch()

  await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'nobody')
  await user.click(screen.getByRole('button', { name: 'Search' }))

  expect(await screen.findByText(/Nobody matched/)).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Register a new customer' })).toHaveAttribute(
    'href',
    '/customers/new',
  )
})

it('explains a refusal instead of showing a blank screen', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya', () =>
    problemResponse(403, 'security.permission-denied'),
  )
  renderSearch()

  await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'priya')
  await user.click(screen.getByRole('button', { name: 'Search' }))

  expect(await screen.findByRole('alert')).toBeInTheDocument()
})

it('offers to register a new customer even before anybody has searched', () => {
  renderSearch()

  expect(screen.getByRole('link', { name: 'Register a new customer' })).toBeInTheDocument()
})

/*
 * A withdrawn record drops out of an ordinary search, which is the point of withdrawing one — and
 * would make it a one-way door if there were no way to ask for them. This is what makes #618's
 * "put it back" reachable by somebody who did not keep the address.
 */
it('does not ask for deactivated records unless it is told to', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya', () =>
    jsonResponse({ customers: [aCustomerCard()], nextCursor: null }),
  )
  renderSearch()

  await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'priya')
  await user.click(screen.getByRole('button', { name: 'Search' }))

  await screen.findByRole('link', { name: /Priya Selvam/ })
  expect(transport.callsTo('GET /api/v1/customers/?term=priya')).toHaveLength(1)
})

it('asks for deactivated records when the filter is on, and finds one', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya&includeDeactivated=true', () =>
    jsonResponse({
      customers: [aCustomerCard({ status: 'Deactivated' })],
      nextCursor: null,
    }),
  )
  renderSearch()

  await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'priya')
  await user.click(screen.getByRole('checkbox', { name: 'Include deactivated records' }))
  await user.click(screen.getByRole('button', { name: 'Search' }))

  expect(await screen.findByRole('link', { name: /Priya Selvam/ })).toBeInTheDocument()
  // The card says which it is, in a word and not only a colour.
  // The badge's own word is `Closed` — the glossary's term is Deactivate, and #624 tracks the
  // disagreement. Asserted as it reads rather than as it ought to, so the test fails when it is fixed.
  expect(screen.getByText('Closed')).toBeInTheDocument()
})

// A ticked box above results fetched without it is the screen saying one thing and showing another,
// and the reading somebody takes from it is "she is not here" — the one conclusion the filter exists
// to prevent.
it('re-asks at once when the filter is turned on after a search', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya', () =>
    jsonResponse({ customers: [], nextCursor: null }),
  )
  transport.route('GET /api/v1/customers/?term=priya&includeDeactivated=true', () =>
    jsonResponse({ customers: [aCustomerCard({ status: 'Deactivated' })], nextCursor: null }),
  )
  renderSearch()

  await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'priya')
  await user.click(screen.getByRole('button', { name: 'Search' }))
  await screen.findByText('Nobody matched. Check the spelling, or register a new customer.')

  // No second press of Search.
  await user.click(screen.getByRole('checkbox', { name: 'Include deactivated records' }))

  expect(await screen.findByRole('link', { name: /Priya Selvam/ })).toBeInTheDocument()
})

/*
 * The trap the immediate re-ask opened if it re-asked the wrong thing.
 *
 * Search "priya", then type over it without pressing Search, then tick the filter. Re-running the
 * committed term would put results for "priya" under a box reading "anitha" — the same "screen says
 * one thing, shows another" failure, and harder to spot, because the box looks right.
 */
it('re-asks what is in the box, not what was last committed', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya', () =>
    jsonResponse({ customers: [aCustomerCard()], nextCursor: null }),
  )
  transport.route('GET /api/v1/customers/?term=anitha&includeDeactivated=true', () =>
    jsonResponse({
      customers: [aCustomerCard({ displayName: 'Anitha K', status: 'Deactivated' })],
      nextCursor: null,
    }),
  )
  renderSearch()

  const box = screen.getByRole('searchbox', { name: 'Search' })
  await user.type(box, 'priya')
  await user.click(screen.getByRole('button', { name: 'Search' }))
  await screen.findByRole('link', { name: /Priya Selvam/ })

  // Typed over, deliberately without pressing Search.
  await user.clear(box)
  await user.type(box, 'anitha')
  await user.click(screen.getByRole('checkbox', { name: 'Include deactivated records' }))

  expect(await screen.findByRole('link', { name: /Anitha K/ })).toBeInTheDocument()
  expect(screen.queryByRole('link', { name: /Priya Selvam/ })).not.toBeInTheDocument()
  expect(
    transport.callsTo('GET /api/v1/customers/?term=priya&includeDeactivated=true'),
  ).toHaveLength(0)
})

// And a draft too short to run is refused rather than quietly re-running the old question.
it('refuses the toggle when the box holds a term too short to search', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya', () =>
    jsonResponse({ customers: [aCustomerCard()], nextCursor: null }),
  )
  renderSearch()

  const box = screen.getByRole('searchbox', { name: 'Search' })
  await user.type(box, 'priya')
  await user.click(screen.getByRole('button', { name: 'Search' }))
  await screen.findByRole('link', { name: /Priya Selvam/ })

  await user.clear(box)
  await user.type(box, 'an')
  await user.click(screen.getByRole('checkbox', { name: 'Include deactivated records' }))

  expect(await screen.findByText(/Type at least 3 characters/)).toBeInTheDocument()
  expect(
    transport.callsTo('GET /api/v1/customers/?term=priya&includeDeactivated=true'),
  ).toHaveLength(0)
})

it('has no accessibility violations', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya', () =>
    jsonResponse({ customers: [aCustomerCard()], nextCursor: null }),
  )
  const { container } = renderSearch()

  await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'priya')
  await user.click(screen.getByRole('button', { name: 'Search' }))
  await screen.findByRole('link', { name: /Priya Selvam/ })

  await expectNoAccessibilityViolations(container)
})
