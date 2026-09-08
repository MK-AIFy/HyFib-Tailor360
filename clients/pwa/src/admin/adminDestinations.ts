import { ADMIN_PERMISSIONS } from './adminPermissions'
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
 */
export const ADMIN_DESTINATIONS: readonly AdminDestination[] = [
  { path: 'users', messageId: 'admin.nav.users', permission: ADMIN_PERMISSIONS.users },
  { path: 'branches', messageId: 'admin.nav.branches', permission: ADMIN_PERMISSIONS.branches },
  { path: 'roles', messageId: 'admin.nav.roles', permission: ADMIN_PERMISSIONS.roles },
  { path: 'features', messageId: 'admin.nav.features', permission: ADMIN_PERMISSIONS.featureFlags },
  { path: 'audit', messageId: 'admin.nav.audit', permission: ADMIN_PERMISSIONS.auditRead },
  { path: 'outbox', messageId: 'admin.nav.outbox', permission: ADMIN_PERMISSIONS.outboxReplay },
]
