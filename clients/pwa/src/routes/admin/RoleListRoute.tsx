import { FormattedMessage, useIntl } from 'react-intl'
import { Link } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { listRoles } from '../../admin/adminApi'
import { useAdminResource } from '../../admin/useAdminResource'
import type { Role } from '../../admin/types'

/**
 * Every role, with what it grants and how many people hold it.
 *
 * The holder count is here rather than only on the detail screen because it is what an administrator
 * checks before opening anything: a role nobody holds can be edited freely, and a role fourteen
 * people hold cannot. Showing it in the list turns "which of these is safe to change" into a glance.
 */
export function RoleListRoute() {
  const intl = useIntl()
  const register = useAdminResource('roles', (signal) => listRoles(signal))
  const roles = register.value ?? []

  return (
    <section>
      <h2>
        <FormattedMessage id="admin.roles.title" />
      </h2>

      <AuthProblemAlert failure={register.failure} />

      {register.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'admin.roles.loading' })} />
      ) : roles.length === 0 ? (
        <EmptyState iconName="users" live="polite">
          {intl.formatMessage({ id: 'admin.roles.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'admin.roles.caption' })}
          rows={roles}
          rowKey={(row) => row.roleId}
          rowLabel={(row) => row.name}
          columns={[
            {
              id: 'name',
              header: intl.formatMessage({ id: 'admin.roles.column.name' }),
              cell: (row: Role) => (
                <>
                  <Link to={`/admin/roles/${row.roleId}`}>{row.name}</Link>
                  {row.isSystem ? (
                    <span className="admin__hint">
                      {' '}
                      {intl.formatMessage({ id: 'admin.roles.system' })}
                    </span>
                  ) : null}
                </>
              ),
            },
            {
              id: 'reach',
              header: intl.formatMessage({ id: 'admin.roles.column.reach' }),
              cell: (row: Role) =>
                intl.formatMessage({
                  id:
                    row.reach === 'Organisation'
                      ? 'admin.roles.reach.Organisation'
                      : 'admin.roles.reach.Branch',
                }),
            },
            {
              id: 'grants',
              header: intl.formatMessage({ id: 'admin.roles.column.grants' }),
              cell: (row: Role) =>
                intl.formatMessage(
                  { id: 'admin.roles.grantCount' },
                  { count: row.permissionKeys.length },
                ),
            },
            {
              id: 'holders',
              header: intl.formatMessage({ id: 'admin.roles.column.holders' }),
              cell: (row: Role) =>
                intl.formatMessage({ id: 'admin.roles.holders' }, { count: row.holders }),
            },
          ]}
        />
      )}
    </section>
  )
}
