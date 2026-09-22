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
  aCommunicationPreference,
  aConsentAnswer,
  aConsentPurpose,
  aCustomer,
  aCustomerCard,
  aDuplicateCandidate,
  aTimelineEntry,
  aTimelinePage,
  anExportReceipt,
} from '../../customers/testing/fixtures'
import { CUSTOMER_DUPLICATES_CODE, CUSTOMER_VERSION_CONFLICT_CODE } from '../../customers/types'
import { CustomerCreateRoute } from './CustomerCreateRoute'
import { CustomerDetailRoute } from './CustomerDetailRoute'
import { CustomerEditRoute } from './CustomerEditRoute'
import { Route, Routes } from 'react-router'
import { CustomerConsentRoute } from './CustomerConsentRoute'
import { CustomerExportRoute } from './CustomerExportRoute'
import { CustomersLayoutRoute } from './CustomersLayoutRoute'
import { CustomerMergeRoute } from './CustomerMergeRoute'
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
    CUSTOMERS_PERMISSIONS.merge,
    CUSTOMERS_PERMISSIONS.readConsent,
    CUSTOMERS_PERMISSIONS.export,
    CUSTOMERS_PERMISSIONS.deactivate,
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

/**
 * The nested routes the layout needs, as a descendant route tree.
 *
 * `withAdminApi` mounts one element at one path, and a master-detail layout needs a parent with a
 * child — so the parent is mounted at a splat and brings its own `Routes`. That is the same shape
 * `router.tsx` declares, one level deeper.
 */
function CustomersAt({ arrangement }: { readonly arrangement?: 'split' | 'stacked' }) {
  return (
    <Routes>
      {/*
        Relative, not `/customers`. These are *descendant* routes — mounted under a `/customers/*`
        route — so they match against what is left of the address after that prefix, which is the
        customer identifier alone. An absolute path here matches nothing and renders a blank screen,
        which is exactly what it did before this comment existed.
      */}
      <Route
        element={<CustomersLayoutRoute {...(arrangement === undefined ? {} : { arrangement })} />}
        path="/"
      >
        <Route element={null} index />
        <Route element={<CustomerDetailRoute />} path=":customerId" />
      </Route>
    </Routes>
  )
}

const listAndRecord = (
  arrangement: 'split' | 'stacked' | undefined,
  at: string,
  routes: Parameters<typeof withAdminApi>[1] = {},
) =>
  withAdminApi(
    <RequirePermission permission={CUSTOMERS_PERMISSIONS.read}>
      <CustomersAt {...(arrangement === undefined ? {} : { arrangement })} />
    </RequirePermission>,
    {
      'GET /api/v1/me': () => storyJson(CUSTOMERS_USER),
      'GET /api/v1/customers/?term=priya': () =>
        storyJson({ customers: [aCustomerCard()], nextCursor: null }),
      [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: () => storyJson(CUSTOMER, 'W/"1"'),
      [TIMELINE]: () => storyJson(aTimelinePage()),
      ...routes,
    },
    { path: '/customers/*', at },
  )
const DUPLICATES = `GET ${CUSTOMERS}${CUSTOMER.customerId}/duplicates`
const DUPLICATES_AT = `/customers/${CUSTOMER.customerId}/duplicates`
const FOLDED_ID = '0199cc00-0000-7000-8000-000000000002'

/** The record that would be folded in — a different person's record, deliberately obviously so. */
const FOLDED = aCustomerCard({
  customerId: FOLDED_ID,
  customerNumber: 'C-000999',
  displayName: 'Priya S',
})

const merge = (routes: Parameters<typeof withAdminApi>[1], online = true) =>
  link(online, () =>
    withAdminApi(
      <RequirePermission permission={CUSTOMERS_PERMISSIONS.read}>
        <CustomerMergeRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson(CUSTOMERS_USER),
        [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: () => storyJson(CUSTOMER, 'W/"7"'),
        [`GET ${CUSTOMERS}${FOLDED_ID}`]: () =>
          storyJson({ ...CUSTOMER, customerId: FOLDED_ID, customerNumber: 'C-000999' }, 'W/"3"'),
        [DUPLICATES]: () =>
          storyJson({
            candidates: [
              aDuplicateCandidate({
                customer: FOLDED,
                reasons: ['Same telephone number', 'Same name'],
              }),
            ],
          }),
        ...routes,
      },
      { path: '/customers/:customerId/duplicates', at: DUPLICATES_AT },
    ),
  )
const CONSENT = `GET ${CUSTOMERS}${CUSTOMER.customerId}/consent`
const PREFS = `GET ${CUSTOMERS}${CUSTOMER.customerId}/communication-preferences`
const CONSENT_AT = `/customers/${CUSTOMER.customerId}/consent`

const consent = (routes: Parameters<typeof withAdminApi>[1], online = true) =>
  link(online, () =>
    withAdminApi(
      <RequirePermission permission={CUSTOMERS_PERMISSIONS.readConsent}>
        <CustomerConsentRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson(CUSTOMERS_USER),
        [CONSENT]: () => storyJson({ purposes: [aConsentPurpose()] }),
        [PREFS]: () => storyJson(aCommunicationPreference()),
        ...routes,
      },
      { path: '/customers/:customerId/consent', at: CONSENT_AT },
    ),
  )

const EXPORT_AT = `/customers/${CUSTOMER.customerId}/export`
const GENERATE = `POST ${CUSTOMERS}${CUSTOMER.customerId}/export`

const exportScreen = (routes: Parameters<typeof withAdminApi>[1], online = true) =>
  link(online, () =>
    withAdminApi(
      <RequirePermission permission={CUSTOMERS_PERMISSIONS.export}>
        <CustomerExportRoute />
      </RequirePermission>,
      {
        'GET /api/v1/me': () => storyJson(CUSTOMERS_USER),
        [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: () => storyJson(CUSTOMER, 'W/"1"'),
        [GENERATE]: () => storyJson(anExportReceipt()),
        ...routes,
      },
      { path: '/customers/:customerId/export', at: EXPORT_AT },
    ),
  )

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

/* Duplicates and the merge ----------------------------------------------------------------------- */

/**
 * The review screen. Read which record survives — it is the one in the heading — before pressing
 * anything: the control says which way round the merge goes, because this is the only operation on a
 * customer record that cannot be undone.
 */
export const Duplicates: Story = { render: () => merge({}) }

export const DuplicatesLoading: Story = { render: () => merge({ [DUPLICATES]: storyPending }) }

/** Nothing resembles this record closely enough to be worth a manager's time. */
export const DuplicatesEmpty: Story = {
  render: () => merge({ [DUPLICATES]: () => storyJson({ candidates: [] }) }),
}

/** A caller who may read the duplicates but not merge them — Reception preparing the decision. */
export const DuplicatesReadOnly: Story = {
  render: () =>
    merge({
      'GET /api/v1/me': () =>
        storyJson({ ...CUSTOMERS_USER, permissions: [CUSTOMERS_PERMISSIONS.read] }),
    }),
}

/**
 * Press "Fold C-000999 into Priya Selvam" to meet the confirmation.
 *
 * On a desktop it asks for a reason *and* the folded record's own number, typed. On a phone the
 * typed tier does not exist, and the substitute is the reason plus a second, explicitly armed press
 * — resize the preview to see it change.
 */
export const DuplicatesConfirm: Story = {
  render: () =>
    merge({
      [`POST ${CUSTOMERS}${CUSTOMER.customerId}/merge`]: () =>
        storyJson(
          {
            customer: CUSTOMER,
            mergeId: '0199cc00-0000-7000-8000-00000000d001',
            mergedCustomerId: FOLDED_ID,
            mergedCustomerNumber: 'C-000999',
            aliasesRecorded: 2,
            visibilityBranchesAdded: 1,
            recordsRepointed: 3,
            mergedAt: '2026-09-21T10:00:00Z',
          },
          'W/"8"',
        ),
    }),
}

/**
 * Confirm the merge to meet the half that went stale: the record that would *survive* changed while
 * the decision was being taken. It sends the reader back to that record, and it does not read like
 * the other 409.
 */
export const DuplicatesSurvivorConflict: Story = {
  render: () =>
    merge({
      [`POST ${CUSTOMERS}${CUSTOMER.customerId}/merge`]: () =>
        storyProblem(409, 'customers.version-conflict'),
    }),
}

/** Confirm the merge to meet the other half: the record about to be folded in changed. */
export const DuplicatesMergedRecordConflict: Story = {
  render: () =>
    merge({
      [`POST ${CUSTOMERS}${CUSTOMER.customerId}/merge`]: () =>
        storyProblem(409, 'customers.merged-record-changed'),
    }),
}

/** Confirm the merge for a refusal that is neither conflict. */
export const DuplicatesError: Story = {
  render: () =>
    merge({
      [`POST ${CUSTOMERS}${CUSTOMER.customerId}/merge`]: () =>
        storyProblem(403, 'security.permission-denied'),
    }),
}

/** Merging needs a connection, and the screen says so rather than offering a doomed control. */
export const DuplicatesOffline: Story = { render: () => merge({}, false) }

/** The 40% growth tolerance on the card and the confirmation's sentences. */
export const DuplicatesPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => merge({}),
}

/* Consent and how to reach her -------------------------------------------------------------------- */

/** Where she stands, what she has said before, and the controls for recording a new answer. */
export const Consent: Story = {
  render: () =>
    consent({
      [CONSENT]: () =>
        storyJson({
          purposes: [
            aConsentPurpose({
              answers: [
                aConsentAnswer(),
                aConsentAnswer({
                  recordId: '0199cc00-0000-7000-8000-00000000c002',
                  decision: 'Withdrawn',
                  recordedAt: '2026-06-01T10:00:00Z',
                  source: 'Over the telephone',
                }),
              ],
            }),
            aConsentPurpose({
              key: 'marketing',
              name: 'Offers and new arrivals',
              description: 'We may tell you about a sale or a new fabric.',
              status: 'NeverAsked',
              answers: [],
            }),
          ],
        }),
    }),
}

export const ConsentLoading: Story = { render: () => consent({ [CONSENT]: storyPending }) }

/** The shop asks about nothing that needs consent — this screen's empty state. */
export const ConsentEmpty: Story = {
  render: () => consent({ [CONSENT]: () => storyJson({ purposes: [] }) }),
}

/**
 * Two purposes that cannot be answered, for two different reasons — and the screen says which.
 * `canBeAnswered` is one flag; recomputing it from `isRetired` would silently merge the two.
 */
export const ConsentUnanswerable: Story = {
  render: () =>
    consent({
      [CONSENT]: () =>
        storyJson({
          purposes: [
            aConsentPurpose({ canBeAnswered: false, isRetired: true, name: 'A retired purpose' }),
            aConsentPurpose({
              key: 'new-thing',
              name: 'A purpose with no wording yet',
              canBeAnswered: false,
              currentWordingVersion: 0,
              status: 'NeverAsked',
              answers: [],
            }),
          ],
        }),
    }),
}

/** A caller who may read the record and not change it — no controls at all. */
export const ConsentReadOnly: Story = {
  render: () =>
    consent({
      'GET /api/v1/me': () =>
        storyJson({ ...CUSTOMERS_USER, permissions: [CUSTOMERS_PERMISSIONS.readConsent] }),
    }),
}

/** Press an answer with the source empty, to see the field say which box it wants. */
export const ConsentSourceMissing: Story = { render: () => consent({}) }

/** A read that failed. */
export const ConsentError: Story = {
  render: () => consent({ [CONSENT]: () => storyProblem(503, 'platform.unavailable') }),
}

/**
 * No preference has ever been recorded for her.
 *
 * The case worth looking at: the save carries **no** `If-Match` at all, because there is no version
 * of a row that does not exist, and the server requires the header's absence rather than a wildcard.
 */
export const ConsentPreferencesNeverRecorded: Story = {
  render: () =>
    consent({
      [PREFS]: () =>
        storyJson(
          aCommunicationPreference({
            hasBeenRecorded: false,
            version: null,
            allowedChannels: [],
            quietHoursStart: null,
            quietHoursEnd: null,
            updatedAt: null,
          }),
        ),
    }),
}

/** The consent record still reads when the preferences alone could not be loaded. */
export const ConsentPreferencesError: Story = {
  render: () => consent({ [PREFS]: () => storyProblem(503, 'platform.unavailable') }),
}

/** Recording an answer needs a connection; the screen says so and keeps what was typed. */
export const ConsentOffline: Story = { render: () => consent({}, false) }

/** The 40% growth tolerance, on the screen with the longest sentences in this module. */
export const ConsentPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => consent({}),
}

/* The list beside the record (#616) ---------------------------------------------------------------- */

/**
 * **The real behaviour: resize the preview and watch it change.**
 *
 * No arrangement is forced here, so `MasterDetail` measures its own container and decides — which is
 * the thing worth looking at, because it is what the application actually does. Narrow, and the list
 * and the record take turns; wide, and they sit side by side. Nothing reads an orientation or a user
 * agent, so 1.3.4 cannot be broken by accident, and a desktop window dragged narrow behaves like the
 * phone it is now the size of.
 *
 * This is why the layout exists at all. A receptionist is usually deciding *which* of two people is
 * in front of them, and that decision is comparing a record against the rest of the list — which a
 * screen that replaced the list with the record makes impossible without searching again.
 *
 * The address carries `?term=priya`, which is where a committed search lives: that is what lets the
 * results survive the list pane being unmounted when the panes cannot both fit, and what makes this
 * screen something somebody can send to a colleague.
 */
export const ListAndRecord: Story = {
  render: () => listAndRecord(undefined, `${DETAIL_AT}?term=priya`),
}

/**
 * The split arrangement, forced.
 *
 * For reviewing the arrangement itself rather than the decision. **It is not a state the application
 * can reach at a phone width** — `MasterDetail` would stack there, because two panes do not fit in
 * 390 px — so a forced split narrower than about 768 px will overflow, and that is the story lying
 * rather than the layout failing.
 */
export const ListAndRecordSplit: Story = {
  render: () => listAndRecord('split', `${DETAIL_AT}?term=priya`),
}

/** Nothing chosen yet: the detail pane says so rather than sitting empty. Search "priya". */
export const ListAndRecordNothingSelected: Story = {
  render: () => listAndRecord(undefined, '/customers'),
}

/**
 * Narrow enough that both panes will not fit — a counter tablet in portrait, or a phone.
 *
 * One pane at a time, with a Back control that returns focus to the list. The arrangement is forced
 * here; in the application it is decided by the measured width of the layout's own container, so the
 * same tablet turned to landscape splits without anything reading an orientation.
 */
export const ListAndRecordStacked: Story = {
  render: () => listAndRecord('stacked', `${DETAIL_AT}?term=priya`),
}

/** The 40% growth tolerance across both panes at once, where the split is tightest. */
export const ListAndRecordPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => listAndRecord(undefined, `${DETAIL_AT}?term=priya`),
}

/* The subject-access export (#619) ---------------------------------------------------------------- */

/**
 * What the copy holds, and — just as plainly — what it does not.
 *
 * Somebody handing this to a customer is answering for its completeness, and finding out afterwards
 * that the images were never in it is the wrong moment.
 */
export const Export: Story = { render: () => exportScreen({}) }

/**
 * Press Generate to meet the confirmation.
 *
 * It says the thing that is a surprise if you meet it by accident: making a copy stops any earlier
 * one working, so a download already given to somebody breaks. Confirm-with-reason, not the typed
 * tier — an export is significant and repeatable, and the reason is what the trail keeps.
 */
export const ExportConfirm: Story = { render: () => exportScreen({}) }

/** Generate, then look at the receipt: when it stops working, and what it replaced. */
export const ExportGenerated: Story = {
  render: () =>
    exportScreen({ [GENERATE]: () => storyJson(anExportReceipt({ supersededCount: 2 })) }),
}

/** Generate, for a refusal. */
export const ExportError: Story = {
  render: () => exportScreen({ [GENERATE]: () => storyProblem(403, 'security.permission-denied') }),
}

/**
 * Generate, then download, to meet the copy having gone.
 *
 * It expired or a newer one replaced it. The record that the export was taken is kept; only the copy
 * of the data is destroyed, and the screen says which.
 */
export const ExportGone: Story = {
  render: () =>
    exportScreen({
      [`GET ${CUSTOMERS}${CUSTOMER.customerId}/exports/${anExportReceipt().exportId}`]: () =>
        storyProblem(404, 'customers.export-expired'),
    }),
}

/** Generating needs a connection, and so does the download. */
export const ExportOffline: Story = { render: () => exportScreen({}, false) }

/** Somebody without `customers.export`: a sentence and who to ask, never a redirect. */
export const ExportForbidden: Story = {
  render: () =>
    exportScreen({ 'GET /api/v1/me': () => storyJson({ ...STORY_USER, permissions: [] }) }),
}

/** The 40% growth tolerance on the longest prose in this module. */
export const ExportPseudoLocale: Story = {
  globals: { locale: PSEUDO_LOCALE },
  render: () => exportScreen({}),
}

/* Deactivating a record, and reactivating one (#618) ---------------------------------------------- */

/**
 * The record with its status controls. Press Deactivate to meet the confirmation.
 *
 * It is a control rather than a link, unlike the four above it: those are separate pieces of work
 * with their own screens, and this is one decision with a reason, recoverable by the same control
 * pointing the other way.
 */
export const DetailWithStatusActions: Story = { render: () => detail({}) }

/**
 * A record that has been deactivated.
 *
 * It says what that means — still here, history intact, simply not offered for a new order — because
 * the word alone reads like a deletion. The control now points the other way.
 */
export const DetailDeactivated: Story = {
  render: () =>
    detail({
      [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: () =>
        storyJson({ ...CUSTOMER, status: 'Deactivated' }, 'W/"1"'),
    }),
}

/**
 * A record that was merged away.
 *
 * No control at all, and a sentence saying why: a merge cannot be undone, so this record is
 * finished. A disabled button somebody has to guess at would be worse.
 */
export const DetailMerged: Story = {
  render: () =>
    detail({
      [`GET ${CUSTOMERS}${CUSTOMER.customerId}`]: () =>
        storyJson(
          {
            ...CUSTOMER,
            status: 'Deactivated',
            mergedIntoCustomerId: '0199cc00-0000-7000-8000-000000000002',
            mergedAt: '2026-09-01T10:00:00Z',
          },
          'W/"1"',
        ),
    }),
}

/**
 * Deactivate, to meet the refusal that is not a failure.
 *
 * Somebody else got there first. The outcome they wanted is the outcome that exists, so it is told
 * politely and in an informational tone rather than announced as an error against what they did.
 */
export const DetailStatusAlready: Story = {
  render: () =>
    detail({
      [`POST ${CUSTOMERS}${CUSTOMER.customerId}/deactivate`]: () =>
        storyProblem(409, 'customers.status-transition-not-allowed'),
    }),
}

/** Deactivate, to meet a stale version: the record changed while it was being read. */
export const DetailStatusConflict: Story = {
  render: () =>
    detail({
      [`POST ${CUSTOMERS}${CUSTOMER.customerId}/deactivate`]: () =>
        storyProblem(409, 'customers.version-conflict'),
    }),
}

/** Search "priya" with the filter on, to find somebody who has been deactivated. */
export const SearchIncludingDeactivated: Story = {
  render: () =>
    search({
      'GET /api/v1/customers/?term=priya&includeDeactivated=true': () =>
        storyJson({
          customers: [aCustomerCard({ status: 'Deactivated' })],
          nextCursor: null,
        }),
    }),
}
