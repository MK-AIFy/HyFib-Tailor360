import type { Meta, StoryObj } from '@storybook/react-vite'
import { RequirePermission } from '../../admin/RequirePermission'
import {
  STORY_USER,
  storyJson,
  storyPending,
  storyProblem,
  withAdminApi,
} from '../../admin/testing/storyTransport'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import { BRANCH_ID } from '../../billing/testing/fixtures'
import {
  aGstRegistration,
  aTaxConfigurationSummary,
} from '../../billing/testing/pricingConfigFixtures'
import { GstRegistrationsRoute } from './GstRegistrationsRoute'
import { TaxConfigurationListRoute } from './TaxConfigurationListRoute'
import '../admin/admin.css'

/**
 * The pricing administration foundation (#237), driven against a stubbed API rather than mocked.
 *
 * ## Why there is no offline story
 *
 * The same reason `catalogScreens.stories.tsx` gives and the one every administration screen in this
 * application follows: recording or amending a GST registration is online-only, and the application
 * says so through `NetworkStatusBanner` and `OfflineBlockedAction`, which carry their own stories. An
 * offline story here would imply these screens queue what a person did, and they must not — a
 * registration queued on a counter device and replayed an hour later could post it against a branch
 * whose registration had already changed under it.
 *
 * ## Why every story renders the real route
 *
 * A loading story that rendered `LoadingState` would prove that component works, which is not in
 * question. What a reviewer is asking is whether *this* screen has a usable loading state, which is
 * a fact about the route's own branches.
 */
const meta = {
  title: 'Billing/Pricing administration',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

const GST_REGISTRATIONS = '/api/v1/billing/gst-registrations'
const BRANCHES = '/api/v1/admin/branches/'
const TAX_VERSIONS = '/api/v1/billing/tax-configuration/versions'

const BRANCH_ROWS = [
  {
    branchId: BRANCH_ID,
    code: 'CBE01',
    name: 'Coimbatore counter',
    isActive: true,
    gstin: null,
    addressLine1: null,
    addressLine2: null,
    city: null,
    state: null,
    postalCode: null,
    phone: null,
  },
]

/* The GST registration register. ----------------------------------------------------------------- */

export const GstRegisterWorking: Story = {
  render: () =>
    withAdminApi(
      <GstRegistrationsRoute />,
      {
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${GST_REGISTRATIONS}`]: () => storyJson([aGstRegistration()]),
      },
      { path: '/admin/gst-registrations', at: '/admin/gst-registrations' },
    ),
}

export const GstRegisterLoading: Story = {
  render: () =>
    withAdminApi(
      <GstRegistrationsRoute />,
      {
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${GST_REGISTRATIONS}`]: storyPending,
      },
      { path: '/admin/gst-registrations', at: '/admin/gst-registrations' },
    ),
}

/** A first install: no registration recorded yet, with the record control beside the fact. */
export const GstRegisterEmpty: Story = {
  render: () =>
    withAdminApi(
      <GstRegistrationsRoute />,
      {
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${GST_REGISTRATIONS}`]: () => storyJson([]),
      },
      { path: '/admin/gst-registrations', at: '/admin/gst-registrations' },
    ),
}

export const GstRegisterError: Story = {
  render: () =>
    withAdminApi(
      <GstRegistrationsRoute />,
      {
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${GST_REGISTRATIONS}`]: () => storyProblem(503, 'platform.unavailable'),
      },
      { path: '/admin/gst-registrations', at: '/admin/gst-registrations' },
    ),
}

/** Somebody holding neither the drafting nor the branch-read key: a sentence, never a redirect. */
export const GstRegisterForbidden: Story = {
  render: () =>
    withAdminApi(
      <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
        <GstRegistrationsRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${GST_REGISTRATIONS}`]: () => storyJson([aGstRegistration()]),
      },
      { path: '/admin/gst-registrations', at: '/admin/gst-registrations' },
    ),
}

/** The register at 40% text growth, which is what makes the Tamil catalogue safe to switch on. */
export const GstRegisterPseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () =>
    withAdminApi(
      <GstRegistrationsRoute />,
      {
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${GST_REGISTRATIONS}`]: () => storyJson([aGstRegistration()]),
      },
      { path: '/admin/gst-registrations', at: '/admin/gst-registrations' },
    ),
}

/* The tax configuration version register. --------------------------------------------------------- */

export const TaxConfigurationWorking: Story = {
  render: () =>
    withAdminApi(
      <TaxConfigurationListRoute />,
      {
        [`GET ${TAX_VERSIONS}`]: () =>
          storyJson([
            aTaxConfigurationSummary({ versionNumber: 2, name: 'Version 2', status: 'Draft' }),
            aTaxConfigurationSummary(),
          ]),
      },
      { path: '/admin/tax-configuration', at: '/admin/tax-configuration' },
    ),
}

export const TaxConfigurationLoading: Story = {
  render: () =>
    withAdminApi(
      <TaxConfigurationListRoute />,
      { [`GET ${TAX_VERSIONS}`]: storyPending },
      { path: '/admin/tax-configuration', at: '/admin/tax-configuration' },
    ),
}

/** No version drafted yet — a fact, with the act that ends it named, not an error. */
export const TaxConfigurationEmpty: Story = {
  render: () =>
    withAdminApi(
      <TaxConfigurationListRoute />,
      { [`GET ${TAX_VERSIONS}`]: () => storyJson([]) },
      { path: '/admin/tax-configuration', at: '/admin/tax-configuration' },
    ),
}

export const TaxConfigurationError: Story = {
  render: () =>
    withAdminApi(
      <TaxConfigurationListRoute />,
      { [`GET ${TAX_VERSIONS}`]: () => storyProblem(503, 'platform.unavailable') },
      { path: '/admin/tax-configuration', at: '/admin/tax-configuration' },
    ),
}

export const TaxConfigurationForbidden: Story = {
  render: () =>
    withAdminApi(
      <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
        <TaxConfigurationListRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }),
        [`GET ${TAX_VERSIONS}`]: () => storyJson([aTaxConfigurationSummary()]),
      },
      { path: '/admin/tax-configuration', at: '/admin/tax-configuration' },
    ),
}

export const TaxConfigurationPseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () =>
    withAdminApi(
      <TaxConfigurationListRoute />,
      { [`GET ${TAX_VERSIONS}`]: () => storyJson([aTaxConfigurationSummary()]) },
      { path: '/admin/tax-configuration', at: '/admin/tax-configuration' },
    ),
}
