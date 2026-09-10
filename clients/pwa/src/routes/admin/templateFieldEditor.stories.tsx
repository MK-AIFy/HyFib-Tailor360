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
import { TemplateVersionEditorRoute } from './TemplateVersionEditorRoute'
import './admin.css'

/**
 * The draft field editor (#102), driven against a stubbed API rather than mocked.
 *
 * Every story renders the real route, because the question a reviewer is asking is whether *this
 * screen* has a usable state — a fact about its branches — and a story that rendered `EmptyState`
 * with the right words would prove only that `EmptyState` works.
 *
 * There is no offline story, for the reason the rest of the section has none: editing a template is
 * an online-only act, and a story here would imply the screen queues what a person did.
 */
const meta = {
  title: 'Administration/Template field editor',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

const TEMPLATES = '/api/v1/customers/measurement-templates'

const CHOICE = aTemplateField({
  templateFieldId: '0199bb00-0000-7000-8000-0000000000d3',
  key: 'neckline_shape',
  label: 'Neckline shape',
  groupName: 'Bodice',
  displayOrder: 2,
  canonicalUnit: 'None',
  displayUnits: [],
  inchFraction: 0,
  centimetreDecimals: 0,
  minimumMillimetres: 0,
  maximumMillimetres: 0,
  warnBelowMillimetres: null,
  warnAboveMillimetres: null,
  helpText: 'Whichever the customer chose on the design sheet.',
  diagramReference: null,
  diagramKey: null,
  diagramAlt: null,
  optionCodes: ['ROUND', 'SQUARE'],
  options: [
    { code: 'ROUND', label: 'Round', labelTamil: null, displayOrder: 0 },
    { code: 'SQUARE', label: 'Square', labelTamil: null, displayOrder: 1 },
  ],
})

const COUNT = aTemplateField({
  templateFieldId: '0199bb00-0000-7000-8000-0000000000d4',
  key: 'hook_count',
  label: 'Hooks',
  groupName: 'Finishing',
  displayOrder: 3,
  canonicalUnit: 'Count',
  displayUnits: ['Count'],
  inchFraction: 0,
  centimetreDecimals: 0,
  minimumMillimetres: 0,
  maximumMillimetres: 0,
  warnBelowMillimetres: null,
  warnAboveMillimetres: null,
  helpText: 'How many hooks the customer asked for.',
  diagramReference: null,
  diagramKey: null,
  diagramAlt: null,
})

const DRAFT = aTemplateVersion({ fields: [aTemplateField(), CHOICE, COUNT] })

const EMPTY_DRAFT = aTemplateVersion({
  templateVersionId: '0199bb00-0000-7000-8000-0000000000e5',
  versionNumber: 4,
  name: 'Version 4',
  fields: [],
})

const PUBLISHED = aTemplateVersion({
  templateVersionId: '0199bb00-0000-7000-8000-0000000000e2',
  versionNumber: 2,
  name: 'Version 2',
  status: 'Published',
  isApproved: true,
  publishedAt: '2026-09-05T09:15:00.000Z',
})

const TEMPLATE = aMeasurementTemplate({ versions: [DRAFT, EMPTY_DRAFT, PUBLISHED] })

const editor = (
  versionId: string,
  routes: Parameters<typeof withAdminApi>[1] = {},
  element = <TemplateVersionEditorRoute />,
) =>
  withAdminApi(
    element,
    {
      [`GET ${TEMPLATES}/${TEMPLATE.measurementTemplateId}`]: () => storyJson(TEMPLATE, 'W/"1"'),
      ...routes,
    },
    {
      path: '/admin/templates/:templateId/versions/:versionId',
      at: `/admin/templates/${TEMPLATE.measurementTemplateId}/versions/${versionId}`,
    },
  )

/** A draft with a length, a choice and a count in it. Open a field to see how each differs. */
export const Fields: Story = {
  render: () => editor(DRAFT.templateVersionId),
}

/** A version nobody has described yet, which is also a version that cannot be published. */
export const NoFieldsYet: Story = {
  render: () => editor(EMPTY_DRAFT.templateVersionId),
}

/** Loading, saying what is being loaded rather than spinning. */
export const Loading: Story = {
  render: () =>
    editor(DRAFT.templateVersionId, {
      [`GET ${TEMPLATES}/${TEMPLATE.measurementTemplateId}`]: storyPending,
    }),
}

/** A refusal, in words a person can act on and without the server's own message. */
export const Error: Story = {
  render: () =>
    editor(DRAFT.templateVersionId, {
      [`GET ${TEMPLATES}/${TEMPLATE.measurementTemplateId}`]: () =>
        storyProblem(503, 'platform.unavailable'),
    }),
}

/** Somebody who holds neither template permission: a sentence and who to ask, never a redirect. */
export const Forbidden: Story = {
  render: () =>
    editor(
      DRAFT.templateVersionId,
      {
        'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }),
      },
      <RequirePermission permission={ADMIN_PERMISSIONS.templatesEdit}>
        <TemplateVersionEditorRoute />
      </RequirePermission>,
    ),
}

/**
 * A published version, which is immutable in the database rather than by a validator.
 *
 * The editing controls are absent rather than disabled, and the sentence says where the change is
 * actually made — starting a draft from it, on the template screen.
 */
export const NotADraft: Story = {
  render: () => editor(PUBLISHED.templateVersionId),
}

/** An address naming a version this template does not have. */
export const NoSuchVersion: Story = {
  render: () => editor('0199bb00-0000-7000-8000-00000000dead'),
}

/**
 * Somebody else changed the template while this one was open.
 *
 * Save a field to see it. Reload is explicit: reading fresh state must not silently repeat a
 * command, and the retry key is kept so that a save whose answer was lost cannot happen twice.
 */
export const ChangedSinceRead: Story = {
  render: () =>
    editor(DRAFT.templateVersionId, {
      [`POST ${TEMPLATES}/${TEMPLATE.measurementTemplateId}/versions/${DRAFT.templateVersionId}/fields`]:
        () => storyProblem(409, 'measurements.version-changed'),
    }),
}

/** The 40% text growth the client guide asks every screen to tolerate. */
export const TextGrowth: Story = {
  ...Fields,
  globals: { locale: 'en-XA' },
}
