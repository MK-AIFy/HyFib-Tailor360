import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import type { ConfirmOutcome } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { ApiError } from '../../auth/apiClient'
import { listBranches, setBranchTrading } from '../../admin/adminApi'
import { useAdminResource } from '../../admin/useAdminResource'
import type { Branch } from '../../admin/types'

/**
 * The branch register.
 *
 * ## Why there is no delete, and why the dialog says so
 *
 * A branch code is embedded in every order, estimate and invoice number it ever produced, and those
 * numbers are printed on paper that customers still hold. A branch therefore leaves the register by
 * being closed, never by being removed — and the confirmation says that in words, because an
 * administrator reaching for "close" is usually asking themselves whether it is the destructive one.
 *
 * ## Why a command uses the rendered row's version
 *
 * The list carries a version per row. Sending that version means another administrator's edit
 * causes a conflict, rather than applying this decision to details the person has never seen.
 * Reload is explicit after a conflict: reading fresh state must not silently repeat the command.
 */
export function BranchListRoute() {
  const intl = useIntl()

  const register = useAdminResource('branches', (signal) => listBranches(signal))

  const [pending, setPending] = useState<{
    readonly branch: Branch
    readonly open: boolean
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

    void setBranchTrading({
      branchId: pending.branch.branchId,
      open: pending.open,
      reason,
      version: pending.branch.version,
      idempotencyKey: pending.idempotencyKey,
    })
      .then(() => {
        register.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
        setPending(null)
      })
  }

  const branches = register.value ?? []
  const conflict = failure instanceof ApiError && failure.code === 'identity.concurrent-change'

  return (
    <section>
      <h2>
        <FormattedMessage id="admin.branches.title" />
      </h2>

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
                register.reload()
              }}
            >
              <FormattedMessage id="admin.reload" />
            </Button>
          }
        >
          <FormattedMessage id="admin.conflict.body" />
        </Alert>
      ) : (
        <AuthProblemAlert failure={failure ?? register.failure} />
      )}

      {register.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'admin.branches.loading' })} />
      ) : branches.length === 0 ? (
        <EmptyState iconName="home" live="polite">
          {intl.formatMessage({ id: 'admin.branches.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'admin.branches.caption' })}
          rows={branches}
          rowKey={(row) => row.branchId}
          rowLabel={(row) => row.name}
          columns={[
            {
              id: 'code',
              header: intl.formatMessage({ id: 'admin.branches.column.code' }),
              cell: (row: Branch) => row.code,
            },
            {
              id: 'name',
              header: intl.formatMessage({ id: 'admin.branches.column.name' }),
              cell: (row: Branch) => row.name,
            },
            {
              id: 'timeZone',
              header: intl.formatMessage({ id: 'admin.branches.column.timeZone' }),
              cell: (row: Branch) => row.timeZoneId,
            },
            {
              id: 'status',
              header: intl.formatMessage({ id: 'admin.branches.column.status' }),
              cell: (row: Branch) =>
                intl.formatMessage({
                  id:
                    row.status === 'Open'
                      ? 'admin.branches.status.open'
                      : 'admin.branches.status.closed',
                }),
            },
          ]}
          rowActions={(row: Branch) => (
            <Button
              variant={row.status === 'Open' ? 'danger' : 'secondary'}
              busy={busy && pending?.branch.branchId === row.branchId}
              onClick={() => {
                setPending({
                  branch: row,
                  open: row.status !== 'Open',
                  idempotencyKey: crypto.randomUUID(),
                })
              }}
            >
              {intl.formatMessage({
                id: row.status === 'Open' ? 'admin.branches.close' : 'admin.branches.reopen',
              })}
            </Button>
          )}
        />
      )}

      <Alert tone="info" live="off">
        <FormattedMessage id="admin.branches.codeHint" />
      </Alert>

      {pending === null ? null : (
        <ConfirmDialog
          open
          tier="reason"
          action={pending.open ? 'reopen-branch' : 'close-branch'}
          title={intl.formatMessage({
            id: pending.open ? 'admin.branches.reopenTitle' : 'admin.branches.closeTitle',
          })}
          confirmLabel={intl.formatMessage({
            id: pending.open ? 'admin.branches.reopen' : 'admin.branches.close',
          })}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          onConfirm={apply}
          onCancel={() => {
            setPending(null)
          }}
        >
          {intl.formatMessage({
            id: pending.open ? 'admin.branches.reopenBody' : 'admin.branches.closeBody',
          })}
        </ConfirmDialog>
      )}
    </section>
  )
}
