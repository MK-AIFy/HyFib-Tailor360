import type { Meta, StoryObj } from '@storybook/react-vite'
import { RequirePermission } from '../../admin/RequirePermission'
import { ADMIN_PERMISSIONS } from '../../admin/adminPermissions'
import {
  STORY_USER,
  storyJson,
  storyPending,
  storyProblem,
  withAdminApi,
} from '../../admin/testing/storyTransport'
import {
  COIMBATORE,
  ERODE,
  aCatalogFinding,
  aCatalogVersion,
  aCatalogVersionSummary,
  aCategory,
  aServiceType,
} from '../../catalog/testing/fixtures'
import { CatalogVersionEditorRoute } from './CatalogVersionEditorRoute'
import { CatalogVersionListRoute } from './CatalogVersionListRoute'
import '../admin/admin.css'

/**
 * The catalogue administration screens, driven against a stubbed API rather than mocked.
 *
 * ## Why there is no offline story
 *
 * The same reason the rest of the administration section has none: editing and publishing a
 * catalogue are online-only acts, and the application says so through `NetworkStatusBanner` and
 * `OfflineBlockedAction`, which carry their own stories. An offline story here would imply these
 * screens queue what a person did, and they must not — a catalogue edit queued on a counter device
 * and replayed an hour later would publish against a version somebody else had already moved.
 *
 * ## Why every story renders the real route
 *
 * A loading story that rendered `LoadingState` would prove that component works, which is not in
 * question. What a reviewer is asking is whether *this* screen has a usable loading state, which is
 * a fact about the route's own branches.
 */
const meta = {
  title: 'Catalogue/Catalogue administration',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

const CATALOG = '/api/v1/catalog'
const BRANCHES = '/api/v1/admin/branches/'

const BRANCH_ROWS = [
  {
    branchId: COIMBATORE,
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
  {
    branchId: ERODE,
    code: 'ERD01',
    name: 'Erode counter',
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

/* Every version ------------------------------------------------------------------------------- */

const PUBLISHED_SUMMARY = aCatalogVersionSummary({
  catalogVersionId: '0199bb00-0000-7000-8000-0000000000c2',
  versionNumber: 2,
  name: 'Version 2',
  status: 'Published',
  publishedAt: '2026-09-04T06:30:00.000Z',
})

const DRAFT_SUMMARY = aCatalogVersionSummary({
  catalogVersionId: '0199bb00-0000-7000-8000-0000000000c3',
  versionNumber: 3,
  name: 'Version 3',
  notes: 'Aari work split out of blouse after the Erode trial.',
  clonedFromVersionId: PUBLISHED_SUMMARY.catalogVersionId,
})

/** A published catalogue and the successor being drafted against it — the working state. */
export const VersionList: Story = {
  render: () =>
    withAdminApi(<CatalogVersionListRoute />, {
      [`GET ${CATALOG}/versions`]: () => storyJson([DRAFT_SUMMARY, PUBLISHED_SUMMARY]),
    }),
}

/**
 * Versions exist, but none is published.
 *
 * Not an error, and said as a fact: it is where every installation sits after the first draft is
 * started, and a shop in it cannot take an order at all.
 */
export const VersionListNothingPublished: Story = {
  render: () =>
    withAdminApi(<CatalogVersionListRoute />, {
      [`GET ${CATALOG}/versions`]: () => storyJson([DRAFT_SUMMARY]),
    }),
}

/** Loading, saying what is being loaded rather than spinning. */
export const VersionListLoading: Story = {
  render: () =>
    withAdminApi(<CatalogVersionListRoute />, { [`GET ${CATALOG}/versions`]: storyPending }),
}

/** A first install: nothing at all, with the one act that ends that state beside the sentence. */
export const VersionListEmpty: Story = {
  render: () =>
    withAdminApi(<CatalogVersionListRoute />, {
      [`GET ${CATALOG}/versions`]: () => storyJson([]),
    }),
}

/** A refusal, in this application's words rather than the server's own message. */
export const VersionListError: Story = {
  render: () =>
    withAdminApi(<CatalogVersionListRoute />, {
      [`GET ${CATALOG}/versions`]: () => storyProblem(503, 'platform.unavailable'),
    }),
}

/** Somebody holding neither catalogue permission: a sentence and who to ask, never a redirect. */
export const VersionListForbidden: Story = {
  render: () =>
    withAdminApi(
      <RequirePermission permission={ADMIN_PERMISSIONS.catalogEdit}>
        <CatalogVersionListRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }),
        [`GET ${CATALOG}/versions`]: () => storyJson([DRAFT_SUMMARY]),
      },
    ),
}

/* One version --------------------------------------------------------------------------------- */

const BLOUSE = aCategory()
const AARI = aCategory({
  categoryId: '0199bb00-0000-7000-8000-0000000000b2',
  parentCategoryId: BLOUSE.categoryId,
  code: 'AARI',
  name: 'Aari work',
  description: 'Hand embroidery on a stitched blouse.',
  displayOrder: 0,
  branchIds: [COIMBATORE],
})
const SALWAR = aCategory({
  categoryId: '0199bb00-0000-7000-8000-0000000000b3',
  code: 'SALWAR',
  name: 'Salwar kameez',
  description: 'Everything stitched as a salwar kameez.',
  displayOrder: 1,
  branchIds: [COIMBATORE, ERODE],
})

const PATTERN = aServiceType()
const READY = aServiceType({
  serviceTypeId: '0199bb00-0000-7000-8000-0000000000d2',
  categoryId: SALWAR.categoryId,
  code: 'READY',
  name: 'Ready to stitch',
  description: 'Stitched to a measurement already on file.',
  displayOrder: 1,
  branchIds: [COIMBATORE, ERODE],
  priceListItemCode: null,
  notOrderable: true,
})

const WORKING = aCatalogVersion({
  version: DRAFT_SUMMARY,
  categories: [BLOUSE, AARI, SALWAR],
  serviceTypes: [PATTERN, READY],
})

const editor = (
  version: typeof WORKING,
  extra: Record<string, () => Response | Promise<Response>> = {},
  user: unknown = STORY_USER,
) =>
  withAdminApi(
    <CatalogVersionEditorRoute />,
    {
      'GET /api/v1/me': () => storyJson(user),
      [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
      [`GET ${CATALOG}/versions/${version.version.catalogVersionId}`]: () =>
        storyJson(version, 'W/"1"'),
      ...extra,
    },
    {
      path: '/admin/catalog/:versionId',
      at: `/admin/catalog/${version.version.catalogVersionId}`,
    },
  )

/**
 * A draft mid-edit: a nested category, a service type missing a link, and both acts offered.
 *
 * `READY` has no price-list item, so the server calls it not orderable and the screen says which of
 * the five links is why — the part a person cannot look up from the flag.
 */
export const VersionEditor: Story = { render: () => editor(WORKING) }

/** The checks run, with a finding sitting on the row that caused it. */
export const VersionEditorWithFindings: Story = {
  render: () =>
    editor(WORKING, {
      [`GET ${CATALOG}/versions/${WORKING.version.catalogVersionId}/validation`]: () =>
        storyJson({
          catalogVersionId: WORKING.version.catalogVersionId,
          publishable: false,
          errorCount: 1,
          warningCount: 1,
          findings: [
            aCatalogFinding({
              target: 'serviceTypes[SALWAR.READY].priceListItemCode',
              message: "'SALWAR.READY' has no price-list item.",
            }),
            aCatalogFinding({
              severity: 'Warning',
              code: 'catalog.category-empty',
              message: "'AARI' offers no service type.",
              target: 'categories[AARI].serviceTypes',
            }),
          ],
        }),
    }),
}

/**
 * A published version: the labels can be corrected and nothing else.
 *
 * That is what the server admits, and a control that would be refused is not a control — the
 * structure a placed order was taken against does not change under it.
 */
export const VersionEditorPublished: Story = {
  render: () =>
    editor(
      aCatalogVersion({
        version: PUBLISHED_SUMMARY,
        categories: [BLOUSE, SALWAR],
        serviceTypes: [PATTERN],
      }),
    ),
}

/**
 * A published version viewed by somebody who may draft but not publish.
 *
 * The label correction asks for the publishing key too, so it is not offered — and the reason is
 * said, because a published version with no controls at all reads as a broken screen.
 */
export const VersionEditorPublishedWithoutPublish: Story = {
  render: () =>
    editor(
      aCatalogVersion({
        version: PUBLISHED_SUMMARY,
        categories: [BLOUSE, SALWAR],
        serviceTypes: [PATTERN],
      }),
      {},
      { ...STORY_USER, permissions: [ADMIN_PERMISSIONS.catalogEdit] },
    ),
}

/**
 * An administrator who may edit a draft but not publish one.
 *
 * Publication and retirement are not rendered greyed out — they are not rendered at all, and the
 * reason is said in words, because a row of controls that each end in a refusal reads as a broken
 * screen rather than as a boundary.
 */
export const VersionEditorWithoutPublish: Story = {
  render: () =>
    editor(WORKING, {}, { ...STORY_USER, permissions: [ADMIN_PERMISSIONS.catalogEdit] }),
}

/** Loading one version. */
export const VersionEditorLoading: Story = {
  render: () =>
    withAdminApi(
      <CatalogVersionEditorRoute />,
      {
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${CATALOG}/versions/${WORKING.version.catalogVersionId}`]: storyPending,
      },
      {
        path: '/admin/catalog/:versionId',
        at: `/admin/catalog/${WORKING.version.catalogVersionId}`,
      },
    ),
}

/** A draft with nothing in it yet, which is a draft that cannot be published. */
export const VersionEditorEmpty: Story = {
  render: () =>
    editor(aCatalogVersion({ version: DRAFT_SUMMARY, categories: [], serviceTypes: [] })),
}

/** A version that is not there any more, or never was. */
export const VersionEditorError: Story = {
  render: () =>
    withAdminApi(
      <CatalogVersionEditorRoute />,
      {
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${CATALOG}/versions/${WORKING.version.catalogVersionId}`]: () =>
          storyProblem(404, 'catalog.version-not-found'),
      },
      {
        path: '/admin/catalog/:versionId',
        at: `/admin/catalog/${WORKING.version.catalogVersionId}`,
      },
    ),
}

/**
 * The editor at 40% text growth, which is what makes the Tamil catalogue safe to switch on.
 *
 * The hierarchy column carries the deepest nesting and the longest sentences on this screen — a
 * category name, its depth prefix, and "2 links still missing" beside it — so it is where a layout
 * that only tolerates English breaks first.
 */
export const VersionEditorPseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () => editor(WORKING),
}

/** Somebody who may reach the address but holds neither catalogue permission. */
export const VersionEditorForbidden: Story = {
  render: () =>
    withAdminApi(
      <RequirePermission permission={ADMIN_PERMISSIONS.catalogEdit}>
        <CatalogVersionEditorRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${CATALOG}/versions/${WORKING.version.catalogVersionId}`]: () =>
          storyJson(WORKING, 'W/"1"'),
      },
      {
        path: '/admin/catalog/:versionId',
        at: `/admin/catalog/${WORKING.version.catalogVersionId}`,
      },
    ),
}
