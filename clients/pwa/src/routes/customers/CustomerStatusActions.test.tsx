import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { aCustomer, versionedResponse } from '../../customers/testing/fixtures'
import {
  CUSTOMER_ALREADY_MERGED_CODE,
  CUSTOMER_STATUS_TRANSITION_CODE,
  CUSTOMER_VERSION_CONFLICT_CODE,
} from '../../customers/types'
import type { Customer } from '../../customers/types'
import { CustomerStatusActions } from './CustomerStatusActions'

const CUSTOMER_ID = '0199cc00-0000-7000-8000-000000000001'
const DEACTIVATE = `POST /api/v1/customers/${CUSTOMER_ID}/deactivate`
const REACTIVATE = `POST /api/v1/customers/${CUSTOMER_ID}/reactivate`

let transport: FetchStub
let changed: number

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  changed = 0
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderActions(customer: Customer = aCustomer(), reloadFailure: unknown = null) {
  return render(
    <AppIntlProvider locale="en-IN">
      <CustomerStatusActions
        customer={customer}
        onChanged={() => {
          changed += 1
        }}
        reloadFailure={reloadFailure}
        version='W/"7"'
      />
    </AppIntlProvider>,
  )
}

/** Opens the confirmation, gives the reason the endpoint requires, and confirms. */
async function confirm(label: string, reason = 'Created in error, no orders against it') {
  await userEvent.click(screen.getByRole('button', { name: label }))
  const dialog = await screen.findByRole('dialog')
  await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), reason)
  await userEvent.click(within(dialog).getByRole('button', { name: label }))
  return dialog
}

it('says what deactivating does, and what it does not, before it is done', async () => {
  renderActions()
  await userEvent.click(screen.getByRole('button', { name: 'Deactivate this record' }))

  const dialog = await screen.findByRole('dialog')
  // Not a deletion, and the screen has to say so — the word "withdraw" alone reads like one.
  expect(dialog).toHaveTextContent(/Nothing is deleted/)
  expect(dialog).toHaveTextContent(/her history stands/)
  // And how to find her again, which is the part that makes it reversible in practice.
  expect(dialog).toHaveTextContent(/Include deactivated records/)
})

it('sends the version it was read at, the reason and a retry key', async () => {
  transport.route(DEACTIVATE, () =>
    versionedResponse(aCustomer({ status: 'Deactivated' }), 'W/"8"'),
  )
  renderActions()
  await confirm('Deactivate this record')

  await waitFor(() => {
    expect(transport.callsTo(DEACTIVATE)).toHaveLength(1)
  })
  const [sent] = transport.callsTo(DEACTIVATE)
  expect(sent?.headers.get('If-Match')).toBe('W/"7"')
  expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
  expect(sent?.body).toEqual({ reason: 'Created in error, no orders against it' })
  expect(changed).toBe(1)
})

it('will not deactivate without a reason', async () => {
  renderActions()
  await userEvent.click(screen.getByRole('button', { name: 'Deactivate this record' }))
  const dialog = await screen.findByRole('dialog')
  await userEvent.click(within(dialog).getByRole('button', { name: 'Deactivate this record' }))

  expect(transport.callsTo(DEACTIVATE)).toHaveLength(0)
})

it('offers the way back on a deactivated record, and says what deactivated means', () => {
  renderActions(aCustomer({ status: 'Deactivated' }))

  expect(screen.getByText('This record has been deactivated')).toBeInTheDocument()
  expect(screen.getByText(/an ordinary search no longer offers her/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Reactivate this record' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Deactivate this record' })).not.toBeInTheDocument()
})

it('reactivates a deactivated record', async () => {
  transport.route(REACTIVATE, () => versionedResponse(aCustomer(), 'W/"8"'))
  renderActions(aCustomer({ status: 'Deactivated' }))
  await confirm('Reactivate this record', 'She came back')

  await waitFor(() => {
    expect(transport.callsTo(REACTIVATE)).toHaveLength(1)
  })
  expect(changed).toBe(1)
})

/*
 * The three refusals send the reader to three different places, so they must not read alike.
 */
it('says the record moved on when the version is stale', async () => {
  transport.route(DEACTIVATE, () => problemResponse(409, CUSTOMER_VERSION_CONFLICT_CODE))
  renderActions()
  await confirm('Deactivate this record')

  expect(
    await screen.findByText(/Somebody corrected this record while you were reading it/),
  ).toBeInTheDocument()
})

// Somebody else got there first. The outcome they wanted is the outcome that exists, so it is
// information rather than an error against what they did — and is not announced assertively.
it('treats “already done” as information, not as a failure', async () => {
  transport.route(DEACTIVATE, () => problemResponse(409, CUSTOMER_STATUS_TRANSITION_CODE))
  renderActions()
  await confirm('Deactivate this record')

  const told = await screen.findByText(/Somebody else already did that/)
  expect(told).toBeInTheDocument()
  expect(told.closest('[role="alert"]')).toBeNull()
})

it('says a merged record cannot come back, and offers no control at all', () => {
  renderActions(
    aCustomer({
      status: 'Deactivated',
      mergedIntoCustomerId: '0199cc00-0000-7000-8000-000000000002',
      mergedAt: '2026-09-01T10:00:00Z',
    }),
  )

  expect(screen.getByText(/a merge cannot be undone/)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Reactivate this record' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Deactivate this record' })).not.toBeInTheDocument()
})

it('renders a merge refusal as itself if the server gets there first', async () => {
  transport.route(REACTIVATE, () => problemResponse(409, CUSTOMER_ALREADY_MERGED_CODE))
  renderActions(aCustomer({ status: 'Deactivated' }))
  await confirm('Reactivate this record', 'She came back')

  expect(await screen.findByText(/a merge cannot be undone/)).toBeInTheDocument()
})

// The key belongs to the request, and the request is this command with this reason.
it('keeps the key for a retry and mints a new one for a corrected reason', async () => {
  transport.route(DEACTIVATE, () => problemResponse(503, 'platform.unavailable'))
  renderActions()
  const dialog = await confirm('Deactivate this record', 'Created in error')

  await waitFor(() => {
    expect(transport.callsTo(DEACTIVATE)).toHaveLength(1)
  })
  await userEvent.click(within(dialog).getByRole('button', { name: 'Deactivate this record' }))
  await waitFor(() => {
    expect(transport.callsTo(DEACTIVATE)).toHaveLength(2)
  })

  await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), ', no orders')
  await userEvent.click(within(dialog).getByRole('button', { name: 'Deactivate this record' }))
  await waitFor(() => {
    expect(transport.callsTo(DEACTIVATE)).toHaveLength(3)
  })

  const sent = transport.callsTo(DEACTIVATE)
  expect(sent[0]?.headers.get('Idempotency-Key')).toBe(sent[1]?.headers.get('Idempotency-Key'))
  expect(sent[1]?.headers.get('Idempotency-Key')).not.toBe(sent[2]?.headers.get('Idempotency-Key'))
})

/*
 * The window between a command being accepted and the reload landing.
 *
 * `useAdminResource` keeps the record it has on screen while it reloads, so for that moment this
 * component still holds the pre-command version and the pre-command status. Left live, the button
 * would invite a second press that sends the superseded version — and the person would be told
 * somebody else had changed the record, about their own action.
 */
it('stays inert after a command until the reloaded record arrives', async () => {
  transport.route(DEACTIVATE, () =>
    versionedResponse(aCustomer({ status: 'Deactivated' }), 'W/"8"'),
  )
  const { rerender } = renderActions()
  await confirm('Deactivate this record')

  await waitFor(() => {
    expect(transport.callsTo(DEACTIVATE)).toHaveLength(1)
  })

  // The parent has re-rendered with the record it still has: the version this command was sent at.
  // The name carries the busy suffix the design system appends, which is the point — the control is
  // still there and still focusable, and it says for itself that it is working.
  const button = screen.getByRole('button', { name: /^Deactivate this record/ })
  expect(button).toHaveAttribute('aria-disabled', 'true')
  await userEvent.click(button)
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(transport.callsTo(DEACTIVATE)).toHaveLength(1)

  // The reload lands, and the surface is live again — against the record as it now stands.
  rerender(
    <AppIntlProvider locale="en-IN">
      <CustomerStatusActions
        customer={aCustomer({ status: 'Deactivated' })}
        onChanged={() => {
          changed += 1
        }}
        version='W/"8"'
      />
    </AppIntlProvider>,
  )

  const back = screen.getByRole('button', { name: 'Reactivate this record' })
  expect(back).not.toHaveAttribute('aria-disabled')
  expect(back).not.toHaveAttribute('aria-busy')
})

/*
 * The reload that was meant to end the wait never arrives.
 *
 * The command itself succeeded; only the read after it failed. Staying inert would leave a control
 * that cannot be pressed again short of reloading the page, and a status on screen that is known to
 * be stale but presented as current.
 */
it('comes back, and says the record is stale, when the reload fails', async () => {
  transport.route(DEACTIVATE, () =>
    versionedResponse(aCustomer({ status: 'Deactivated' }), 'W/"8"'),
  )
  const { rerender } = renderActions()
  await confirm('Deactivate this record')

  await waitFor(() => {
    expect(transport.callsTo(DEACTIVATE)).toHaveLength(1)
  })

  // The parent still holds the old record, and now also the reason it could not get a new one.
  rerender(
    <AppIntlProvider locale="en-IN">
      <CustomerStatusActions
        customer={aCustomer()}
        onChanged={() => {
          changed += 1
        }}
        reloadFailure={new Error('the network went')}
        version='W/"7"'
      />
    </AppIntlProvider>,
  )

  expect(screen.getByText(/could not be read again just now/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Deactivate this record' })).not.toHaveAttribute(
    'aria-disabled',
  )
})

// The message says to read the record again and decide once more. Until now there was no way to do
// that: retrying sent the same superseded If-Match and was refused identically, forever.
it('asks for the record again when the version has moved', async () => {
  transport.route(DEACTIVATE, () => problemResponse(409, CUSTOMER_VERSION_CONFLICT_CODE))
  renderActions()
  await confirm('Deactivate this record')

  expect(
    await screen.findByText(/Somebody corrected this record while you were reading it/),
  ).toBeInTheDocument()
  // The dialog is closed, because the record being decided about is not the record that exists.
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  // And the parent was asked for a fresh one, so the next attempt carries a version that can win.
  expect(changed).toBe(1)
})

it('blocks the command with an explanation when the connection goes', async () => {
  renderActions()
  expect(screen.getByRole('button', { name: 'Deactivate this record' })).toBeInTheDocument()

  window.dispatchEvent(new Event('offline'))
  await waitFor(() => {
    expect(screen.queryByRole('button', { name: 'Deactivate this record' })).not.toBeInTheDocument()
  })
  expect(screen.getByText(/Needs connection — this will not be queued/)).toBeInTheDocument()

  window.dispatchEvent(new Event('online'))
})

it('has no accessibility violations', async () => {
  const { container } = renderActions()
  await userEvent.click(screen.getByRole('button', { name: 'Deactivate this record' }))
  await screen.findByRole('dialog')

  await expectNoAccessibilityViolations(container)
})
