import { render, screen, waitFor } from '@testing-library/react'
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
 * The server says it in a field on a 200, not as a 400 — changing the status would break a v1
 * caller reading `200 []` as "nobody matched". Reachable when the client's minimum and the server's
 * have drifted apart, which the .NET contract test exists to prevent, and it belongs at the field
 * the person has to change rather than as a bare empty state.
 */
it("shows the server's too-short refusal at the field, in the same words", async () => {
  transport.route('GET /api/v1/customers/?term=abc', () =>
    jsonResponse({
      customers: [],
      nextCursor: null,
      refusal: 'customers.search-term-too-short',
    }),
  )
  renderSearch('/customers?term=abc')

  expect(await screen.findByText(/Type at least 3 characters/)).toBeInTheDocument()
  // And emphatically not "Nobody matched", which is a different and wrong answer.
  expect(
    screen.queryByText('Nobody matched. Check the spelling, or register a new customer.'),
  ).not.toBeInTheDocument()
})

/*
 * A refusal this build has never heard of.
 *
 * The field is documented as open-ended, so the server may add a reason without that being a
 * breaking change. The one thing an old client must not do is fall back to "Nobody matched": it
 * does not know why the page is empty, and claiming there is nobody is the wrong half of the
 * uncertainty to resolve.
 */
it('does not claim nobody matched on a refusal it does not recognise', async () => {
  transport.route('GET /api/v1/customers/?term=priya', () =>
    jsonResponse({
      customers: [],
      nextCursor: null,
      refusal: 'customers.search-unavailable-in-this-branch',
    }),
  )
  renderSearch('/customers?term=priya')

  // Waited for by its text, not by the request: asserting an absence before the render has
  // happened passes for the wrong reason, which is how the first version of this test was vacuous.
  expect(await screen.findByText(/cannot say why/)).toBeInTheDocument()
  expect(
    screen.queryByText('Nobody matched. Check the spelling, or register a new customer.'),
  ).not.toBeInTheDocument()
})

// The ordinary empty page still reads as it always did: no refusal, nobody matched.
it('still says nobody matched when the page is empty for no stated reason', async () => {
  transport.route('GET /api/v1/customers/?term=priya', () =>
    jsonResponse({ customers: [], nextCursor: null, refusal: null }),
  )
  renderSearch('/customers?term=priya')

  expect(
    await screen.findByText('Nobody matched. Check the spelling, or register a new customer.'),
  ).toBeInTheDocument()
})

/*
 * A pasted or bookmarked address carrying a term too short to run.
 *
 * Nobody pressed anything, and the screen does not ask the server for a term this short, so without
 * deriving the error from the address there is nothing on screen at all: the term in the box, no
 * results, and no reason. "No reason" is read as "no such person".
 */
it('explains a too-short term that arrived in the address', async () => {
  renderSearch('/customers?term=ab')

  expect(await screen.findByText(/Type at least 3 characters/)).toBeInTheDocument()
  expect(transport.callsTo('GET /api/v1/customers/?term=ab')).toHaveLength(0)
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

/*
 * The checkbox must never disagree with the list under it.
 *
 * Tick "Include deactivated records" while the box holds a term too short to run, and the old
 * results stay on screen. If the tick had landed, they would sit under a filter that did not fetch
 * them, and somebody reads that as "she is deactivated and still not here" — the one conclusion the
 * filter exists to prevent.
 */
it('does not move the filter it cannot honour', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya', () =>
    jsonResponse({ customers: [aCustomerCard()], nextCursor: null, refusal: null }),
  )
  renderSearch()

  const box = screen.getByRole('searchbox', { name: 'Search' })
  await user.type(box, 'priya')
  await user.click(screen.getByRole('button', { name: 'Search' }))
  await screen.findByRole('link', { name: /Priya Selvam/ })

  await user.clear(box)
  await user.type(box, 'an')
  const filter = screen.getByRole('checkbox', { name: 'Include deactivated records' })
  await user.click(filter)

  expect(await screen.findByText(/Type at least 3 characters/)).toBeInTheDocument()
  // Unticked, so it still describes the results that are on screen.
  expect(filter).not.toBeChecked()
})

/*
 * An error somebody cannot clear by fixing what it complains about teaches them to ignore errors.
 *
 * The deep-link error is derived from the address, which does not change until a search is
 * submitted — so typing a valid term left "Type at least 3 characters" standing against it.
 */
it('clears the address-derived error once the field is valid', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=abc', () =>
    jsonResponse({ customers: [aCustomerCard()], nextCursor: null, refusal: null }),
  )
  renderSearch('/customers?term=ab')

  expect(await screen.findByText(/Type at least 3 characters/)).toBeInTheDocument()

  await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'c')

  await waitFor(() => {
    expect(screen.queryByText(/Type at least 3 characters/)).not.toBeInTheDocument()
  })
})

// Emptying the box is also a fix, and left the same error standing.
it('clears the address-derived error when the field is emptied', async () => {
  const user = userEvent.setup()
  renderSearch('/customers?term=ab')

  expect(await screen.findByText(/Type at least 3 characters/)).toBeInTheDocument()

  await user.clear(screen.getByRole('searchbox', { name: 'Search' }))

  await waitFor(() => {
    expect(screen.queryByText(/Type at least 3 characters/)).not.toBeInTheDocument()
  })
})

/*
 * The segmented Phone / Name mode (#629).
 *
 * Specified by the plan's #26 [E04-F01] blueprint and by exceptions.md section 4.1, where it is
 * part of duplicate prevention: Reception who cannot type a number quickly searches less, and a
 * search not made is how the same person is registered twice.
 */
it('raises the telephone keypad in phone mode, and the text keyboard in name mode', async () => {
  const user = userEvent.setup()
  renderSearch()

  const box = screen.getByRole('searchbox', { name: 'Search' })
  // Name is the default, and it is what this field did before the modes existed.
  expect(box).not.toHaveAttribute('inputmode')

  await user.click(screen.getByRole('radio', { name: 'Phone number' }))

  expect(await screen.findByRole('searchbox', { name: 'Search' })).toHaveAttribute(
    'inputmode',
    'tel',
  )
})

// The mode is not part of the question: the endpoint matches a name, a number or the tail of a
// phone number either way. Re-running on a switch would be work nobody asked for, and would risk
// results that disagree with the control above them.
it('does not re-run the search when the keyboard changes', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya', () =>
    jsonResponse({ customers: [aCustomerCard()], nextCursor: null, refusal: null }),
  )
  renderSearch('/customers?term=priya')

  await screen.findByRole('link', { name: /Priya Selvam/ })
  await user.click(screen.getByRole('radio', { name: 'Phone number' }))

  expect(transport.callsTo('GET /api/v1/customers/?term=priya')).toHaveLength(1)
  // And the results it already had are still the ones on screen.
  expect(screen.getByRole('link', { name: /Priya Selvam/ })).toBeInTheDocument()
})

// In the address, for the reason the term and the filter are: `MasterDetail` takes this pane out of
// the DOM when a record is open, so anything held in state dies when somebody opens a customer.
it('keeps the mode, the term and the filter together in the address', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya&includeDeactivated=true', () =>
    jsonResponse({ customers: [aCustomerCard()], nextCursor: null, refusal: null }),
  )
  renderSearch('/customers?term=priya&withdrawn=true&mode=phone')

  await screen.findByRole('link', { name: /Priya Selvam/ })

  expect(screen.getByRole('radio', { name: 'Phone number' })).toBeChecked()
  expect(screen.getByRole('checkbox', { name: 'Include deactivated records' })).toBeChecked()
  expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('priya')

  // And a fresh search from here does not quietly drop the keyboard back.
  await user.click(screen.getByRole('button', { name: 'Search' }))
  expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveAttribute('inputmode', 'tel')
})

/*
 * Switching the keyboard writes the whole query back, so everything else in it has to be carried.
 *
 * `setParams` replaces the address rather than merging into it, which makes every part of the
 * question something the switch can silently drop — the committed term, and the filter that decides
 * whether a deactivated record is even offered. Dropping the filter would put results back that
 * exclude her, under a box still showing ticked.
 */
it('keeps the committed term and the filter when the keyboard changes', async () => {
  const user = userEvent.setup()
  transport.route('GET /api/v1/customers/?term=priya&includeDeactivated=true', () =>
    jsonResponse({
      customers: [aCustomerCard({ status: 'Deactivated' })],
      nextCursor: null,
      refusal: null,
    }),
  )
  renderSearch('/customers?term=priya&withdrawn=true')

  await screen.findByRole('link', { name: /Priya Selvam/ })

  await user.click(screen.getByRole('radio', { name: 'Phone number' }))

  expect(screen.getByRole('checkbox', { name: 'Include deactivated records' })).toBeChecked()
  expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('priya')
  // Still the one answer, not re-asked and not re-asked differently.
  expect(
    transport.callsTo('GET /api/v1/customers/?term=priya&includeDeactivated=true'),
  ).toHaveLength(1)
  expect(screen.getByRole('link', { name: /Priya Selvam/ })).toBeInTheDocument()
})

// Switching the keyboard must not cost somebody the term they have typed.
it('keeps a typed term when the keyboard changes', async () => {
  const user = userEvent.setup()
  renderSearch()

  await user.type(screen.getByRole('searchbox', { name: 'Search' }), '98765')
  await user.click(screen.getByRole('radio', { name: 'Phone number' }))

  expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('98765')
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
