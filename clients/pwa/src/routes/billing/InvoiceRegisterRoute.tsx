import { useState } from 'react'
import type { FormEvent } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, Navigate } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { ApiError } from '../../auth/apiClient'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { listInvoices, resolveInvoiceBarcode } from '../../billing/billingApi'
import { billingProblemCode } from '../../billing/billingProblems'
import type { InvoiceSummary } from '../../billing/types'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { Filters } from '../../components/primitives/Filters'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { NetworkStatusBanner } from '../../components/states/NetworkStatusBanner'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { Select } from '../../design-system/components/forms/Select'
import { TextField } from '../../design-system/components/forms/TextField'
import { getFormatters } from '../../i18n/formatters'
import './billing.css'

/** The three status names `GET /api/v1/billing/invoices` accepts, by name and case-sensitively. */
const INVOICE_STATUSES = ['Draft', 'Posted', 'Discarded'] as const

/**
 * The page size, cited from `IInvoiceStore.InvoiceListQuery.DefaultLimit` rather than chosen here —
 * the issue's own instruction, so the client and the server agree on what a page is without either
 * one guessing at the other's default.
 */
const PAGE_SIZE = 20

/**
 * The branch's invoices, newest first (#302), and a barcode lookup that opens one by its printed
 * `I-` payload.
 *
 * ## Why a discarded draft is listed rather than hidden
 *
 * **OD-22** leaves open "whether a discarded draft is listed or hidden", and this register lists it
 * — the reversible reading, because a discarded draft is still audited and a cashier asking "what
 * happened to the invoice I started for this order" deserves an answer rather than a gap.
 *
 * ## Why the barcode lookup is a plain text field
 *
 * Camera and wedge-scanner capture of the `I-` payload belong to Custody (#190–#197); this slice
 * accepts whatever a person types or a keyboard-wedge scanner injects into an ordinary field, which
 * is already scanner-compatible without any scanner-specific code here.
 */
export function InvoiceRegisterRoute() {
  const intl = useIntl()
  const formatters = getFormatters()
  const network = useNetworkState()

  const [status, setStatus] = useState('')
  const [cursors, setCursors] = useState<readonly string[]>([])
  const [seen, setSeen] = useState<readonly InvoiceSummary[]>([])

  const cursor = cursors.at(-1)

  const page = useAdminResource(`${status}|${cursor ?? ''}`, (signal) =>
    listInvoices({
      ...(status === '' ? {} : { status }),
      ...(cursor === undefined ? {} : { cursor }),
      limit: PAGE_SIZE,
      signal,
    }),
  )

  // The pages are concatenated as they arrive, exactly as the audit trail's "show older entries"
  // does: a person pressing Show more must not lose the rows already on screen.
  const invoices =
    cursor === undefined ? (page.value?.invoices ?? []) : [...seen, ...(page.value?.invoices ?? [])]

  const applyStatus = (next: string): void => {
    setStatus(next)
    setCursors([])
    setSeen([])
  }

  const [barcode, setBarcode] = useState('')
  const [barcodeIncomplete, setBarcodeIncomplete] = useState(false)
  const [lookingUp, setLookingUp] = useState(false)
  const [lookupFailure, setLookupFailure] = useState<unknown>(null)
  const [resolvedInvoiceId, setResolvedInvoiceId] = useState<string | null>(null)

  const lookUp = (event: FormEvent): void => {
    event.preventDefault()
    const trimmed = barcode.trim()
    if (trimmed === '') {
      setBarcodeIncomplete(true)
      return
    }
    setBarcodeIncomplete(false)
    setLookingUp(true)
    setLookupFailure(null)

    resolveInvoiceBarcode(trimmed)
      .then((resolved) => {
        setResolvedInvoiceId(resolved.invoiceId)
      })
      .catch((cause: unknown) => {
        setLookupFailure(cause)
      })
      .finally(() => {
        setLookingUp(false)
      })
  }

  // The route's only other answer besides `billing.document-not-found` — the caller's session
  // carries no branch — is a validation problem naming the field, not a not-found. It is read from
  // `errors.branch` rather than mapped in the shared code table, because `billing.value-required` is
  // shared with unrelated validation failures elsewhere in Billing (billingProblems.ts's own note).
  const branchRequired =
    lookupFailure instanceof ApiError &&
    billingProblemCode(lookupFailure) === 'billing.value-required' &&
    lookupFailure.problem?.errors?.branch !== undefined

  if (resolvedInvoiceId !== null) {
    return <Navigate to={`/billing/invoices/${resolvedInvoiceId}`} />
  }

  return (
    <section className="page billing">
      <h1>
        <FormattedMessage id="billing.invoices.title" />
      </h1>

      <NetworkStatusBanner />

      <section className="billing__lookup" aria-labelledby="invoice-lookup-heading">
        <h2 id="invoice-lookup-heading">
          <FormattedMessage id="billing.invoices.lookup.title" />
        </h2>
        {network.online ? (
          <form className="billing__form" noValidate onSubmit={lookUp}>
            <TextField
              description={intl.formatMessage({ id: 'billing.invoices.lookup.hint' })}
              id="invoice-barcode"
              label={intl.formatMessage({ id: 'billing.invoices.lookup.label' })}
              name="barcode"
              onValueChange={setBarcode}
              value={barcode}
              {...(barcodeIncomplete
                ? { error: intl.formatMessage({ id: 'billing.invoices.lookup.required' }) }
                : {})}
            />
            {branchRequired ? (
              <Alert live="assertive" tone="danger">
                <p className="state-line">
                  {intl.formatMessage({ id: 'billing.invoices.lookup.branchRequired' })}
                </p>
              </Alert>
            ) : (
              <BillingProblemAlert failure={lookupFailure} />
            )}
            <Button busy={lookingUp} iconName="search" type="submit" variant="primary">
              {intl.formatMessage({
                id: lookingUp
                  ? 'billing.invoices.lookup.finding'
                  : 'billing.invoices.lookup.action',
              })}
            </Button>
          </form>
        ) : (
          <OfflineBlockedAction
            action={intl.formatMessage({ id: 'billing.invoices.lookup.offlineAction' })}
          />
        )}
      </section>

      <Filters
        applied={
          status === ''
            ? []
            : [
                {
                  id: 'status',
                  label: intl.formatMessage(
                    { id: 'billing.invoices.filter.applied' },
                    {
                      status: intl.formatMessage({
                        id: `billing.invoices.status.${status.toLowerCase()}`,
                      }),
                    },
                  ),
                },
              ]
        }
        onClearAll={() => {
          applyStatus('')
        }}
        onRemove={() => {
          applyStatus('')
        }}
        resultCount={invoices.length}
      >
        <Select
          emptyLabel={intl.formatMessage({ id: 'billing.invoices.filter.status.all' })}
          id="invoice-status-filter"
          label={intl.formatMessage({ id: 'billing.invoices.filter.status.label' })}
          name="status"
          onValueChange={applyStatus}
          options={INVOICE_STATUSES.map((value) => ({
            value,
            label: intl.formatMessage({ id: `billing.invoices.status.${value.toLowerCase()}` }),
          }))}
          value={status}
        />
      </Filters>

      {page.failure === null ? null : (
        <>
          <BillingProblemAlert failure={page.failure} />
          <Button
            iconName="refresh"
            onClick={() => {
              page.reload()
            }}
            variant="secondary"
          >
            <FormattedMessage id="states.error.retry" />
          </Button>
        </>
      )}

      {page.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'billing.invoices.loading' })} />
      ) : invoices.length === 0 && page.failure === null ? (
        <EmptyState
          iconName="receipt"
          live="polite"
          title={intl.formatMessage({
            id:
              status === ''
                ? 'billing.invoices.empty.title'
                : 'billing.invoices.empty.filtered.title',
          })}
        >
          {intl.formatMessage({
            id: status === '' ? 'billing.invoices.empty' : 'billing.invoices.empty.filtered',
          })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'billing.invoices.caption' })}
          columns={[
            {
              id: 'invoice',
              header: intl.formatMessage({ id: 'billing.invoices.column.invoice' }),
              primary: true,
              cell: (row: InvoiceSummary) =>
                row.invoiceNumber ?? intl.formatMessage({ id: 'billing.invoice.header.draft' }),
            },
            {
              id: 'order',
              header: intl.formatMessage({ id: 'billing.invoices.column.order' }),
              cell: (row: InvoiceSummary) => row.orderNumber,
            },
            {
              id: 'customer',
              header: intl.formatMessage({ id: 'billing.invoices.column.customer' }),
              cell: (row: InvoiceSummary) => row.customerDisplayName,
            },
            {
              id: 'status',
              header: intl.formatMessage({ id: 'billing.invoices.column.status' }),
              cell: (row: InvoiceSummary) =>
                intl.formatMessage({ id: `billing.invoices.status.${row.status.toLowerCase()}` }),
            },
            {
              id: 'total',
              header: intl.formatMessage({ id: 'billing.invoices.column.total' }),
              numeric: true,
              cell: (row: InvoiceSummary) => formatters.formatMoney(row.grandTotal),
            },
            {
              id: 'created',
              header: intl.formatMessage({ id: 'billing.invoices.column.created' }),
              hideWhenNarrow: true,
              cell: (row: InvoiceSummary) => formatters.formatShortDate(row.createdAt),
            },
          ]}
          rowActions={(row: InvoiceSummary) => (
            <Link
              aria-label={intl.formatMessage(
                { id: 'billing.invoices.open.label' },
                { reference: row.invoiceNumber ?? row.orderNumber },
              )}
              to={`/billing/invoices/${row.invoiceId}`}
            >
              <FormattedMessage id="billing.invoices.open" />
            </Link>
          )}
          rowKey={(row) => row.invoiceId}
          rowLabel={(row) => row.invoiceNumber ?? row.orderNumber}
          rows={invoices}
        />
      )}

      {page.value?.nextCursor == null ? null : network.online ? (
        <div className="billing__actions">
          <Button
            onClick={() => {
              setSeen(invoices)
              setCursors((previous) => [...previous, page.value?.nextCursor ?? ''])
            }}
            variant="secondary"
          >
            <FormattedMessage id="billing.invoices.loadMore" />
          </Button>
        </div>
      ) : (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'billing.invoices.loadMore.offlineAction' })}
        />
      )}
    </section>
  )
}
