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
  TAX_CONFIGURATION_VERSION_ID,
  aBillingFinding,
  aGstRegistration,
  aTaxConfiguration,
  aTaxConfigurationSummary,
} from '../../billing/testing/pricingConfigFixtures'
import {
  PRICE_LIST_VERSION_ID,
  aDiscountRule,
  aPriceList,
  aPriceListVersion,
  aPriceListVersionSummary,
} from '../../billing/testing/priceListFixtures'
import { GstRegistrationsRoute } from './GstRegistrationsRoute'
import { PriceListsRoute } from './PriceListsRoute'
import { PriceListVersionEditorRoute } from './PriceListVersionEditorRoute'
import { PriceListVersionsRoute } from './PriceListVersionsRoute'
import { TaxConfigurationEditorRoute } from './TaxConfigurationEditorRoute'
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
const PRICE_LISTS = '/api/v1/billing/price-lists'
const PRICE_LIST = aPriceList()
const PRICE_LIST_VERSIONS = `${PRICE_LISTS}/${PRICE_LIST.priceListId}/versions`
const TAX_VERSION = `${TAX_VERSIONS}/${TAX_CONFIGURATION_VERSION_ID}`

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

/* The price-list register (#252). ------------------------------------------------------------------ */

export const PriceListsWorking: Story = {
  render: () =>
    withAdminApi(
      <PriceListsRoute />,
      { [`GET ${PRICE_LISTS}`]: () => storyJson([PRICE_LIST]) },
      { path: '/admin/price-lists', at: '/admin/price-lists' },
    ),
}

export const PriceListsLoading: Story = {
  render: () =>
    withAdminApi(
      <PriceListsRoute />,
      { [`GET ${PRICE_LISTS}`]: storyPending },
      { path: '/admin/price-lists', at: '/admin/price-lists' },
    ),
}

/** A first install: no price list yet, with the create control beside the fact. */
export const PriceListsEmpty: Story = {
  render: () =>
    withAdminApi(
      <PriceListsRoute />,
      { [`GET ${PRICE_LISTS}`]: () => storyJson([]) },
      { path: '/admin/price-lists', at: '/admin/price-lists' },
    ),
}

export const PriceListsError: Story = {
  render: () =>
    withAdminApi(
      <PriceListsRoute />,
      { [`GET ${PRICE_LISTS}`]: () => storyProblem(503, 'platform.unavailable') },
      { path: '/admin/price-lists', at: '/admin/price-lists' },
    ),
}

export const PriceListsForbidden: Story = {
  render: () =>
    withAdminApi(
      <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
        <PriceListsRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }),
        [`GET ${PRICE_LISTS}`]: () => storyJson([PRICE_LIST]),
      },
      { path: '/admin/price-lists', at: '/admin/price-lists' },
    ),
}

export const PriceListsPseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () =>
    withAdminApi(
      <PriceListsRoute />,
      { [`GET ${PRICE_LISTS}`]: () => storyJson([PRICE_LIST]) },
      { path: '/admin/price-lists', at: '/admin/price-lists' },
    ),
}

/* A price list's versions. --------------------------------------------------------------------- */

const PRICE_LIST_AT = `/admin/price-lists/${PRICE_LIST.priceListId}`

export const PriceListVersionsWorking: Story = {
  render: () =>
    withAdminApi(
      <PriceListVersionsRoute />,
      {
        [`GET ${PRICE_LISTS}/${PRICE_LIST.priceListId}`]: () => storyJson(PRICE_LIST, 'W/"1"'),
        [`GET ${PRICE_LIST_VERSIONS}`]: () => storyJson([aPriceListVersionSummary()]),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
      },
      { path: '/admin/price-lists/:priceListId', at: PRICE_LIST_AT },
    ),
}

export const PriceListVersionsLoading: Story = {
  render: () =>
    withAdminApi(
      <PriceListVersionsRoute />,
      {
        [`GET ${PRICE_LISTS}/${PRICE_LIST.priceListId}`]: () => storyJson(PRICE_LIST, 'W/"1"'),
        [`GET ${PRICE_LIST_VERSIONS}`]: storyPending,
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
      },
      { path: '/admin/price-lists/:priceListId', at: PRICE_LIST_AT },
    ),
}

/** No version drafted yet — a fact, with the act that starts one named, not an error. */
export const PriceListVersionsEmpty: Story = {
  render: () =>
    withAdminApi(
      <PriceListVersionsRoute />,
      {
        [`GET ${PRICE_LISTS}/${PRICE_LIST.priceListId}`]: () => storyJson(PRICE_LIST, 'W/"1"'),
        [`GET ${PRICE_LIST_VERSIONS}`]: () => storyJson([]),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
      },
      { path: '/admin/price-lists/:priceListId', at: PRICE_LIST_AT },
    ),
}

export const PriceListVersionsError: Story = {
  render: () =>
    withAdminApi(
      <PriceListVersionsRoute />,
      {
        [`GET ${PRICE_LISTS}/${PRICE_LIST.priceListId}`]: () => storyJson(PRICE_LIST, 'W/"1"'),
        [`GET ${PRICE_LIST_VERSIONS}`]: () => storyProblem(503, 'platform.unavailable'),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
      },
      { path: '/admin/price-lists/:priceListId', at: PRICE_LIST_AT },
    ),
}

export const PriceListVersionsForbidden: Story = {
  render: () =>
    withAdminApi(
      <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
        <PriceListVersionsRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }),
        [`GET ${PRICE_LISTS}/${PRICE_LIST.priceListId}`]: () => storyJson(PRICE_LIST, 'W/"1"'),
        [`GET ${PRICE_LIST_VERSIONS}`]: () => storyJson([aPriceListVersionSummary()]),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
      },
      { path: '/admin/price-lists/:priceListId', at: PRICE_LIST_AT },
    ),
}

export const PriceListVersionsPseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () =>
    withAdminApi(
      <PriceListVersionsRoute />,
      {
        [`GET ${PRICE_LISTS}/${PRICE_LIST.priceListId}`]: () => storyJson(PRICE_LIST, 'W/"1"'),
        [`GET ${PRICE_LIST_VERSIONS}`]: () => storyJson([aPriceListVersionSummary()]),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
      },
      { path: '/admin/price-lists/:priceListId', at: PRICE_LIST_AT },
    ),
}

/* The tax configuration editor (E09-F01-5). --------------------------------------------------------- */

const TAX_EDITOR_ROUTE = {
  path: '/admin/tax-configuration/:versionId',
  at: `/admin/tax-configuration/${TAX_CONFIGURATION_VERSION_ID}`,
}

export const TaxConfigurationEditorWorking: Story = {
  render: () =>
    withAdminApi(
      <TaxConfigurationEditorRoute />,
      { [`GET ${TAX_VERSION}`]: () => storyJson(aTaxConfiguration(), 'W/"1"') },
      TAX_EDITOR_ROUTE,
    ),
}

export const TaxConfigurationEditorLoading: Story = {
  render: () =>
    withAdminApi(
      <TaxConfigurationEditorRoute />,
      { [`GET ${TAX_VERSION}`]: storyPending },
      TAX_EDITOR_ROUTE,
    ),
}

/** A freshly started draft, with no tax code yet, and the add control beside the fact. */
export const TaxConfigurationEditorEmpty: Story = {
  render: () =>
    withAdminApi(
      <TaxConfigurationEditorRoute />,
      {
        [`GET ${TAX_VERSION}`]: () => storyJson(aTaxConfiguration({ taxCodes: [] }), 'W/"1"'),
      },
      TAX_EDITOR_ROUTE,
    ),
}

export const TaxConfigurationEditorError: Story = {
  render: () =>
    withAdminApi(
      <TaxConfigurationEditorRoute />,
      { [`GET ${TAX_VERSION}`]: () => storyProblem(503, 'platform.unavailable') },
      TAX_EDITOR_ROUTE,
    ),
}

export const TaxConfigurationEditorForbidden: Story = {
  render: () =>
    withAdminApi(
      <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
        <TaxConfigurationEditorRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }),
        [`GET ${TAX_VERSION}`]: () => storyJson(aTaxConfiguration(), 'W/"1"'),
      },
      TAX_EDITOR_ROUTE,
    ),
}

/** A published version: no editing control, only the sentence that sends the reader to the clone. */
export const TaxConfigurationEditorPublished: Story = {
  render: () =>
    withAdminApi(
      <TaxConfigurationEditorRoute />,
      {
        [`GET ${TAX_VERSION}`]: () =>
          storyJson(
            aTaxConfiguration({ version: aTaxConfigurationSummary({ status: 'Published' }) }),
            'W/"1"',
          ),
      },
      TAX_EDITOR_ROUTE,
    ),
}

export const TaxConfigurationEditorPseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () =>
    withAdminApi(
      <TaxConfigurationEditorRoute />,
      { [`GET ${TAX_VERSION}`]: () => storyJson(aTaxConfiguration(), 'W/"1"') },
      TAX_EDITOR_ROUTE,
    ),
}

/* Validating and publishing a version (E09-F01-5b). ------------------------------------------------- */

/** "Check this version" run against a draft with one blocking problem and one warning. */
export const TaxConfigurationEditorValidated: Story = {
  render: () =>
    withAdminApi(
      <TaxConfigurationEditorRoute />,
      {
        [`GET ${TAX_VERSION}`]: () => storyJson(aTaxConfiguration(), 'W/"1"'),
        [`GET ${TAX_VERSION}/validation`]: () =>
          storyJson({
            versionId: TAX_CONFIGURATION_VERSION_ID,
            canPublish: false,
            findings: [
              aBillingFinding({
                severity: 'Error',
                code: 'billing.intra-state-pair-incomplete',
                message: 'The tax code STITCHING_5 carries a CGST without its matching SGST.',
                target: 'taxCodes[STITCHING_5]',
              }),
              aBillingFinding({
                severity: 'Warning',
                code: 'billing.nil-rated-code',
                message: 'The tax code ALTER_0 carries no rate at all.',
                target: 'taxCodes[ALTER_0]',
              }),
            ],
          }),
      },
      TAX_EDITOR_ROUTE,
    ),
}

/** A holder of the drafting key but not the publishing one: the control is a `Forbidden` region. */
export const TaxConfigurationEditorPublishForbidden: Story = {
  render: () =>
    withAdminApi(
      <TaxConfigurationEditorRoute />,
      {
        'GET /api/v1/me': () =>
          storyJson({ ...STORY_USER, permissions: [BILLING_PERMISSIONS.managePriceLists] }),
        [`GET ${TAX_VERSION}`]: () => storyJson(aTaxConfiguration(), 'W/"1"'),
      },
      TAX_EDITOR_ROUTE,
    ),
}

/* The price-list version editor: conventions, items and the tax-code picker (E09-F01-7, #268). --- */

const PRICE_LIST_VERSION_BASE = `${PRICE_LISTS}/versions`
const PRICE_LIST_VERSION = `${PRICE_LIST_VERSION_BASE}/${PRICE_LIST_VERSION_ID}`
const PRICE_LIST_VERSION_EDITOR_ROUTE = {
  path: '/admin/price-lists/versions/:versionId',
  at: PRICE_LIST_VERSION,
}

export const PriceListVersionEditorWorking: Story = {
  render: () =>
    withAdminApi(
      <PriceListVersionEditorRoute />,
      {
        [`GET ${PRICE_LIST_VERSION}`]: () => storyJson(aPriceListVersion(), 'W/"1"'),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${TAX_VERSIONS}`]: () => storyJson([]),
      },
      PRICE_LIST_VERSION_EDITOR_ROUTE,
    ),
}

export const PriceListVersionEditorLoading: Story = {
  render: () =>
    withAdminApi(
      <PriceListVersionEditorRoute />,
      {
        [`GET ${PRICE_LIST_VERSION}`]: storyPending,
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${TAX_VERSIONS}`]: () => storyJson([]),
      },
      PRICE_LIST_VERSION_EDITOR_ROUTE,
    ),
}

/** A freshly started draft, with no item yet, and the add control beside the fact. */
export const PriceListVersionEditorEmpty: Story = {
  render: () =>
    withAdminApi(
      <PriceListVersionEditorRoute />,
      {
        [`GET ${PRICE_LIST_VERSION}`]: () => storyJson(aPriceListVersion({ items: [] }), 'W/"1"'),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${TAX_VERSIONS}`]: () => storyJson([]),
      },
      PRICE_LIST_VERSION_EDITOR_ROUTE,
    ),
}

export const PriceListVersionEditorError: Story = {
  render: () =>
    withAdminApi(
      <PriceListVersionEditorRoute />,
      {
        [`GET ${PRICE_LIST_VERSION}`]: () => storyProblem(503, 'platform.unavailable'),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${TAX_VERSIONS}`]: () => storyJson([]),
      },
      PRICE_LIST_VERSION_EDITOR_ROUTE,
    ),
}

export const PriceListVersionEditorForbidden: Story = {
  render: () =>
    withAdminApi(
      <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
        <PriceListVersionEditorRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }),
        [`GET ${PRICE_LIST_VERSION}`]: () => storyJson(aPriceListVersion(), 'W/"1"'),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${TAX_VERSIONS}`]: () => storyJson([]),
      },
      PRICE_LIST_VERSION_EDITOR_ROUTE,
    ),
}

/** A published version: no item or conventions control, but its discount rules still visible. */
export const PriceListVersionEditorPublished: Story = {
  render: () =>
    withAdminApi(
      <PriceListVersionEditorRoute />,
      {
        [`GET ${PRICE_LIST_VERSION}`]: () =>
          storyJson(
            aPriceListVersion({
              version: aPriceListVersionSummary({ status: 'Published' }),
              discountRules: [aDiscountRule()],
            }),
            'W/"1"',
          ),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${TAX_VERSIONS}`]: () => storyJson([]),
      },
      PRICE_LIST_VERSION_EDITOR_ROUTE,
    ),
}

export const PriceListVersionEditorPseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () =>
    withAdminApi(
      <PriceListVersionEditorRoute />,
      {
        [`GET ${PRICE_LIST_VERSION}`]: () => storyJson(aPriceListVersion(), 'W/"1"'),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${TAX_VERSIONS}`]: () => storyJson([]),
      },
      PRICE_LIST_VERSION_EDITOR_ROUTE,
    ),
}

/* Validating and publishing a version (E09-F01-7b). ------------------------------------------------- */

/** "Check this version" run against a draft with one blocking problem and one real warning (OD-19). */
export const PriceListVersionEditorValidated: Story = {
  render: () =>
    withAdminApi(
      <PriceListVersionEditorRoute />,
      {
        [`GET ${PRICE_LIST_VERSION}`]: () => storyJson(aPriceListVersion(), 'W/"1"'),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${TAX_VERSIONS}`]: () => storyJson([]),
        [`GET ${PRICE_LIST_VERSION}/validation`]: () =>
          storyJson({
            versionId: PRICE_LIST_VERSION_ID,
            canPublish: false,
            findings: [
              aBillingFinding({
                severity: 'Error',
                code: 'billing.tax-configuration-missing',
                message:
                  "No tax configuration version is published, so no item's tax code can be resolved. Publish one first.",
                target: 'items',
              }),
              aBillingFinding({
                severity: 'Warning',
                code: 'billing.branch-left-unpriced',
                message:
                  '1 branch(es) the published version prices are dropped by this one, and the published catalogue offers priced services there. Keep the branch, or publish another list’s version for it first.',
                target: 'branchIds',
              }),
            ],
          }),
      },
      PRICE_LIST_VERSION_EDITOR_ROUTE,
    ),
}

/** A holder of the drafting key but not the publishing one: the control is a `Forbidden` region. */
export const PriceListVersionEditorPublishForbidden: Story = {
  render: () =>
    withAdminApi(
      <PriceListVersionEditorRoute />,
      {
        'GET /api/v1/me': () =>
          storyJson({ ...STORY_USER, permissions: [BILLING_PERMISSIONS.managePriceLists] }),
        [`GET ${PRICE_LIST_VERSION}`]: () => storyJson(aPriceListVersion(), 'W/"1"'),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${TAX_VERSIONS}`]: () => storyJson([]),
      },
      PRICE_LIST_VERSION_EDITOR_ROUTE,
    ),
}
