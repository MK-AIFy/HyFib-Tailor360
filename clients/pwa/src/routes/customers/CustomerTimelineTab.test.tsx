import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { aTimelineEntry, aTimelinePage } from '../../customers/testing/fixtures'
import { CustomerTimelineTab } from './CustomerTimelineTab'

const CUSTOMER_ID = '0199cc00-0000-7000-8000-000000000001'
const TIMELINE = `GET /api/v1/customers/${CUSTOMER_ID}/timeline`
// The stub matches on the whole path, query string included, so an older page is its own signature —
// which is also what proves the cursor was sent back verbatim rather than rebuilt.
const OLDER = `${TIMELINE}?cursor=cursor-2`

let transport: FetchStub

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderTimeline() {
  return render(
    <AppIntlProvider locale="en-IN">
      <CustomerTimelineTab customerId={CUSTOMER_ID} />
    </AppIntlProvider>,
  )
}

it('lists what happened, newest first, with who did it and when', async () => {
  transport.route(TIMELINE, () => jsonResponse(aTimelinePage()))
  renderTimeline()

  expect(await screen.findByText('Customer record corrected')).toBeInTheDocument()
  expect(screen.getByText(/Kavitha R/)).toBeInTheDocument()
  // The absolute instant is always rendered, never a relative cue alone.
  expect(screen.getByText('01-09-2026 03:30 PM')).toBeInTheDocument()
})

it('says the system did it, rather than leaving the actor blank', async () => {
  transport.route(TIMELINE, () =>
    jsonResponse(aTimelinePage({ entries: [aTimelineEntry({ actorDisplayName: null })] })),
  )
  renderTimeline()

  expect(await screen.findByText(/the system/)).toBeInTheDocument()
})

// The whole reason the server sends `reasonPermission` beside `reason`: a reason withheld and a
// reason never given are different facts, and a reader deciding whether somebody explained
// themselves has to be able to tell them apart.
it('shows a reason that was given', async () => {
  transport.route(TIMELINE, () => jsonResponse(aTimelinePage()))
  renderTimeline()

  expect(await screen.findByText(/Reason: Spelling on her identity document/)).toBeInTheDocument()
})

it('says a reason was given but withheld, rather than showing nothing', async () => {
  transport.route(TIMELINE, () =>
    jsonResponse(
      aTimelinePage({
        entries: [aTimelineEntry({ reason: null, reasonPermission: 'customers.read_notes' })],
      }),
    ),
  )
  renderTimeline()

  expect(await screen.findByText(/permission you do not have/)).toBeInTheDocument()
})

it('says nothing at all when no reason was ever given', async () => {
  transport.route(TIMELINE, () =>
    jsonResponse(
      aTimelinePage({ entries: [aTimelineEntry({ reason: null, reasonPermission: null })] }),
    ),
  )
  renderTimeline()

  await screen.findByText('Customer record corrected')

  expect(screen.queryByText(/permission you do not have/)).not.toBeInTheDocument()
  expect(screen.queryByText(/^Reason:/)).not.toBeInTheDocument()
})

// A gap that is actually a failed source must not read as "nothing happened" — somebody reads this
// list to decide whether a garment has come back before.
it('names a source that could not answer, so a gap is visible rather than silent', async () => {
  transport.route(TIMELINE, () =>
    jsonResponse(aTimelinePage({ unavailableSources: ['orders', 'billing'] })),
  )
  renderTimeline()

  expect(await screen.findByText('Part of this history could not be loaded')).toBeInTheDocument()
  expect(screen.getByText(/orders, billing/)).toBeInTheDocument()
})

it('falls back to the source key for a module the catalogue does not name yet', async () => {
  transport.route(TIMELINE, () =>
    jsonResponse(aTimelinePage({ unavailableSources: ['notifications'] })),
  )
  renderTimeline()

  expect(await screen.findByText(/notifications/)).toBeInTheDocument()
})

it('appends an older page on request and keeps what was already read', async () => {
  const older = aTimelineEntry({
    entryId: '0199cc00-0000-7000-8000-00000000e002',
    title: 'Customer registered',
  })
  transport.route(TIMELINE, () => jsonResponse(aTimelinePage({ nextCursor: 'cursor-2' })))
  transport.route(OLDER, () => jsonResponse(aTimelinePage({ entries: [older], nextCursor: null })))
  renderTimeline()

  await screen.findByText('Customer record corrected')
  await userEvent.click(screen.getByRole('button', { name: 'Show older' }))

  expect(await screen.findByText('Customer registered')).toBeInTheDocument()
  // Appended, not replaced: an audit trail that dropped what was on screen would lose the reader's
  // place at the moment they are comparing two entries.
  expect(screen.getByText('Customer record corrected')).toBeInTheDocument()
  // And the control goes away once there is nothing older.
  expect(screen.queryByRole('button', { name: 'Show older' })).not.toBeInTheDocument()
  // The opaque cursor went back exactly as it came, which is the only correct thing to do with one.
  expect(transport.callsTo(OLDER)).toHaveLength(1)
})

it('offers no "Show older" when the first page is the whole history', async () => {
  transport.route(TIMELINE, () => jsonResponse(aTimelinePage()))
  renderTimeline()

  await screen.findByText('Customer record corrected')

  expect(screen.queryByRole('button', { name: 'Show older' })).not.toBeInTheDocument()
})

it('shows an empty state for a customer nothing has happened to', async () => {
  transport.route(TIMELINE, () => jsonResponse(aTimelinePage({ entries: [] })))
  renderTimeline()

  expect(
    await screen.findByText('Nothing has been recorded against this customer yet.'),
  ).toBeInTheDocument()
})

it('renders a failed read as a problem rather than as an empty history', async () => {
  transport.route(TIMELINE, () => problemResponse(503, 'platform.unavailable'))
  renderTimeline()

  expect(await screen.findByRole('alert')).toBeInTheDocument()
  expect(
    screen.queryByText('Nothing has been recorded against this customer yet.'),
  ).not.toBeInTheDocument()
})

it('keeps the history on screen when fetching an older page fails', async () => {
  transport.route(TIMELINE, () => jsonResponse(aTimelinePage({ nextCursor: 'cursor-2' })))
  transport.route(OLDER, () => problemResponse(503, 'platform.unavailable'))
  renderTimeline()

  await screen.findByText('Customer record corrected')
  await userEvent.click(screen.getByRole('button', { name: 'Show older' }))

  expect(await screen.findByRole('alert')).toBeInTheDocument()
  expect(screen.getByText('Customer record corrected')).toBeInTheDocument()
  // Still offered, because the page that failed is still there to be asked for again.
  expect(screen.getByRole('button', { name: 'Show older' })).toBeInTheDocument()
})

it('names the list, so a screen reader meets a history and not an unlabelled list', async () => {
  transport.route(TIMELINE, () => jsonResponse(aTimelinePage()))
  renderTimeline()

  const list = await screen.findByRole('list', { name: 'What has happened to this customer' })
  expect(within(list).getAllByRole('listitem')).toHaveLength(1)
})

it('has no accessibility violations', async () => {
  transport.route(TIMELINE, () =>
    jsonResponse(aTimelinePage({ nextCursor: 'cursor-2', unavailableSources: ['orders'] })),
  )
  const { container } = renderTimeline()
  await screen.findByText('Customer record corrected')

  await expectNoAccessibilityViolations(container)
})
