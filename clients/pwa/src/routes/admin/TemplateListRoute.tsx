import { FormattedMessage, useIntl } from 'react-intl'
import { Link } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { Alert } from '../../components/primitives/Alert'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { listMeasurementTemplates } from '../../admin/templateApi'
import { useAdminResource } from '../../admin/useAdminResource'
import type { MeasurementTemplate, TemplateVersion } from '../../admin/types'

/**
 * The measurement templates, and which version of each a tailor is measuring against.
 *
 * ## Why the middle column is "capturing against" rather than "status"
 *
 * A template has no state of its own worth reading; its versions do. The one thing an administrator
 * opens this screen to learn is whether the shop can measure a blouse today, and the answer is the
 * number of the published version or the fact that there is not one. A column headed "status" would
 * have to say "active" about a template with nothing published, which is exactly the wrong answer.
 *
 * ## Why nothing is offered here
 *
 * No row action: every act belongs to a version, and choosing a version is choosing which act is
 * even possible. Sending somebody to the template first is one more tap and several fewer wrong
 * ones.
 */
export function TemplateListRoute() {
  const intl = useIntl()

  const templates = useAdminResource('measurement-templates', (signal) =>
    listMeasurementTemplates(signal),
  )

  const rows = templates.value ?? []

  /** The version measurements are captured against, or null when there is not one. */
  const published = (template: MeasurementTemplate): TemplateVersion | null =>
    template.versions.find(
      (version) => version.templateVersionId === template.publishedVersionId,
    ) ?? null

  /** Draft and in-review versions: what somebody is working on, which is what "in progress" means. */
  const inProgress = (template: MeasurementTemplate): number =>
    template.versions.filter(
      (version) => version.status === 'Draft' || version.status === 'InReview',
    ).length

  return (
    <section>
      <h2>
        <FormattedMessage id="admin.templates.title" />
      </h2>

      <AuthProblemAlert failure={templates.failure} />

      {templates.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'admin.templates.loading' })} />
      ) : rows.length === 0 ? (
        <EmptyState iconName="ruler" live="polite">
          {intl.formatMessage({ id: 'admin.templates.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'admin.templates.caption' })}
          rows={rows}
          rowKey={(row) => row.measurementTemplateId}
          rowLabel={(row) => row.name}
          columns={[
            {
              id: 'name',
              header: intl.formatMessage({ id: 'admin.templates.column.name' }),
              primary: true,
              cell: (row: MeasurementTemplate) => (
                <Link
                  to={`/admin/templates/${row.measurementTemplateId}`}
                  aria-label={intl.formatMessage(
                    { id: 'admin.templates.open' },
                    { name: row.name },
                  )}
                >
                  {row.name}
                </Link>
              ),
            },
            {
              id: 'code',
              header: intl.formatMessage({ id: 'admin.templates.column.code' }),
              cell: (row: MeasurementTemplate) => row.code,
            },
            {
              id: 'published',
              header: intl.formatMessage({ id: 'admin.templates.column.published' }),
              cell: (row: MeasurementTemplate) => {
                const live = published(row)

                return live === null
                  ? intl.formatMessage({ id: 'admin.templates.none' })
                  : intl.formatMessage(
                      { id: 'admin.templates.version' },
                      { number: live.versionNumber },
                    )
              },
            },
            {
              id: 'inProgress',
              header: intl.formatMessage({ id: 'admin.templates.column.inProgress' }),
              cell: (row: MeasurementTemplate) =>
                intl.formatMessage(
                  { id: 'admin.templates.inProgress' },
                  { count: inProgress(row) },
                ),
            },
          ]}
        />
      )}

      <Alert tone="info" live="off">
        <FormattedMessage id="admin.templates.notPublishedHint" />
      </Alert>
    </section>
  )
}
