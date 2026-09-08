import type { Meta, StoryObj } from '@storybook/react-vite'
import { RequirePermission } from '../../admin/RequirePermission'
import { ADMIN_PERMISSIONS } from '../../admin/adminPermissions'
import {
  aBranch,
  aDeadLetter,
  aFeatureFlag,
  aPermission,
  aRole,
  aStaffSummary,
  aStaffUser,
} from '../../admin/testing/fixtures'
import {
  STORY_USER,
  storyJson,
  storyPending,
  storyProblem,
  withAdminApi,
} from '../../admin/testing/storyTransport'
import { BranchListRoute } from './BranchListRoute'
import { FeatureFlagRoute } from './FeatureFlagRoute'
import { OutboxRoute } from './OutboxRoute'
import { RoleDetailRoute } from './RoleDetailRoute'
import { StaffDetailRoute } from './StaffDetailRoute'
import { StaffListRoute } from './StaffListRoute'
import './admin.css'

/**
 * The administration screens, in each of the states Definition of Done item 7 asks for.
 *
 * These render the **real screens** against a stubbed API rather than the state components with
 * administration-shaped words in them. The difference matters: what a reviewer is asking is whether
 * this screen shows a usable empty state, which is a fact about the screen's own branches. A story
 * that rendered `<EmptyState>` directly would answer a question nobody asked.
 *
 * **Offline is deliberately absent from this file.** An administrative write is online-only —
 * suspending somebody, changing a grant and replaying a message are none of them things to queue
 * on a shop-floor device and apply later — so the offline state of this section is the
 * application-level `NetworkStatusBanner` and `OfflineBlockedAction`, which have their own stories.
 * Adding a third here would imply these screens have an offline behaviour of their own, and they
 * must not.
 *
 * Every value is synthetic. The locale, theme, text-size and density toolbars apply as they do
 * everywhere — the pseudo-locale one is the 40% text-growth check for these layouts.
 */
const meta = {
  title: 'Administration/Screens',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

const STAFF = '/api/v1/admin/users/'

/* Staff accounts ---------------------------------------------------------------------------- */

/** The ordinary case: a list of people, each with a status that is a word. */
export const StaffList: Story = {
  render: () =>
    withAdminApi(<StaffListRoute />, {
      [`GET ${STAFF}`]: () =>
        storyJson({
          users: [
            aStaffSummary(),
            aStaffSummary({
              userId: '0199bb00-0000-7000-8000-000000000002',
              displayName: 'Ravi (workshop)',
              userName: 'ravi.workshop',
              status: 'Suspended',
              roleKeys: ['tailor'],
              lastSignInAt: null,
            }),
            aStaffSummary({
              userId: '0199bb00-0000-7000-8000-000000000003',
              displayName: 'Nila (stores)',
              userName: 'nila.stores',
              status: 'Invited',
              roleKeys: ['inventory_clerk'],
              lastSignInAt: null,
            }),
          ],
          nextCursor: null,
        }),
    }),
}

/** Loading, saying what is being loaded. */
export const StaffListLoading: Story = {
  render: () => withAdminApi(<StaffListRoute />, { [`GET ${STAFF}`]: storyPending }),
}

/** Nothing matched — which reads as an answer, not as a broken search. */
export const StaffListEmpty: Story = {
  render: () =>
    withAdminApi(<StaffListRoute />, {
      [`GET ${STAFF}`]: () => storyJson({ users: [], nextCursor: null }),
    }),
}

/** The read failed. The sentence is chosen from the problem code, never from its message. */
export const StaffListError: Story = {
  render: () =>
    withAdminApi(<StaffListRoute />, {
      [`GET ${STAFF}`]: () => storyProblem(503, 'platform.unavailable'),
    }),
}

/**
 * Forbidden.
 *
 * Deny-by-default makes this an ordinary state rather than an error, so it says what is missing and
 * who to ask — and it is a sentence rather than a redirect, because the permission list comes from a
 * `GET /me` that may be minutes old and bouncing somebody out of a screen they are entitled to is
 * the worse failure.
 */
export const StaffListForbidden: Story = {
  render: () =>
    withAdminApi(
      <RequirePermission permission={ADMIN_PERMISSIONS.users}>
        <StaffListRoute />
      </RequirePermission>,
      { 'GET /api/v1/me': () => storyJson(STORY_WITHOUT_PERMISSIONS) },
    ),
}

/**
 * A signed-in account holding no administrative permission at all.
 *
 * Built from the story Owner rather than written out again, so a member added to the payload cannot
 * be forgotten here and leave the forbidden story rendering against a shape the application no
 * longer sends.
 */
const STORY_WITHOUT_PERMISSIONS = {
  ...STORY_USER,
  userId: '0199bb00-0000-7000-8000-00000000000e',
  userName: 'tailor.story',
  displayName: 'Kumar (tailor)',
  email: 'tailor.story@example.invalid',
  permissions: [],
}

/** One account, with the six commands and the destructive ones kept apart. */
export const StaffDetail: Story = {
  render: () =>
    withAdminApi(
      <StaffDetailRoute />,
      { [`GET ${STAFF}${aStaffUser().userId}`]: () => storyJson(aStaffUser(), 'W/"1"') },
      { path: '/admin/users/:userId', at: `/admin/users/${aStaffUser().userId}` },
    ),
}

/**
 * The administrator's own account.
 *
 * No commands at all, and a sentence saying why before anything is pressed — because suspending
 * yourself locks you out of the screen that undoes it.
 */
export const StaffDetailOwnAccount: Story = {
  render: () => {
    const self = aStaffUser({ userId: '0199bb00-0000-7000-8000-00000000000f' })
    return withAdminApi(
      <StaffDetailRoute />,
      { [`GET ${STAFF}${self.userId}`]: () => storyJson(self, 'W/"1"') },
      { path: '/admin/users/:userId', at: `/admin/users/${self.userId}` },
    )
  },
}

/* Branches ----------------------------------------------------------------------------------- */

/** Close and reopen, and no delete anywhere: a branch code lives in every document it produced. */
export const Branches: Story = {
  render: () =>
    withAdminApi(<BranchListRoute />, {
      'GET /api/v1/admin/branches/': () =>
        storyJson([
          aBranch(),
          aBranch({
            branchId: '0199bb00-0000-7000-8000-0000000000ab',
            code: 'ERD01',
            name: 'Erode counter',
            status: 'Closed',
          }),
        ]),
    }),
}

/** Nothing opened yet — the state a brand-new installation starts in. */
export const BranchesEmpty: Story = {
  render: () =>
    withAdminApi(<BranchListRoute />, { 'GET /api/v1/admin/branches/': () => storyJson([]) }),
}

/* Roles -------------------------------------------------------------------------------------- */

/**
 * The permission editor.
 *
 * The three flags are words beside each permission because each is a consequence for everybody
 * holding the role — a tick here is what makes somebody re-authenticate mid-task with a customer
 * waiting.
 */
export const RolePermissions: Story = {
  render: () =>
    withAdminApi(
      <RoleDetailRoute />,
      {
        [`GET /api/v1/admin/roles/${aRole().roleId}`]: () => storyJson(aRole(), 'W/"1"'),
        'GET /api/v1/admin/permissions/': () =>
          storyJson([
            aPermission(),
            aPermission({
              key: 'billing.post_invoice',
              description: 'Post an invoice for a confirmed order.',
              module: 'Billing',
              requiresMfa: true,
            }),
            aPermission({
              key: 'admin.roles',
              description: 'Edit roles and what each one allows.',
              module: 'Identity',
              scope: 'Organisation',
              requiresMfa: true,
              requiresStepUp: true,
              requiresReason: true,
            }),
          ]),
      },
      { path: '/admin/roles/:roleId', at: `/admin/roles/${aRole().roleId}` },
    ),
}

/* Feature settings --------------------------------------------------------------------------- */

/** Two settings, one on and one off, each saying when it last changed and why. */
export const FeatureSettings: Story = {
  render: () =>
    withAdminApi(<FeatureFlagRoute />, {
      'GET /api/v1/admin/feature-flags/': () =>
        storyJson([
          aFeatureFlag(),
          aFeatureFlag({
            key: 'module.Inventory',
            enabled: true,
            reason: 'Switched on for the November stock count.',
          }),
        ]),
    }),
}

/* The dead letter ---------------------------------------------------------------------------- */

/** Messages that were given up on, each with the failure it was given up on. */
export const FailedMessages: Story = {
  render: () =>
    withAdminApi(<OutboxRoute />, {
      'GET /api/v1/admin/outbox/dead-letters': () =>
        storyJson([
          aDeadLetter(),
          aDeadLetter({
            id: '0199bb00-0000-7000-8000-0000000000c9',
            eventType: 'billing.invoice_posted',
            attemptCount: 5,
            lastError: 'The accounting export timed out after 30 seconds.',
          }),
        ]),
    }),
}

/**
 * Nothing failing.
 *
 * The one empty state in this section that is good news, and it says so rather than reporting an
 * empty table — an operator opening this screen during an incident should be able to tell "nothing
 * is broken" from "nothing loaded".
 */
export const FailedMessagesEmpty: Story = {
  render: () =>
    withAdminApi(<OutboxRoute />, {
      'GET /api/v1/admin/outbox/dead-letters': () => storyJson([]),
    }),
}
