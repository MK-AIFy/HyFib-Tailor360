import type { Meta, StoryObj } from '@storybook/react-vite'
import {
  STORY_USER,
  storyJson,
  storyPending,
  storyProblem,
  withAdminApi,
} from '../../admin/testing/storyTransport'
import { RequirePermission } from '../../admin/RequirePermission'
import { PSEUDO_LOCALE } from '../../i18n/pseudo'
import { CATALOG_PERMISSIONS } from '../../catalog/catalogPermissions'
import {
  aDesignCheck,
  aDesignMigrationPrompt,
  aDesignPicker,
  aDesignPickerGroup,
  aDesignPickerOption,
  aDesignSelectionDraft,
  aGarmentDesignSelectionSnapshot,
  aGarmentDesignSnapshot,
} from '../../catalog/testing/fixtures'
import { DesignPickerRoute } from './DesignPickerRoute'
import { GarmentDesignCard } from './GarmentDesignCard'

/**
 * The design picker and the job-card design component (#142), driven against a stubbed API.
 *
 * Five states per screen, the same reasoning `measurementScreens.stories.tsx` records: this is a
 * customer-facing surface, so offline is one of the five rather than left out the way the
 * administration screens leave it out — saving a choice is exactly the act a person is most likely
 * to attempt with no signal at the counter, and the screen has to say so in place.
 *
 * The job-card component takes no network states of its own — it renders a `GarmentDesignSnapshot`
 * given to it and asks the catalogue for nothing, which is the whole point of a snapshot — so it
 * gets a default story and a pseudo-locale story rather than the five.
 */
const meta = {
  title: 'Catalog/Design picker',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

const PICKER_USER = { ...STORY_USER, permissions: [CATALOG_PERMISSIONS.designSelect] }

const CATALOG = '/api/v1/catalog'
const SERVICE_TYPE_ID = '0199bb00-0000-7000-8000-0000000000d1'
const DRAFT_ID = '0199bb00-0000-7000-8000-000000009a1'
const PICKER_PATH = `${CATALOG}/current/service-types/${SERVICE_TYPE_ID}/design`
const DRAFT_PATH = `${CATALOG}/design-drafts/${DRAFT_ID}`

const ROUND = aDesignPickerOption({ code: 'ROUND', name: 'Round neck' })
const V_NECK = aDesignPickerOption({
  designOptionId: '0199bb00-0000-7000-8000-0000000000e2',
  code: 'V_NECK',
  name: 'V neck',
  displayOrder: 1,
  helpText: 'A neckline cut to a point at the front.',
})
const NECKLINE = aDesignPickerGroup({
  code: 'neckline',
  name: 'Neckline',
  options: [ROUND, V_NECK],
})
const SHORT = aDesignPickerOption({
  designOptionId: '0199bb00-0000-7000-8000-0000000000e3',
  code: 'SHORT',
  name: 'Short sleeve',
})
const SLEEVE = aDesignPickerGroup({
  designOptionGroupId: '0199bb00-0000-7000-8000-0000000000e4',
  code: 'sleeve',
  name: 'Sleeve length',
  required: false,
  displayOrder: 1,
  options: [SHORT],
})

/** What `fetch` does with no connection: rejects before any response exists. */
function storyUnreachable(): Promise<Response> {
  return Promise.reject(new TypeError('Failed to fetch'))
}

/** Sets the link state the network hook reads, the same way `measurementScreens.stories.tsx` does. */
function link<T>(online: boolean, render: () => T): T {
  Object.defineProperty(navigator, 'onLine', { configurable: true, value: online })
  window.dispatchEvent(new Event(online ? 'online' : 'offline'))
  return render()
}

const picker = (routes: Parameters<typeof withAdminApi>[1], online = true) =>
  link(online, () =>
    withAdminApi(
      <RequirePermission permission={CATALOG_PERMISSIONS.designSelect}>
        <DesignPickerRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson(PICKER_USER),
        [`GET ${PICKER_PATH}`]: () =>
          storyJson(aDesignPicker({ serviceTypeId: SERVICE_TYPE_ID, groups: [NECKLINE, SLEEVE] })),
        [`GET ${DRAFT_PATH}`]: () =>
          storyJson(aDesignSelectionDraft({ designSelectionDraftId: DRAFT_ID }), 'W/"1"'),
        [`GET ${DRAFT_PATH}/check?hasReferenceImage=false`]: () => storyJson(aDesignCheck()),
        [`PUT ${DRAFT_PATH}`]: () =>
          storyJson(aDesignSelectionDraft({ designSelectionDraftId: DRAFT_ID }), 'W/"2"'),
        ...routes,
      },
      {
        path: '/catalog/design/:serviceTypeId/:draftId',
        at: `/catalog/design/${SERVICE_TYPE_ID}/${DRAFT_ID}`,
      },
    ),
  )

/** Choosing a neckline and a sleeve length, nothing pre-selected. */
export const Picker: Story = { render: () => picker({}) }

export const PickerLoading: Story = {
  render: () =>
    picker({ [`GET ${PICKER_PATH}`]: storyPending, [`GET ${DRAFT_PATH}`]: storyPending }),
}

/** A service type published with no design groups at all: there is nothing to choose from yet. */
export const PickerEmpty: Story = {
  render: () =>
    picker({
      [`GET ${PICKER_PATH}`]: () =>
        storyJson(aDesignPicker({ serviceTypeId: SERVICE_TYPE_ID, groups: [] })),
    }),
}

export const PickerError: Story = {
  render: () => picker({ [`GET ${PICKER_PATH}`]: () => storyProblem(503, 'platform.unavailable') }),
}

/** Somebody without `catalog.design.select`: a sentence and who to ask, never a redirect. */
export const PickerForbidden: Story = {
  render: () => picker({ 'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }) }),
}

/** Choosing needs a connection; every card stays visible and disabled, and nothing is queued. */
export const PickerOffline: Story = {
  render: () => picker({ [`GET ${DRAFT_PATH}`]: storyUnreachable }, false),
}

/** The catalogue changed since this draft was pinned: migrate now, or finish on this version. */
export const PickerMigrationPrompt: Story = {
  render: () =>
    picker({
      [`GET ${DRAFT_PATH}`]: () =>
        storyJson(
          aDesignSelectionDraft({
            designSelectionDraftId: DRAFT_ID,
            migrationPrompt: aDesignMigrationPrompt(),
          }),
          'W/"1"',
        ),
    }),
}

export const PickerPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => picker({}),
}

/* The job-card design component ---------------------------------------------------------------- */

const SNAPSHOT = aGarmentDesignSnapshot({
  catalogVersionNumber: 4,
  categoryLabel: 'Blouse (Pattern)',
  serviceTypeLabel: 'Pattern work',
  selections: [
    aGarmentDesignSelectionSnapshot({
      groupCode: 'neckline',
      groupLabel: 'Neckline',
      groupDisplayOrder: 0,
      optionCode: 'ROUND',
      optionLabel: 'Round neck',
    }),
    aGarmentDesignSelectionSnapshot({
      groupCode: 'sleeve',
      groupLabel: 'Sleeve length',
      groupDisplayOrder: 1,
      optionCode: 'SHORT',
      optionLabel: 'Short sleeve',
      priceListItemCode: null,
    }),
  ],
  conditionalNotes: ['Cut the lining 5 mm wider than the shell at the armhole.'],
  instructions: 'Customer asked for a slightly deeper back.',
})

/** Renders from the snapshot alone — no catalogue request, so a two-year-old order prints the same. */
export const JobCard: Story = {
  render: () => <GarmentDesignCard snapshot={SNAPSHOT} />,
}

export const JobCardPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => <GarmentDesignCard snapshot={SNAPSHOT} />,
}
