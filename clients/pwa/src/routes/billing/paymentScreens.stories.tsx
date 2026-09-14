import type { Meta, StoryObj } from '@storybook/react-vite'
import { RequirePermission } from '../../admin/RequirePermission'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import {
  CASHIER_ID,
  CASHIER_SESSION_ID,
  ORDER_ID,
  PAYMENT_ID,
  aCashierSession,
  aPayment,
  aReconciliationBatch,
  anAvailablePaymentModeList,
  anInvoiceBalance,
  anInvoicePage,
  anInvoiceSummary,
  anOrderBalance,
} from '../../billing/testing/fixtures'
import {
  storyJson,
  storyPending,
  storyProblem,
  withBillingApi,
} from '../../billing/testing/storyTransport'
import { PSEUDO_LOCALE } from '../../i18n/pseudo'
import { AllocateAdvanceRoute } from './AllocateAdvanceRoute'
import { CashierSessionRoute } from './CashierSessionRoute'
import { DispatchExceptionApprovalRoute } from './DispatchExceptionApprovalRoute'
import { OutstandingBalancesRoute } from './OutstandingBalancesRoute'
import { PaymentDetailRoute } from './PaymentDetailRoute'
import { ReconciliationApprovalRoute } from './ReconciliationApprovalRoute'
import { TakePaymentRoute } from './TakePaymentRoute'
import './billing.css'

/**
 * The screens #217 (Refs #165) added — take a payment, allocate an advance, outstanding balances,
 * the cashier session, reconciliation approval and dispatch exception approval — in each of the
 * states Definition of Done item 7 asks for. #217 disclosed these stories as owed rather than
 * shipping them silently; this file is that debt paid (#216).
 *
 * The shape is `routes/billing/invoiceScreens.stories.tsx`'s own: every story renders the **real
 * screen** against a stubbed `fetch`, never a state component with billing-shaped words typed into
 * it. Unlike that file, every screen here reads `useNetworkState` for at least one of its acts —
 * Billing is online-only (plan Section 4.6) — so an explicit offline story is included wherever the
 * screen has one, following `measurementScreens.stories.tsx`'s `link` helper rather than omitting it.
 *
 * A pseudo-locale story (`en-XA`, `docs/nfr/accessibility-localisation.md` section 4.2's 40% growth
 * check) is included per screen instead of relying on the toolbar alone, matching
 * `catalogScreens.stories.tsx` and `measurementScreens.stories.tsx`.
 */
const meta = {
  title: 'Billing/Payments',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

const BILLING = '/api/v1/billing'
const PAYMENT_MODES = `${BILLING}/payment-modes/available`
const orderBalanceUrl = (orderId: string) => `${BILLING}/orders/${orderId}/balance`
const dispatchBalanceUrl = (orderId: string) =>
  `${BILLING}/orders/${orderId}/dispatch-exception-balance`
const paymentUrl = (id: string) => `${BILLING}/payments/${id}`
const sessionReconciliationUrl = (id: string) => `${BILLING}/cashier-sessions/${id}/reconciliation`

/**
 * Sets the link state `useNetworkState` reads, the same way `measurementScreens.stories.tsx` and
 * `catalogDesignPickerScreens.stories.tsx` do: the state outlives the story that set it, so every
 * story sets it rather than only the offline one.
 */
function link<T>(online: boolean, render: () => T): T {
  Object.defineProperty(navigator, 'onLine', { configurable: true, value: online })
  window.dispatchEvent(new Event(online ? 'online' : 'offline'))
  return render()
}

/**
 * A full `GET /api/v1/me` body, since `billing/testing/storyTransport.tsx`'s own `storyUser` is not
 * exported — the same reason `invoiceScreens.stories.tsx` carries its own `withNoPermissions()`.
 * `userId` defaults to the same identifier `withBillingApi` itself answers with, so a fixture that
 * does not care who is signed in (most of these screens) never has to pass it; `CashierSessionRoute`
 * matches its open session to the signed-in cashier by that id, so its stories override it.
 */
function meWith(
  overrides: { readonly userId?: string; readonly permissions?: readonly string[] } = {},
) {
  return {
    userId: overrides.userId ?? '0199bb00-0000-7000-8000-0000000000f1',
    userName: 'cashier.story',
    displayName: 'Anitha (counter)',
    email: 'cashier.story@example.invalid',
    status: 'Active',
    organisationId: '0199bb00-0000-7000-8000-0000000000ff',
    branchId: '0199bb00-0000-7000-8000-0000000000aa',
    permissions: overrides.permissions ?? [],
    security: {
      mfaEnrolment: 'Enrolled',
      mustChangePassword: false,
      mfaSatisfied: true,
      lastStrongAuthenticationAt: '2026-09-12T09:00:00.000Z',
      factors: { authenticator: true, recoveryCode: true, passkey: false },
      unusedRecoveryCodes: 8,
    },
    preferences: {
      locale: 'en-IN',
      timeZoneId: 'Asia/Kolkata',
      theme: 'System',
      density: 'Comfortable',
      reducedMotion: false,
      landingRoute: null,
    },
    session: {
      idleExpiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
      absoluteExpiresAt: new Date(Date.now() + 11 * 60 * 60 * 1000).toISOString(),
      warningLeadSeconds: 120,
      mfaSatisfied: true,
    },
  }
}

/* Taking a payment. ---------------------------------------------------------------------------- */

const PAYMENT_AT = `/billing/payments/new?orderId=${ORDER_ID}&orderNumber=${encodeURIComponent('O-CBE01-2627-000512')}`
const PAYMENT_OPTIONS = { path: '/billing/payments/new', at: PAYMENT_AT }

const takePayment = (
  routes: Parameters<typeof withBillingApi>[1],
  options: Parameters<typeof withBillingApi>[2] = PAYMENT_OPTIONS,
  online = true,
) =>
  link(online, () =>
    withBillingApi(
      <TakePaymentRoute />,
      {
        [`GET ${orderBalanceUrl(ORDER_ID)}`]: () => storyJson(anOrderBalance()),
        [`GET ${PAYMENT_MODES}`]: () => storyJson(anAvailablePaymentModeList()),
        ...routes,
      },
      { ...PAYMENT_OPTIONS, ...options },
    ),
  )

export const TakePaymentLoading: Story = {
  render: () =>
    takePayment({
      [`GET ${orderBalanceUrl(ORDER_ID)}`]: storyPending,
      [`GET ${PAYMENT_MODES}`]: storyPending,
    }),
}

/** Reached with no order named in the address — a sentence, never a blank form. */
export const TakePaymentEmpty: Story = {
  render: () => takePayment({}, { path: '/billing/payments/new', at: '/billing/payments/new' }),
}

export const TakePayment: Story = { render: () => takePayment({}) }

export const TakePaymentError: Story = {
  render: () =>
    takePayment({
      [`GET ${orderBalanceUrl(ORDER_ID)}`]: () => storyProblem(503, 'platform.unavailable'),
    }),
}

/** Somebody without `payments.record`: a sentence and who to ask, never a redirect. */
export const TakePaymentForbidden: Story = {
  render: () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.recordPayment}>
        <TakePaymentRoute />
      </RequirePermission>,
      { 'GET /api/v1/me': () => storyJson(meWith()) },
      { ...PAYMENT_OPTIONS, permissions: [] },
    ),
}

/** Recording a payment needs a connection; the screen says so and keeps every typed value. */
export const TakePaymentOffline: Story = {
  render: () => takePayment({}, PAYMENT_OPTIONS, false),
}

/**
 * At 40% growth: the balance banner, the segmented payment-mode buttons and the cash-tendered
 * change line all carry a formatted amount beside a sentence, which is where English-only spacing
 * breaks first.
 */
export const TakePaymentPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => takePayment({}),
}

/* Allocating an advance by hand. ---------------------------------------------------------------- */

const ALLOCATE_OPTIONS = {
  path: '/billing/payments/:paymentId/allocate',
  at: `/billing/payments/${PAYMENT_ID}/allocate`,
}

const allocate = (
  payment: ReturnType<typeof aPayment>,
  routes: Parameters<typeof withBillingApi>[1] = {},
  options: Parameters<typeof withBillingApi>[2] = ALLOCATE_OPTIONS,
) =>
  withBillingApi(
    <AllocateAdvanceRoute />,
    {
      [`GET ${paymentUrl(PAYMENT_ID)}`]: () => storyJson(payment),
      [`GET ${orderBalanceUrl(ORDER_ID)}`]: () =>
        storyJson(anOrderBalance({ invoices: [anInvoiceBalance()] })),
      ...routes,
    },
    { ...ALLOCATE_OPTIONS, ...options },
  )

export const AllocateAdvanceLoading: Story = {
  render: () => allocate(aPayment(), { [`GET ${paymentUrl(PAYMENT_ID)}`]: storyPending }),
}

export const AllocateAdvance: Story = {
  render: () => allocate(aPayment({ unappliedAdvance: 500 })),
}

/** Nothing left held against this payment — the reason there is no invoice to choose from. */
export const AllocateAdvanceEmpty: Story = {
  render: () => allocate(aPayment({ unappliedAdvance: 0 })),
}

export const AllocateAdvanceError: Story = {
  render: () =>
    allocate(aPayment(), {
      [`GET ${paymentUrl(PAYMENT_ID)}`]: () => storyProblem(503, 'platform.unavailable'),
    }),
}

/** Somebody without `payments.allocate_manual`: a sentence and who to ask, never a redirect. */
export const AllocateAdvanceForbidden: Story = {
  render: () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.allocateAdvanceManual}>
        <AllocateAdvanceRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson(meWith()),
        [`GET ${paymentUrl(PAYMENT_ID)}`]: () => storyJson(aPayment({ unappliedAdvance: 500 })),
        [`GET ${orderBalanceUrl(ORDER_ID)}`]: () =>
          storyJson(anOrderBalance({ invoices: [anInvoiceBalance()] })),
      },
      { ...ALLOCATE_OPTIONS, permissions: [] },
    ),
}

/** Moving a held advance needs a connection; the screen says so and keeps the chosen invoice. */
export const AllocateAdvanceOffline: Story = {
  render: () => link(false, () => allocate(aPayment({ unappliedAdvance: 500 }))),
}

/** At 40% growth: the invoice picker's option text carries an amount beside its number. */
export const AllocateAdvancePseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => allocate(aPayment({ unappliedAdvance: 500 })),
}

/* Outstanding balances. -------------------------------------------------------------------------- */

const OUTSTANDING_OPTIONS = { path: '/billing/outstanding', at: '/billing/outstanding' }
const INVOICES = `${BILLING}/invoices`

const SECOND_ORDER_ID = '0199dd00-0000-7000-8000-000000006001'
const SECOND_INVOICE = anInvoiceSummary({
  invoiceId: '0199dd00-0000-7000-8000-000000006002',
  orderId: SECOND_ORDER_ID,
  orderNumber: 'O-CBE01-2627-000731',
  customerDisplayName: 'Devi (owner)',
  invoiceNumber: 'I-CBE01-2627-000844',
  grandTotal: 1200,
})

const outstanding = (routes: Parameters<typeof withBillingApi>[1]) =>
  withBillingApi(<OutstandingBalancesRoute />, routes, OUTSTANDING_OPTIONS)

export const OutstandingBalancesLoading: Story = {
  render: () => outstanding({ [`GET ${INVOICES}?status=Posted&limit=50`]: storyPending }),
}

/** No posted invoice at this branch still owes anything — the fact, not a table with nothing in it. */
export const OutstandingBalancesEmpty: Story = {
  render: () =>
    outstanding({
      [`GET ${INVOICES}?status=Posted&limit=50`]: () => storyJson(anInvoicePage({ invoices: [] })),
    }),
}

export const OutstandingBalances: Story = {
  render: () =>
    outstanding({
      [`GET ${INVOICES}?status=Posted&limit=50`]: () =>
        storyJson(anInvoicePage({ invoices: [anInvoiceSummary(), SECOND_INVOICE] })),
      [`GET ${orderBalanceUrl(ORDER_ID)}`]: () => storyJson(anOrderBalance()),
      [`GET ${orderBalanceUrl(SECOND_ORDER_ID)}`]: () =>
        storyJson(
          anOrderBalance({
            orderId: SECOND_ORDER_ID,
            outstanding: 1200,
            invoices: [
              anInvoiceBalance({
                invoiceId: SECOND_INVOICE.invoiceId,
                invoiceNumber: 'I-CBE01-2627-000844',
                charges: 1200,
                allocated: 0,
                outstanding: 1200,
              }),
            ],
          }),
        ),
    }),
}

export const OutstandingBalancesError: Story = {
  render: () =>
    outstanding({
      [`GET ${INVOICES}?status=Posted&limit=50`]: () => storyProblem(503, 'platform.unavailable'),
    }),
}

/** Somebody without `billing.create_invoice`, the key this reads behind: a sentence, no redirect. */
export const OutstandingBalancesForbidden: Story = {
  render: () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.createInvoice}>
        <OutstandingBalancesRoute />
      </RequirePermission>,
      { 'GET /api/v1/me': () => storyJson(meWith()) },
      { ...OUTSTANDING_OPTIONS, permissions: [] },
    ),
}

/*
 * No offline story: this screen only ever reads, and reading with no connection is the ordinary
 * `RetryableError` a failed `fetch` already produces — there is no write, and so no act this screen
 * has to say will not be queued, unlike every other screen in this file.
 */

export const OutstandingBalancesPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () =>
    outstanding({
      [`GET ${INVOICES}?status=Posted&limit=50`]: () =>
        storyJson(anInvoicePage({ invoices: [anInvoiceSummary(), SECOND_INVOICE] })),
      [`GET ${orderBalanceUrl(ORDER_ID)}`]: () => storyJson(anOrderBalance()),
      [`GET ${orderBalanceUrl(SECOND_ORDER_ID)}`]: () =>
        storyJson(
          anOrderBalance({
            orderId: SECOND_ORDER_ID,
            outstanding: 1200,
            invoices: [
              anInvoiceBalance({
                invoiceId: SECOND_INVOICE.invoiceId,
                invoiceNumber: 'I-CBE01-2627-000844',
                charges: 1200,
                allocated: 0,
                outstanding: 1200,
              }),
            ],
          }),
        ),
    }),
}

/* The cashier session. --------------------------------------------------------------------------- */

const CASHIER_OPTIONS = { path: '/billing/cashier', at: '/billing/cashier' }
const CASHIER_SESSIONS = `${BILLING}/cashier-sessions`

/**
 * `CashierSessionRoute` matches the open session it lists against the signed-in cashier's own id
 * (Codex review, PR #217), so every story in this section signs in as the same cashier the fixture's
 * default `cashierId` (`CASHIER_ID`) already carries, rather than the file's usual signed-in user.
 */
const cashier = (
  openSessions: readonly ReturnType<typeof aCashierSession>[],
  routes: Parameters<typeof withBillingApi>[1] = {},
) =>
  withBillingApi(
    <CashierSessionRoute />,
    {
      'GET /api/v1/me': () =>
        storyJson(
          meWith({ userId: CASHIER_ID, permissions: [BILLING_PERMISSIONS.cashierSession] }),
        ),
      [`GET ${CASHIER_SESSIONS}?status=Open`]: () => storyJson(openSessions),
      [`GET ${PAYMENT_MODES}`]: () => storyJson(anAvailablePaymentModeList()),
      ...routes,
    },
    CASHIER_OPTIONS,
  )

export const CashierSessionLoading: Story = {
  render: () => cashier([], { [`GET ${CASHIER_SESSIONS}?status=Open`]: storyPending }),
}

/** Nobody has opened the drawer yet today — the opening-float form, not an empty list. */
export const CashierSessionEmpty: Story = { render: () => cashier([]) }

/** Open, with the denomination count sheet computing the counted total as it is typed. */
export const CashierSessionOpen: Story = { render: () => cashier([aCashierSession()]) }

export const CashierSessionError: Story = {
  render: () =>
    cashier([], {
      [`GET ${CASHIER_SESSIONS}?status=Open`]: () => storyProblem(503, 'platform.unavailable'),
    }),
}

/** Somebody without `payments.session`: a sentence and who to ask, never a redirect. */
export const CashierSessionForbidden: Story = {
  render: () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.cashierSession}>
        <CashierSessionRoute />
      </RequirePermission>,
      { 'GET /api/v1/me': () => storyJson(meWith()) },
      { ...CASHIER_OPTIONS, permissions: [] },
    ),
}

/** Opening and closing both need a connection; the count sheet stays exactly as counted. */
export const CashierSessionOffline: Story = {
  render: () => link(false, () => cashier([aCashierSession()])),
}

/**
 * At 40% growth: the count sheet's note and coin labels, and the mode-total rows, are the densest
 * repeating row layout in Billing.
 */
export const CashierSessionPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => cashier([aCashierSession()]),
}

/* Reconciliation approval. ------------------------------------------------------------------------ */

const RECONCILIATION_OPTIONS = {
  path: '/billing/cashier-sessions/:sessionId/reconciliation',
  at: `/billing/cashier-sessions/${CASHIER_SESSION_ID}/reconciliation`,
}

const reconciliation = (session: ReturnType<typeof aCashierSession>) =>
  withBillingApi(
    <ReconciliationApprovalRoute />,
    { [`GET ${sessionReconciliationUrl(session.id)}`]: () => storyJson(session) },
    RECONCILIATION_OPTIONS,
  )

export const ReconciliationApprovalLoading: Story = {
  render: () =>
    withBillingApi(
      <ReconciliationApprovalRoute />,
      { [`GET ${sessionReconciliationUrl(CASHIER_SESSION_ID)}`]: storyPending },
      RECONCILIATION_OPTIONS,
    ),
}

/** A closed session with a variance beyond the branch's threshold, waiting on a second signature. */
export const ReconciliationApproval: Story = {
  render: () => reconciliation(aCashierSession({ reconciliationBatch: aReconciliationBatch() })),
}

/** Nothing to approve — closed clean, or already signed off by somebody else. */
export const ReconciliationApprovalEmpty: Story = {
  render: () =>
    reconciliation(
      aCashierSession({
        reconciliationBatch: aReconciliationBatch({
          status: 'Approved',
          approvedAt: '2026-09-12T09:00:00.000Z',
          approvedBy: '0199dd00-0000-7000-8000-000000004001',
        }),
      }),
    ),
}

export const ReconciliationApprovalError: Story = {
  render: () =>
    withBillingApi(
      <ReconciliationApprovalRoute />,
      {
        [`GET ${sessionReconciliationUrl(CASHIER_SESSION_ID)}`]: () =>
          storyProblem(503, 'platform.unavailable'),
      },
      RECONCILIATION_OPTIONS,
    ),
}

/** Somebody without `payments.approve_reconciliation`: a sentence and who to ask, never a redirect. */
export const ReconciliationApprovalForbidden: Story = {
  render: () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.approveReconciliation}>
        <ReconciliationApprovalRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson(meWith()),
        [`GET ${sessionReconciliationUrl(CASHIER_SESSION_ID)}`]: () =>
          storyJson(aCashierSession({ reconciliationBatch: aReconciliationBatch() })),
      },
      { ...RECONCILIATION_OPTIONS, permissions: [] },
    ),
}

/** Approving a variance needs a connection; the screen says so and the batch stays on screen. */
export const ReconciliationApprovalOffline: Story = {
  render: () =>
    link(false, () =>
      reconciliation(aCashierSession({ reconciliationBatch: aReconciliationBatch() })),
    ),
}

/** At 40% growth: the per-mode variance list is the row layout most likely to wrap badly. */
export const ReconciliationApprovalPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => reconciliation(aCashierSession({ reconciliationBatch: aReconciliationBatch() })),
}

/* Dispatch exception approval. --------------------------------------------------------------------- */

const DISPATCH_OPTIONS = {
  path: '/billing/dispatch-exceptions/new',
  at: '/billing/dispatch-exceptions/new',
}
const dispatchAt = (orderId: string) => `/billing/dispatch-exceptions/new?orderId=${orderId}`

const dispatch = (
  routes: Parameters<typeof withBillingApi>[1],
  options: Parameters<typeof withBillingApi>[2] = DISPATCH_OPTIONS,
) => withBillingApi(<DispatchExceptionApprovalRoute />, routes, { ...DISPATCH_OPTIONS, ...options })

export const DispatchExceptionApprovalLoading: Story = {
  render: () =>
    dispatch(
      { [`GET ${dispatchBalanceUrl(ORDER_ID)}`]: storyPending },
      { path: DISPATCH_OPTIONS.path, at: dispatchAt(ORDER_ID) },
    ),
}

/** Reached with no order named yet — the blank form, which is what "empty" is on a screen this shape. */
export const DispatchExceptionApprovalEmpty: Story = { render: () => dispatch({}) }

/** An order named, its outstanding balance shown, ready for the reason and the job references. */
export const DispatchExceptionApproval: Story = {
  render: () =>
    dispatch(
      { [`GET ${dispatchBalanceUrl(ORDER_ID)}`]: () => storyJson(anOrderBalance()) },
      { path: DISPATCH_OPTIONS.path, at: dispatchAt(ORDER_ID) },
    ),
}

export const DispatchExceptionApprovalError: Story = {
  render: () =>
    dispatch(
      {
        [`GET ${dispatchBalanceUrl(ORDER_ID)}`]: () => storyProblem(503, 'platform.unavailable'),
      },
      { path: DISPATCH_OPTIONS.path, at: dispatchAt(ORDER_ID) },
    ),
}

/** Somebody without `billing.approve_dispatch_exception` — the Owner-only key: a sentence, no redirect. */
export const DispatchExceptionApprovalForbidden: Story = {
  render: () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.approveDispatchException}>
        <DispatchExceptionApprovalRoute />
      </RequirePermission>,
      { 'GET /api/v1/me': () => storyJson(meWith()) },
      { ...DISPATCH_OPTIONS, permissions: [] },
    ),
}

/** Approving an exception needs a connection; the screen says so and every typed field stays. */
export const DispatchExceptionApprovalOffline: Story = {
  render: () =>
    link(false, () =>
      dispatch(
        { [`GET ${dispatchBalanceUrl(ORDER_ID)}`]: () => storyJson(anOrderBalance()) },
        { path: DISPATCH_OPTIONS.path, at: dispatchAt(ORDER_ID) },
      ),
    ),
}

/** At 40% growth: the reason fields and the balance banner are free text beside a formatted amount. */
export const DispatchExceptionApprovalPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () =>
    dispatch(
      { [`GET ${dispatchBalanceUrl(ORDER_ID)}`]: () => storyJson(anOrderBalance()) },
      { path: DISPATCH_OPTIONS.path, at: dispatchAt(ORDER_ID) },
    ),
}

/* The payment receipt, read back. ------------------------------------------------------------------ */

const PAYMENT_DETAIL_OPTIONS = {
  path: '/billing/payments/:paymentId',
  at: `/billing/payments/${PAYMENT_ID}`,
}

export const PaymentDetailLoading: Story = {
  render: () =>
    withBillingApi(
      <PaymentDetailRoute />,
      { [`GET ${paymentUrl(PAYMENT_ID)}`]: storyPending },
      PAYMENT_DETAIL_OPTIONS,
    ),
}

export const PaymentDetail: Story = {
  render: () =>
    withBillingApi(
      <PaymentDetailRoute />,
      { [`GET ${paymentUrl(PAYMENT_ID)}`]: () => storyJson(aPayment()) },
      PAYMENT_DETAIL_OPTIONS,
    ),
}

/*
 * No empty story: one payment either reads or it does not — the same reason
 * `invoiceScreens.stories.tsx`'s `InvoiceDetailRoute` carries no Empty story of its own.
 */

/** A payment recorded at another branch, or an identifier that never existed, reads alike as 404. */
export const PaymentDetailError: Story = {
  render: () =>
    withBillingApi(
      <PaymentDetailRoute />,
      {
        [`GET ${paymentUrl(PAYMENT_ID)}`]: () => storyProblem(404, 'billing.payment-not-found'),
      },
      PAYMENT_DETAIL_OPTIONS,
    ),
}

/** Somebody without `payments.record`: a sentence and who to ask, never a redirect. */
export const PaymentDetailForbidden: Story = {
  render: () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.recordPayment}>
        <PaymentDetailRoute />
      </RequirePermission>,
      { 'GET /api/v1/me': () => storyJson(meWith()) },
      { ...PAYMENT_DETAIL_OPTIONS, permissions: [] },
    ),
}

/*
 * No offline story: reading back a receipt already issued makes no write and blocks nothing — the
 * same reason `OutstandingBalancesRoute` above carries none.
 */

export const PaymentDetailPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () =>
    withBillingApi(
      <PaymentDetailRoute />,
      { [`GET ${paymentUrl(PAYMENT_ID)}`]: () => storyJson(aPayment()) },
      PAYMENT_DETAIL_OPTIONS,
    ),
}
