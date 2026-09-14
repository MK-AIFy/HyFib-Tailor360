import type { Meta, StoryObj } from '@storybook/react-vite'
import { RequirePermission } from '../../admin/RequirePermission'
import { PSEUDO_LOCALE } from '../../i18n/pseudo'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import {
  CASHIER_SESSION_ID,
  ORDER_ID,
  PAYMENT_ID,
  aCashierSession,
  anAvailablePaymentModeList,
  anOrderBalance,
  aPayment,
} from '../../billing/testing/fixtures'
import type { StoryRoutes } from '../../billing/testing/storyTransport'
import {
  STORY_USER_ID,
  storyJson,
  storyPending,
  storyProblem,
  withBillingApi,
} from '../../billing/testing/storyTransport'
import { AllocateAdvanceRoute } from './AllocateAdvanceRoute'
import { CashierSessionRoute } from './CashierSessionRoute'
import { PaymentDetailRoute } from './PaymentDetailRoute'
import { TakePaymentRoute } from './TakePaymentRoute'
import './billing.css'

/**
 * The four screens a cashier uses at the counter (#165), driven against a stubbed API rather than a
 * mock of one — the same reasoning `invoiceScreens.stories.tsx` and `measurementScreens.stories.tsx`
 * record: the value is in these being the real screens, so a reviewer is looking at this screen's own
 * branches rather than a state component with billing-shaped words typed into it.
 *
 * ## Offline is one of the five here
 *
 * Unlike the invoice register and detail screens (`invoiceScreens.stories.tsx`'s own note on why it
 * leaves offline out), taking a payment and running the drawer are exactly the acts `CLAUDE.md`
 * section 6 names as online-only, and the counter is where "no signal" actually happens. Payment
 * detail is read-only, so it has no offline story — say so once, where its offline story would have
 * been, rather than adding one that blocks nothing.
 *
 * ## Forcing the connection
 *
 * `useNetworkState` holds one snapshot for the whole document (its own module comment says why), so
 * `link` below sets `navigator.onLine` and dispatches the matching event — the Storybook-safe
 * equivalent of the `vi.spyOn` the tests use — exactly the way `measurementScreens.stories.tsx` and
 * `catalogDesignPickerScreens.stories.tsx` already do it. Every story calls it, the offline ones with
 * `false` and every other one with `true`, because the state outlives the story that set it and a
 * reviewer clicking from the offline story to the next one must not carry it along.
 *
 * ## The cashier session's own trap
 *
 * `CashierSessionRoute` picks the caller's own open session by matching `cashierId` against the
 * signed-in user, never by list position (a defect PR #217 already fixed once). So a story with an
 * "open" session has to give that session `cashierId: STORY_USER_ID` — the signed-in story user's own
 * id — or it silently renders the "no open session" branch instead.
 */
const meta = {
  title: 'Billing/Counter screens',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

/**
 * Sets the link state the network hook reads, and tells it so.
 *
 * `navigator.onLine` is read once and then followed through the `online` and `offline` events, so a
 * story has to do both — and every story sets it, because the state outlives the story that set it and
 * a reviewer clicking from the offline story to the next one must not carry it along.
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

/* Take payment. --------------------------------------------------------------------------------- */

const PAYMENT_PATH = '/billing/payments/new'
const BALANCE = `/api/v1/billing/orders/${ORDER_ID}/balance`
const MODES = '/api/v1/billing/payment-modes/available'
const PAYMENTS = '/api/v1/billing/payments'
const TAKE_PAYMENT_AT = `${PAYMENT_PATH}?orderId=${ORDER_ID}&orderNumber=ORD-0001`

const takePayment = (routes: StoryRoutes, options: ScreenOptions = {}) =>
  link(options.online ?? true, () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.recordPayment}>
        <TakePaymentRoute />
      </RequirePermission>,
      {
        [`GET ${BALANCE}`]: () => storyJson(anOrderBalance()),
        [`GET ${MODES}`]: () => storyJson(anAvailablePaymentModeList()),
        [`POST ${PAYMENTS}`]: () => storyJson(aPayment()),
        ...routes,
      },
      {
        path: PAYMENT_PATH,
        at: options.at ?? TAKE_PAYMENT_AT,
        permissions: options.permissions ?? [BILLING_PERMISSIONS.recordPayment],
      },
    ),
  )

/** The order's outstanding balance and the branch's payment modes, ready to record against. */
export const TakePayment: Story = { render: () => takePayment({}) }

export const TakePaymentLoading: Story = {
  render: () => takePayment({ [`GET ${BALANCE}`]: storyPending, [`GET ${MODES}`]: storyPending }),
}

/** Reached with no `orderId` on the query string — the screen's own line 150 branch, not a form. */
export const TakePaymentEmpty: Story = {
  render: () => takePayment({}, { at: PAYMENT_PATH }),
}

export const TakePaymentError: Story = {
  render: () =>
    takePayment({ [`GET ${BALANCE}`]: () => storyProblem(503, 'platform.unavailable') }),
}

/** Recording needs a connection; the amount already typed would stay, this story just cannot type it. */
export const TakePaymentOffline: Story = {
  render: () => takePayment({}, { online: false }),
}

/** Somebody without `payments.record`: a sentence and who to ask, never a redirect. */
export const TakePaymentForbidden: Story = {
  render: () => takePayment({}, { permissions: [] }),
}

export const TakePaymentPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => takePayment({}),
}

/** The 320 px reflow floor: the phone-first screen a cashier actually opens at the counter. */
export const TakePaymentReflowFloor: Story = {
  globals: { viewport: { value: 'reflowFloor' } },
  render: () => takePayment({}),
}

/* Allocate an advance. ---------------------------------------------------------------------------- */

const ALLOCATE_PATH = '/billing/payments/:paymentId/allocate'
const ALLOCATE_AT = `/billing/payments/${PAYMENT_ID}/allocate`
const PAYMENT_URL = `/api/v1/billing/payments/${PAYMENT_ID}`
const ALLOCATIONS = `${PAYMENT_URL}/allocations`

const allocateAdvance = (routes: StoryRoutes, options: ScreenOptions = {}) =>
  link(options.online ?? true, () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.allocateAdvanceManual}>
        <AllocateAdvanceRoute />
      </RequirePermission>,
      {
        [`GET ${PAYMENT_URL}`]: () => storyJson(aPayment({ unappliedAdvance: 500 })),
        [`GET ${BALANCE}`]: () => storyJson(anOrderBalance()),
        [`POST ${ALLOCATIONS}`]: () => storyJson(aPayment({ unappliedAdvance: 191 })),
        ...routes,
      },
      {
        path: ALLOCATE_PATH,
        at: options.at ?? ALLOCATE_AT,
        permissions: options.permissions ?? [BILLING_PERMISSIONS.allocateAdvanceManual],
      },
    ),
  )

/** ₹500 held, unapplied, against one posted invoice still owing ₹309. */
export const AllocateAdvance: Story = { render: () => allocateAdvance({}) }

export const AllocateAdvanceLoading: Story = {
  render: () =>
    allocateAdvance({ [`GET ${PAYMENT_URL}`]: storyPending, [`GET ${BALANCE}`]: storyPending }),
}

/** This payment holds no unapplied advance — "Nothing held", not a form with nothing to allocate. */
export const AllocateAdvanceEmpty: Story = {
  render: () =>
    allocateAdvance({
      [`GET ${PAYMENT_URL}`]: () => storyJson(aPayment({ unappliedAdvance: 0 })),
    }),
}

export const AllocateAdvanceError: Story = {
  render: () =>
    allocateAdvance({ [`GET ${PAYMENT_URL}`]: () => storyProblem(503, 'platform.unavailable') }),
}

export const AllocateAdvanceOffline: Story = {
  render: () => allocateAdvance({}, { online: false }),
}

/** Somebody without `payments.allocate_manual`: a sentence and who to ask, never a redirect. */
export const AllocateAdvanceForbidden: Story = {
  render: () => allocateAdvance({}, { permissions: [] }),
}

export const AllocateAdvancePseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => allocateAdvance({}),
}

/* Payment detail. ---------------------------------------------------------------------------------- */

const DETAIL_PATH = '/billing/payments/:paymentId'
const DETAIL_AT = `/billing/payments/${PAYMENT_ID}`

const paymentDetail = (routes: StoryRoutes, options: ScreenOptions = {}) =>
  link(options.online ?? true, () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.recordPayment}>
        <PaymentDetailRoute />
      </RequirePermission>,
      {
        [`GET ${PAYMENT_URL}`]: () => storyJson(aPayment()),
        ...routes,
      },
      {
        path: DETAIL_PATH,
        at: options.at ?? DETAIL_AT,
        permissions: options.permissions ?? [BILLING_PERMISSIONS.recordPayment],
      },
    ),
  )

/** A payment applied to a posted invoice, with the outstanding order balance stated. */
export const PaymentDetail: Story = { render: () => paymentDetail({}) }

export const PaymentDetailLoading: Story = {
  render: () => paymentDetail({ [`GET ${PAYMENT_URL}`]: storyPending }),
}

/** A payment with nothing applied and nothing held — the receipt's own zero-row rendering. */
function aPaymentWithNoAllocation() {
  const payment = aPayment()
  return {
    ...payment,
    allocated: 0,
    unappliedAdvance: 0,
    allocations: [],
    receipt:
      payment.receipt === null ? null : { ...payment.receipt, allocated: 0, unappliedAdvance: 0 },
  }
}

/** Nothing applied and nothing held — the receipt's own zero-row rendering, not an `EmptyState`. */
export const PaymentDetailEmpty: Story = {
  render: () =>
    paymentDetail({
      [`GET ${PAYMENT_URL}`]: () => storyJson(aPaymentWithNoAllocation()),
    }),
}

/** A payment recorded at another branch reads as not-found, never as a raw status. */
export const PaymentDetailError: Story = {
  render: () =>
    paymentDetail({
      [`GET ${PAYMENT_URL}`]: () => storyProblem(404, 'billing.payment-not-found'),
    }),
}

/** Somebody without `payments.record`: a sentence and who to ask, never a redirect. */
export const PaymentDetailForbidden: Story = {
  render: () => paymentDetail({}, { permissions: [] }),
}

export const PaymentDetailPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => paymentDetail({}),
}

/**
 * No offline story: this screen only reads. `OfflineBlockedAction` guards a write, and this one has
 * none to guard.
 */

/* Cashier session. --------------------------------------------------------------------------------- */

const CASHIER_PATH = '/billing/cashier'
const SESSIONS = '/api/v1/billing/cashier-sessions'
const CLOSE = `${SESSIONS}/${CASHIER_SESSION_ID}/close`

/** The signed-in story user's own open session — see the file's own note on why `cashierId` matters. */
const openSession = () => aCashierSession({ cashierId: STORY_USER_ID })

const cashierSession = (routes: StoryRoutes, options: ScreenOptions = {}) =>
  link(options.online ?? true, () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.cashierSession}>
        <CashierSessionRoute />
      </RequirePermission>,
      {
        [`GET ${SESSIONS}?status=Open`]: () => storyJson([openSession()]),
        [`GET ${MODES}`]: () => storyJson(anAvailablePaymentModeList()),
        [`POST ${CLOSE}`]: () => storyJson(aCashierSession({ status: 'Closed' })),
        ...routes,
      },
      {
        path: CASHIER_PATH,
        at: options.at ?? CASHIER_PATH,
        permissions: options.permissions ?? [BILLING_PERMISSIONS.cashierSession],
      },
    ),
  )

/** An open session at the drawer: the denomination count sheet, ready to close against. */
export const CashierSession: Story = { render: () => cashierSession({}) }

export const CashierSessionLoading: Story = {
  render: () =>
    cashierSession({
      [`GET ${SESSIONS}?status=Open`]: storyPending,
      [`GET ${MODES}`]: storyPending,
    }),
}

/** No open session for this cashier yet — the opening form, not a count sheet. */
export const CashierSessionEmpty: Story = {
  render: () => cashierSession({ [`GET ${SESSIONS}?status=Open`]: () => storyJson([]) }),
}

export const CashierSessionError: Story = {
  render: () =>
    cashierSession({
      [`GET ${SESSIONS}?status=Open`]: () => storyProblem(503, 'platform.unavailable'),
    }),
}

/** Closing needs a connection; the count already entered stays on screen. */
export const CashierSessionOffline: Story = {
  render: () => cashierSession({}, { online: false }),
}

/** Somebody without `payments.session`: a sentence and who to ask, never a redirect. */
export const CashierSessionForbidden: Story = {
  render: () => cashierSession({}, { permissions: [] }),
}

export const CashierSessionPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => cashierSession({}),
}

/** The 320 px reflow floor: the other phone-first screen, the drawer at the end of a shift. */
export const CashierSessionReflowFloor: Story = {
  globals: { viewport: { value: 'reflowFloor' } },
  render: () => cashierSession({}),
}
