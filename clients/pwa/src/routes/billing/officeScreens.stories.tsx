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
  anOutstandingBalancePage,
  anOutstandingBalanceRow,
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
import { OutstandingBalancesRoute } from './OutstandingBalancesRoute'
import { ReconciliationApprovalRoute } from './ReconciliationApprovalRoute'
import './billing.css'

/**
 * The three "office" screens #165 added — outstanding balances, reconciliation approval and
 * dispatch-exception approval — driven against a stubbed API rather than a mock of one, in the shape
 * `counterScreens.stories.tsx` established for the other four (#423).
 *
 * Outstanding balances stubs `GET /api/v1/billing/outstanding-balances`, the aggregate read #421
 * added; that issue merged first, so this file no longer carries the "not yet" note its own earlier
 * revision did.
 *
 * ## Forcing the connection
 *
 * `useNetworkState` holds one snapshot for the whole document, so `link` below sets `navigator.onLine`
 * and dispatches the matching event — the Storybook-safe equivalent of the `vi.spyOn` the tests use.
 * Every story calls it, the offline ones with `false` and every other one with `true`, because the
 * state outlives the story that set it. Outstanding balances has no write, so it has no offline
 * story — say so once, where its offline story would have been, rather than adding one that blocks
 * nothing (the same reasoning `counterScreens.stories.tsx` records for payment detail).
 *
 * ## Why "Error" here means the read failing, not the approval
 *
 * The two approval screens' `AuthProblemAlert` (the read) and `BillingProblemAlert` (the approval)
 * render from `failure` state a person only reaches by first loading successfully and then
 * submitting — a second step no static story render can reach without a real interaction.
 * `counterScreens.stories.tsx`'s own `Error` stories are the same shape: the read failing, which is
 * the one failure a story can show without acting through the screen. The approvals' own refusals are
 * the component tests' job. Outstanding balances only ever reads, so its `Error` story is simply that
 * read failing — there is no second, write-side failure to distinguish it from.
 *
 * ## The forced-state map
 *
 * `docs/nfr/a11y-checklist.md` section 3.7 asks for the fixture and forced-state list a screen-reader
 * run needs before it starts, so a runner does not invent one mid-run. One row per state for all
 * seven billing screens — the four counter screens `counterScreens.stories.tsx` (#422/#514) adds, as
 * well as the three here — each naming the Storybook story id that reaches it, or in one line why the
 * state is unreachable.
 *
 * | Screen | Loading | Loaded | Empty | Error | Offline | Forbidden | Pseudo-locale |
 * | --- | --- | --- | --- | --- | --- | --- | --- |
 * | Take payment | `billing-counter-screens--take-payment-loading` | `billing-counter-screens--take-payment` | `billing-counter-screens--take-payment-empty` (no `orderId` on the address) | `billing-counter-screens--take-payment-error` | `billing-counter-screens--take-payment-offline` | `billing-counter-screens--take-payment-forbidden` | `billing-counter-screens--take-payment-pseudo-locale` |
 * | Allocate advance | `billing-counter-screens--allocate-advance-loading` | `billing-counter-screens--allocate-advance` | `billing-counter-screens--allocate-advance-empty` (nothing held) | `billing-counter-screens--allocate-advance-error` | `billing-counter-screens--allocate-advance-offline` | `billing-counter-screens--allocate-advance-forbidden` | `billing-counter-screens--allocate-advance-pseudo-locale` |
 * | Payment detail | `billing-counter-screens--payment-detail-loading` | `billing-counter-screens--payment-detail` | `billing-counter-screens--payment-detail-empty` (nothing applied or held) | `billing-counter-screens--payment-detail-error` | no write on this screen, so no blocked-action state | `billing-counter-screens--payment-detail-forbidden` | `billing-counter-screens--payment-detail-pseudo-locale` |
 * | Cashier session | `billing-counter-screens--cashier-session-loading` | `billing-counter-screens--cashier-session` | `billing-counter-screens--cashier-session-empty` (no open session) | `billing-counter-screens--cashier-session-error` | `billing-counter-screens--cashier-session-offline` | `billing-counter-screens--cashier-session-forbidden` | `billing-counter-screens--cashier-session-pseudo-locale` |
 * | Outstanding balances | `billing-office-screens--outstanding-balances-loading` | `billing-office-screens--outstanding-balances` | `billing-office-screens--outstanding-balances-empty` | `billing-office-screens--outstanding-balances-error` | no write on this screen, so no blocked-action state | `billing-office-screens--outstanding-balances-forbidden` | `billing-office-screens--outstanding-balances-pseudo-locale` |
 * | Reconciliation approval | `billing-office-screens--reconciliation-approval-loading` | `billing-office-screens--reconciliation-approval` | `billing-office-screens--reconciliation-approval-approved` (already signed off) and `billing-office-screens--reconciliation-approval-no-variance` (closed clean, nothing to approve) | `billing-office-screens--reconciliation-approval-error` | `billing-office-screens--reconciliation-approval-offline` | `billing-office-screens--reconciliation-approval-forbidden` | `billing-office-screens--reconciliation-approval-pseudo-locale` |
 * | Dispatch exception approval | `billing-office-screens--dispatch-exception-approval-loading` | `billing-office-screens--dispatch-exception-approval` | `billing-office-screens--dispatch-exception-approval-empty` (no order on the address) | `billing-office-screens--dispatch-exception-approval-error` | `billing-office-screens--dispatch-exception-approval-offline` | `billing-office-screens--dispatch-exception-approval-forbidden` | `billing-office-screens--dispatch-exception-approval-pseudo-locale` |
 *
 * Three screens also carry a 320 px reflow-floor story, beyond the table above: `TakePayment` and
 * `CashierSession` (`billing-counter-screens--take-payment-reflow-floor`,
 * `billing-counter-screens--cashier-session-reflow-floor`) as the two phone-first counter screens, and
 * `billing-office-screens--outstanding-balances-reflow-floor` here — the one screen of this group with
 * a wide table, where `DataTable`'s `hideWhenNarrow` columns are the thing a reviewer has to see
 * collapse. The two approval screens are single-column forms, already covered by the text-size and
 * locale toolbars, so they carry no reflow-floor story of their own.
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

/* Outstanding balances. ------------------------------------------------------------------------ */

const OUTSTANDING_PATH = '/billing/outstanding'
const OUTSTANDING_FIRST_PAGE = '/api/v1/billing/outstanding-balances?limit=20'
const OUTSTANDING_SECOND_PAGE =
  '/api/v1/billing/outstanding-balances?cursor=story-cursor-2&limit=20'

const outstandingBalances = (routes: StoryRoutes, options: ScreenOptions = {}) =>
  link(options.online ?? true, () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.createInvoice}>
        <OutstandingBalancesRoute />
      </RequirePermission>,
      {
        [`GET ${OUTSTANDING_FIRST_PAGE}`]: () =>
          storyJson(anOutstandingBalancePage({ nextCursor: 'story-cursor-2' })),
        [`GET ${OUTSTANDING_SECOND_PAGE}`]: () =>
          storyJson(
            anOutstandingBalancePage({
              rows: [
                anOutstandingBalanceRow({
                  invoiceId: '0199dd00-0000-7000-8000-000000006101',
                  invoiceNumber: 'I-CBE01-2627-000900',
                  orderNumber: 'O-CBE01-2627-000900',
                }),
              ],
              nextCursor: null,
            }),
          ),
        ...routes,
      },
      {
        path: OUTSTANDING_PATH,
        at: options.at ?? OUTSTANDING_PATH,
        permissions: options.permissions ?? [BILLING_PERMISSIONS.createInvoice],
      },
    ),
  )

/**
 * Every posted invoice at this branch with money still owed, one page at a time — a second page
 * waiting behind Show more, stubbed here so the control actually works if a reviewer clicks it.
 */
export const OutstandingBalances: Story = { render: () => outstandingBalances({}) }

export const OutstandingBalancesLoading: Story = {
  render: () => outstandingBalances({ [`GET ${OUTSTANDING_FIRST_PAGE}`]: storyPending }),
}

/** Every posted invoice at this branch is paid in full — the scan exhausted with nothing owed. */
export const OutstandingBalancesEmpty: Story = {
  render: () =>
    outstandingBalances({
      [`GET ${OUTSTANDING_FIRST_PAGE}`]: () =>
        storyJson(anOutstandingBalancePage({ rows: [], nextCursor: null })),
    }),
}

export const OutstandingBalancesError: Story = {
  render: () =>
    outstandingBalances({
      [`GET ${OUTSTANDING_FIRST_PAGE}`]: () => storyProblem(503, 'platform.unavailable'),
    }),
}

/**
 * No offline story: this screen only reads, so `OfflineBlockedAction` never applies here — see the
 * file's own header note.
 */

/** Somebody without `billing.create_invoice`: a sentence and who to ask, never a redirect. */
export const OutstandingBalancesForbidden: Story = {
  render: () => outstandingBalances({}, { permissions: [] }),
}

export const OutstandingBalancesPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => outstandingBalances({}),
}

/**
 * At the 320 px reflow floor, over the same render as the loaded story: the `hideWhenNarrow` invoice
 * and total columns are gone, the table scrolls inside its own container, and the page itself does
 * not scroll horizontally.
 */
export const OutstandingBalancesReflowFloor: Story = {
  globals: { viewport: { value: 'reflowFloor' } },
  render: () => outstandingBalances({}),
}
