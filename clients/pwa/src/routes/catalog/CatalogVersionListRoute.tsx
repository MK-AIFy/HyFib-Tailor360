import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import type { ConfirmOutcome } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { useAdminResource } from '../../admin/useAdminResource'
import { createCatalogDraft, listCatalogVersions } from '../../catalog/catalogApi'
import type { CatalogVersionSummary } from '../../catalog/types'
import type { MessageKey } from '../../i18n/en-IN'

const STATUS: Readonly<Record<string, MessageKey>> = {
  Draft: 'catalog.status.Draft',
  Published: 'catalog.status.Published',
  Retired: 'catalog.status.Retired',
}

/**
 * Every version of the catalogue, and the one act that starts a new one.
 *
 * ## Why a draft is started from here rather than from a version
 *
 * A draft is either empty or a copy, and both are the same act with a different source. Offering
 * "start a draft" once, with the version to copy chosen in the confirmation, is one control instead
 * of one per row — and it is the only place a person can start an *empty* one, which a per-row
 * control has nowhere to live.
 *
 * ## Why nothing published says so loudly
 *
 * A shop with no published catalogue cannot take an order at all. That is a real state — it is where
 * every installation begins — and it is not an error, so it is said as a fact with the act that ends
 * it beside it, rather than as a failure.
 */
export function CatalogVersionListRoute() {
  const intl = useIntl()
  const versions = useAdminResource('catalog-versions', (signal) => listCatalogVersions(signal))

  const [starting, setStarting] = useState<{
    readonly from: CatalogVersionSummary | null
    readonly idempotencyKey: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const rows = versions.value ?? []
  const published = rows.find((row) => row.status === 'Published')

  const start = (outcome: ConfirmOutcome): void => {
    if (starting === null) {
      return
    }

    const name = outcome.reason?.trim() ?? ''

    if (name === '') {
      setFailure(new Error('A name is required.'))
      return
    }

    setBusy(true)
    setFailure(null)

    void createCatalogDraft({
      name,
      notes: null,
      cloneFromVersionId: starting.from?.catalogVersionId ?? null,
      // Held across a refusal, so a retry cannot start a second draft: conventions section 4.3.
      idempotencyKey: starting.idempotencyKey,
    })
      .then(() => {
        setStarting(null)
        setNotice(intl.formatMessage({ id: 'catalog.draft.started' }))
        versions.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  if (versions.loading) {
    return (
      <section>
        <LoadingState what={intl.formatMessage({ id: 'catalog.versions.loading' })} />
      </section>
    )
  }

  return (
    <section>
      <h2>
        <FormattedMessage id="catalog.title" />
      </h2>
      <p>
        <FormattedMessage id="catalog.body" />
      </p>

      {notice === null ? null : (
        <Alert
          live="polite"
          onDismiss={() => {
            setNotice(null)
          }}
          tone="success"
        >
          {notice}
        </Alert>
      )}

      <AuthProblemAlert failure={failure ?? versions.failure} />

      {published === undefined && rows.length > 0 ? (
        <Alert live="polite" tone="warning">
          <FormattedMessage id="catalog.notPublished" />
        </Alert>
      ) : null}

      {rows.length === 0 ? (
        <EmptyState iconName="list" live="polite">
          {intl.formatMessage({ id: 'catalog.versions.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'catalog.versions.caption' })}
          columns={[
            {
              id: 'version',
              header: intl.formatMessage({ id: 'catalog.versions.column.version' }),
              numeric: true,
              primary: true,
              cell: (row: CatalogVersionSummary) => String(row.versionNumber),
            },
            {
              id: 'status',
              header: intl.formatMessage({ id: 'catalog.versions.column.status' }),
              cell: (row: CatalogVersionSummary) =>
                intl.formatMessage({ id: STATUS[row.status] ?? 'catalog.status.unknown' }),
            },
            {
              id: 'name',
              header: intl.formatMessage({ id: 'catalog.versions.column.name' }),
              cell: (row: CatalogVersionSummary) => row.name,
            },
          ]}
          rowActions={(row: CatalogVersionSummary) => (
            <>
              <Link to={`/admin/catalog/${row.catalogVersionId}`}>
                {intl.formatMessage({ id: 'catalog.versions.open' }, { number: row.versionNumber })}
              </Link>
              {row.status === 'Draft' ? null : (
                <Button
                  onClick={() => {
                    setFailure(null)
                    setStarting({ from: row, idempotencyKey: crypto.randomUUID() })
                  }}
                  variant="secondary"
                >
                  <FormattedMessage id="catalog.draft.clone" />
                </Button>
              )}
            </>
          )}
          rowKey={(row) => row.catalogVersionId}
          rowLabel={(row) =>
            intl.formatMessage({ id: 'catalog.version' }, { number: row.versionNumber })
          }
          rows={rows}
        />
      )}

      <Button
        onClick={() => {
          setFailure(null)
          setStarting({ from: null, idempotencyKey: crypto.randomUUID() })
        }}
        variant="primary"
      >
        <FormattedMessage id="catalog.draft.start" />
      </Button>

      {starting === null ? null : (
        <ConfirmDialog
          action={intl.formatMessage({ id: 'catalog.draft.start' })}
          busy={busy}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          confirmLabel={intl.formatMessage({ id: 'catalog.draft.start' })}
          onCancel={() => {
            setStarting(null)
          }}
          onConfirm={start}
          open
          tier="reason"
          title={intl.formatMessage({ id: 'catalog.draft.title' })}
        >
          {intl.formatMessage({ id: 'catalog.draft.body' })}
        </ConfirmDialog>
      )}
    </section>
  )
}
