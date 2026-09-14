import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useNavigate } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import {
  createTaxConfigurationDraft,
  listTaxConfigurationVersions,
} from '../../billing/pricingConfigApi'
import type {
  CreateTaxConfigurationDraftRequest,
  TaxConfigurationSummary,
} from '../../billing/pricingAdminTypes'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { NetworkStatusBanner } from '../../components/states/NetworkStatusBanner'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { DateField } from '../../design-system/components/forms/DateField'
import { Select } from '../../design-system/components/forms/Select'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import { getFormatters } from '../../i18n/formatters'
import type { MessageKey } from '../../i18n/en-IN'

const STATUS: Readonly<Record<string, MessageKey>> = {
  Draft: 'pricing.tax.status.Draft',
  Published: 'pricing.tax.status.Published',
  Retired: 'pricing.tax.status.Retired',
}

interface StartDraft {
  readonly name: string
  readonly notes: string
  readonly effectiveFrom: string
  readonly cloneFromVersionId: string
}

function blankStartDraft(cloneFromVersionId = ''): StartDraft {
  return { name: '', notes: '', effectiveFrom: '', cloneFromVersionId }
}

/**
 * Every tax configuration version, newest first (#237), and the one act that starts a new one
 * (E09-F01-5): empty, or a copy of an existing version carrying its codes forward.
 *
 * ## Why starting a draft is one control rather than one per row
 *
 * A draft is either empty or a copy, and both are the same act with a different source — the source
 * chosen here, as `CatalogVersionListRoute.tsx` does for the catalogue. Offering it once is also the
 * only place a person can start an *empty* draft, which a per-row control has nowhere to live.
 *
 * ## Why the accountant is taken straight into the editor
 *
 * Starting a draft demands a name and a first day up front, same as every other field on it — but
 * this screen collects only enough to create the row; the accountant changes the name, the notes and
 * the first day for real once inside the editor, which is where `DescribeTaxConfigurationVersion`
 * lives.
 */
export function TaxConfigurationListRoute() {
  const intl = useIntl()
  const formatters = getFormatters()
  const navigate = useNavigate()
  const network = useNetworkState()

  const versions = useAdminResource('tax-configuration-versions', listTaxConfigurationVersions)
  const rows = versions.value ?? []

  const [starting, setStarting] = useState<{
    readonly draft: StartDraft
    readonly idempotencyKey: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)

  const openStart = (cloneFromVersionId = ''): void => {
    setFailure(null)
    setStarting({ draft: blankStartDraft(cloneFromVersionId), idempotencyKey: crypto.randomUUID() })
  }

  const start = (): void => {
    if (starting === null) {
      return
    }
    const { draft } = starting

    const body: CreateTaxConfigurationDraftRequest = {
      name: draft.name.trim() === '' ? null : draft.name.trim(),
      notes: draft.notes.trim() === '' ? null : draft.notes.trim(),
      effectiveFrom: draft.effectiveFrom === '' ? null : draft.effectiveFrom,
      cloneFromVersionId: draft.cloneFromVersionId === '' ? null : draft.cloneFromVersionId,
    }

    setBusy(true)
    setFailure(null)

    void createTaxConfigurationDraft({ body, idempotencyKey: starting.idempotencyKey })
      .then((created) => {
        setStarting(null)
        void navigate(`/admin/tax-configuration/${created.version.taxConfigurationVersionId}`)
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const startControl =
    starting !== null ? null : network.online ? (
      <Button
        onClick={() => {
          openStart()
        }}
        variant="primary"
      >
        <FormattedMessage id="pricing.tax.start.action" />
      </Button>
    ) : (
      <OfflineBlockedAction
        action={intl.formatMessage({ id: 'pricing.tax.start.offlineAction' })}
      />
    )

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
          actions={startControl}
          iconName="list"
          live="polite"
          title={intl.formatMessage({ id: 'pricing.tax.empty.title' })}
        >
          {intl.formatMessage({ id: 'pricing.tax.empty' })}
        </EmptyState>
      ) : (
        <>
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
                cell: (row: TaxConfigurationSummary) =>
                  formatters.formatShortDate(row.effectiveFrom),
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
            rowActions={(row: TaxConfigurationSummary) => (
              <>
                <Link to={`/admin/tax-configuration/${row.taxConfigurationVersionId}`}>
                  {intl.formatMessage({ id: 'pricing.tax.open' }, { number: row.versionNumber })}
                </Link>
                <Button
                  aria-label={intl.formatMessage(
                    { id: 'pricing.tax.clone.label' },
                    { number: row.versionNumber },
                  )}
                  onClick={() => {
                    openStart(row.taxConfigurationVersionId)
                  }}
                  variant="secondary"
                >
                  {intl.formatMessage({ id: 'pricing.tax.clone' })}
                </Button>
              </>
            )}
            rowKey={(row) => row.taxConfigurationVersionId}
            rowLabel={(row) =>
              intl.formatMessage({ id: 'pricing.tax.column.version' }) +
              ` ${String(row.versionNumber)}`
            }
            rows={rows}
          />

          {startControl}
        </>
      )}

      {starting === null ? null : !network.online ? (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'pricing.tax.start.offlineAction' })}
        />
      ) : (
        <form
          aria-label={intl.formatMessage({ id: 'pricing.tax.start.title' })}
          onSubmit={(event) => {
            event.preventDefault()
            start()
          }}
        >
          <h3>{intl.formatMessage({ id: 'pricing.tax.start.title' })}</h3>
          <p>{intl.formatMessage({ id: 'pricing.tax.start.body' })}</p>

          <BillingProblemAlert failure={failure} />

          <TextField
            id="tax-draft-name"
            label={intl.formatMessage({ id: 'pricing.tax.start.name' })}
            name="name"
            onValueChange={(next) => {
              setStarting({ ...starting, draft: { ...starting.draft, name: next } })
            }}
            required
            value={starting.draft.name}
          />

          <TextArea
            id="tax-draft-notes"
            label={intl.formatMessage({ id: 'pricing.tax.start.notes' })}
            name="notes"
            onValueChange={(next) => {
              setStarting({ ...starting, draft: { ...starting.draft, notes: next } })
            }}
            value={starting.draft.notes}
          />

          <DateField
            id="tax-draft-effectiveFrom"
            label={intl.formatMessage({ id: 'pricing.tax.start.effectiveFrom' })}
            name="effectiveFrom"
            onValueChange={(next) => {
              setStarting({ ...starting, draft: { ...starting.draft, effectiveFrom: next } })
            }}
            required
            value={starting.draft.effectiveFrom}
          />

          <Select
            emptyLabel={intl.formatMessage({ id: 'pricing.tax.start.cloneFrom.empty' })}
            id="tax-draft-cloneFrom"
            label={intl.formatMessage({ id: 'pricing.tax.start.cloneFrom' })}
            name="cloneFromVersionId"
            onValueChange={(next) => {
              setStarting({ ...starting, draft: { ...starting.draft, cloneFromVersionId: next } })
            }}
            options={rows.map((row) => ({
              value: row.taxConfigurationVersionId,
              label:
                intl.formatMessage({ id: 'pricing.tax.column.version' }) +
                ` ${String(row.versionNumber)} — ${row.name}`,
            }))}
            value={starting.draft.cloneFromVersionId}
          />

          <Button busy={busy} type="submit" variant="primary">
            {intl.formatMessage({ id: 'pricing.tax.start.save' })}
          </Button>
          <Button
            onClick={() => {
              setStarting(null)
              setFailure(null)
            }}
            type="button"
            variant="secondary"
          >
            <FormattedMessage id="admin.cancel" />
          </Button>
        </form>
      )}
    </section>
  )
}
