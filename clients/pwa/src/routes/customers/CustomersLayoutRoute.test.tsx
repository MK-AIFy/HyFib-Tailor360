import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import type { RouteObject } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import {
  aCustomer,
  aCustomerCard,
  aTimelinePage,
  versionedResponse,
} from '../../customers/testing/fixtures'
import type { MasterDetailArrangement } from '../../components/layout/MasterDetail'
import { router } from '../../app/router'
import { CustomerDetailRoute } from './CustomerDetailRoute'
import { CustomersLayoutRoute } from './CustomersLayoutRoute'

const CUSTOMER_ID = '0199cc00-0000-7000-8000-000000000001'
const SEARCH = 'GET /api/v1/customers/?term=priya'

let transport: FetchStub

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: ['customers.read'] })),
  )
  transport.route(SEARCH, () => jsonResponse({ customers: [aCustomerCard()], nextCursor: null }))
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}`, () =>
    versionedResponse(aCustomer(), 'W/"1"'),
  )
  transport.route(`GET /api/v1/customers/${CUSTOMER_ID}/timeline`, () =>
    jsonResponse(aTimelinePage()),
  )
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderCustomers(arrangement: MasterDetailArrangement) {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/customers']}>
          <Routes>
            <Route element={<CustomersLayoutRoute arrangement={arrangement} />} path="/customers">
              <Route element={null} index />
              <Route element={<CustomerDetailRoute />} path=":customerId" />
            </Route>
            <Route element={<p>the register form</p>} path="/customers/new" />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/** Searches for "priya" and opens the one result. */
async function findAndOpen() {
  await userEvent.type(await screen.findByRole('searchbox', { name: 'Search' }), 'priya')
  await userEvent.click(screen.getByRole('button', { name: 'Search' }))
  await userEvent.click(await screen.findByRole('link', { name: /Priya Selvam/ }))
}

it('shows the results and the record side by side when there is room for both', async () => {
  renderCustomers('split')
  await findAndOpen()

  const list = screen.getByRole('region', { name: 'Customer search and results' })
  const detail = screen.getByRole('region', { name: 'The customer record' })

  // Both panes at once is the whole point: a receptionist deciding which of two people is in front
  // of them is comparing the record against the rest of the results.
  expect(within(list).getByRole('link', { name: /Priya Selvam/ })).toBeInTheDocument()
  expect(within(detail).getByRole('heading', { name: 'Priya Selvam' })).toBeInTheDocument()
})

it('shows one pane at a time when there is not, and offers the way back', async () => {
  renderCustomers('stacked')
  await findAndOpen()

  expect(screen.getByRole('region', { name: 'The customer record' })).toBeInTheDocument()
  // The list is replaced rather than pushed off screen, so there is exactly one thing to read.
  expect(
    screen.queryByRole('region', { name: 'Customer search and results' }),
  ).not.toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: 'Back to the list' }))

  expect(
    await screen.findByRole('region', { name: 'Customer search and results' }),
  ).toBeInTheDocument()
  expect(screen.queryByRole('region', { name: 'The customer record' })).not.toBeInTheDocument()
})

// The reason the layout exists at all. If opening a record threw the results away, the screen would
// look split and behave like two addresses replacing one another.
it('keeps the search term and the results when a record is opened', async () => {
  renderCustomers('split')
  await findAndOpen()

  expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('priya')
  expect(screen.getByRole('link', { name: /Priya Selvam/ })).toBeInTheDocument()
  // And the search was made once, not again on navigation.
  expect(transport.callsTo(SEARCH)).toHaveLength(1)
})

it('says to choose somebody when nothing is selected yet', async () => {
  renderCustomers('split')
  await screen.findByRole('searchbox', { name: 'Search' })

  expect(screen.getByText('Choose an item from the list to see it here.')).toBeInTheDocument()
})

/*
 * The config itself, not a replica of it.
 *
 * Read off `router.routes` rather than a separately exported array: `createBrowserRouter` keeps the
 * routes it was given, and exporting a second constant from `router.tsx` would make that module
 * export both components and a value — which is the fast-refresh boundary this codebase moves unions
 * into their own files to avoid.
 *
 * `customers/new` has to stay a *sibling* of `customers` rather than becoming one of its children:
 * registering somebody is a whole screen, not a pane. React Router ranks a static segment above a
 * dynamic one, so `/customers/new` wins against `/customers/:customerId` — but the failure mode if
 * somebody moves it is a register screen that tries to load a customer called "new", which is the
 * kind of thing that is obvious in hindsight and silent in review.
 */
it('keeps customers/new a sibling of the list, not a pane inside it', () => {
  const siblingsOf = (wanted: string, routes: readonly RouteObject[]): readonly RouteObject[] => {
    for (const route of routes) {
      if (route.path === wanted) {
        return routes
      }
      const found = route.children === undefined ? [] : siblingsOf(wanted, route.children)
      if (found.length > 0) {
        return found
      }
    }
    return []
  }

  const siblings = siblingsOf('customers', router.routes)
  const customers = siblings.find((route) => route.path === 'customers')
  const register = siblings.find((route) => route.path === 'customers/new')

  // Siblings, in the same list, not parent and child.
  expect(customers).toBeDefined()
  expect(register).toBeDefined()
  // And the list route has exactly two children: nothing selected, and the record.
  expect(customers?.children?.map((child) => child.path ?? 'index')).toEqual([
    'index',
    ':customerId',
  ])
})

it('renders the register form, not a record called "new"', async () => {
  render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={['/customers/new']}>
          <Routes>
            <Route element={<CustomersLayoutRoute arrangement="split" />} path="/customers">
              <Route element={null} index />
              <Route element={<CustomerDetailRoute />} path=":customerId" />
            </Route>
            <Route element={<p>the register form</p>} path="/customers/new" />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )

  expect(await screen.findByText('the register form')).toBeInTheDocument()
  expect(transport.callsTo('GET /api/v1/customers/new')).toHaveLength(0)
})

it('has no accessibility violations when split', async () => {
  const { container } = renderCustomers('split')
  await findAndOpen()

  await expectNoAccessibilityViolations(container)
})

it('has no accessibility violations when stacked', async () => {
  const { container } = renderCustomers('stacked')
  await findAndOpen()

  await expectNoAccessibilityViolations(container)
})

// Two `h1`s on one screen would be two documents pretending to be one. The search owns the page
// heading; the record's name is a heading of a section within its own named landmark.
it('has one page heading, with the record a level below it', async () => {
  renderCustomers('split')
  await findAndOpen()

  expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
  expect(screen.getByRole('heading', { level: 2, name: 'Priya Selvam' })).toBeInTheDocument()
})
