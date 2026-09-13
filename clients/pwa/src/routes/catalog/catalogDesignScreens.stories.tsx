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
  aCatalogVersion,
  aCatalogVersionSummary,
  aCategory,
  aDesignGroup,
  aDesignOption,
  aDesignRule,
  anOperand,
} from '../../catalog/testing/fixtures'
import { CatalogDesignRoute } from './CatalogDesignRoute'
import '../admin/admin.css'

/**
 * The design administration screen, driven against a stubbed API rather than mocked.
 *
 * ## Why there is no offline story
 *
 * The same reasoning as `catalogScreens.stories.tsx`: editing a catalogue is an online-only act.
 *
 * ## Why every story renders the real route
 *
 * A loading story that rendered `LoadingState` would prove that component works, which is not in
 * question — what a reviewer is asking is whether this screen's own loading branch is usable.
 */
const meta = {
  title: 'Catalogue/Design administration',
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

const DRAFT_SUMMARY = aCatalogVersionSummary({
  catalogVersionId: '0199bb00-0000-7000-8000-0000000000c3',
  versionNumber: 3,
  name: 'Version 3',
})

const PUBLISHED_SUMMARY = aCatalogVersionSummary({
  catalogVersionId: '0199bb00-0000-7000-8000-0000000000c2',
  versionNumber: 2,
  name: 'Version 2',
  status: 'Published',
  publishedAt: '2026-09-04T06:30:00.000Z',
})

const BLOUSE = aCategory()

const NECKLINE = aDesignGroup({
  designOptionGroupId: '0199bb00-0000-7000-8000-0000000000e0',
  categoryId: BLOUSE.categoryId,
  code: 'neckline',
  name: 'Neckline',
  displayOrder: 0,
  options: [
    aDesignOption({
      designOptionId: '0199bb00-0000-7000-8000-0000000000e1',
      designOptionGroupId: '0199bb00-0000-7000-8000-0000000000e0',
      code: 'ROUND',
      name: 'Round',
      displayOrder: 0,
    }),
    aDesignOption({
      designOptionId: '0199bb00-0000-7000-8000-0000000000e5',
      designOptionGroupId: '0199bb00-0000-7000-8000-0000000000e0',
      code: 'V_NECK',
      name: 'V-neck',
      illustrationKey: 'design_blouse_v1#neckline.V_NECK',
      displayOrder: 1,
    }),
  ],
})

const SLEEVE = aDesignGroup({
  designOptionGroupId: '0199bb00-0000-7000-8000-0000000000e6',
  categoryId: BLOUSE.categoryId,
  code: 'sleeve',
  name: 'Sleeve length',
  displayOrder: 1,
  options: [
    aDesignOption({
      designOptionId: '0199bb00-0000-7000-8000-0000000000e7',
      designOptionGroupId: '0199bb00-0000-7000-8000-0000000000e6',
      code: 'SHORT',
      name: 'Short',
      displayOrder: 0,
    }),
  ],
})

const RULE = aDesignRule({
  categoryId: BLOUSE.categoryId,
  antecedent: anOperand({ groupCode: 'neckline', form: 'Equals', optionCodes: ['V_NECK'] }),
  consequent: anOperand({ groupCode: 'sleeve', form: 'Equals', optionCodes: ['SHORT'] }),
  statement: 'If neckline is V-neck, then sleeve length is Short is required.',
})

const WORKING = aCatalogVersion({
  version: DRAFT_SUMMARY,
  categories: [BLOUSE],
  serviceTypes: [],
  designGroups: [NECKLINE, SLEEVE],
  designRules: [RULE],
})

const editor = (
  version: typeof WORKING,
  extra: Record<string, () => Response | Promise<Response>> = {},
  user: unknown = STORY_USER,
) =>
  withAdminApi(
    <CatalogDesignRoute />,
    {
      'GET /api/v1/me': () => storyJson(user),
      [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
      [`GET ${CATALOG}/versions/${version.version.catalogVersionId}`]: () =>
        storyJson(version, 'W/"1"'),
      ...extra,
    },
    {
      path: '/admin/catalog/:versionId/categories/:categoryId/design',
      at: `/admin/catalog/${version.version.catalogVersionId}/categories/${BLOUSE.categoryId}/design`,
    },
  )

/** A draft's neckline and sleeve groups, and the rule between them. */
export const DesignEditor: Story = { render: () => editor(WORKING) }

/** The checks run, with a finding sitting on the row that caused it. */
export const DesignEditorWithFindings: Story = {
  render: () =>
    editor(WORKING, {
      [`GET ${CATALOG}/versions/${WORKING.version.catalogVersionId}/validation`]: () =>
        storyJson({
          catalogVersionId: WORKING.version.catalogVersionId,
          publishable: false,
          errorCount: 1,
          warningCount: 0,
          findings: [
            {
              severity: 'Error',
              code: 'catalog.design-option-retired',
              message: "'BLOUSE.neckline.V_NECK' is retired and cannot be used by a rule.",
              target: 'designGroups[BLOUSE.neckline].code',
              validator: 'DesignRuleValidator',
            },
          ],
        }),
    }),
}

/** A published version: only the labels can be corrected. */
export const DesignEditorPublished: Story = {
  render: () =>
    editor(
      aCatalogVersion({
        version: PUBLISHED_SUMMARY,
        categories: [BLOUSE],
        serviceTypes: [],
        designGroups: [NECKLINE, SLEEVE],
        designRules: [RULE],
      }),
    ),
}

/** An administrator who may draft but not publish. */
export const DesignEditorWithoutPublish: Story = {
  render: () =>
    editor(WORKING, {}, { ...STORY_USER, permissions: [ADMIN_PERMISSIONS.catalogEdit] }),
}

/** Loading. */
export const DesignEditorLoading: Story = {
  render: () =>
    withAdminApi(
      <CatalogDesignRoute />,
      {
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${CATALOG}/versions/${WORKING.version.catalogVersionId}`]: storyPending,
      },
      {
        path: '/admin/catalog/:versionId/categories/:categoryId/design',
        at: `/admin/catalog/${WORKING.version.catalogVersionId}/categories/${BLOUSE.categoryId}/design`,
      },
    ),
}

/** A category with no design groups yet. */
export const DesignEditorEmpty: Story = {
  render: () =>
    editor(
      aCatalogVersion({
        version: DRAFT_SUMMARY,
        categories: [BLOUSE],
        serviceTypes: [],
        designGroups: [],
        designRules: [],
      }),
    ),
}

/** A version that is not there any more, or never was. */
export const DesignEditorError: Story = {
  render: () =>
    withAdminApi(
      <CatalogDesignRoute />,
      {
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${CATALOG}/versions/${WORKING.version.catalogVersionId}`]: () =>
          storyProblem(404, 'catalog.version-not-found'),
      },
      {
        path: '/admin/catalog/:versionId/categories/:categoryId/design',
        at: `/admin/catalog/${WORKING.version.catalogVersionId}/categories/${BLOUSE.categoryId}/design`,
      },
    ),
}

/** The editor at 40% text growth. */
export const DesignEditorPseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () => editor(WORKING),
}

/** Somebody who may reach the address but holds neither catalogue permission. */
export const DesignEditorForbidden: Story = {
  render: () =>
    withAdminApi(
      <RequirePermission permission={ADMIN_PERMISSIONS.catalogEdit}>
        <CatalogDesignRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }),
        [`GET ${BRANCHES}`]: () => storyJson(BRANCH_ROWS),
        [`GET ${CATALOG}/versions/${WORKING.version.catalogVersionId}`]: () =>
          storyJson(WORKING, 'W/"1"'),
      },
      {
        path: '/admin/catalog/:versionId/categories/:categoryId/design',
        at: `/admin/catalog/${WORKING.version.catalogVersionId}/categories/${BLOUSE.categoryId}/design`,
      },
    ),
}
