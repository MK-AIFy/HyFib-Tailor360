import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { ApiError } from '../../auth/apiClient'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { billingProblemCode, billingProblemMessage } from '../../billing/billingProblems'
import {
  createPriceList,
  listPriceLists,
  readPriceList,
  renamePriceList,
} from '../../billing/priceListApi'
import type {
  CreatePriceListRequest,
  PriceList,
  RenamePriceListRequest,
} from '../../billing/priceListTypes'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { NetworkStatusBanner } from '../../components/states/NetworkStatusBanner'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { PriceListForm } from './PriceListForm'
import { blankPriceListDraft, priceListDraftForRename } from './priceListDraft'
import type { PriceListDraft } from './priceListDraft'

/** The refusal codes named beside the code field rather than through a page-level alert. */
const CODE_CODES = ['billing.code-not-unique', 'billing.code-not-well-formed']

/** The stale-`If-Match` refusal on a rename, which gets its own "read it again" rather than a bare retry. */
const CONCURRENCY_CODE = 'billing.price-list-changed'

/**
 * Every price list the organisation has, by code (#252) — the register the client's other pricing
 * screens hang off. A list's versions, its items and its discount rules are opened from a row; this
 * screen only creates a list and renames one.
 *
 * ## Why a rename re-reads before it edits
 *
 * `ListPriceLists` answers no version: only `GetPriceList` carries the `ETag` a rename is made
 * against, so opening the form for a known row still reads that row again to hold a precondition that
 * is actually current — the same reasoning `GstRegistrationsRoute.tsx` records for its own amendment.
 */
export function PriceListsRoute() {
  const intl = useIntl()
  const network = useNetworkState()

  const priceLists = useAdminResource('price-lists', listPriceLists)

  const [editing, setEditing] = useState<{
    readonly existing: PriceList | null
    readonly draft: PriceListDraft
    readonly version: string | undefined
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [keys, setKeys] = useState<Readonly<Record<string, string>>>({})

  const keyFor = (id: string): string => {
    const existingKey = keys[id]
    if (existingKey !== undefined) {
      return existingKey
    }
    const minted = crypto.randomUUID()
    setKeys((all) => ({ ...all, [id]: minted }))
    return minted
  }

  const forget = (id: string): void => {
    setKeys((all) => Object.fromEntries(Object.entries(all).filter(([spent]) => spent !== id)))
  }

  const openCreate = (): void => {
    setFailure(null)
    setNotice(null)
    setEditing({ existing: null, draft: blankPriceListDraft(), version: undefined })
  }

  const openRename = (priceList: PriceList): void => {
    setFailure(null)
    setNotice(null)
    setEditing({
      existing: priceList,
      draft: priceListDraftForRename(priceList.name),
      version: undefined,
    })
    if (!network.online) {
      return
    }
    void readPriceList(priceList.priceListId)
      .then(({ version }) => {
        setEditing((current) =>
          current?.existing?.priceListId === priceList.priceListId
            ? { ...current, version }
            : current,
        )
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
  }

  const reread = (): void => {
    const id = editing?.existing?.priceListId
    if (id === undefined) {
      return
    }
    setFailure(null)
    void readPriceList(id)
      .then(({ value, version }) => {
        setEditing({ existing: value, draft: priceListDraftForRename(value.name), version })
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
  }

  const submit = (): void => {
    if (editing === null) {
      return
    }
    const { draft, existing } = editing

    const id = existing === null ? 'create' : `rename:${existing.priceListId}`
    const idempotencyKey = keyFor(id)

    setBusy(true)
    setFailure(null)

    const write: Promise<PriceList> =
      existing === null
        ? createPriceList({
            body: {
              code: draft.code.trim() === '' ? null : draft.code.trim(),
              name: draft.name.trim() === '' ? null : draft.name.trim(),
              reason: draft.reason.trim() === '' ? null : draft.reason.trim(),
            } satisfies CreatePriceListRequest,
            idempotencyKey,
          })
        : editing.version === undefined
          ? Promise.reject(new ApiError('The price list must be read again.', { status: 409 }))
          : renamePriceList({
              priceListId: existing.priceListId,
              body: {
                name: draft.name.trim() === '' ? null : draft.name.trim(),
                reason: draft.reason.trim() === '' ? null : draft.reason.trim(),
              } satisfies RenamePriceListRequest,
              version: editing.version,
              idempotencyKey,
            }).then((result) => result.value)

    write
      .then(() => {
        forget(id)
        setEditing(null)
        setNotice(
          intl.formatMessage({
            id: existing === null ? 'pricing.priceList.created' : 'pricing.priceList.renamed',
          }),
        )
        priceLists.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const code = billingProblemCode(failure)
  const fieldMessageId =
    code !== undefined && CODE_CODES.includes(code) ? billingProblemMessage(failure) : undefined
  const codeError =
    fieldMessageId === undefined ? undefined : intl.formatMessage({ id: fieldMessageId })
  const isConcurrencyFailure = code === CONCURRENCY_CODE

  const rows = priceLists.value ?? []
  const createAction = (
    <Button onClick={openCreate} variant="primary">
      <FormattedMessage id="pricing.priceList.create.action" />
    </Button>
  )

  return (
    <section>
      <h2>
        <FormattedMessage id="pricing.priceList.title" />
      </h2>
      <p>
        <FormattedMessage id="pricing.priceList.body" />
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

      {priceLists.failure === null ? null : (
        <>
          <BillingProblemAlert failure={priceLists.failure} />
          <Button
            iconName="refresh"
            onClick={() => {
              priceLists.reload()
            }}
            variant="secondary"
          >
            <FormattedMessage id="states.error.retry" />
          </Button>
        </>
      )}

      {priceLists.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'pricing.priceList.loading' })} />
      ) : rows.length === 0 && priceLists.failure === null ? (
        <EmptyState
          actions={network.online ? createAction : null}
          iconName="list"
          live="polite"
          title={intl.formatMessage({ id: 'pricing.priceList.empty.title' })}
        >
          {intl.formatMessage({ id: 'pricing.priceList.empty' })}
        </EmptyState>
      ) : (
        <>
          <DataTable
            caption={intl.formatMessage({ id: 'pricing.priceList.caption' })}
            columns={[
              {
                id: 'code',
                header: intl.formatMessage({ id: 'pricing.priceList.column.code' }),
                primary: true,
                cell: (row: PriceList) => row.code,
              },
              {
                id: 'name',
                header: intl.formatMessage({ id: 'pricing.priceList.column.name' }),
                cell: (row: PriceList) => row.name,
              },
            ]}
            rowActions={(row: PriceList) => (
              <>
                <Link
                  aria-label={intl.formatMessage(
                    { id: 'pricing.priceList.open.label' },
                    { code: row.code },
                  )}
                  to={`/admin/price-lists/${row.priceListId}`}
                >
                  {intl.formatMessage({ id: 'pricing.priceList.open' })}
                </Link>
                <Button
                  aria-label={intl.formatMessage(
                    { id: 'pricing.priceList.rename.label' },
                    { code: row.code },
                  )}
                  onClick={() => {
                    openRename(row)
                  }}
                  variant="secondary"
                >
                  {intl.formatMessage({ id: 'pricing.priceList.rename' })}
                </Button>
              </>
            )}
            rowKey={(row) => row.priceListId}
            rowLabel={(row) => row.code}
            rows={rows}
          />

          {network.online ? (
            createAction
          ) : (
            <OfflineBlockedAction
              action={intl.formatMessage({ id: 'pricing.priceList.create.offlineAction' })}
            />
          )}
        </>
      )}

      {editing === null ? null : !network.online ? (
        <OfflineBlockedAction
          action={intl.formatMessage({
            id:
              editing.existing === null
                ? 'pricing.priceList.create.offlineAction'
                : 'pricing.priceList.rename.offlineAction',
          })}
        />
      ) : (
        <>
          {codeError === undefined ? <BillingProblemAlert failure={failure} /> : null}
          {isConcurrencyFailure ? (
            <Button onClick={reread} variant="secondary">
              {intl.formatMessage({ id: 'pricing.priceList.form.reread' })}
            </Button>
          ) : null}
          <PriceListForm
            busy={busy}
            controlId={(name) => `price-list-${name}`}
            draft={editing.draft}
            existingCode={editing.existing?.code ?? null}
            mode={editing.existing === null ? 'create' : 'rename'}
            onCancel={() => {
              setEditing(null)
              setFailure(null)
            }}
            onChange={(draft) => {
              setEditing({ ...editing, draft })
            }}
            onSubmit={submit}
            {...(codeError === undefined ? {} : { codeError })}
          />
        </>
      )}
    </section>
  )
}
