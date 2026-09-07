/**
 * The permission keys the administration screens ask for, named once.
 *
 * They are the same strings the server's catalogue declares. A screen never decides anything from
 * them — the server re-checks every request — but naming them here means a rename on the server is
 * one edit on the client rather than a search through six components.
 */
export const ADMIN_PERMISSIONS = {
  users: 'admin.users',
  branches: 'admin.branches',
  roles: 'admin.roles',
  featureFlags: 'admin.feature_flags',
  auditRead: 'admin.audit.read',
  outboxReplay: 'admin.outbox.replay',
} as const
