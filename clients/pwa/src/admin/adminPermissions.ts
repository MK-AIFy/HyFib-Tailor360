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
  // Measurement templates (#27). Two keys rather than one, because drafting and publishing are
  // different acts by different people: the submitter of a version is not the person who decides it
  // is right. Both are granted to `owner` and `admin` and to nobody else, so a reviewer always holds
  // the drafting one too — which is why the screens gate on the key each action needs rather than
  // assuming a reviewer cannot read.
  templatesEdit: 'catalog.templates.edit',
  templatesPublish: 'catalog.templates.publish',
} as const
