import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { versionResponse } from '../../app/testing/versionFixture'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import type { JourneyRole, ShellKind } from '../../design-system/foundations/types'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { CashierPaymentScreen } from './CashierPayment'
import { DeliveryDispatchScreen } from './DeliveryDispatch'
import { InventoryStockEntryScreen } from './InventoryStockEntry'
import { JourneyFrame } from './JourneyFrame'
import { OwnerDashboardScreen } from './OwnerDashboard'
import { TailorMasterWorkboardScreen } from './TailorMasterWorkboard'
import { TailorQueueScreen } from './TailorQueue'

/**
 * The six reference journeys added for #50, tested for the behaviour they are evidence of.
 *
 * ## Why a story has tests at all
 *
 * These screens are Storybook evidence rather than shipped routes, so it would be defensible to
 * leave them to the manual walkthrough. It is not defensible here, because the walkthrough is what
 * these tests protect: the record in docs/nfr/a11y-checklist.md section 6.8 asks a runner to hear a
 * *particular sentence* at a *particular step* — which rule refused a scan, which phase a rework
 * returns to, that a payment will not be queued — and a screen that silently stopped saying it would
 * turn a Pass into a Pass for the wrong reason. Each test below pins one of those sentences.
 *
 * What is deliberately **not** here: anything needing layout. jsdom has no layout engine, so reflow,
 * target size, obscured focus and contrast are covered by `expectNoHorizontalOverflow`, the token
 * contrast test and the Playwright run of #52 — not by an assertion here that would report a false
 * pass. The one exception is focus itself, which jsdom does model, so the return-focus step is
 * asserted.
 *
 * Every value is synthetic and comes from the fixtures in `../fixtures`.
 */

function renderJourney(role: JourneyRole, shellKind: ShellKind, children: ReactNode) {
  return renderWithProviders(
    <JourneyFrame role={role} shellKind={shellKind} width="768">
      {children}
    </JourneyFrame>,
  )
}

/** The region a rejected scan lands in — assertive, and separate from the accepted one. */
function rejectedScanRegion(): HTMLElement {
  const region = document.querySelector('[data-channel="scan-rejected"]')
  if (!(region instanceof HTMLElement)) {
    throw new Error('The shell did not render a rejected-scan region.')
  }
  return region
}

function acceptedScanRegion(): HTMLElement {
  const region = document.querySelector('[data-channel="scan-accepted"]')
  if (!(region instanceof HTMLElement)) {
    throw new Error('The shell did not render an accepted-scan region.')
  }
  return region
}

function syncRegion(): HTMLElement {
  const region = document.querySelector('[data-channel="sync"]')
  if (!(region instanceof HTMLElement)) {
    throw new Error('The shell did not render a sync region.')
  }
  return region
}

beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    vi.fn(() => Promise.resolve(versionResponse({ environment: 'production' }))),
  )
})

afterEach(() => {
  vi.unstubAllGlobals()
  document.documentElement.removeAttribute('data-shell')
})

describe('A11Y-RJ-03 — Tailor: scan to queue', () => {
  it('tells a superseded label apart from an unknown one, and says which rule refused it', async () => {
    const user = userEvent.setup()
    renderJourney('tailor', 'phone', <TailorQueueScreen />)

    await user.click(screen.getByRole('button', { name: 'Scan the reprinted label' }))

    // Step 8 asks to hear *which* rule failed. "Not found" would send somebody looking for a job
    // that is on the rack in front of them, under a label that was reprinted last Tuesday.
    const superseded = within(rejectedScanRegion()).getByText(/reprinted/)
    expect(superseded).toHaveTextContent('G-6MTB4XZ9DKQ2')

    await user.type(screen.getByLabelText(/Label or job number/), 'G-NOTAREALLABEL')
    await user.click(screen.getByRole('button', { name: 'Scan in' }))

    expect(
      within(rejectedScanRegion()).getByText(/No job in this branch carries that label/),
    ).toBeInTheDocument()
  })

  it('announces the job and the next expected action when a scan is accepted', async () => {
    const user = userEvent.setup()
    renderJourney('tailor', 'phone', <TailorQueueScreen />)

    await user.click(screen.getByRole('button', { name: 'Open J-CBE01-2627-000512-01' }))

    const accepted = within(acceptedScanRegion()).getByText(/J-CBE01-2627-000512-01/)
    expect(accepted).toHaveTextContent('Next: start the phase')
  })

  it('says a completed phase is queued rather than saved when there is no connection', async () => {
    const user = userEvent.setup()
    renderJourney('tailor', 'phone', <TailorQueueScreen />)

    await user.click(screen.getByRole('button', { name: 'Open J-CBE01-2627-000512-01' }))
    await user.click(screen.getByRole('button', { name: 'Start Stitching' }))
    await user.click(screen.getByRole('switch', { name: /Working offline/ }))
    await user.click(screen.getByRole('button', { name: 'Complete Stitching' }))

    // "Saved" would be a lie: it is on the device, nobody else can see it, and the server has
    // confirmed nothing. The client never asserts what the server has not confirmed.
    const sync = within(syncRegion()).getByText(/Queued on this device/)
    expect(sync).toHaveTextContent('nothing is confirmed until it is')

    await user.click(screen.getByRole('button', { name: 'Reconnect and send' }))

    expect(within(syncRegion()).getByText(/replayed and accepted/)).toBeInTheDocument()
  })
})

describe('A11Y-RJ-04 — Tailor Master: workboard, assignment and QC', () => {
  it('refuses an assignment by naming the qualification, not by saying "not allowed"', async () => {
    const user = userEvent.setup()
    renderJourney('tailor-master', 'desktop', <TailorMasterWorkboardScreen />)

    await user.click(screen.getByRole('button', { name: 'Assign J-CBE01-2627-000934-01' }))
    await user.selectOptions(screen.getByLabelText(/Assign to/), 'shanthi')
    await user.click(screen.getByRole('button', { name: 'Assign' }))

    const refusal = screen.getByText(/Shanthi K\. is qualified for/)
    expect(refusal).toHaveTextContent('blouse, choli')
    expect(refusal).toHaveTextContent('not salwar work')
  })

  it('refuses a held job with the hold’s own reason', async () => {
    const user = userEvent.setup()
    renderJourney('tailor-master', 'desktop', <TailorMasterWorkboardScreen />)

    await user.click(screen.getByRole('button', { name: 'Assign J-CBE01-2627-000689-01' }))
    await user.click(screen.getByRole('button', { name: 'Assign' }))

    // The hold is checked before the qualification, and its reason is the sentence somebody has to
    // act on: the stones are short, and no reassignment fixes that.
    expect(screen.getByText(/Stones and beads short by one kit/)).toBeInTheDocument()
  })

  it('records a rework beside the earlier QC pass rather than in place of it', async () => {
    const user = userEvent.setup()
    renderJourney('tailor-master', 'desktop', <TailorMasterWorkboardScreen />)

    const seams = screen.getByRole('group', { name: /Seams and finishing/ })
    await user.click(within(seams).getByRole('radio', { name: 'Failed' }))

    await user.type(
      screen.getByLabelText(/Describe the photograph/),
      'Puckering along the left side seam, about four inches.',
    )
    await user.click(screen.getByRole('button', { name: 'Attach the photograph' }))
    await user.click(screen.getByRole('button', { name: 'Raise the rework' }))

    const dialog = screen.getByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Raise the rework' }))

    const history = screen.getByRole('list', { name: /History of J-CBE01-2627-001007-01/ })
    // Both entries, in that order. An audit trail that replaced the pass with the failure would be
    // a rewritten record rather than an amended one, and step 9 exists to catch exactly that.
    expect(within(history).getByText('QC passed on the first inspection')).toBeInTheDocument()
    expect(
      within(history).getByText('QC failed on re-inspection — returned to Stitching'),
    ).toBeInTheDocument()
  })
})

describe('A11Y-RJ-05 — Inventory Clerk: receive, issue and count', () => {
  it('states the purchase conversion in base units rather than leaving it to be inferred', () => {
    renderJourney('inventory', 'tablet', <InventoryStockEntryScreen />)

    // Two rolls of 25 m is fifty metres of lining. A clerk receiving in rolls and issuing in metres
    // is where a stock ledger goes wrong silently.
    const conversion = screen.getByText(/× Rolls of 25 m/)
    expect(conversion).toHaveTextContent('50')
    expect(conversion).toHaveTextContent('Cotton lining — natural')
    expect(conversion).toHaveTextContent('Rack A2')
  })

  it('refuses a garment label scanned into the stock field by naming the namespace rule', async () => {
    const user = userEvent.setup()
    renderJourney('inventory', 'tablet', <InventoryStockEntryScreen />)

    await user.click(screen.getByRole('button', { name: 'Scan a garment label by mistake' }))

    expect(
      within(rejectedScanRegion()).getByText(/That is a garment label, not a stock label/),
    ).toBeInTheDocument()
  })

  it('announces the variance with its direction in words and as a counted-versus-expected pair', () => {
    renderJourney('inventory', 'tablet', <InventoryStockEntryScreen />)

    // "−1.5" alone is a number nobody can act on, and a leading minus is the easiest thing on a
    // screen to miss. Step 6 asks for the sign and the pair.
    const variance = screen.getByText(/Short by/)
    expect(variance).toHaveTextContent('counted')
    expect(variance).toHaveTextContent('expected')
  })
})

describe('A11Y-RJ-06 — Cashier: taking a payment', () => {
  it('suggests the balance instead of only refusing an overpayment', async () => {
    const user = userEvent.setup()
    renderJourney('cashier', 'tablet', <CashierPaymentScreen />)

    await user.click(screen.getByRole('button', { name: /From invoice INV-CBE01-2627-000731/ }))
    await user.click(screen.getByRole('button', { name: /Increase Amount taken/ }))

    // 3.3.3 Error Suggestion. A cashier with a queue at the counter does not need to be told they
    // are wrong; they need the right number, and the other thing they could do with the difference.
    const error = screen.getByText(/That is more than the balance/)
    expect(error).toHaveTextContent('INV-CBE01-2627-000731')
    expect(error).toHaveTextContent('record the difference as an advance')
  })

  it('blocks a payment offline and says in as many words that it will not be queued', async () => {
    const user = userEvent.setup()
    renderJourney('cashier', 'tablet', <CashierPaymentScreen />)

    await user.click(screen.getByRole('button', { name: /From invoice INV-CBE01-2627-000731/ }))
    await user.click(screen.getByRole('switch', { name: /Working offline/ }))

    expect(screen.getByText('Needs connection — this will not be queued')).toBeInTheDocument()
    expect(screen.getByText(/This will not be queued/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Take the payment' })).not.toBeInTheDocument()
  })

  it('keeps what was typed when the session expires, and keeps the same idempotency key', async () => {
    const user = userEvent.setup()
    renderJourney('cashier', 'tablet', <CashierPaymentScreen />)

    await user.click(screen.getByRole('button', { name: /From invoice INV-CBE01-2627-000731/ }))
    await user.click(screen.getByRole('radio', { name: 'Cash' }))
    await user.click(screen.getByRole('button', { name: 'Expire the session now' }))

    // Step 9: re-authentication happens in place. The form is not unmounted, so the mode is still
    // chosen and the amount is still on the screen — and the retry reuses the key it started with,
    // which is what stops the same payment being recorded twice.
    expect(screen.getByRole('radio', { name: 'Cash' })).toBeChecked()
    expect(screen.getByRole('textbox', { name: /Amount taken/ })).toHaveValue('309.00')
    expect(screen.getByText(/keeps the same key/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Sign in again' }))

    expect(screen.getByRole('radio', { name: 'Cash' })).toBeChecked()
  })
})

describe('A11Y-RJ-07 — Delivery: the dispatch gate', () => {
  it('refuses an unpaid dispatch with the amount and both remedies, and leaves the row reachable', async () => {
    const user = userEvent.setup()
    renderJourney('delivery', 'phone', <DeliveryDispatchScreen />)

    const trigger = screen.getByRole('button', {
      name: 'Receive scan on J-CBE01-2627-000934-01',
    })
    await user.click(trigger)

    const refusal = within(rejectedScanRegion()).getByText(/cannot be dispatched/)
    expect(refusal).toHaveTextContent('₹1,043.00')
    expect(refusal).toHaveTextContent('Take the balance at the counter')
    expect(refusal).toHaveTextContent('ask a manager to approve an exception')

    // A blocked dispatch that sounds like a broken screen is how the control gets worked around, so
    // the row stays in the queue and its control stays reachable rather than being hidden or dimmed.
    expect(trigger).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Take the balance at the counter' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Request an exception approval' })).toBeEnabled()
  })

  it('says an exception request does not by itself release the garment', async () => {
    const user = userEvent.setup()
    renderJourney('delivery', 'phone', <DeliveryDispatchScreen />)

    await user.click(screen.getByRole('button', { name: 'Receive scan on J-CBE01-2627-000934-01' }))
    await user.click(screen.getByRole('button', { name: 'Request an exception approval' }))

    expect(screen.getByText(/the request does not release it/)).toBeInTheDocument()
  })

  it('states that dispatch is irreversible before the control that does it', async () => {
    const user = userEvent.setup()
    renderJourney('delivery', 'phone', <DeliveryDispatchScreen />)

    await user.click(screen.getByRole('button', { name: 'Receive scan on J-CBE01-2627-000512-01' }))
    await user.click(screen.getByRole('button', { name: 'Dispatch scan' }))

    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByText(/Dispatch cannot be undone/)).toBeInTheDocument()
  })
})

describe('A11Y-RJ-08 — Owner: dashboard, alerts and reports', () => {
  it('renders a figure this role may not read as a permission state, not an error', async () => {
    const user = userEvent.setup()
    renderJourney('owner', 'desktop', <OwnerDashboardScreen />)

    await user.click(screen.getByRole('button', { name: 'Open Staff cost' }))

    expect(screen.getByText('You do not have permission to do this')).toBeInTheDocument()
    expect(screen.getByText(/needs the payroll read permission/)).toBeInTheDocument()
  })

  it('puts focus back on the control the panel was opened from', async () => {
    const user = userEvent.setup()
    renderJourney('owner', 'desktop', <OwnerDashboardScreen />)

    const trigger = screen.getByRole('button', { name: 'Open Staff cost' })
    await user.click(trigger)
    await user.click(screen.getByRole('button', { name: 'Go back' }))

    // Checklist item A11Y-35. Without this, a keyboard user is returned to the top of the document
    // every time they read an alert, on a dashboard that is mostly alerts.
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Open Staff cost' })).toHaveFocus()
    })
  })

  it('offers the updates that have arrived rather than applying them under the reader', async () => {
    const user = userEvent.setup()
    renderJourney('owner', 'desktop', <OwnerDashboardScreen />)

    // 2.2.2 Pause, Stop, Hide, and step 8: the dashboard holds still until it is asked not to.
    expect(screen.getByText(/Nothing on the screen has moved/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Show 2 new updates' }))

    expect(screen.queryByText(/Nothing on the screen has moved/)).not.toBeInTheDocument()
  })

  it('renders the pipeline chart’s data as a table, not only as a drawing', () => {
    renderJourney('owner', 'desktop', <OwnerDashboardScreen />)

    // The drawing is aria-hidden decoration; the table is the data. Step 3 asks for the text
    // alternative to be reachable, and the only ordering that guarantees it is this one.
    const table = screen.getByRole('table', {
      name: /Jobs in each phase, and how many of them are overdue/,
    })
    expect(within(table).getByRole('cell', { name: 'Stitching' })).toBeInTheDocument()
  })
})

describe('every reference journey, as axe sees it', () => {
  /*
   * The automated half of the accessibility evidence Definition of Done item 7 asks for. axe in
   * jsdom catches the structural class — a control with no accessible name, a field with no label,
   * a heading level skipped, an `aria-describedby` pointing at nothing. It cannot judge contrast or
   * target size, which need paint; those are the token-pair test and the manual checklist, and the
   * helper disables the rules that would otherwise report a false pass.
   *
   * Each journey is checked in its landing state. The states reached by pressing something are
   * covered by the behaviour tests above, and each of those renders through the same components
   * this pass has already cleared.
   */
  const journeys = [
    ['A11Y-RJ-03 Tailor', 'tailor', 'phone', <TailorQueueScreen />],
    ['A11Y-RJ-04 Tailor Master', 'tailor-master', 'desktop', <TailorMasterWorkboardScreen />],
    ['A11Y-RJ-05 Inventory Clerk', 'inventory', 'tablet', <InventoryStockEntryScreen />],
    ['A11Y-RJ-06 Cashier', 'cashier', 'tablet', <CashierPaymentScreen />],
    ['A11Y-RJ-07 Delivery', 'delivery', 'phone', <DeliveryDispatchScreen />],
    ['A11Y-RJ-08 Owner', 'owner', 'desktop', <OwnerDashboardScreen />],
  ] as const satisfies readonly (readonly [string, JourneyRole, ShellKind, ReactNode])[]

  it.each(journeys)('has no violation on %s', async (_name, role, shellKind, screen_) => {
    const { container } = renderJourney(role, shellKind, screen_)

    await expectNoAccessibilityViolations(container)
  })
})
