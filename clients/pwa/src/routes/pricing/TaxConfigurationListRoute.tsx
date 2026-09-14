import { FormattedMessage, useIntl } from 'react-intl'
import { useAdminResource } from '../../admin/useAdminResource'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { listTaxConfigurationVersions } from '../../billing/pricingConfigApi'
import type { TaxConfigurationSummary } from '../../billing/pricingAdminTypes'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { NetworkStatusBanner } from '../../components/states/NetworkStatusBanner'
import { getFormatters } from '../../i18n/formatters'
import type { MessageKey } from '../../i18n/en-IN'

const STATUS: Readonly<Record<string, MessageKey>> = {
  Draft: 'pricing.tax.status.Draft',
  Published: 'pricing.tax.status.Published',
  Retired: 'pricing.tax.status.Retired',
}

/**
 * Every tax configuration version, newest first (#237) — read only. Drafting, checking and
 * publishing a version are E09-F01-5 and E09-F01-5b; rows here link nowhere yet, because a link to a
 * route that does not exist is a dead end.
 */
export function TaxConfigurationListRoute() {
  const intl = useIntl()
  const formatters = getFormatters()

  const versions = useAdminResource('tax-configuration-versions', listTaxConfigurationVersions)
  const rows = versions.value ?? []

  return (
    <section>
      <h2>
        <FormattedMessage id="pricing.tax.title" />
      </h2>
      <p>
        <FormattedMessage id="pricing.tax.body" />
      </p>

      <NetworkStatusBanner />

      {versions.failure === null ? null : (
        <>
          <BillingProblemAlert failure={versions.failure} />
          <Button
            iconName="refresh"
            onClick={() => {
              versions.reload()
            }}
            variant="secondary"
          >
            <FormattedMessage id="states.error.retry" />
          </Button>
        </>
      )}

      {versions.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'pricing.tax.loading' })} />
      ) : rows.length === 0 && versions.failure === null ? (
        <EmptyState
          iconName="list"
          live="polite"
          title={intl.formatMessage({ id: 'pricing.tax.empty.title' })}
        >
          {intl.formatMessage({ id: 'pricing.tax.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'pricing.tax.caption' })}
          columns={[
            {
              id: 'version',
              header: intl.formatMessage({ id: 'pricing.tax.column.version' }),
              numeric: true,
              primary: true,
              cell: (row: TaxConfigurationSummary) => String(row.versionNumber),
            },
            {
              id: 'name',
              header: intl.formatMessage({ id: 'pricing.tax.column.name' }),
              cell: (row: TaxConfigurationSummary) => row.name,
            },
            {
              id: 'status',
              header: intl.formatMessage({ id: 'pricing.tax.column.status' }),
              cell: (row: TaxConfigurationSummary) =>
                intl.formatMessage({ id: STATUS[row.status] ?? 'pricing.tax.status.unknown' }),
            },
            {
              id: 'effectiveFrom',
              header: intl.formatMessage({ id: 'pricing.tax.column.effectiveFrom' }),
              cell: (row: TaxConfigurationSummary) => formatters.formatShortDate(row.effectiveFrom),
            },
            {
              id: 'publishedAt',
              header: intl.formatMessage({ id: 'pricing.tax.column.publishedAt' }),
              cell: (row: TaxConfigurationSummary) =>
                row.publishedAt === null
                  ? intl.formatMessage({ id: 'pricing.tax.notPublishedYet' })
                  : formatters.formatShortDate(row.publishedAt),
            },
          ]}
          rowKey={(row) => row.taxConfigurationVersionId}
          rowLabel={(row) =>
            intl.formatMessage({ id: 'pricing.tax.column.version' }) +
            ` ${String(row.versionNumber)}`
          }
          rows={rows}
        />
      )}
    </section>
  )
}
