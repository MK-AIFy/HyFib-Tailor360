import type { Meta, StoryObj } from '@storybook/react-vite'
import { RequirePermission } from '../../admin/RequirePermission'
import {
  STORY_USER,
  storyJson,
  storyPending,
  storyProblem,
  withAdminApi,
} from '../../admin/testing/storyTransport'
import { PSEUDO_LOCALE } from '../../i18n/pseudo'
import { CUSTOMERS_PERMISSIONS } from '../../customers/customersPermissions'
import {
  aCustomer,
  aCustomerCard,
  aDuplicateCandidate,
  aTimelineEntry,
  aTimelinePage,
} from '../../customers/testing/fixtures'
import { CUSTOMER_DUPLICATES_CODE, CUSTOMER_VERSION_CONFLICT_CODE } from '../../customers/types'
import { CustomerCreateRoute } from './CustomerCreateRoute'
import { CustomerDetailRoute } from './CustomerDetailRoute'
import { CustomerEditRoute } from './CustomerEditRoute'
import { CustomerSearchRoute } from './CustomerSearchRoute'
import './customers.css'

/**
 * Finding, registering and reading a customer (#26, #182 unit 1), driven against a stubbed API
 * rather than a mock of one — the same reasoning `adminScreens.stories.tsx` and
 * `measurementScreens.stories.tsx` record: the value is in these being the real screens, so a
 * reviewer is looking at this screen's own branches rather than a state component with
 * customer-shaped words typed into it.
 *
 * ## Search and detail have no offline story
 *
 * Neither screen makes a write: search only reads, and the detail screen's one action is a link to
 * the correction form rather than a write of its own. Registering and correcting do write, so
 * `CustomerCreateRoute` and `CustomerEditRoute` gate on `useNetworkState` the same way a counter
 * payment does, and those are the two offline stories here.
 *
 * ## Why the search stories say what to type
 *
 * The search box only calls the API once somebody submits it — there is no term to stub in advance —
 * so each story stubs one literal term and its own doc comment says which to type, exactly the
 * convention `measurementScreens.stories.tsx`'s own customer search already established.
 *
 * ## Loading and empty, for a screen with nothing to load
 *
 * `CustomerCreateRoute` fetches nothing on mount, so it has no loading skeleton and no empty list —
 * its "empty" is the blank form itself, and its "loading" is the busy button between Register and the
 * response, which `CreateSubmitting` shows.
 */
const meta = {
  title: 'Customers/Screens',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

const CUSTOMERS = '/api/v1/customers/'
const CUSTOMER = aCustomer()
const DETAIL_AT = `/customers/${CUSTOMER.customerId}`

/**
 * `STORY_USER` predates this module, so it holds none of `CUSTOMERS_PERMISSIONS` — the same reason
 * `measurementScreens.stories.tsx` builds its own `CAPTURE_USER` rather than growing the shared
 * fixture for every module that comes after it.
 */
const CUSTOMERS_USER = {
  ...STORY_USER,
  permissions: [
    CUSTOMERS_PERMISSIONS.read,
    CUSTOMERS_PERMISSIONS.create,
    CUSTOMERS_PERMISSIONS.readContact,
    CUSTOMERS_PERMISSIONS.update,
  ],
}

/**
 * Sets the link state the network hook reads, and tells it so.
 *
 * `navigator.onLine` is read once and then followed through the `online` and `offline` events, so a
 * story has to do both — and every story sets it, because the state outlives the story that set it
 * and a reviewer clicking from the offline story to the next one must not carry it along.
 */
function link<T>(online: boolean, render: () => T): T {
  Object.defineProperty(navigator, 'onLine', { configurable: true, value: online })
  window.dispatchEvent(new Event(online ? 'online' : 'offline'))
  return render()
}

const search = (routes: Parameters<typeof withAdminApi>[1]) =>
  withAdminApi(
    <RequirePermission permission={CUSTOMERS_PERMISSIONS.read}>
      <CustomerSearchRoute />
    </RequirePermission>,
    {
      'GET /api/v1/me': () => storyJson(CUSTOMERS_USER),
      ...routes,
    },
    { path: '/customers', at: '/customers' },
  )

const create = (routes: Parameters<typeof withAdminApi>[1], online = true) =>
  link(online, () =>
    withAdminApi(
      <RequirePermission permission={CUSTOMERS_PERMISSIONS.create}>
        <CustomerCreateRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson(CUSTOMERS_USER),
        [`POST ${CUSTOMERS}`]: () => storyJson(CUSTOMER, 'W/"1"'),
        ...routes,
      },
      { path: '/customers/new', at: '/customers/new' },
    ),
  )

const detail = (routes: Parameters<typeof withAdminApi>[1]) =>
  withAdminApi(
    <RequirePermission permission={CUSTOMERS_PERMISSIONS.read}>
      <CustomerDetailRoute />
    </RequirePermission>,
    {
      'GET /api/v1/me': () => storyJson(CUSTOMERS_USER),
      [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: () => storyJson(CUSTOMER, 'W/"1"'),
      [TIMELINE]: () => storyJson(aTimelinePage()),
      ...routes,
    },
    { path: '/customers/:customerId', at: DETAIL_AT },
  )

const TIMELINE = `GET ${CUSTOMERS}${CUSTOMER.customerId}/timeline`
const EDIT_AT = `/customers/${CUSTOMER.customerId}/edit`

const edit = (routes: Parameters<typeof withAdminApi>[1], online = true) =>
  link(online, () =>
    withAdminApi(
      <RequirePermission permission={CUSTOMERS_PERMISSIONS.update}>
        <CustomerEditRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson(CUSTOMERS_USER),
        [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: () => storyJson(CUSTOMER, 'W/"1"'),
        [`PUT ${CUSTOMERS}${CUSTOMER.customerId}`]: () => storyJson(CUSTOMER, 'W/"2"'),
        ...routes,
      },
      { path: '/customers/:customerId/edit', at: EDIT_AT },
    ),
  )

/* Search ----------------------------------------------------------------------------------- */

/** Search "priya" to see a match, and open it. */
export const Search: Story = {
  render: () =>
    search({
      'GET /api/v1/customers/?term=priya': () =>
        storyJson({ customers: [aCustomerCard()], nextCursor: null }),
    }),
}

/** Search "priya" to see the loading state — the request never answers. */
export const SearchLoading: Story = {
  render: () => search({ 'GET /api/v1/customers/?term=priya': storyPending }),
}

/** Search "nobody" for nothing to match, and the way out. */
export const SearchEmpty: Story = {
  render: () =>
    search({
      'GET /api/v1/customers/?term=nobody': () => storyJson({ customers: [], nextCursor: null }),
    }),
}

/** Search "priya" for a read that failed. */
export const SearchError: Story = {
  render: () =>
    search({
      'GET /api/v1/customers/?term=priya': () => storyProblem(503, 'platform.unavailable'),
    }),
}

/** Search "priya" for a result at a branch the caller cannot see into, offered anyway. */
export const SearchMaskedResult: Story = {
  render: () =>
    search({
      'GET /api/v1/customers/?term=priya': () =>
        storyJson({ customers: [aCustomerCard({ visibleToCaller: false })], nextCursor: null }),
    }),
}

/** Somebody without `customers.read`: a sentence and who to ask, never a redirect. */
export const SearchForbidden: Story = {
  render: () => search({ 'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }) }),
}

/* Registering -------------------------------------------------------------------------------- */

/** A blank form — this screen's empty state, ready to fill in. Type a name and Register. */
export const Create: Story = { render: () => create({}) }

/** Register with a name typed, to see the busy button before the record opens. */
export const CreateSubmitting: Story = {
  render: () => create({ [`POST ${CUSTOMERS}`]: storyPending }),
}

/**
 * Type a name and a telephone number that matches a synthetic record, then Register, to see why
 * each candidate matched and the decision to register anyway.
 */
export const CreateDuplicates: Story = {
  render: () =>
    create({
      [`POST ${CUSTOMERS}`]: () =>
        storyProblem(409, CUSTOMER_DUPLICATES_CODE, {
          candidates: [aDuplicateCandidate({ reasons: ['Same telephone number', 'Same name'] })],
        }),
    }),
}

/** Register, for a refusal that is not the duplicate question. */
export const CreateError: Story = {
  render: () =>
    create({ [`POST ${CUSTOMERS}`]: () => storyProblem(403, 'security.permission-denied') }),
}

/** Registering needs a connection; the screen says so and keeps every value typed. */
export const CreateOffline: Story = { render: () => create({}, false) }

/** Somebody without `customers.create`: a sentence and who to ask, never a redirect. */
export const CreateForbidden: Story = {
  render: () => create({ 'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }) }),
}

/** The 40% growth tolerance, on the screen with the most field labels per page in this module. */
export const CreatePseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => create({}),
}

/* Reading one record --------------------------------------------------------------------------- */

/** The ordinary record: a status that is a word, and its contact details. */
export const Detail: Story = { render: () => detail({}) }

export const DetailLoading: Story = {
  render: () => detail({ [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: storyPending }),
}

/** Not found, or not one this caller can reach — the two read alike on purpose. */
export const DetailNotFound: Story = {
  render: () =>
    detail({
      [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: () =>
        storyProblem(404, 'customers.customer-not-found'),
    }),
}

export const DetailError: Story = {
  render: () =>
    detail({
      [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: () => storyProblem(503, 'platform.unavailable'),
    }),
}

/**
 * A caller without `customers.read_contact`: the contact fields say a permission is missing, never
 * blank — blank is reserved for a customer who genuinely gave none, which reads identically to this
 * caller only by coincidence of styling, never of wording.
 */
export const DetailContactWithheld: Story = {
  render: () =>
    detail({
      [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: () =>
        storyJson({ ...CUSTOMER, contactIncluded: false, phone: null, email: null }, 'W/"1"'),
    }),
}

/** Somebody without `customers.read`: a sentence and who to ask, never a redirect. */
export const DetailForbidden: Story = {
  render: () => detail({ 'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }) }),
}

/** The 40% growth tolerance, on the detail grid's own container-query fix. */
export const DetailPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => detail({}),
}

/* Correcting one record ------------------------------------------------------------------------ */

/** The form as the record fills it. Change a field, write a reason, and save. */
export const Edit: Story = { render: () => edit({}) }

/** This screen's loading state: the record it is about to become a form for. */
export const EditLoading: Story = {
  render: () => edit({ [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: storyPending }),
}

/** Write a reason and save, to see the busy button before the record is read again. */
export const EditSaving: Story = {
  render: () => edit({ [`PUT ${CUSTOMERS}${CUSTOMER.customerId}`]: storyPending }),
}

/**
 * Write a reason and save, to meet the conflict: somebody else corrected the record first. The
 * reload is offered as a control, and what was typed stays in the fields — a correction is somebody
 * reading a document aloud, and throwing that away to show them the spelling they just rejected is
 * the failure the client guide's "never discards typed input" rule names.
 */
export const EditVersionConflict: Story = {
  render: () =>
    edit({
      [`PUT ${CUSTOMERS}${CUSTOMER.customerId}`]: () =>
        storyProblem(409, CUSTOMER_VERSION_CONFLICT_CODE),
    }),
}

/** Save, for a refusal that is not the conflict — rendered as itself, with no reload offered. */
export const EditError: Story = {
  render: () =>
    edit({
      [`PUT ${CUSTOMERS}${CUSTOMER.customerId}`]: () =>
        storyProblem(403, 'security.permission-denied'),
    }),
}

/** Not found, or not one this caller can reach — this screen's empty state. */
export const EditNotFound: Story = {
  render: () =>
    edit({
      [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: () =>
        storyProblem(404, 'customers.customer-not-found'),
    }),
}

/**
 * A caller without `customers.read_contact`, who therefore cannot correct this record at all.
 *
 * The screen offers no form. A correction is a whole-record `PUT`, so one built from a record whose
 * contact fields were withheld would ask the server to clear the customer's telephone number — the
 * server refuses it, and a form whose save is guaranteed to fail is a broken screen rather than a
 * boundary. See `CustomerEditRoute`'s own doc comment.
 */
export const EditContactWithheld: Story = {
  render: () =>
    edit({
      [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: () =>
        storyJson({ ...CUSTOMER, contactIncluded: false, phone: null }, 'W/"1"'),
    }),
}

/**
 * Press Save without writing a reason, to see the error summary take focus, list the sentence, and
 * point at the field — the endpoint records a reason against every correction, so an empty one is a
 * refusal the screen has to explain rather than a save that quietly does nothing.
 */
export const EditValidation: Story = { render: () => edit({}) }

/** Correcting a record needs a connection; the screen says so and keeps every value typed. */
export const EditOffline: Story = { render: () => edit({}, false) }

/** Somebody without `customers.update`: a sentence and who to ask, never a redirect. */
export const EditForbidden: Story = {
  render: () => edit({ 'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }) }),
}

/** The 40% growth tolerance, on the screen that now has the most labels per page in this module. */
export const EditPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => edit({}),
}

/* The history tab ------------------------------------------------------------------------------- */

/** Open History to see the merged rail: what happened, when, who did it and why. */
export const DetailHistory: Story = {
  render: () =>
    detail({
      [TIMELINE]: () =>
        storyJson(
          aTimelinePage({
            entries: [
              aTimelineEntry(),
              aTimelineEntry({
                entryId: '0199cc00-0000-7000-8000-00000000e002',
                kind: 'customers.consent.recorded',
                title: 'Consent recorded for appointment reminders',
                detail: null,
                reason: null,
                reasonPermission: null,
                occurredAt: '2026-08-20T09:15:00Z',
              }),
              aTimelineEntry({
                entryId: '0199cc00-0000-7000-8000-00000000e003',
                kind: 'customers.record.registered',
                title: 'Customer registered',
                detail: null,
                reason: null,
                reasonPermission: null,
                actorDisplayName: null,
                occurredAt: '2026-01-04T05:00:00Z',
              }),
            ],
          }),
        ),
    }),
}

/** Open History for this screen's loading state — the request never answers. */
export const DetailHistoryLoading: Story = {
  render: () => detail({ [TIMELINE]: storyPending }),
}

/** Open History for a customer nothing has been recorded against yet. */
export const DetailHistoryEmpty: Story = {
  render: () => detail({ [TIMELINE]: () => storyJson(aTimelinePage({ entries: [] })) }),
}

/**
 * Open History to meet the case this tab exists to get right: two modules could not answer, so the
 * rail below is incomplete and says so *before* anything a reader could mistake for completeness.
 * A gap read as "nothing happened" is a worse answer than no answer.
 */
export const DetailHistoryPartial: Story = {
  render: () =>
    detail({
      [TIMELINE]: () => storyJson(aTimelinePage({ unavailableSources: ['orders', 'billing'] })),
    }),
}

/**
 * Open History to see a reason that was given and withheld, beside one that was never given at all.
 * The two must not read alike: `reasonPermission` is what tells them apart.
 */
export const DetailHistoryReasonWithheld: Story = {
  render: () =>
    detail({
      [TIMELINE]: () =>
        storyJson(
          aTimelinePage({
            entries: [
              aTimelineEntry({ reason: null, reasonPermission: 'customers.read_notes' }),
              aTimelineEntry({
                entryId: '0199cc00-0000-7000-8000-00000000e004',
                title: 'Customer deactivated',
                detail: null,
                reason: null,
                reasonPermission: null,
              }),
            ],
          }),
        ),
    }),
}

/** Open History, then "Show older" — pages are appended, and nothing moves under the reader. */
export const DetailHistoryMorePages: Story = {
  render: () =>
    detail({
      [TIMELINE]: () => storyJson(aTimelinePage({ nextCursor: 'cursor-2' })),
      [`${TIMELINE}?cursor=cursor-2`]: () =>
        storyJson(
          aTimelinePage({
            entries: [
              aTimelineEntry({
                entryId: '0199cc00-0000-7000-8000-00000000e005',
                title: 'Customer registered',
                detail: null,
                reason: null,
                reasonPermission: null,
              }),
            ],
          }),
        ),
    }),
}

/** Open History for a read that failed — a problem, never an empty history. */
export const DetailHistoryError: Story = {
  render: () => detail({ [TIMELINE]: () => storyProblem(503, 'platform.unavailable') }),
}

/** The 40% growth tolerance on the rail, where the metadata line is tightest. */
export const DetailHistoryPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => detail({ [TIMELINE]: () => storyJson(aTimelinePage()) }),
}
