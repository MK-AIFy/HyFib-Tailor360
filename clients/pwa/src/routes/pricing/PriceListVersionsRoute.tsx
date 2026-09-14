import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useParams } from 'react-router'
import { listBranches } from '../../admin/adminApi'
import { useAdminResource } from '../../admin/useAdminResource'
import { ApiError } from '../../auth/apiClient'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { billingProblemCode, billingProblemMessage } from '../../billing/billingProblems'
import {
  createPriceListDraft,
  listPriceListVersions,
  readPriceList,
} from '../../billing/priceListApi'
import type { PriceListVersionRequest, PriceListVersionSummary } from '../../billing/priceListTypes'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { NetworkStatusBanner } from '../../components/states/NetworkStatusBanner'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { getFormatters } from '../../i18n/formatters'
import { parseDecimalString } from '../../i18n/parseNumber'
import type { MessageKey } from '../../i18n/en-IN'
import { PriceListVersionForm } from './PriceListVersionForm'
import {
  blankPriceListVersionDraft,
  priceListVersionDraftFromSummary,
} from './priceListVersionDraft'
import type { PriceListVersionDraft } from './priceListVersionDraft'

const STATUS: Readonly<Record<string, MessageKey>> = {
  Draft: 'pricing.priceList.version.status.Draft',
  Published: 'pricing.priceList.version.status.Published',
  Retired: 'pricing.priceList.version.status.Retired',
}

const ROUND_OFF: Readonly<Record<string, MessageKey>> = {
  None: 'pricing.priceList.roundOff.none',
  NearestRupee: 'pricing.priceList.roundOff.nearestRupee',
}

/** The one code every field of the convention form can be refused with, named by `problem.errors`. */
const VALUE_REQUIRED_CODE = 'billing.value-required'

/** The refusal a second draft started for the same list at the same moment answers with. */
const CONCURRENCY_CODE = 'billing.draft-number-conflict'

/**
 * A price list's versions, newest first (#252), and the one act that starts a new one — empty, or
 * cloned from a row. Items, discount rules, the validation report and publication are E09-F01-7 and
 * E09-F01-7b; a row here links nowhere yet, because a link to a route that does not exist is a dead
 * end.
 */
export function PriceListVersionsRoute() {
  const { priceListId } = useParams<{ priceListId: string }>()
  const id = priceListId ?? ''
  const intl = useIntl()
  const formatters = getFormatters()
  const network = useNetworkState()

  const priceList = useAdminResource(`price-list:${id}`, (signal) =>
    readPriceList(id, signal).then((read) => read.value),
  )
  const versions = useAdminResource(`price-list-versions:${id}`, (signal) =>
    listPriceListVersions(id, signal),
  )
  const branches = useAdminResource('branches-for-pricing', listBranches)

  const branchNamesAvailable = branches.failure === null
  const branchName = (branchId: string): string | undefined =>
    branches.value?.find((branch) => branch.branchId === branchId)?.name
  const branchOptions = (branches.value ?? []).map((branch) => ({
    value: branch.branchId,
    label: `${branch.name} (${branch.code})`,
  }))

  const [starting, setStarting] = useState<{
    readonly draft: PriceListVersionDraft
    readonly cloneFromVersionNumber: number | string | null
    readonly idempotencyKey: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const openStart = (): void => {
    setFailure(null)
    setStarting({
      draft: blankPriceListVersionDraft(),
      cloneFromVersionNumber: null,
      idempotencyKey: crypto.randomUUID(),
    })
  }

  const openClone = (row: PriceListVersionSummary): void => {
    setFailure(null)
    setStarting({
      draft: priceListVersionDraftFromSummary(row),
      cloneFromVersionNumber: row.versionNumber,
      idempotencyKey: crypto.randomUUID(),
    })
  }

  const submit = (): void => {
    if (starting === null) {
      return
    }
    const { draft } = starting

    const body: PriceListVersionRequest = {
      name: draft.name.trim() === '' ? null : draft.name.trim(),
      notes: draft.notes.trim() === '' ? null : draft.notes.trim(),
      effectiveFrom: draft.effectiveFrom === '' ? null : draft.effectiveFrom,
      taxInclusive: draft.taxInclusive === '' ? null : draft.taxInclusive === 'true',
      roundOff: draft.roundOff === '' ? null : draft.roundOff,
      overrideThresholdPercent: parseDecimalString(draft.overrideThresholdPercent),
      branchIds: draft.branchIdsTouched ? draft.branchIds : null,
      cloneFromVersionId: draft.cloneFromVersionId,
      reason: draft.reason.trim() === '' ? null : draft.reason.trim(),
      saysTaxInclusive: draft.taxInclusive !== '',
      saysBranchIds: draft.branchIdsTouched,
    }

    setBusy(true)
    setFailure(null)

    void createPriceListDraft({ priceListId: id, body, idempotencyKey: starting.idempotencyKey })
      .then(() => {
        setStarting(null)
        setNotice(intl.formatMessage({ id: 'pricing.priceList.version.started' }))
        versions.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const code = billingProblemCode(failure)
  const problem = failure instanceof ApiError ? failure.problem : undefined
  const valueRequiredField =
    code === VALUE_REQUIRED_CODE ? Object.keys(problem?.errors ?? {})[0] : undefined
  const valueRequiredMessageId =
    code === VALUE_REQUIRED_CODE ? billingProblemMessage(failure) : undefined
  const valueRequiredSentence =
    valueRequiredMessageId === undefined
      ? undefined
      : intl.formatMessage({ id: valueRequiredMessageId })
  const fieldErrorFor = (field: string): string | undefined =>
    valueRequiredField === field ? valueRequiredSentence : undefined
  const isFieldFailure = valueRequiredField !== undefined
  const isConcurrencyFailure = code === CONCURRENCY_CODE

  const branchIdsError = fieldErrorFor('branchIds')
  const effectiveFromError = fieldErrorFor('effectiveFrom')
  const overrideThresholdPercentError = fieldErrorFor('overrideThresholdPercent')
  const roundOffError = fieldErrorFor('roundOff')
  const taxInclusiveError = fieldErrorFor('taxInclusive')

  const rows = versions.value ?? []
  const startAction = (
    <Button onClick={openStart} variant="primary">
      <FormattedMessage id="pricing.priceList.version.start.action" />
    </Button>
  )

  const readFailure = priceList.failure ?? versions.failure

  return (
    <section>
      <h2>
        <FormattedMessage
          id="pricing.priceList.version.title"
          values={{ code: priceList.value?.code ?? priceListId ?? '' }}
        />
      </h2>
      <p>
        <FormattedMessage id="pricing.priceList.version.body" />
      </p>

      <NetworkStatusBanner />

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

      {readFailure === null ? null : (
        <>
          <BillingProblemAlert failure={readFailure} />
          <Button
            iconName="refresh"
            onClick={() => {
              priceList.reload()
              versions.reload()
            }}
            variant="secondary"
          >
            <FormattedMessage id="states.error.retry" />
          </Button>
        </>
      )}

      {versions.loading || priceList.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'pricing.priceList.version.loading' })} />
      ) : rows.length === 0 && readFailure === null ? (
        <EmptyState
          actions={network.online ? startAction : null}
          iconName="list"
          live="polite"
          title={intl.formatMessage({ id: 'pricing.priceList.version.empty.title' })}
        >
          {intl.formatMessage({ id: 'pricing.priceList.version.empty' })}
        </EmptyState>
      ) : (
        <>
          <DataTable
            caption={intl.formatMessage({ id: 'pricing.priceList.version.caption' })}
            columns={[
              {
                id: 'version',
                header: intl.formatMessage({ id: 'pricing.priceList.version.column.version' }),
                numeric: true,
                primary: true,
                cell: (row: PriceListVersionSummary) => String(row.versionNumber),
              },
              {
                id: 'status',
                header: intl.formatMessage({ id: 'pricing.priceList.version.column.status' }),
                cell: (row: PriceListVersionSummary) =>
                  intl.formatMessage({
                    id: STATUS[row.status] ?? 'pricing.priceList.version.status.unknown',
                  }),
              },
              {
                id: 'name',
                header: intl.formatMessage({ id: 'pricing.priceList.version.column.name' }),
                cell: (row: PriceListVersionSummary) => row.name,
              },
              {
                id: 'effectiveFrom',
                header: intl.formatMessage({
                  id: 'pricing.priceList.version.column.effectiveFrom',
                }),
                cell: (row: PriceListVersionSummary) =>
                  formatters.formatShortDate(row.effectiveFrom),
              },
              {
                id: 'tax',
                header: intl.formatMessage({ id: 'pricing.priceList.version.column.tax' }),
                cell: (row: PriceListVersionSummary) =>
                  intl.formatMessage({
                    id: row.taxInclusive
                      ? 'pricing.priceList.version.tax.inclusive'
                      : 'pricing.priceList.version.tax.exclusive',
                  }),
              },
              {
                id: 'roundOff',
                header: intl.formatMessage({ id: 'pricing.priceList.version.column.roundOff' }),
                hideWhenNarrow: true,
                cell: (row: PriceListVersionSummary) =>
                  intl.formatMessage({
                    id: ROUND_OFF[row.roundOff] ?? 'pricing.priceList.roundOff.none',
                  }),
              },
              {
                id: 'threshold',
                header: intl.formatMessage({ id: 'pricing.priceList.version.column.threshold' }),
                hideWhenNarrow: true,
                cell: (row: PriceListVersionSummary) =>
                  formatters.formatPercent(row.overrideThresholdPercent),
              },
              {
                id: 'branches',
                header: intl.formatMessage({ id: 'pricing.priceList.version.column.branches' }),
                hideWhenNarrow: true,
                cell: (row: PriceListVersionSummary) =>
                  row.branchIds.length === 0
                    ? intl.formatMessage({ id: 'pricing.priceList.version.branches.none' })
                    : row.branchIds.map((branchId) => branchName(branchId) ?? branchId).join(', '),
              },
              {
                id: 'clonedFrom',
                header: intl.formatMessage({ id: 'pricing.priceList.version.column.clonedFrom' }),
                hideWhenNarrow: true,
                cell: (row: PriceListVersionSummary) => {
                  if (row.clonedFromVersionId === null) {
                    return intl.formatMessage({ id: 'pricing.priceList.version.clonedFrom.none' })
                  }
                  const source = rows.find(
                    (other) => other.priceListVersionId === row.clonedFromVersionId,
                  )
                  return intl.formatMessage(
                    { id: 'pricing.priceList.version.clonedFrom.value' },
                    { versionNumber: source?.versionNumber ?? '—' },
                  )
                },
              },
            ]}
            rowActions={(row: PriceListVersionSummary) => (
              <>
                <Link
                  aria-label={intl.formatMessage(
                    { id: 'pricing.priceList.version.open.label' },
                    { versionNumber: row.versionNumber },
                  )}
                  to={`/admin/price-lists/versions/${row.priceListVersionId}`}
                >
                  {intl.formatMessage({ id: 'pricing.priceList.version.open' })}
                </Link>
                <Button
                  aria-label={intl.formatMessage(
                    { id: 'pricing.priceList.version.clone.label' },
                    { versionNumber: row.versionNumber },
                  )}
                  onClick={() => {
                    openClone(row)
                  }}
                  variant="secondary"
                >
                  <FormattedMessage id="pricing.priceList.version.clone.action" />
                </Button>
              </>
            )}
            rowKey={(row) => row.priceListVersionId}
            rowLabel={(row) => String(row.versionNumber)}
            rows={rows}
          />

          {network.online ? (
            startAction
          ) : (
            <OfflineBlockedAction
              action={intl.formatMessage({ id: 'pricing.priceList.version.start.offlineAction' })}
            />
          )}
        </>
      )}

      {starting === null ? null : !network.online ? (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'pricing.priceList.version.start.offlineAction' })}
        />
      ) : (
        <>
          {isFieldFailure ? null : <BillingProblemAlert failure={failure} />}
          {isConcurrencyFailure ? (
            <Button
              onClick={() => {
                // A fresh key, not a reused one: the conflict means somebody else's version now
                // occupies the number this request would have replayed into, so replaying the same
                // key would only hand back the same conflict rather than attempt a new one.
                setFailure(null)
                versions.reload()
                setStarting((current) =>
                  current === null ? null : { ...current, idempotencyKey: crypto.randomUUID() },
                )
              }}
              variant="secondary"
            >
              {intl.formatMessage({ id: 'pricing.priceList.form.reread' })}
            </Button>
          ) : null}
          <PriceListVersionForm
            branchNamesAvailable={branchNamesAvailable}
            branchOptions={branchOptions}
            busy={busy}
            cloneFromVersionNumber={starting.cloneFromVersionNumber}
            controlId={(name) => `price-list-version-${name}`}
            draft={starting.draft}
            mode="start"
            onCancel={() => {
              setStarting(null)
              setFailure(null)
            }}
            onChange={(draft) => {
              setStarting({ ...starting, draft })
            }}
            onSubmit={submit}
            {...(branchIdsError === undefined ? {} : { branchIdsError })}
            {...(effectiveFromError === undefined ? {} : { effectiveFromError })}
            {...(overrideThresholdPercentError === undefined
              ? {}
              : { overrideThresholdPercentError })}
            {...(roundOffError === undefined ? {} : { roundOffError })}
            {...(taxInclusiveError === undefined ? {} : { taxInclusiveError })}
          />
        </>
      )}
    </section>
  )
}
