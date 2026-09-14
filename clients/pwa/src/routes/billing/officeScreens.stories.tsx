import type { Meta, StoryObj } from '@storybook/react-vite'
import { RequirePermission } from '../../admin/RequirePermission'
import { PSEUDO_LOCALE } from '../../i18n/pseudo'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import {
  CASHIER_SESSION_ID,
  ORDER_ID,
  aCashierSession,
  aDispatchException,
  anOrderBalance,
  aReconciliationBatch,
} from '../../billing/testing/fixtures'
import type { StoryRoutes } from '../../billing/testing/storyTransport'
import {
  storyJson,
  storyPending,
  storyProblem,
  withBillingApi,
} from '../../billing/testing/storyTransport'
import { DispatchExceptionApprovalRoute } from './DispatchExceptionApprovalRoute'
import { ReconciliationApprovalRoute } from './ReconciliationApprovalRoute'
import './billing.css'

/**
 * Two of the three "office" screens #165 added — reconciliation approval and dispatch-exception
 * approval — driven against a stubbed API rather than a mock of one, in the shape
 * `counterScreens.stories.tsx` established for the other four.
 *
 * ## Outstanding balances is deliberately not in this file
 *
 * #423 covers all three office screens, but its own scope note is explicit: the outstanding-balances
 * story has to stub `GET /api/v1/billing/outstanding-balances`, the aggregate read #421 adds, and
 * #421 has not merged — `OutstandingBalanceEndpoints.cs` does not exist on `main` yet. Stubbing
 * today's client-side fan-out (`ListInvoices` + one `GetOrderBalance` per invoice) now would mean
 * rewriting this file's outstanding-balances stories the moment #421 lands, which is exactly the
 * churn #423 asks an implementer to avoid. That third screen's stories, and the rest of the
 * forced-state map below, follow once #421 is on `main`.
 *
 * ## Forcing the connection
 *
 * `useNetworkState` holds one snapshot for the whole document, so `link` below sets `navigator.onLine`
 * and dispatches the matching event — the Storybook-safe equivalent of the `vi.spyOn` the tests use.
 * Every story calls it, the offline ones with `false` and every other one with `true`, because the
 * state outlives the story that set it.
 *
 * ## Why "Error" here means the read failing, not the approval
 *
 * Both screens' `AuthProblemAlert` (the read) and `BillingProblemAlert` (the approval) render from
 * `failure` state a person only reaches by first loading successfully and then submitting — a second
 * step no static story render can reach without a real interaction. `counterScreens.stories.tsx`'s own
 * `Error` stories are the same shape: the read failing, which is the one failure a story can show
 * without acting through the screen. The approval's own refusals are the component tests' job.
 *
 * ## The forced-state map
 *
 * `docs/nfr/a11y-checklist.md` section 3.7 asks for the fixture and forced-state list a screen-reader
 * run needs before it starts, so a runner does not invent one mid-run. One row per state per screen
 * this file adds; the four counter screens' map is `counterScreens.stories.tsx`'s own header, and
 * outstanding balances' row follows with #421.
 *
 * | Screen | Loading | Loaded | Empty | Error | Offline | Forbidden | Pseudo-locale |
 * | --- | --- | --- | --- | --- | --- | --- | --- |
 * | Reconciliation approval | `billing-office-screens--reconciliation-approval-loading` | `billing-office-screens--reconciliation-approval` | `billing-office-screens--reconciliation-approval-approved` (already signed off) and `billing-office-screens--reconciliation-approval-no-variance` (closed clean, nothing to approve) | `billing-office-screens--reconciliation-approval-error` | `billing-office-screens--reconciliation-approval-offline` | `billing-office-screens--reconciliation-approval-forbidden` | `billing-office-screens--reconciliation-approval-pseudo-locale` |
 * | Dispatch exception approval | `billing-office-screens--dispatch-exception-approval-loading` | `billing-office-screens--dispatch-exception-approval` | `billing-office-screens--dispatch-exception-approval-empty` (no order on the address) | `billing-office-screens--dispatch-exception-approval-error` | `billing-office-screens--dispatch-exception-approval-offline` | `billing-office-screens--dispatch-exception-approval-forbidden` | `billing-office-screens--dispatch-exception-approval-pseudo-locale` |
 * | Outstanding balances | not yet — #421 | not yet — #421 | not yet — #421 | not yet — #421 | no write on this screen, so no state to force | not yet — #421 | not yet — #421 |
 */
const meta = {
  title: 'Billing/Office screens',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

/**
 * Sets the link state the network hook reads, and tells it so — the same helper
 * `counterScreens.stories.tsx` uses.
 */
function link<T>(online: boolean, render: () => T): T {
  Object.defineProperty(navigator, 'onLine', { configurable: true, value: online })
  window.dispatchEvent(new Event(online ? 'online' : 'offline'))
  return render()
}

interface ScreenOptions {
  readonly online?: boolean
  readonly at?: string
  readonly permissions?: readonly string[]
}

/* Reconciliation approval. ------------------------------------------------------------------------ */

const RECONCILIATION_PATH = '/billing/cashier-sessions/:sessionId/reconciliation'
const RECONCILIATION_AT = `/billing/cashier-sessions/${CASHIER_SESSION_ID}/reconciliation`
const SESSION = `/api/v1/billing/cashier-sessions/${CASHIER_SESSION_ID}/reconciliation`
const APPROVE = `${SESSION}/approve`

const reconciliationApproval = (routes: StoryRoutes, options: ScreenOptions = {}) =>
  link(options.online ?? true, () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.approveReconciliation}>
        <ReconciliationApprovalRoute />
      </RequirePermission>,
      {
        [`GET ${SESSION}`]: () =>
          storyJson(
            aCashierSession({ status: 'Closed', reconciliationBatch: aReconciliationBatch() }),
          ),
        [`POST ${APPROVE}`]: () =>
          storyJson(
            aReconciliationBatch({ status: 'Approved', approvedAt: '2026-09-12T09:00:00.000Z' }),
          ),
        ...routes,
      },
      {
        path: RECONCILIATION_PATH,
        at: options.at ?? RECONCILIATION_AT,
        permissions: options.permissions ?? [BILLING_PERMISSIONS.approveReconciliation],
      },
    ),
  )

/** A closed session with a −₹170.00 variance beyond the branch's threshold, waiting on a second signature. */
export const ReconciliationApproval: Story = { render: () => reconciliationApproval({}) }

export const ReconciliationApprovalLoading: Story = {
  render: () => reconciliationApproval({ [`GET ${SESSION}`]: storyPending }),
}

/** Already signed off by somebody else — "approval not required", not a second control (#423). */
export const ReconciliationApprovalApproved: Story = {
  render: () =>
    reconciliationApproval({
      [`GET ${SESSION}`]: () =>
        storyJson(
          aCashierSession({
            status: 'Closed',
            reconciliationBatch: aReconciliationBatch({
              status: 'Approved',
              approvedAt: '2026-09-12T09:00:00.000Z',
              approvedBy: '0199dd00-0000-7000-8000-000000004001',
            }),
          }),
        ),
    }),
}

/**
 * Closed with no batch at all: the variance never crossed the branch's threshold (OD-24's default of
 * zero makes this rare, not impossible). The same "approval not required" state as the approved
 * story, reached for a different reason — #423's own exception case.
 */
export const ReconciliationApprovalNoVariance: Story = {
  render: () =>
    reconciliationApproval({
      [`GET ${SESSION}`]: () =>
        storyJson(aCashierSession({ status: 'Closed', reconciliationBatch: null })),
    }),
}

/** Another branch's session, or one that never existed, reads alike as not found. */
export const ReconciliationApprovalError: Story = {
  render: () =>
    reconciliationApproval({
      [`GET ${SESSION}`]: () => storyProblem(404, 'billing.cashier-session-not-found'),
    }),
}

/** Approving needs a connection; the batch stays on screen, only the control is replaced. */
export const ReconciliationApprovalOffline: Story = {
  render: () => reconciliationApproval({}, { online: false }),
}

/** Somebody without `payments.approve_reconciliation`: a sentence and who to ask, never a redirect. */
export const ReconciliationApprovalForbidden: Story = {
  render: () => reconciliationApproval({}, { permissions: [] }),
}

export const ReconciliationApprovalPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => reconciliationApproval({}),
}

/* Dispatch exception approval. --------------------------------------------------------------------- */

const DISPATCH_PATH = '/billing/dispatch-exceptions/new'
const DISPATCH_AT = `${DISPATCH_PATH}?orderId=${ORDER_ID}`
const DISPATCH_BALANCE = `/api/v1/billing/orders/${ORDER_ID}/dispatch-exception-balance`
const DISPATCH_EXCEPTIONS = '/api/v1/billing/dispatch-exceptions'

const dispatchExceptionApproval = (routes: StoryRoutes, options: ScreenOptions = {}) =>
  link(options.online ?? true, () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.approveDispatchException}>
        <DispatchExceptionApprovalRoute />
      </RequirePermission>,
      {
        [`GET ${DISPATCH_BALANCE}`]: () => storyJson(anOrderBalance({ orderId: ORDER_ID })),
        [`POST ${DISPATCH_EXCEPTIONS}`]: () => storyJson(aDispatchException()),
        ...routes,
      },
      {
        path: DISPATCH_PATH,
        at: options.at ?? DISPATCH_AT,
        permissions: options.permissions ?? [BILLING_PERMISSIONS.approveDispatchException],
      },
    ),
  )

/** An order named on the address, its outstanding balance shown, ready for the reason and the jobs. */
export const DispatchExceptionApproval: Story = { render: () => dispatchExceptionApproval({}) }

export const DispatchExceptionApprovalLoading: Story = {
  render: () => dispatchExceptionApproval({ [`GET ${DISPATCH_BALANCE}`]: storyPending }),
}

/** No order on the address yet — the reference field and nothing else, not a form with a gap in it. */
export const DispatchExceptionApprovalEmpty: Story = {
  render: () => dispatchExceptionApproval({}, { at: DISPATCH_PATH }),
}

export const DispatchExceptionApprovalError: Story = {
  render: () =>
    dispatchExceptionApproval({
      [`GET ${DISPATCH_BALANCE}`]: () => storyProblem(503, 'platform.unavailable'),
    }),
}

/** Approving needs a connection; every typed field — the order, the jobs, the reason — stays. */
export const DispatchExceptionApprovalOffline: Story = {
  render: () => dispatchExceptionApproval({}, { online: false }),
}

/** Somebody without `billing.approve_dispatch_exception` — the Owner-only key: a sentence, no redirect. */
export const DispatchExceptionApprovalForbidden: Story = {
  render: () => dispatchExceptionApproval({}, { permissions: [] }),
}

export const DispatchExceptionApprovalPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => dispatchExceptionApproval({}),
}
