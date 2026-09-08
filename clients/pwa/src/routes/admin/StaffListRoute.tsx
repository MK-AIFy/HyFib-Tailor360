import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { StatusBadge } from '../../components/primitives/StatusBadge'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { listStaff } from '../../admin/adminApi'
import { useAdminResource } from '../../admin/useAdminResource'
import { accountStatusKind } from '../../admin/accountStatus'
import type { StaffSummary } from '../../admin/types'

/**
 * Every staff account, with the one thing an administrator is usually looking for: who can sign in.
 *
 * ## The search matches names, and says so
 *
 * Not an oversight and not a limitation to apologise for. Matching an address or a phone number would
 * turn this box into a way of asking "is this person a member of staff", answerable by anybody who
 * reached the screen, and the server refuses to do it. The hint says as much, because a person who
 * types a phone number and gets nothing deserves to know why rather than to conclude the search is
 * broken.
 *
 * ## Why a row is not a command
 *
 * A row opens the account; it does not suspend it. The version an edit presents has to be the one the
 * row carries *now*, and a table that has been on screen for five minutes carries the version from
 * five minutes ago — so the list deliberately does not receive one from the server, and every command
 * goes through the detail screen, which reads the account first.
 */
export function StaffListRoute() {
  const intl = useIntl()

  const [status, setStatus] = useState('')
  const [search, setSearch] = useState('')
  /** What has actually been asked for, which changes only when the person submits. */
  const [applied, setApplied] = useState<{ status: string; search: string }>({
    status: '',
    search: '',
  })

  const page = useAdminResource(`${applied.status}|${applied.search}`, (signal) =>
    listStaff({
      ...(applied.status === '' ? {} : { status: applied.status }),
      ...(applied.search === '' ? {} : { search: applied.search }),
      signal,
    }),
  )

  const rows = page.value?.users ?? []

  return (
    <section>
      <h2>
        <FormattedMessage id="admin.users.title" />
      </h2>

      <form
        className="admin__toolbar"
        onSubmit={(event) => {
          event.preventDefault()
          setApplied({ status, search })
        }}
      >
        <div className="admin__field">
          <label htmlFor="staff-search">
            <FormattedMessage id="admin.users.search.label" />
          </label>
          <input
            id="staff-search"
            type="search"
            value={search}
            aria-describedby="staff-search-hint"
            onChange={(event) => {
              setSearch(event.target.value)
            }}
          />
        </div>

        <div className="admin__field">
          <label htmlFor="staff-status">
            <FormattedMessage id="admin.users.filter.status" />
          </label>
          <select
            id="staff-status"
            value={status}
            onChange={(event) => {
              setStatus(event.target.value)
            }}
          >
            <option value="">{intl.formatMessage({ id: 'admin.users.filter.anyStatus' })}</option>
            {(['Invited', 'Active', 'Suspended', 'Deactivated'] as const).map((value) => (
              <option key={value} value={value}>
                {intl.formatMessage({ id: `primitives.status.${accountStatusKind(value)}` })}
              </option>
            ))}
          </select>
        </div>

        <Button type="submit" iconName="search">
          <FormattedMessage id="admin.users.search.label" />
        </Button>
      </form>

      <p className="admin__hint" id="staff-search-hint">
        <FormattedMessage id="admin.users.search.hint" />
      </p>

      <AuthProblemAlert failure={page.failure} />

      {page.value === null && page.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'admin.users.loading' })} />
      ) : rows.length === 0 && page.failure === null ? (
        <EmptyState iconName="users" live="polite">
          {intl.formatMessage({ id: 'admin.users.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'admin.users.caption' })}
          rows={rows}
          rowKey={(row) => row.userId}
          rowLabel={(row) => row.displayName}
          columns={[
            {
              id: 'name',
              header: intl.formatMessage({ id: 'admin.users.column.name' }),
              cell: (row: StaffSummary) => (
                <Link to={`/admin/users/${row.userId}`}>{row.displayName}</Link>
              ),
            },
            {
              id: 'signIn',
              header: intl.formatMessage({ id: 'admin.users.column.signIn' }),
              cell: (row: StaffSummary) => row.userName,
            },
            {
              id: 'status',
              header: intl.formatMessage({ id: 'admin.users.column.status' }),
              cell: (row: StaffSummary) => <StatusBadge status={accountStatusKind(row.status)} />,
            },
            {
              id: 'roles',
              header: intl.formatMessage({ id: 'admin.users.column.roles' }),
              cell: (row: StaffSummary) =>
                row.roleKeys.length === 0
                  ? intl.formatMessage({ id: 'admin.users.noRoles' })
                  : row.roleKeys.join(', '),
            },
            {
              id: 'lastSignIn',
              header: intl.formatMessage({ id: 'admin.users.column.lastSignIn' }),
              cell: (row: StaffSummary) =>
                row.lastSignInAt === null
                  ? intl.formatMessage({ id: 'admin.users.neverSignedIn' })
                  : intl.formatDate(row.lastSignInAt, { dateStyle: 'medium' }),
            },
          ]}
        />
      )}
    </section>
  )
}
