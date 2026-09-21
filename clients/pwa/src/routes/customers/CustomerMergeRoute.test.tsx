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
import { aCustomer, aDuplicateCandidate, versionedResponse } from '../../customers/testing/fixtures'
import {
  CUSTOMER_MERGED_RECORD_CHANGED_CODE,
  CUSTOMER_VERSION_CONFLICT_CODE,
} from '../../customers/types'
import { CustomerMergeRoute } from './CustomerMergeRoute'

const SURVIVOR = '0199cc00-0000-7000-8000-000000000001'
const FOLDED = '0199cc00-0000-7000-8000-000000000002'
const READ_SURVIVOR = `GET /api/v1/customers/${SURVIVOR}`
const READ_FOLDED = `GET /api/v1/customers/${FOLDED}`
const DUPLICATES = `GET /api/v1/customers/${SURVIVOR}/duplicates`
const MERGE = `POST /api/v1/customers/${SURVIVOR}/merge`

/** The candidate is a *different* record from the survivor — the whole screen is about the pair. */
const CANDIDATE = aDuplicateCandidate({
  customer: {
    customerId: FOLDED,
    customerNumber: 'C-000999',
    displayName: 'Priya S',
    nativeName: null,
    maskedPhone: '••••••1234',
    owningBranchId: '0199a000-0000-7000-8000-000000000001',
    visibleToCaller: true,
    status: 'Active',
    lastSeenAt: '2026-09-01T10:00:00Z',
  },
  reasons: ['Same telephone number'],
})

let transport: FetchStub

/**
 * Makes the viewport a desktop, which is what lets the typed tier exist at all.
 *
 * jsdom has no `matchMedia`, and `useViewportShellKind` answers "phone" without one — deliberately,
 * because the phone is the shell where the typed tier is forbidden. So a test that wants to see the
 * typed confirmation has to say it is on a desktop, and the test that wants the phone substitute
 * simply does not call this.
 */
function onADesktop() {
  vi.stubGlobal(
    'matchMedia',
    (query: string): MediaQueryList =>
      ({
        matches: true,
        media: query,
        onchange: null,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
        addListener: () => undefined,
        removeListener: () => undefined,
        dispatchEvent: () => false,
      }) as MediaQueryList,
  )
}

function signedInAs(permissions: readonly string[]) {
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [...permissions] })),
  )
}

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  signedInAs(['customers.read', 'customers.merge'])
  transport.route(READ_SURVIVOR, () => versionedResponse(aCustomer(), 'W/"7"'))
  transport.route(READ_FOLDED, () =>
    versionedResponse(aCustomer({ customerId: FOLDED, customerNumber: 'C-000999' }), 'W/"3"'),
  )
  transport.route(DUPLICATES, () => jsonResponse({ candidates: [CANDIDATE] }))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderMerge() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[`/customers/${SURVIVOR}/duplicates`]}>
          <Routes>
            <Route path="/customers/:customerId" element={<p>a customer record</p>} />
            <Route path="/customers/:customerId/duplicates" element={<CustomerMergeRoute />} />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/**
 * Opens the confirmation on a desktop and satisfies the typed tier.
 *
 * The phrase is the *folded* record's number, which is the point of choosing it: somebody who has
 * not read which record they are destroying cannot type it.
 */
async function confirmTheMerge(reason = 'Same person, checked her identity document') {
  await userEvent.click(screen.getByRole('button', { name: 'Fold C-000999 into Priya Selvam' }))
  const dialog = await screen.findByRole('dialog')
  await userEvent.type(screen.getByRole('textbox', { name: 'Reason' }), reason)
  await userEvent.type(
    screen.getByRole('textbox', { name: 'Type C-000999 to confirm' }),
    'C-000999',
  )
  await userEvent.click(screen.getByRole('button', { name: 'Fold C-000999 in' }))
  return dialog
}

it('names the record that survives, so the direction is never inferred', async () => {
  renderMerge()

  expect(
    await screen.findByRole('heading', { name: 'Possible duplicates of Priya Selvam' }),
  ).toBeInTheDocument()
  expect(screen.getByText(/is the record that will survive/)).toBeInTheDocument()
  // And the control says which way round it goes, rather than "Merge".
  expect(
    screen.getByRole('button', { name: 'Fold C-000999 into Priya Selvam' }),
  ).toBeInTheDocument()
})

it('sends both preconditions: the survivor in If-Match and the folded record in the body', async () => {
  transport.route(MERGE, () =>
    versionedResponse(
      {
        customer: aCustomer(),
        mergeId: '0199cc00-0000-7000-8000-00000000d001',
        mergedCustomerId: FOLDED,
        mergedCustomerNumber: 'C-000999',
        aliasesRecorded: 2,
        visibilityBranchesAdded: 1,
        recordsRepointed: 3,
        mergedAt: '2026-09-21T10:00:00Z',
      },
      'W/"8"',
    ),
  )
  onADesktop()
  renderMerge()
  await screen.findByRole('button', { name: 'Fold C-000999 into Priya Selvam' })
  await confirmTheMerge()

  await waitFor(() => {
    expect(transport.callsTo(MERGE)).toHaveLength(1)
  })
  const [sent] = transport.callsTo(MERGE)
  expect(sent?.headers.get('If-Match')).toBe('W/"7"')
  expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
  expect(sent?.body).toMatchObject({
    mergedCustomerId: FOLDED,
    // The folded record's own version, read immediately before the merge — never `*`.
    mergedCustomerVersion: 'W/"3"',
    reason: 'Same person, checked her identity document',
  })
})

it('reads the folded record at the moment of the merge, not when the list was drawn', async () => {
  transport.route(MERGE, () => problemResponse(503, 'platform.unavailable'))
  onADesktop()
  renderMerge()
  await screen.findByRole('button', { name: 'Fold C-000999 into Priya Selvam' })

  // Drawing the list must not have read it; confirming must.
  expect(transport.callsTo(READ_FOLDED)).toHaveLength(0)
  await confirmTheMerge()

  await waitFor(() => {
    expect(transport.callsTo(READ_FOLDED)).toHaveLength(1)
  })
})

// The two 409s send the reader to different records, so they must not read alike. This is the pair
// of negative cases the whole two-precondition shape exists for.
it('says the surviving record changed, when that is the half that went stale', async () => {
  transport.route(MERGE, () => problemResponse(409, CUSTOMER_VERSION_CONFLICT_CODE))
  onADesktop()
  renderMerge()
  await screen.findByRole('button', { name: 'Fold C-000999 into Priya Selvam' })
  await confirmTheMerge()

  expect(
    await screen.findByText(/The record that would survive has been corrected/),
  ).toBeInTheDocument()
  expect(screen.queryByText(/The record you are folding in/)).not.toBeInTheDocument()
})

it('says the folded record changed, when that is the half that went stale', async () => {
  transport.route(MERGE, () => problemResponse(409, CUSTOMER_MERGED_RECORD_CHANGED_CODE))
  onADesktop()
  renderMerge()
  await screen.findByRole('button', { name: 'Fold C-000999 into Priya Selvam' })
  await confirmTheMerge()

  expect(
    await screen.findByText(/The record you are folding in has been corrected/),
  ).toBeInTheDocument()
  expect(screen.queryByText(/The record that would survive/)).not.toBeInTheDocument()
})

it('keeps the refusal inside the dialog that caused it', async () => {
  transport.route(MERGE, () => problemResponse(409, CUSTOMER_VERSION_CONFLICT_CODE))
  onADesktop()
  renderMerge()
  await screen.findByRole('button', { name: 'Fold C-000999 into Priya Selvam' })
  const dialog = await confirmTheMerge()

  const refusal = await screen.findByText(/The record that would survive has been corrected/)
  // Behind a modal backdrop and outside the focus trap, an alert on the screen beneath would be
  // invisible to a sighted user and silent to a screen reader.
  expect(dialog).toContainElement(refusal)
})

it('refuses to merge when the folded record carries no version to present', async () => {
  // No ETag: there is no concrete version to put in `mergedCustomerVersion`, and `*` is refused by
  // the server precisely because there is no "any version" of a record somebody approved destroying.
  transport.route(READ_FOLDED, () => jsonResponse(aCustomer({ customerId: FOLDED })))
  onADesktop()
  renderMerge()
  await screen.findByRole('button', { name: 'Fold C-000999 into Priya Selvam' })
  await confirmTheMerge()

  await waitFor(() => {
    expect(transport.callsTo(READ_FOLDED)).toHaveLength(1)
  })
  expect(transport.callsTo(MERGE)).toHaveLength(0)
})

it('reports what the merge actually did, in numbers somebody can check', async () => {
  transport.route(MERGE, () =>
    versionedResponse(
      {
        customer: aCustomer(),
        mergeId: '0199cc00-0000-7000-8000-00000000d001',
        mergedCustomerId: FOLDED,
        mergedCustomerNumber: 'C-000999',
        aliasesRecorded: 2,
        visibilityBranchesAdded: 1,
        recordsRepointed: 3,
        mergedAt: '2026-09-21T10:00:00Z',
      },
      'W/"8"',
    ),
  )
  onADesktop()
  renderMerge()
  await screen.findByRole('button', { name: 'Fold C-000999 into Priya Selvam' })
  await confirmTheMerge()

  expect(await screen.findByText('The records were merged')).toBeInTheDocument()
  expect(screen.getByText('3 records re-pointed to the surviving customer')).toBeInTheDocument()
  expect(screen.getByText('2 aliases recorded on the surviving record')).toBeInTheDocument()
})

// The typed tier does not exist on a phone (checklist A11Y-BI-13), and the substitute is named: a
// mandatory reason plus a second, explicitly armed press. This asserts the irreversible action is
// still hard to take by accident there, rather than quietly becoming a single tap.
it('substitutes a second press for the typed phrase on a phone', async () => {
  transport.route(MERGE, () => problemResponse(503, 'platform.unavailable'))
  renderMerge()
  await userEvent.click(
    await screen.findByRole('button', { name: 'Fold C-000999 into Priya Selvam' }),
  )
  await screen.findByRole('dialog')

  // No phrase to type — a minute of one-handed typing in front of a waiting customer.
  expect(
    screen.queryByRole('textbox', { name: 'Type C-000999 to confirm' }),
  ).not.toBeInTheDocument()

  await userEvent.type(screen.getByRole('textbox', { name: 'Reason' }), 'Same person')
  await userEvent.click(screen.getByRole('button', { name: 'Fold C-000999 in' }))

  // The first press arms rather than merges.
  expect(transport.callsTo(MERGE)).toHaveLength(0)
  expect(
    screen.getByText(/Tap Confirm once more to folding C-000999 into Priya Selvam/),
  ).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: 'Confirm again' }))
  await waitFor(() => {
    expect(transport.callsTo(MERGE)).toHaveLength(1)
  })
})

it('shows the duplicates to a caller who may read but offers no merge control', async () => {
  signedInAs(['customers.read'])
  renderMerge()

  expect(await screen.findByText('C-000999')).toBeInTheDocument()
  expect(
    screen.queryByRole('button', { name: 'Fold C-000999 into Priya Selvam' }),
  ).not.toBeInTheDocument()
})

it('says so when nothing resembles this record', async () => {
  transport.route(DUPLICATES, () => jsonResponse({ candidates: [] }))
  renderMerge()

  expect(
    await screen.findByText(
      'No other record resembles this one closely enough to be worth reviewing.',
    ),
  ).toBeInTheDocument()
})

it('shows an empty state when the surviving record cannot be reached', async () => {
  transport.route(READ_SURVIVOR, () => problemResponse(404, 'customers.customer-not-found'))
  renderMerge()

  expect(
    await screen.findByText('This record could not be found, or is not one you can reach.'),
  ).toBeInTheDocument()
})

it('has no accessibility violations', async () => {
  const { container } = renderMerge()
  await screen.findByRole('button', { name: 'Fold C-000999 into Priya Selvam' })

  await expectNoAccessibilityViolations(container)
})
