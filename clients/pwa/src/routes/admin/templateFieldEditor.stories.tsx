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

const CENTIMETRE_ONLY = aTemplateField({
  templateFieldId: '0199bb00-0000-7000-8000-0000000000d5',
  key: 'shoulder_width',
  label: 'Shoulder width',
  groupName: 'Bodice',
  displayOrder: 4,
  displayUnits: ['Centimetre'],
  inchFraction: 0,
  centimetreDecimals: 2,
  minimumMillimetres: 250,
  maximumMillimetres: 600,
  warnBelowMillimetres: 300,
  warnAboveMillimetres: 520,
  helpText: 'Shoulder point to shoulder point, across the back.',
  diagramReference: null,
  diagramKey: null,
  diagramAlt: null,
})

const UNBOUNDED = aTemplateField({
  templateFieldId: '0199bb00-0000-7000-8000-0000000000d6',
  key: 'notes_length',
  label: 'Any extra length',
  groupName: 'Finishing',
  displayOrder: 5,
  minimumMillimetres: 0,
  maximumMillimetres: 0,
  warnBelowMillimetres: null,
  warnAboveMillimetres: null,
  helpText: 'Only when the customer asked for extra.',
  diagramReference: null,
  diagramKey: null,
  diagramAlt: null,
})

const BAND_DRAFT = aTemplateVersion({
  templateVersionId: '0199bb00-0000-7000-8000-0000000000e6',
  versionNumber: 5,
  name: 'Version 5',
  fields: [aTemplateField(), CENTIMETRE_ONLY, UNBOUNDED],
})

const BAND_TEMPLATE = aMeasurementTemplate({
  measurementTemplateId: '0199bb00-0000-7000-8000-0000000000f3',
  versions: [BAND_DRAFT],
})

/**
 * Bounds and thresholds in a tailor's units (#103).
 *
 * Three fields, deliberately: one read in inches at eighths, one that declares centimetres only and
 * to two places, and one that accepts any measurement. The table states each range in the unit the
 * field is read in — never in millimetres, which a tailor never sees — and the third states nothing
 * at all, because "0–0 mm" would read as a field that accepts only zero.
 *
 * Open a field to see the four controls: inches are a whole-number box plus a fraction strip, both
 * reachable from the keyboard, and never a decimal box.
 */
export const Bands: Story = {
  render: () =>
    withAdminApi(
      <TemplateVersionEditorRoute />,
      {
        [`GET ${TEMPLATES}/${BAND_TEMPLATE.measurementTemplateId}`]: () =>
          storyJson(BAND_TEMPLATE, 'W/"1"'),
      },
      {
        path: '/admin/templates/:templateId/versions/:versionId',
        at: `/admin/templates/${BAND_TEMPLATE.measurementTemplateId}/versions/${BAND_DRAFT.templateVersionId}`,
      },
    ),
}

const ORDER_SLEEVE = aTemplateField({
  templateFieldId: '0199bb00-0000-7000-8000-0000000000d7',
  key: 'sleeve_length',
  label: 'Sleeve length',
  groupName: 'Sleeve',
  displayOrder: 2,
})

const ORDER_CUFF = aTemplateField({
  templateFieldId: '0199bb00-0000-7000-8000-0000000000d8',
  key: 'cuff_round',
  label: 'Cuff round',
  groupName: 'Sleeve',
  displayOrder: 3,
})

const ORDER_WAIST = aTemplateField({
  templateFieldId: '0199bb00-0000-7000-8000-0000000000d9',
  key: 'waist',
  label: 'Waist',
  groupName: 'Bodice',
  displayOrder: 1,
})

const ORDER_DRAFT = aTemplateVersion({
  templateVersionId: '0199bb00-0000-7000-8000-0000000000e7',
  versionNumber: 6,
  name: 'Version 6',
  fields: [aTemplateField(), ORDER_WAIST, ORDER_SLEEVE, ORDER_CUFF],
})

const ORDER_TEMPLATE = aMeasurementTemplate({
  measurementTemplateId: '0199bb00-0000-7000-8000-0000000000f4',
  versions: [ORDER_DRAFT],
})

const ORDER_FIELDS = `${TEMPLATES}/${ORDER_TEMPLATE.measurementTemplateId}/versions/${ORDER_DRAFT.templateVersionId}/fields`

const orderEditor = (routes: Parameters<typeof withAdminApi>[1] = {}) =>
  withAdminApi(
    <TemplateVersionEditorRoute />,
    {
      [`GET ${TEMPLATES}/${ORDER_TEMPLATE.measurementTemplateId}`]: () =>
        storyJson(ORDER_TEMPLATE, 'W/"1"'),
      ...routes,
    },
    {
      path: '/admin/templates/:templateId/versions/:versionId',
      at: `/admin/templates/${ORDER_TEMPLATE.measurementTemplateId}/versions/${ORDER_DRAFT.templateVersionId}`,
    },
  )

/**
 * Grouping and ordering (#104).
 *
 * Two steps, four fields. Every move is a button — there is no drag here at all, which is the
 * strongest form of the rule that every drag has a button alternative. Each field says where it
 * sits in words, because a display order is global to the version and not contiguous until
 * something renumbers it.
 */
export const Ordering: Story = {
  render: () =>
    orderEditor(
      Object.fromEntries(
        [aTemplateField(), ORDER_WAIST, ORDER_SLEEVE, ORDER_CUFF].map((field) => [
          `PUT ${ORDER_FIELDS}/${field.templateFieldId}`,
          () => storyJson(ORDER_TEMPLATE, 'W/"1"'),
        ]),
      ),
    ),
}

/**
 * A move that stops part-way.
 *
 * Press "Measure Waist earlier". The first write lands and the second is refused, so a prefix of the
 * renumbering is saved — correct as far as it went, and not the state the person asked for. The
 * screen says how far it got and offers the reload that shows where the order actually stands,
 * rather than retrying silently or claiming the move happened.
 */
export const MoveStoppedPartWay: Story = {
  render: () =>
    orderEditor({
      [`PUT ${ORDER_FIELDS}/${ORDER_WAIST.templateFieldId}`]: () =>
        storyJson(ORDER_TEMPLATE, 'W/"2"'),
      [`PUT ${ORDER_FIELDS}/${aTemplateField().templateFieldId}`]: () =>
        storyProblem(409, 'measurements.version-changed'),
    }),
}

/** The 40% text growth the client guide asks every screen to tolerate. */
export const TextGrowth: Story = {
  ...Fields,
  globals: { locale: 'en-XA' },
}
