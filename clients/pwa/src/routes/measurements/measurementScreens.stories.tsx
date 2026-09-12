import type { Meta, StoryObj } from '@storybook/react-vite'
import { ShellStatusProvider } from '../../components/layout/ShellStatusProvider'
import {
  STORY_USER,
  storyJson,
  storyPending,
  storyProblem,
  withAdminApi,
} from '../../admin/testing/storyTransport'
import { RequirePermission } from '../../admin/RequirePermission'
import { PSEUDO_LOCALE } from '../../i18n/pseudo'
import { MEASUREMENT_PERMISSIONS } from '../../measurements/measurementsPermissions'
import {
  DRAFT_ID,
  aCaptureTemplate,
  aCustomerCard,
  aMeasurementDraft,
  anOrderableCatalog,
  anOrderableService,
} from '../../measurements/testing/fixtures'
import { MeasurementDraftRoute } from './MeasurementDraftRoute'
import { MeasurementStartRoute } from './MeasurementStartRoute'
import './measurements.css'

/**
 * The measurement-capture screens (#123), driven against a stubbed API rather than mocked.
 *
 * ## The five states, and where the offline one is
 *
 * Loading, empty, error and forbidden are stories here, one per screen, each rendering the real
 * route. The offline state is a story here too, unlike the administration screens: saving a step
 * and confirming are the acts a person is most likely to attempt with no signal in the back of a
 * shop, and the screen has to say — in place, beside the fields — that it needs a connection and
 * will not queue, while keeping every value typed. The story forces the link state the same way the
 * hook reads it.
 *
 * ## Why every story wraps a `ShellStatusProvider`
 *
 * The wizard publishes "Saving…", "Saved." and the unit change into the shell's autosave region
 * rather than into a toast, which is the rule for every status message on this product. The shell
 * provides the region in the application; a story provides it here so the wizard can publish.
 */
const meta = {
  title: 'Measurements/Capture',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

const CAPTURE_USER = {
  ...STORY_USER,
  permissions: [
    MEASUREMENT_PERMISSIONS.capture,
    MEASUREMENT_PERMISSIONS.customersRead,
    MEASUREMENT_PERMISSIONS.catalogRead,
  ],
}

const DRAFTS = '/api/v1/customers/measurement-drafts'
const DRAFT = `${DRAFTS}/${DRAFT_ID}`

const start = (routes: Parameters<typeof withAdminApi>[1], online = true) =>
  link(online, () =>
    withAdminApi(
      <ShellStatusProvider>
        <RequirePermission permission={MEASUREMENT_PERMISSIONS.capture}>
          <MeasurementStartRoute />
        </RequirePermission>
      </ShellStatusProvider>,
      {
        'GET /api/v1/me': () => storyJson(CAPTURE_USER),
        'GET /api/v1/catalog/current': () => storyJson(anOrderableCatalog()),
        'GET /api/v1/customers/?term=Asha': () =>
          storyJson({ customers: [aCustomerCard()], nextCursor: null }),
        [`POST ${DRAFTS}`]: () => storyJson(aMeasurementDraft(), 'W/"1"'),
        ...routes,
      },
      { path: '/measurements/new', at: '/measurements/new' },
    ),
  )

const wizard = (routes: Parameters<typeof withAdminApi>[1], online = true) =>
  link(online, () =>
    withAdminApi(
      <ShellStatusProvider>
        <RequirePermission permission={MEASUREMENT_PERMISSIONS.capture}>
          <MeasurementDraftRoute />
        </RequirePermission>
      </ShellStatusProvider>,
      {
        'GET /api/v1/me': () => storyJson(CAPTURE_USER),
        [`GET ${DRAFT}`]: () => storyJson(aMeasurementDraft(), 'W/"1"'),
        [`GET ${DRAFT}/template`]: () => storyJson(aCaptureTemplate()),
        [`GET ${DRAFT}/check`]: () =>
          storyJson({ measurementDraftId: DRAFT_ID, confirmable: true, findings: [] }),
        [`POST ${DRAFT}/sections`]: () => storyJson(aMeasurementDraft(), 'W/"2"'),
        ...routes,
      },
      { path: '/measurements/drafts/:draftId', at: `/measurements/drafts/${DRAFT_ID}` },
    ),
  )

/**
 * Sets the link state the network hook reads, and tells it so.
 *
 * `navigator.onLine` is read once and then followed through the `online` and `offline` events, so
 * a story has to do both — and every story sets it, because the state outlives the story that set
 * it and a reviewer clicking from the offline story to the next one must not carry it along.
 */
function link<T>(online: boolean, render: () => T): T {
  Object.defineProperty(navigator, 'onLine', { configurable: true, value: online })
  window.dispatchEvent(new Event(online ? 'online' : 'offline'))
  return render()
}

/* Starting ------------------------------------------------------------------------------------ */

/** Search "Asha" to see the customer, choose the garment, and start. */
export const Start: Story = { render: () => start({}) }

export const StartLoading: Story = {
  render: () => start({ 'GET /api/v1/catalog/current': storyPending }),
}

/** No service in the published catalogue points at a template: the screen says what would fix it. */
export const StartEmpty: Story = {
  render: () =>
    start({
      'GET /api/v1/catalog/current': () =>
        storyJson(
          anOrderableCatalog({ services: [anOrderableService({ measurementTemplateId: null })] }),
        ),
    }),
}

export const StartError: Story = {
  render: () =>
    start({ 'GET /api/v1/catalog/current': () => storyProblem(503, 'platform.unavailable') }),
}

/** Somebody without `measurements.capture`: a sentence and who to ask, never a redirect. */
export const StartForbidden: Story = {
  render: () => start({ 'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }) }),
}

/** Starting needs a connection; the screen says so and keeps the search available. */
export const StartOffline: Story = { render: () => start({}, false) }

/* The wizard ---------------------------------------------------------------------------------- */

/** The bodice step of a two-step blouse template, opened fresh. */
export const Wizard: Story = { render: () => wizard({}) }

/** A draft pre-filled from an earlier measurement, which the screen says before anything else. */
export const WizardReused: Story = {
  render: () =>
    wizard({
      [`GET ${DRAFT}`]: () =>
        storyJson(
          aMeasurementDraft({
            reusedFromVersionId: '0199cc00-0000-7000-8000-0000000000b0',
            values: [
              {
                key: 'chest_bust',
                millimetres: 927.1,
                enteredUnit: 'Inch',
                choice: null,
                acknowledged: false,
              },
              {
                key: 'closure',
                millimetres: null,
                enteredUnit: 'Inch',
                choice: 'back_hooks',
                acknowledged: false,
              },
            ],
          }),
          'W/"4"',
        ),
    }),
}

export const WizardLoading: Story = {
  render: () => wizard({ [`GET ${DRAFT}`]: storyPending, [`GET ${DRAFT}/template`]: storyPending }),
}

/** The draft became a record already: the closest thing this screen has to an empty state. */
export const WizardConfirmedAlready: Story = {
  render: () =>
    wizard({
      [`GET ${DRAFT}`]: () =>
        storyJson(aMeasurementDraft({ consumedAt: '2026-09-11T04:30:00.000Z' }), 'W/"3"'),
    }),
}

/** A draft that ran out of time: the values are gone and the screen says where to start again. */
export const WizardExpired: Story = {
  render: () =>
    wizard({
      [`GET ${DRAFT}`]: () => storyProblem(410, 'measurements.draft-expired'),
      [`GET ${DRAFT}/template`]: () => storyProblem(410, 'measurements.draft-expired'),
    }),
}

export const WizardError: Story = {
  render: () =>
    wizard({
      [`GET ${DRAFT}`]: () => storyProblem(503, 'platform.unavailable'),
      [`GET ${DRAFT}/template`]: () => storyProblem(503, 'platform.unavailable'),
    }),
}

export const WizardForbidden: Story = {
  render: () => wizard({ 'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }) }),
}

/** Saving needs a connection: the bar gives way to the blocked-action state, and the values stay. */
export const WizardOffline: Story = { render: () => wizard({}, false) }

/** The 40% growth tolerance, on the screen with the most controls per row in the product. */
export const WizardPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => wizard({}),
}
