import type { Meta, StoryObj } from '@storybook/react-vite'
import {
  aMeasurementTemplate,
  aTemplateField,
  aTemplateVersion,
} from '../../admin/testing/fixtures'
import {
  STORY_USER,
  storyJson,
  storyPending,
  storyProblem,
  withAdminApi,
} from '../../admin/testing/storyTransport'
import { RequirePermission } from '../../admin/RequirePermission'
import { ADMIN_PERMISSIONS } from '../../admin/adminPermissions'
import { TemplateDetailRoute } from './TemplateDetailRoute'
import { TemplateListRoute } from './TemplateListRoute'
import './admin.css'

/**
 * The measurement-template screens, driven against a stubbed API rather than mocked.
 *
 * ## Why there is no offline story
 *
 * The same reason the rest of the administration section has none: publishing a template is an
 * online-only act, and the application says so through `NetworkStatusBanner` and
 * `OfflineBlockedAction`, which have their own stories. An offline story here would imply these
 * screens queue what a person did, and they must not.
 *
 * ## Why the states are per screen rather than shared
 *
 * A loading story that rendered `LoadingState` would prove that component works, which is not in
 * question. What a reviewer is asking is whether *this* screen has a usable loading state — a fact
 * about its branches — so every story below renders the real route.
 */
const meta = {
  title: 'Administration/Measurement templates',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

const TEMPLATES = '/api/v1/customers/measurement-templates'

const BLOUSE = aMeasurementTemplate()

const SALWAR = aMeasurementTemplate({
  measurementTemplateId: '0199bb00-0000-7000-8000-0000000000f2',
  code: 'MT_SALWAR',
  name: 'Salwar kameez',
  description: 'What is measured for a salwar kameez.',
  publishedVersionId: '0199bb00-0000-7000-8000-0000000000e2',
  versions: [
    aTemplateVersion({
      templateVersionId: '0199bb00-0000-7000-8000-0000000000e2',
      versionNumber: 2,
      name: 'Version 2',
      status: 'Published',
      isApproved: true,
      publishedAt: '2026-09-05T09:15:00.000Z',
      fields: [
        aTemplateField(),
        aTemplateField({
          templateFieldId: '0199bb00-0000-7000-8000-0000000000d2',
          key: 'waist',
          label: 'Waist',
          groupName: 'Lower',
          displayOrder: 1,
          minimumMillimetres: 400,
          maximumMillimetres: 1600,
          warnBelowMillimetres: 500,
          warnAboveMillimetres: 1400,
        }),
      ],
    }),
    aTemplateVersion({
      templateVersionId: '0199bb00-0000-7000-8000-0000000000e4',
      versionNumber: 3,
      name: 'Version 3',
      status: 'InReview',
      notes: 'Widened the waist band after the Erode trial.',
    }),
  ],
})

/* The list ------------------------------------------------------------------------------------ */

/** Two templates: one capturing against a published version, one that cannot measure anything yet. */
export const TemplateList: Story = {
  render: () =>
    withAdminApi(<TemplateListRoute />, {
      [`GET ${TEMPLATES}`]: () => storyJson([SALWAR, BLOUSE]),
    }),
}

/** Loading, saying what is being loaded rather than spinning. */
export const TemplateListLoading: Story = {
  render: () => withAdminApi(<TemplateListRoute />, { [`GET ${TEMPLATES}`]: storyPending }),
}

/** A first install: nothing set up, and the screen says what that means for the shop. */
export const TemplateListEmpty: Story = {
  render: () => withAdminApi(<TemplateListRoute />, { [`GET ${TEMPLATES}`]: () => storyJson([]) }),
}

/** A refusal, in words a person can act on and without the server's own message. */
export const TemplateListError: Story = {
  render: () =>
    withAdminApi(<TemplateListRoute />, {
      [`GET ${TEMPLATES}`]: () => storyProblem(503, 'platform.unavailable'),
    }),
}

const WITHOUT_PERMISSIONS = {
  ...STORY_USER,
  userId: '0199bb00-0000-7000-8000-00000000000f',
  permissions: [],
}

/** Somebody who holds neither template permission: a sentence and who to ask, never a redirect. */
export const TemplateListForbidden: Story = {
  render: () =>
    withAdminApi(
      <RequirePermission permission={ADMIN_PERMISSIONS.templatesEdit}>
        <TemplateListRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson(WITHOUT_PERMISSIONS),
        [`GET ${TEMPLATES}`]: () => storyJson([SALWAR]),
      },
    ),
}

/* One template -------------------------------------------------------------------------------- */

const detail = (template: typeof BLOUSE) =>
  withAdminApi(
    <TemplateDetailRoute />,
    {
      [`GET ${TEMPLATES}/${template.measurementTemplateId}`]: () => storyJson(template, 'W/"1"'),
    },
    {
      path: '/admin/templates/:templateId',
      at: `/admin/templates/${template.measurementTemplateId}`,
    },
  )

/** A template with a published version and a successor in review — the ordinary working state. */
export const TemplateDetail: Story = { render: () => detail(SALWAR) }

/** A draft, before anybody has been asked to look at it. Only one act is offered. */
export const TemplateDetailDraft: Story = { render: () => detail(BLOUSE) }

/**
 * A version waiting for a second administrator.
 *
 * Publication is not offered: approval comes first, and a control that would be refused is not a
 * control.
 */
export const TemplateDetailInReview: Story = {
  render: () =>
    detail(
      aMeasurementTemplate({
        versions: [aTemplateVersion({ status: 'InReview', name: 'Version 1' })],
      }),
    ),
}

/** A version with no fields yet, which is a version that cannot be published. */
export const TemplateDetailNoFields: Story = {
  render: () => detail(aMeasurementTemplate({ versions: [aTemplateVersion({ fields: [] })] })),
}

/**
 * An administrator who may draft but not review.
 *
 * Four of the five acts are gated on `catalog.templates.publish`, which a custom role may withhold
 * while still granting `catalog.templates.edit`. The controls are not rendered greyed out — they are
 * not rendered at all, and the reason is said in words, because a row of controls that each end in a
 * refusal reads as a broken screen rather than as a boundary.
 */
export const TemplateDetailWithoutPublish: Story = {
  render: () => {
    const template = aMeasurementTemplate({
      versions: [aTemplateVersion({ status: 'InReview', name: 'Version 1' })],
    })

    return withAdminApi(
      <TemplateDetailRoute />,
      {
        'GET /api/v1/me': () =>
          storyJson({ ...STORY_USER, permissions: [ADMIN_PERMISSIONS.templatesEdit] }),
        [`GET ${TEMPLATES}/${template.measurementTemplateId}`]: () => storyJson(template, 'W/"1"'),
      },
      {
        path: '/admin/templates/:templateId',
        at: `/admin/templates/${template.measurementTemplateId}`,
      },
    )
  },
}

/** Loading one template. */
export const TemplateDetailLoading: Story = {
  render: () =>
    withAdminApi(
      <TemplateDetailRoute />,
      { [`GET ${TEMPLATES}/${BLOUSE.measurementTemplateId}`]: storyPending },
      {
        path: '/admin/templates/:templateId',
        at: `/admin/templates/${BLOUSE.measurementTemplateId}`,
      },
    ),
}

/** A template that is not there any more, or never was. */
export const TemplateDetailError: Story = {
  render: () =>
    withAdminApi(
      <TemplateDetailRoute />,
      {
        [`GET ${TEMPLATES}/${BLOUSE.measurementTemplateId}`]: () =>
          storyProblem(404, 'measurements.template-not-found'),
      },
      {
        path: '/admin/templates/:templateId',
        at: `/admin/templates/${BLOUSE.measurementTemplateId}`,
      },
    ),
}
