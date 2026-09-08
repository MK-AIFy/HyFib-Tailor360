import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useParams } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { ApiError } from '../../auth/apiClient'
import { listPermissions, readRole, replacePermissions } from '../../admin/adminApi'
import { useAdminResource } from '../../admin/useAdminResource'
import { AdminReasonField } from './AdminReasonField'
import type { Permission } from '../../admin/types'

/**
 * What one role allows, and how an administrator changes it.
 *
 * ## Why the flags are on screen beside every permission
 *
 * Granting a role a permission marked for step-up means everybody holding that role will be asked to
 * re-authenticate before they can use it — on a shop floor, mid-task, with a customer waiting. That
 * is a consequence of the tick, and a list of dotted keys hides it until somebody hits it. The three
 * flags are shown as words for the same reason a status badge carries a word: a coloured mark that
 * means "this one is stricter" is a mark nobody can read.
 *
 * ## Why the whole set is sent
 *
 * The screen sends what the role should grant when the administrator is finished, not what they
 * added. That is what the checkboxes show, and it is what an audit entry can be read against — "these
 * fourteen, where it used to be these twelve" is a sentence; "added two" is a diff somebody has to
 * reconstruct from a state they no longer have.
 *
 * ## The four refusals, and why they are the server's to make
 *
 * A key the catalogue does not declare; an organisation-wide permission on a branch-reach role; a
 * grant of something the administrator does not hold themselves; and a change that would leave nobody
 * able to edit roles. The screen could pre-empt some of them and deliberately does not: its idea of
 * what the caller holds comes from a `GET /me` that may be minutes old, and a control greyed out on a
 * stale claim is worse than a refusal that explains itself.
 */
export function RoleDetailRoute() {
  const intl = useIntl()
  const { roleId = '' } = useParams()

  const role = useAdminResource(roleId, (signal) => readRole(roleId, signal))
  const catalogue = useAdminResource('permissions', (signal) => listPermissions(signal))

  /**
   * The ticks the administrator has made, tagged with the version they were made against.
   *
   * Derived rather than seeded by an effect. A `useEffect` that copied the server's set into state
   * would be a cascading render on every read, and — worse — it would have to be careful to re-seed
   * after a save and after a conflict the administrator reloaded past. Tagging the draft with the
   * version it belongs to gets both for nothing: a draft against a version that is no longer current
   * is stale by definition, so the boxes fall back to what the role actually grants.
   */
  const [draft, setDraft] = useState<{
    readonly version: string
    readonly keys: readonly string[]
  } | null>(null)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [saved, setSaved] = useState(false)
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID())

  const current = role.value?.value ?? null
  const version = role.value?.version

  const selected =
    draft !== null && draft.version === version ? draft.keys : (current?.permissionKeys ?? null)

  const setSelected = (next: (previous: readonly string[]) => readonly string[]) => {
    if (version !== undefined) {
      setDraft({ version, keys: next(selected ?? []) })
    }
  }

  if (role.value === null && role.loading) {
    return <LoadingState what={intl.formatMessage({ id: 'admin.role.loading' })} />
  }

  if (current === null || selected === null) {
    return (
      <>
        <AuthProblemAlert failure={role.failure} />
        <EmptyState iconName="users" live="polite">
          {intl.formatMessage({ id: 'admin.role.notFound' })}
        </EmptyState>
      </>
    )
  }

  const permissions = catalogue.value ?? []
  const conflict = failure instanceof ApiError && failure.status === 409

  const save = () => {
    if (reason.trim() === '') {
      setFailure(new ApiError('A reason is required.', { status: 400 }))
      return
    }

    if (version === undefined) {
      return
    }

    setBusy(true)
    setFailure(null)
    setSaved(false)

    void replacePermissions({
      roleId: current.roleId,
      permissionKeys: selected,
      reason: reason.trim(),
      version,
      idempotencyKey,
    })
      .then(() => {
        setSaved(true)
        setReason('')
        // A new key for the next, separate decision. The guarantee is that a retry of *this* change
        // replays, not that two different edits collapse into one.
        setIdempotencyKey(crypto.randomUUID())
        role.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const byModule = new Map<string, Permission[]>()
  for (const permission of permissions) {
    const group = byModule.get(permission.module) ?? []
    group.push(permission)
    byModule.set(permission.module, group)
  }

  return (
    <section>
      <p>
        <Link to="/admin/roles">
          <FormattedMessage id="admin.back" />
        </Link>
      </p>

      <h2>{current.name}</h2>
      <p className="admin__lede">{current.description}</p>

      {saved ? (
        <Alert
          tone="success"
          live="polite"
          onDismiss={() => {
            setSaved(false)
          }}
        >
          <FormattedMessage id="admin.saved" />
        </Alert>
      ) : null}

      {conflict ? (
        <Alert
          tone="warning"
          live="assertive"
          title={intl.formatMessage({ id: 'admin.conflict.title' })}
          actions={
            <Button
              variant="secondary"
              onClick={() => {
                setFailure(null)
                role.reload()
              }}
            >
              <FormattedMessage id="admin.reload" />
            </Button>
          }
        >
          <FormattedMessage id="admin.conflict.body" />
        </Alert>
      ) : (
        <AuthProblemAlert failure={failure} />
      )}

      <h3>
        <FormattedMessage id="admin.role.permissions" />
      </h3>
      <p className="admin__hint">
        <FormattedMessage id="admin.role.permissionsHint" />
      </p>

      {catalogue.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'admin.roles.loading' })} />
      ) : null}

      {[...byModule.entries()].map(([module, group]) => (
        <fieldset key={module}>
          <legend>{module}</legend>
          <ul className="admin__checkList">
            {group.map((permission) => (
              <li className="admin__check" key={permission.key}>
                <input
                  checked={selected.includes(permission.key)}
                  id={`grant-${permission.key}`}
                  type="checkbox"
                  onChange={(event) => {
                    setSelected((previous) =>
                      event.target.checked
                        ? [...(previous ?? []), permission.key]
                        : (previous ?? []).filter((key) => key !== permission.key),
                    )
                  }}
                />
                <label htmlFor={`grant-${permission.key}`}>
                  {permission.description}
                  {FLAGS.filter(({ held }) => held(permission)).map(({ id }) => (
                    <span className="admin__hint" key={id}>
                      {' · '}
                      {intl.formatMessage({ id })}
                    </span>
                  ))}
                </label>
              </li>
            ))}
          </ul>
        </fieldset>
      ))}

      <AdminReasonField
        id="role-reason"
        label={intl.formatMessage({ id: 'admin.role.reason' })}
        value={reason}
        onChange={setReason}
      />

      <div className="admin__actions">
        <Button busy={busy} onClick={save}>
          <FormattedMessage id="admin.role.save" />
        </Button>
      </div>
    </section>
  )
}

/**
 * The three flags, and the word each is shown as.
 *
 * Declared as a list rather than three conditionals so that a flag added to the catalogue is one
 * entry here rather than an edit to the rendering. Each carries a real consequence for whoever holds
 * the role, which is why none of them is rendered as a mark alone.
 */
const FLAGS: readonly {
  readonly id: 'admin.role.flag.mfa' | 'admin.role.flag.stepUp' | 'admin.role.flag.reason'
  readonly held: (permission: Permission) => boolean
}[] = [
  { id: 'admin.role.flag.mfa', held: (permission) => permission.requiresMfa },
  { id: 'admin.role.flag.stepUp', held: (permission) => permission.requiresStepUp },
  { id: 'admin.role.flag.reason', held: (permission) => permission.requiresReason },
]
