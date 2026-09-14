import { ADMIN_PERMISSIONS } from './adminPermissions'
import { BILLING_PERMISSIONS } from '../billing/billingPermissions'
import type { MessageKey } from '../i18n/en-IN'

/** One screen in the administration section, and the permission that reveals it. */
export interface AdminDestination {
  readonly path: string
  readonly messageId: MessageKey
  readonly permission: string
}

/**
 * The section's screens, in the order they are offered.
 *
 * Accounts first because it is what an administrator opens this section for nine times in ten;
 * the trail and the failed messages last because they are read when something has already gone
 * wrong. The order is fixed rather than personalised: a person who finds Roles in the fourth place
 * today must find it in the fourth place tomorrow (3.2.3 Consistent Navigation).
 *
 * The three pricing destinations (#237, #252) are inserted after `catalog`, which puts every
 * configuration destination together — catalogue, then pricing — before the operational ones. That
 * insertion moved `features`, `audit` and `outbox` down from where #24 first put them; a later pull
 * request that also touches this list should re-read this comment rather than appending blindly.
 * #252 added `price-lists` as the third pricing destination, immediately after the two #237 added.
 */
export const ADMIN_DESTINATIONS: readonly AdminDestination[] = [
  { path: 'users', messageId: 'admin.nav.users', permission: ADMIN_PERMISSIONS.users },
  { path: 'branches', messageId: 'admin.nav.branches', permission: ADMIN_PERMISSIONS.branches },
  { path: 'roles', messageId: 'admin.nav.roles', permission: ADMIN_PERMISSIONS.roles },
  {
    path: 'templates',
    messageId: 'admin.nav.templates',
    permission: ADMIN_PERMISSIONS.templatesEdit,
  },
  { path: 'catalog', messageId: 'admin.nav.catalog', permission: ADMIN_PERMISSIONS.catalogEdit },
  {
    path: 'gst-registrations',
    messageId: 'pricing.nav.gstRegistrations',
    permission: BILLING_PERMISSIONS.managePriceLists,
  },
  {
    path: 'tax-configuration',
    messageId: 'pricing.nav.taxConfiguration',
    permission: BILLING_PERMISSIONS.managePriceLists,
  },
  {
    path: 'price-lists',
    messageId: 'pricing.nav.priceLists',
    permission: BILLING_PERMISSIONS.managePriceLists,
  },
  { path: 'features', messageId: 'admin.nav.features', permission: ADMIN_PERMISSIONS.featureFlags },
  { path: 'audit', messageId: 'admin.nav.audit', permission: ADMIN_PERMISSIONS.auditRead },
  { path: 'outbox', messageId: 'admin.nav.outbox', permission: ADMIN_PERMISSIONS.outboxReplay },
]
