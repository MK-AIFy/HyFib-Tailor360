import type { Meta, StoryObj } from '@storybook/react-vite'
import { RequirePermission } from '../../admin/RequirePermission'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import {
  aCancelledInvoice,
  anInvoice,
  anInvoicePage,
  anInvoiceSummary,
} from '../../billing/testing/fixtures'
import {
  storyJson,
  storyPending,
  storyProblem,
  withBillingApi,
} from '../../billing/testing/storyTransport'
import { InvoiceDetailRoute } from './InvoiceDetailRoute'
import { InvoiceRegisterRoute } from './InvoiceRegisterRoute'
import './billing.css'

/**
 * The invoice register and detail screens (#302), in each of the states Definition of Done item 7
 * asks for.
 *
 * These render the **real screens** against a stubbed API, in the shape
 * `routes/admin/adminScreens.stories.tsx` established — see that file's own note on why. `main`
 * cannot produce a real invoice today (`docs/billing/go-live-plan.md`'s own note: Orders publishes no
 * `order-confirmed` event yet, so `billing.order_facts` stays empty), which is exactly why these
 * stories exist: the populated states are otherwise unreachable evidence.
 *
 * **Offline is deliberately absent from this file.** The register's own offline behaviour — Show
 * more and the barcode lookup both blocked with "this will not be queued", nothing queued — is a
 * fact of this screen and is exercised by `InvoiceRegisterRoute.test.tsx`'s own offline case; a
 * Storybook story adds a click-through of the same branch these tests already assert directly, so it
 * is left to the test rather than duplicated here as a click-through with less coverage.
 */
const meta = {
  title: 'Billing/Invoices',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

const INVOICES = '/api/v1/billing/invoices'
const invoiceUrl = (id: string) => `/api/v1/billing/invoices/${id}`

/* The register. ----------------------------------------------------------------------------- */

export const InvoiceRegisterLoading: Story = {
  render: () =>
    withBillingApi(<InvoiceRegisterRoute />, { [`GET ${INVOICES}?limit=20`]: storyPending }),
}

/** No invoices at this branch yet — a sentence about what would put one here, not a blank table. */
export const InvoiceRegisterEmpty: Story = {
  render: () =>
    withBillingApi(<InvoiceRegisterRoute />, {
      [`GET ${INVOICES}?limit=20`]: () => storyJson(anInvoicePage({ invoices: [] })),
    }),
}

/** The branch's invoices, newest first — a draft, a posted invoice and a discarded one (OD-22). */
export const InvoiceRegister: Story = {
  render: () =>
    withBillingApi(<InvoiceRegisterRoute />, {
      [`GET ${INVOICES}?limit=20`]: () =>
        storyJson(
          anInvoicePage({
            invoices: [
              anInvoiceSummary(),
              anInvoiceSummary({
                invoiceId: '0199dd00-0000-7000-8000-000000007001',
                invoiceNumber: null,
                orderNumber: 'O-CBE01-2627-000901',
                status: 'Draft',
              }),
              anInvoiceSummary({
                invoiceId: '0199dd00-0000-7000-8000-000000007002',
                invoiceNumber: null,
                orderNumber: 'O-CBE01-2627-000902',
                status: 'Discarded',
              }),
            ],
          }),
        ),
    }),
}

/** Deny by default: a sentence and a suggestion, never a redirect. */
export const InvoiceRegisterForbidden: Story = {
  render: () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.createInvoice}>
        <InvoiceRegisterRoute />
      </RequirePermission>,
      { 'GET /api/v1/me': () => storyJson(withNoPermissions()) },
    ),
}

/* The detail screen. -------------------------------------------------------------------------- */

const DETAIL_OPTIONS = {
  path: '/billing/invoices/:invoiceId',
  at: `/billing/invoices/${anInvoice().invoiceId}`,
}

export const InvoiceDetailLoading: Story = {
  render: () =>
    withBillingApi(
      <InvoiceDetailRoute />,
      { [`GET ${invoiceUrl(anInvoice().invoiceId)}`]: storyPending },
      DETAIL_OPTIONS,
    ),
}

/**
 * A posted invoice with two lines, a loyalty discount, a lining surcharge's tax carried through and
 * a non-zero round-off — the fixture checklist item A11Y-BI-06 asks for, because a zero round-off is
 * not rendered at all and so cannot answer it.
 */
export const InvoiceDetail: Story = {
  render: () =>
    withBillingApi(
      <InvoiceDetailRoute />,
      { [`GET ${invoiceUrl(anInvoice().invoiceId)}`]: () => storyJson(anInvoice()) },
      DETAIL_OPTIONS,
    ),
}

/** Cancelled by its compensating record: the number and totals stand, and the banner says so. */
export const InvoiceDetailCancelled: Story = {
  render: () =>
    withBillingApi(
      <InvoiceDetailRoute />,
      { [`GET ${invoiceUrl(anInvoice().invoiceId)}`]: () => storyJson(aCancelledInvoice()) },
      DETAIL_OPTIONS,
    ),
}

/** An unknown identifier reads as `billing.problem.invoiceNotFound`, never as a code. */
export const InvoiceDetailError: Story = {
  render: () =>
    withBillingApi(
      <InvoiceDetailRoute />,
      {
        [`GET ${invoiceUrl(anInvoice().invoiceId)}`]: () =>
          storyProblem(404, 'billing.invoice-not-found'),
      },
      DETAIL_OPTIONS,
    ),
}

export const InvoiceDetailForbidden: Story = {
  render: () =>
    withBillingApi(
      <RequirePermission permission={BILLING_PERMISSIONS.createInvoice}>
        <InvoiceDetailRoute />
      </RequirePermission>,
      { 'GET /api/v1/me': () => storyJson(withNoPermissions()) },
      DETAIL_OPTIONS,
    ),
}

function withNoPermissions() {
  return {
    userId: '0199bb00-0000-7000-8000-0000000000f2',
    userName: 'tailor.story',
    displayName: 'Ravi (workshop)',
    email: 'tailor.story@example.invalid',
    status: 'Active',
    organisationId: '0199bb00-0000-7000-8000-0000000000ff',
    branchId: '0199bb00-0000-7000-8000-0000000000aa',
    permissions: [] as readonly string[],
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
