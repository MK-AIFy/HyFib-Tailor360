import { apiRequest, apiRequestVersioned } from '../auth/apiClient'
import type { VersionedResponse } from '../auth/apiClient'
import type {
  AssignedAccess,
  AuditPage,
  Branch,
  DeadLetteredMessage,
  FeatureFlag,
  Permission,
  Role,
  StaffUser,
  StaffUserPage,
} from './types'

/**
 * Every call the administration screens make, named for what a person is doing rather than for the
 * HTTP verb underneath.
 *
 * ## The three things every command here carries
 *
 * **A written reason.** Not a nicety: the server refuses the request without one, because the audit
 * trail's job is to answer "why did somebody do this" a year later, and nobody reconstructs that from
 * a timestamp. The screen collects it in the confirmation, so it is typed while the decision is fresh.
 *
 * **The version it is editing against**, in `If-Match`. The screen holds the value from the read it
 * rendered; presenting it is what turns "somebody else changed this while you had it open" into a 409
 * the screen can explain, instead of a silent overwrite of a decision the person never saw.
 *
 * **A retry key**, in `Idempotency-Key`, minted by the caller when the person commits to the action
 * and held for as long as it may retry. Generating one here would make it fresh on every call, which
 * is the single thing it must not be — a retried suspension would suspend twice.
 *
 * ## And one thing they all demand of the session
 *
 * Every route on this surface requires step-up: a second factor answered within the last few minutes,
 * not merely at some point today. A screen does not check that itself — the server does, and answers
 * 403 with `security.step-up-required` — but it is why an administrator who has been reading for
 * twenty minutes is asked to re-authenticate when they finally press save, and why that is correct
 * rather than a bug.
 */

const ADMIN = '/api/v1/admin'

/** Reads a page of staff accounts. */
export async function listStaff(query: {
  readonly status?: string
  readonly roleKey?: string
  readonly branchId?: string
  readonly search?: string
  readonly cursor?: string
  readonly limit?: number
  readonly signal?: AbortSignal
}): Promise<StaffUserPage> {
  const parameters = new URLSearchParams()
  if (query.status !== undefined) parameters.set('status', query.status)
  if (query.roleKey !== undefined) parameters.set('roleKey', query.roleKey)
  if (query.branchId !== undefined) parameters.set('branchId', query.branchId)
  if (query.search !== undefined) parameters.set('search', query.search)
  if (query.cursor !== undefined) parameters.set('cursor', query.cursor)
  if (query.limit !== undefined) parameters.set('limit', String(query.limit))

  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`

  return await apiRequest<StaffUserPage>(`${ADMIN}/users/${suffix}`, {
    ...(query.signal === undefined ? {} : { signal: query.signal }),
  })
}

/** Reads one account, with the version an edit must present. */
export async function readStaffUser(
  userId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<StaffUser>> {
  return await apiRequestVersioned<StaffUser>(`${ADMIN}/users/${userId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** The six commands that change an account's standing, keyed by their route segment. */
export const STAFF_COMMANDS = [
  'suspend',
  'reinstate',
  'deactivate',
  'reactivate',
  'reset-mfa',
  'revoke-sessions',
] as const

export type StaffCommand = (typeof STAFF_COMMANDS)[number]

/** Applies one of the six commands to an account. */
export async function commandStaffUser(input: {
  readonly userId: string
  readonly command: StaffCommand
  readonly reason: string
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<StaffUser>> {
  return await apiRequestVersioned<StaffUser>(`${ADMIN}/users/${input.userId}/${input.command}`, {
    method: 'POST',
    body: { reason: input.reason },
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Invites somebody: creates an account that cannot sign in yet and sends them a single-use link. */
export async function inviteStaffUser(input: {
  readonly displayName: string
  readonly userName: string
  readonly email: string
  readonly homeBranchId: string | null
  readonly roleKeys: readonly string[]
  readonly reason: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<StaffUser>> {
  return await apiRequestVersioned<StaffUser>(`${ADMIN}/users/`, {
    method: 'POST',
    body: {
      displayName: input.displayName,
      userName: input.userName,
      email: input.email,
      homeBranchId: input.homeBranchId,
      roleKeys: input.roleKeys,
      reason: input.reason,
    },
    idempotencyKey: input.idempotencyKey,
  })
}

/** Reads what one account may do and where. */
export async function readAccess(
  userId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<AssignedAccess>> {
  return await apiRequestVersioned<AssignedAccess>(`${ADMIN}/users/${userId}/access`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/**
 * Replaces the roles an account holds.
 *
 * Whole-set, not add-and-remove: the screen sends the roles the person should hold when it is
 * finished. That is what the screen shows and what an audit entry can be read against — "these three,
 * where it used to be these two" is a sentence, and "added one, removed one" is a diff a reader has
 * to reconstruct from a state they no longer have.
 */
export async function replaceRoles(input: {
  readonly userId: string
  readonly roleKeys: readonly string[]
  readonly reason: string
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<AssignedAccess>> {
  return await apiRequestVersioned<AssignedAccess>(`${ADMIN}/users/${input.userId}/roles`, {
    method: 'PUT',
    body: { roleKeys: input.roleKeys, reason: input.reason },
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Replaces the branches an account works in. Whole-set, for the reason `replaceRoles` gives. */
export async function replaceBranches(input: {
  readonly userId: string
  readonly branches: readonly { readonly branchId: string; readonly isPrimary: boolean }[]
  readonly reason: string
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<AssignedAccess>> {
  return await apiRequestVersioned<AssignedAccess>(`${ADMIN}/users/${input.userId}/branches`, {
    method: 'PUT',
    body: { branches: input.branches, reason: input.reason },
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Lists the organisation's branches. */
export async function listBranches(signal?: AbortSignal): Promise<readonly Branch[]> {
  return await apiRequest<readonly Branch[]>(`${ADMIN}/branches/`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Reads one branch, with the version an edit must present. */
export async function readBranch(
  branchId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<Branch>> {
  return await apiRequestVersioned<Branch>(`${ADMIN}/branches/${branchId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Opens a branch. The code is set here and never again. */
export async function openBranch(input: {
  readonly code: string
  readonly name: string
  readonly timeZoneId: string
  readonly reason: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<Branch>> {
  return await apiRequestVersioned<Branch>(`${ADMIN}/branches/`, {
    method: 'POST',
    body: {
      code: input.code,
      name: input.name,
      timeZoneId: input.timeZoneId,
      reason: input.reason,
    },
    idempotencyKey: input.idempotencyKey,
  })
}

/** Changes a branch's name, timezone, address and contacts. Never its code. */
export async function reconfigureBranch(input: {
  readonly branchId: string
  readonly fields: Readonly<Record<string, string | null>>
  readonly reason: string
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<Branch>> {
  return await apiRequestVersioned<Branch>(`${ADMIN}/branches/${input.branchId}`, {
    method: 'PUT',
    body: { ...input.fields, reason: input.reason },
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Closes or reopens a branch. Nothing is ever deleted: a code lives in documents customers hold. */
export async function setBranchTrading(input: {
  readonly branchId: string
  readonly open: boolean
  readonly reason: string
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<Branch>> {
  return await apiRequestVersioned<Branch>(
    `${ADMIN}/branches/${input.branchId}/${input.open ? 'reopen' : 'close'}`,
    {
      method: 'POST',
      body: { reason: input.reason },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Lists the organisation's roles with their grants and holder counts. */
export async function listRoles(signal?: AbortSignal): Promise<readonly Role[]> {
  return await apiRequest<readonly Role[]>(`${ADMIN}/roles/`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Reads one role, with the version an edit must present. */
export async function readRole(
  roleId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<Role>> {
  return await apiRequestVersioned<Role>(`${ADMIN}/roles/${roleId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Every permission the application declares, with its flags. The role screen picks from this. */
export async function listPermissions(signal?: AbortSignal): Promise<readonly Permission[]> {
  return await apiRequest<readonly Permission[]>(`${ADMIN}/permissions/`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Defines a custom role. It starts granting nothing; grants go through `replacePermissions`. */
export async function defineRole(input: {
  readonly key: string
  readonly name: string
  readonly description: string
  readonly reach: string
  readonly reason: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<Role>> {
  return await apiRequestVersioned<Role>(`${ADMIN}/roles/`, {
    method: 'POST',
    body: {
      key: input.key,
      name: input.name,
      description: input.description,
      reach: input.reach,
      reason: input.reason,
    },
    idempotencyKey: input.idempotencyKey,
  })
}

/** Replaces what a role grants with exactly the permissions given. */
export async function replacePermissions(input: {
  readonly roleId: string
  readonly permissionKeys: readonly string[]
  readonly reason: string
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<Role>> {
  return await apiRequestVersioned<Role>(`${ADMIN}/roles/${input.roleId}/permissions`, {
    method: 'PUT',
    body: { permissionKeys: input.permissionKeys, reason: input.reason },
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Deletes a custom role that nobody holds. A system role is refused. */
export async function deleteRole(input: {
  readonly roleId: string
  readonly reason: string
  readonly version: string
  readonly idempotencyKey: string
}): Promise<void> {
  await apiRequest<void>(`${ADMIN}/roles/${input.roleId}/delete`, {
    method: 'POST',
    body: { reason: input.reason },
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}

/** Lists the configured feature flags and module toggles. */
export async function listFeatureFlags(signal?: AbortSignal): Promise<readonly FeatureFlag[]> {
  return await apiRequest<readonly FeatureFlag[]>(`${ADMIN}/feature-flags/`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/**
 * Turns a flag on or off.
 *
 * The version is optional, and that is the one place this surface differs from every other command on
 * it: a flag that has never been configured has no version to edit against, so demanding one would
 * ask the administrator for a value that does not exist.
 */
export async function setFeatureFlag(input: {
  readonly key: string
  readonly enabled: boolean
  readonly reason: string
  readonly version: string | undefined
  readonly idempotencyKey: string
}): Promise<VersionedResponse<FeatureFlag>> {
  return await apiRequestVersioned<FeatureFlag>(`${ADMIN}/feature-flags/${input.key}`, {
    method: 'PUT',
    body: { enabled: input.enabled, reason: input.reason },
    ...(input.version === undefined ? {} : { ifMatch: input.version }),
    idempotencyKey: input.idempotencyKey,
  })
}

/** Switches a module and its menu entries on or off. A toggle is a flag under a reserved prefix. */
export async function setModuleEnabled(input: {
  readonly code: string
  readonly enabled: boolean
  readonly reason: string
  readonly version: string | undefined
  readonly idempotencyKey: string
}): Promise<VersionedResponse<FeatureFlag>> {
  return await apiRequestVersioned<FeatureFlag>(`${ADMIN}/feature-flags/modules/${input.code}`, {
    method: 'PUT',
    body: { enabled: input.enabled, reason: input.reason },
    ...(input.version === undefined ? {} : { ifMatch: input.version }),
    idempotencyKey: input.idempotencyKey,
  })
}

/** Reads the audit trail, filtered by subject, actor, action prefix and time. */
export async function readAuditTrail(query: {
  readonly entityType?: string
  readonly entityId?: string
  readonly actorId?: string
  readonly action?: string
  readonly from?: string
  readonly to?: string
  readonly cursor?: string
  readonly limit?: number
  readonly signal?: AbortSignal
}): Promise<AuditPage> {
  const parameters = new URLSearchParams()
  if (query.entityType !== undefined) parameters.set('entityType', query.entityType)
  if (query.entityId !== undefined) parameters.set('entityId', query.entityId)
  if (query.actorId !== undefined) parameters.set('actorId', query.actorId)
  if (query.action !== undefined) parameters.set('action', query.action)
  if (query.from !== undefined) parameters.set('from', query.from)
  if (query.to !== undefined) parameters.set('to', query.to)
  if (query.cursor !== undefined) parameters.set('cursor', query.cursor)
  if (query.limit !== undefined) parameters.set('limit', String(query.limit))

  const suffix = parameters.size === 0 ? '' : `?${parameters.toString()}`

  return await apiRequest<AuditPage>(`${ADMIN}/audit/${suffix}`, {
    ...(query.signal === undefined ? {} : { signal: query.signal }),
  })
}

/** Lists the outbox messages that exhausted their delivery attempts. */
export async function listDeadLetters(
  signal?: AbortSignal,
): Promise<readonly DeadLetteredMessage[]> {
  return await apiRequest<readonly DeadLetteredMessage[]>(`${ADMIN}/outbox/dead-letters`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/**
 * Puts one dead-lettered message back on the queue.
 *
 * No `If-Match`: a message carries no version, and "somebody already replayed it" is answered as a
 * 404 rather than as a lost race — which is the right answer, because there is then nothing left to
 * put back.
 */
export async function replayDeadLetter(input: {
  readonly messageId: string
  readonly reason: string
  readonly idempotencyKey: string
}): Promise<DeadLetteredMessage> {
  return await apiRequest<DeadLetteredMessage>(`${ADMIN}/outbox/${input.messageId}/replay`, {
    method: 'POST',
    body: { reason: input.reason },
    idempotencyKey: input.idempotencyKey,
  })
}
