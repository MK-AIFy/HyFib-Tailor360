import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import type { ConfirmOutcome } from '../../components/dialogs/ConfirmDialog'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { ApiError } from '../../auth/apiClient'
import { listFeatureFlags, setFeatureFlag } from '../../admin/adminApi'
import { useAdminResource } from '../../admin/useAdminResource'
import type { FeatureFlag } from '../../admin/types'

/**
 * The settings that switch parts of the application on and off.
 *
 * ## Why the confirmation quotes a number of seconds
 *
 * A flag is read from a cached snapshot on every till, so a change does not take effect everywhere at
 * once. An administrator who does not know that moves the toggle, walks to a till, sees the old
 * behaviour and moves it back — which is how a setting ends up changed twice and audited twice for
 * one decision. The server publishes the propagation bound with each flag and the dialog says it.
 *
 * ## Why the version is optional here and nowhere else
 *
 * A flag that has never been configured has no version to edit against. Demanding one would ask the
 * administrator for a value that does not exist, so the first write of a flag sends no `If-Match` and
 * every later one does — which is why `setFeatureFlag` takes `string | undefined` rather than
 * `string`, and why the screen passes what the read gave it rather than asserting there was one.
 */
export function FeatureFlagRoute() {
  const intl = useIntl()
  const flags = useAdminResource('feature-flags', (signal) => listFeatureFlags(signal))

  const [pending, setPending] = useState<{
    readonly flag: FeatureFlag
    readonly idempotencyKey: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)

  const apply = (outcome: ConfirmOutcome) => {
    if (pending === null) {
      return
    }

    const reason = outcome.reason?.trim() ?? ''
    if (reason === '') {
      setFailure(new ApiError('A reason is required.', { status: 400 }))
      return
    }

    setBusy(true)
    setFailure(null)

    void setFeatureFlag({
      key: pending.flag.key,
      enabled: !pending.flag.enabled,
      reason,
      version: pending.flag.version,
      idempotencyKey: pending.idempotencyKey,
    })
      .then(() => {
        flags.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
        setPending(null)
      })
  }

  const rows = flags.value ?? []

  return (
    <section>
      <h2>
        <FormattedMessage id="admin.features.title" />
      </h2>

      <AuthProblemAlert failure={failure ?? flags.failure} />

      {flags.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'admin.features.loading' })} />
      ) : rows.length === 0 ? (
        <EmptyState iconName="settings" live="polite">
          {intl.formatMessage({ id: 'admin.features.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'admin.features.caption' })}
          rows={rows}
          rowKey={(row) => row.key}
          rowLabel={(row) => row.key}
          columns={[
            {
              id: 'key',
              header: intl.formatMessage({ id: 'admin.features.column.key' }),
              cell: (row: FeatureFlag) => row.key,
            },
            {
              id: 'state',
              header: intl.formatMessage({ id: 'admin.features.column.state' }),
              cell: (row: FeatureFlag) =>
                intl.formatMessage({
                  id: row.enabled ? 'admin.features.on' : 'admin.features.off',
                }),
            },
            {
              id: 'changed',
              header: intl.formatMessage({ id: 'admin.features.column.changed' }),
              cell: (row: FeatureFlag) =>
                intl.formatDate(row.updatedAt, { dateStyle: 'medium', timeStyle: 'short' }),
            },
            {
              id: 'reason',
              header: intl.formatMessage({ id: 'admin.features.column.reason' }),
              cell: (row: FeatureFlag) =>
                row.reason ?? intl.formatMessage({ id: 'admin.features.neverChanged' }),
            },
          ]}
          rowActions={(row: FeatureFlag) => (
            <Button
              variant="secondary"
              busy={busy && pending?.flag.key === row.key}
              onClick={() => {
                setPending({ flag: row, idempotencyKey: crypto.randomUUID() })
              }}
            >
              {intl.formatMessage({
                id: row.enabled ? 'admin.features.turnOff' : 'admin.features.turnOn',
              })}
            </Button>
          )}
        />
      )}

      {pending === null ? null : (
        <ConfirmDialog
          open
          tier="reason"
          action={`feature-flag-${pending.flag.key}`}
          title={intl.formatMessage({
            id: pending.flag.enabled ? 'admin.features.confirmOff' : 'admin.features.confirmOn',
          })}
          confirmLabel={intl.formatMessage({
            id: pending.flag.enabled ? 'admin.features.turnOff' : 'admin.features.turnOn',
          })}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          onConfirm={apply}
          onCancel={() => {
            setPending(null)
          }}
        >
          {intl.formatMessage(
            { id: 'admin.features.body' },
            { seconds: pending.flag.propagationSeconds },
          )}
        </ConfirmDialog>
      )}
    </section>
  )
}
