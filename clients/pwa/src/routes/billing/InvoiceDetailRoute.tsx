import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { useParams } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { ApiError } from '../../auth/apiClient'
import type { VersionedResponse } from '../../auth/apiClient'
import { useCurrentUser } from '../../auth/useSession'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import {
  discardInvoiceDraft,
  downloadInvoiceDocument,
  downloadNoteDocument,
  getInvoice,
  postInvoice,
  printInvoice,
} from '../../billing/billingApi'
import { InvoiceDocumentView } from '../../billing/InvoiceDocumentView'
import type { AdjustmentNote, Invoice } from '../../billing/types'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import type { ConfirmOutcome } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ButtonGroup } from '../../components/primitives/ButtonGroup'
import { LoadingState } from '../../components/states/LoadingState'
import { NetworkStatusBanner } from '../../components/states/NetworkStatusBanner'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import type { NetworkState } from '../../components/states/useNetworkState'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { getFormatters } from '../../i18n/formatters'
import './billing.css'

/**
 * One invoice: the document view, the three ways to get it off the screen (#302, #336), and — on a
 * draft — the two ways to move it on (#345).
 */
export function InvoiceDetailRoute() {
  const intl = useIntl()
  const { invoiceId } = useParams()

  const resource = useAdminResource<VersionedResponse<Invoice>>(
    `invoice:${invoiceId ?? ''}`,
    (signal) => getInvoice(invoiceId ?? '', signal),
  )
  const invoice = resource.value?.value ?? null
  const version = resource.value?.version

  return (
    <section className="page billing">
      <h1>
        {invoice === null
          ? intl.formatMessage({ id: 'billing.invoice.title' })
          : (invoice.invoiceNumber ?? intl.formatMessage({ id: 'billing.invoice.header.draft' }))}
      </h1>

      <NetworkStatusBanner />

      {resource.failure === null ? null : (
        <>
          <BillingProblemAlert failure={resource.failure} />
          <Button
            iconName="refresh"
            onClick={() => {
              resource.reload()
            }}
            variant="secondary"
          >
            <FormattedMessage id="states.error.retry" />
          </Button>
        </>
      )}

      {resource.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'billing.invoice.loading' })} />
      ) : invoice === null ? null : (
        <InvoiceDetail invoice={invoice} onChanged={resource.reload} version={version} />
      )}
    </section>
  )
}

function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  try {
    const link = document.createElement('a')
    link.href = url
    link.download = fileName
    link.click()
  } finally {
    URL.revokeObjectURL(url)
  }
}

function InvoiceDetail({
  invoice,
  version,
  onChanged,
}: {
  invoice: Invoice
  version: string | undefined
  onChanged: () => void
}) {
  const intl = useIntl()
  const formatters = getFormatters()
  const network = useNetworkState()
  // Held here, not inside PostDiscardControls: a successful post or discard changes `invoice.status`
  // away from `Draft`, which unmounts that component on the very next render — along with any state
  // of its own — before the notice it just set could ever be seen.
  const [lifecycleNotice, setLifecycleNotice] = useState<string | null>(null)

  return (
    <>
      <p className="billing__lede">
        {intl.formatMessage({ id: `billing.invoices.status.${invoice.status.toLowerCase()}` })}
      </p>
      <p className="billing__hint">
        {intl.formatMessage(
          { id: 'billing.invoice.header.order' },
          { orderNumber: invoice.orderNumber },
        )}
      </p>
      {invoice.financialYear === null ? null : (
        <p className="billing__hint">
          {intl.formatMessage(
            { id: 'billing.invoice.header.financialYear' },
            { year: invoice.financialYear },
          )}
        </p>
      )}
      <p className="billing__hint">
        {invoice.postedAt !== null
          ? intl.formatMessage(
              { id: 'billing.invoice.header.posted' },
              { date: formatters.formatShortDate(invoice.postedAt) },
            )
          : invoice.discardedAt !== null
            ? intl.formatMessage(
                { id: 'billing.invoice.header.discarded' },
                { date: formatters.formatShortDate(invoice.discardedAt) },
              )
            : intl.formatMessage({ id: 'billing.invoice.header.notPosted' })}
      </p>

      {invoice.cancelled && invoice.cancellation !== null ? (
        <Alert
          live="polite"
          title={intl.formatMessage({ id: 'billing.invoice.cancelled.title' })}
          tone="warning"
        >
          <p className="state-line">
            {intl.formatMessage(
              { id: 'billing.invoice.cancelled.body' },
              {
                date: formatters.formatShortDate(invoice.cancellation.cancelledAt),
                reason: invoice.cancellation.reason,
              },
            )}
          </p>
        </Alert>
      ) : null}

      {lifecycleNotice === null ? null : (
        <Alert live="polite" tone="success">
          {lifecycleNotice}
        </Alert>
      )}

      <PostDiscardControls
        invoice={invoice}
        network={network}
        onChanged={onChanged}
        onNotice={setLifecycleNotice}
        version={version}
      />

      <PrintControls invoice={invoice} network={network} />

      <InvoiceDocumentView invoice={invoice} />

      {invoice.notes.length === 0 ? null : (
        <section aria-labelledby="invoice-notes-heading">
          <h2 id="invoice-notes-heading">
            <FormattedMessage id="billing.invoice.notes.title" />
          </h2>
          <ul className="billing__notes">
            {invoice.notes.map((note) => (
              <li key={note.noteId}>
                <NoteCard invoice={invoice} note={note} />
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  )
}

function NoteCard({ invoice, note }: { invoice: Invoice; note: AdjustmentNote }) {
  const intl = useIntl()
  const formatters = getFormatters()
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)

  const download = (): void => {
    setBusy(true)
    setFailure(null)
    downloadNoteDocument({
      invoiceId: invoice.invoiceId,
      noteId: note.noteId,
      fallbackFileName: `${note.number}.pdf`,
    })
      .then(({ blob, fileName }) => {
        saveBlob(blob, fileName ?? `${note.number}.pdf`)
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  return (
    <article>
      <h3>
        {intl.formatMessage(
          {
            id:
              note.kind === 'Credit'
                ? 'billing.invoice.notes.credit'
                : 'billing.invoice.notes.debit',
          },
          { number: note.number },
        )}
      </h3>
      <p className="billing__hint">
        {intl.formatMessage(
          { id: 'billing.invoice.notes.posted' },
          { date: formatters.formatShortDate(note.postedAt) },
        )}
      </p>
      <p>{intl.formatMessage({ id: 'billing.invoice.notes.reason' }, { reason: note.reason })}</p>
      <p>{formatters.formatMoney(note.totals.grandTotal)}</p>
      <Button
        aria-label={intl.formatMessage(
          { id: 'billing.invoice.notes.download.label' },
          { number: note.number },
        )}
        busy={busy}
        iconName="receipt"
        onClick={download}
        variant="subtle"
      >
        {intl.formatMessage({ id: 'billing.invoice.notes.download' })}
      </Button>
      <BillingProblemAlert failure={failure} />
    </article>
  )
}

/**
 * Post and Discard, on a `Draft` invoice only (#345). Neither is offered on a posted invoice, and
 * each is offered only to a caller who holds the permission its own route declares — a caller
 * holding `billing.create_invoice` but not `billing.post_invoice` never sees Post at all, matching
 * the server's own refusal rather than showing a control that would answer `403`.
 *
 * The `Idempotency-Key` is minted the moment the confirmation opens, not when it is pressed, and
 * held for as long as that one confirmed act is in flight — including the replay `apiClient` sends
 * after an in-place re-authentication — so a retry can only ever replay the first outcome.
 *
 * A stale `ETag` answers `412 billing.invoice-changed`: the sentence renders through
 * `BillingProblemAlert` like any other refusal, and `onChanged` refetches the invoice in the
 * background so the next press carries a fresh precondition, all without closing the dialog or
 * losing the reason the person already typed into it.
 */
function PostDiscardControls({
  invoice,
  version,
  network,
  onChanged,
  onNotice,
}: {
  invoice: Invoice
  version: string | undefined
  network: NetworkState
  onChanged: () => void
  /**
   * Reports a success sentence to the parent rather than rendering one itself: the write that
   * produces it also changes `invoice.status` away from `Draft`, which unmounts this component on
   * the very next render, before a notice held in its own state could ever be seen.
   */
  onNotice: (message: string) => void
}) {
  const intl = useIntl()
  const formatters = getFormatters()
  const { permissions } = useCurrentUser()
  const canPost = permissions.includes(BILLING_PERMISSIONS.postInvoice)
  const canDiscard = permissions.includes(BILLING_PERMISSIONS.updateInvoice)

  const [confirming, setConfirming] = useState<'post' | 'discard' | null>(null)
  const [actionKey, setActionKey] = useState<{
    readonly action: 'post' | 'discard'
    readonly key: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)

  if (invoice.status !== 'Draft' || (!canPost && !canDiscard)) {
    return null
  }

  const open = (action: 'post' | 'discard'): void => {
    setFailure(null)
    setActionKey({ action, key: crypto.randomUUID() })
    setConfirming(action)
  }

  const cancel = (): void => {
    setConfirming(null)
    setFailure(null)
  }

  const run = (outcome: ConfirmOutcome): void => {
    if (confirming === null || version === undefined || actionKey?.action !== confirming) {
      return
    }
    setBusy(true)
    setFailure(null)

    const action = confirming
    const key = actionKey.key
    const write =
      action === 'post'
        ? postInvoice({ invoiceId: invoice.invoiceId, reason: null, version, idempotencyKey: key })
        : discardInvoiceDraft({
            invoiceId: invoice.invoiceId,
            reason: outcome.reason ?? '',
            version,
            idempotencyKey: key,
          })

    write
      .then((response) => {
        setConfirming(null)
        setActionKey(null)
        onNotice(
          action === 'post'
            ? intl.formatMessage(
                { id: 'billing.invoice.post.posted' },
                { invoiceNumber: response.value.invoiceNumber ?? '' },
              )
            : intl.formatMessage({ id: 'billing.invoice.discard.discarded' }),
        )
        onChanged()
      })
      .catch((cause: unknown) => {
        // The invoice moved under the caller: refetch so the dialog's next press, if there is one,
        // carries a fresh precondition. The dialog itself stays open — closing it here would lose
        // the reason the person already typed.
        if (cause instanceof ApiError && cause.status === 412) {
          onChanged()
        }
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  return (
    <section aria-labelledby="invoice-lifecycle-heading" className="billing__actions">
      <h2 id="invoice-lifecycle-heading">
        <FormattedMessage id="billing.invoices.status.draft" />
      </h2>

      {network.online ? (
        <ButtonGroup>
          {canPost ? (
            <Button
              iconName="check-circle"
              onClick={() => {
                open('post')
              }}
              size="primary"
              variant="primary"
            >
              {intl.formatMessage({ id: 'billing.invoice.post.action' })}
            </Button>
          ) : null}
          {canDiscard ? (
            <Button
              iconName="x-circle"
              onClick={() => {
                open('discard')
              }}
              variant="secondary"
            >
              {intl.formatMessage({ id: 'billing.invoice.discard.action' })}
            </Button>
          ) : null}
        </ButtonGroup>
      ) : (
        <>
          {canPost ? (
            <OfflineBlockedAction
              action={intl.formatMessage({ id: 'billing.invoice.post.offlineAction' })}
            />
          ) : null}
          {canDiscard ? (
            <OfflineBlockedAction
              action={intl.formatMessage({ id: 'billing.invoice.discard.offlineAction' })}
            />
          ) : null}
        </>
      )}

      {confirming !== 'post' ? null : (
        <ConfirmDialog
          action={intl.formatMessage({ id: 'billing.invoice.post.action' })}
          busy={busy}
          confirmLabel={intl.formatMessage({ id: 'billing.invoice.post.action' })}
          irreversible
          onCancel={cancel}
          onConfirm={run}
          open
          problem={<BillingProblemAlert failure={failure} />}
          tier="typed"
          title={intl.formatMessage({ id: 'billing.invoice.post.confirm.title' })}
          typedPhrase={intl.formatMessage(
            { id: 'billing.invoice.post.confirm.typedPhrase' },
            { orderNumber: invoice.orderNumber },
          )}
        >
          {intl.formatMessage(
            { id: 'billing.invoice.post.confirm.body' },
            { amount: formatters.formatMoney(invoice.totals.grandTotal) },
          )}
        </ConfirmDialog>
      )}

      {confirming !== 'discard' ? null : (
        <ConfirmDialog
          action={intl.formatMessage({ id: 'billing.invoice.discard.action' })}
          busy={busy}
          confirmLabel={intl.formatMessage({ id: 'billing.invoice.discard.action' })}
          onCancel={cancel}
          onConfirm={run}
          open
          problem={<BillingProblemAlert failure={failure} />}
          tier="reason"
          title={intl.formatMessage({ id: 'billing.invoice.discard.confirm.title' })}
        >
          {intl.formatMessage({ id: 'billing.invoice.discard.confirm.body' })}
        </ConfirmDialog>
      )}
    </section>
  )
}

/**
 * The three named controls (#336): print this page over the document view, send to the branch's
 * print station, and download the stored PDF. Only the first works offline — the other two are
 * online-only, per every other billing write.
 */
function PrintControls({ invoice, network }: { invoice: Invoice; network: NetworkState }) {
  const intl = useIntl()

  const [copies, setCopies] = useState(1)
  const [copiesIncomplete, setCopiesIncomplete] = useState(false)
  const [printBusy, setPrintBusy] = useState(false)
  const [printFailure, setPrintFailure] = useState<unknown>(null)
  const [printJobId, setPrintJobId] = useState<string | null>(null)
  const [printKey, setPrintKey] = useState<{
    readonly fingerprint: string
    readonly key: string
  } | null>(null)

  const [downloadBusy, setDownloadBusy] = useState(false)
  const [downloadFailure, setDownloadFailure] = useState<unknown>(null)

  const sendToStation = (): void => {
    if (!Number.isInteger(copies) || copies < 1 || copies > 5) {
      setCopiesIncomplete(true)
      return
    }
    setCopiesIncomplete(false)
    setPrintBusy(true)
    setPrintFailure(null)

    const fingerprint = `${invoice.invoiceId}:${copies}`
    const key =
      printKey !== null && printKey.fingerprint === fingerprint ? printKey.key : crypto.randomUUID()
    setPrintKey({ fingerprint, key })

    printInvoice({ invoiceId: invoice.invoiceId, body: { copies }, idempotencyKey: key })
      .then((job) => {
        setPrintKey(null)
        setPrintJobId(job.printJobId)
      })
      .catch((cause: unknown) => {
        setPrintFailure(cause)
      })
      .finally(() => {
        setPrintBusy(false)
      })
  }

  const download = (): void => {
    setDownloadBusy(true)
    setDownloadFailure(null)
    const fallbackFileName = `${invoice.invoiceNumber ?? invoice.invoiceId}.pdf`

    downloadInvoiceDocument(invoice.invoiceId, fallbackFileName)
      .then(({ blob, fileName }) => {
        saveBlob(blob, fileName ?? fallbackFileName)
      })
      .catch((cause: unknown) => {
        setDownloadFailure(cause)
      })
      .finally(() => {
        setDownloadBusy(false)
      })
  }

  return (
    <section aria-labelledby="invoice-print-heading" className="billing__actions">
      <h2 id="invoice-print-heading">
        <FormattedMessage id="billing.invoice.print.title" />
      </h2>
      <ButtonGroup>
        <Button
          iconName="clipboard"
          onClick={() => {
            window.print()
          }}
          variant="secondary"
        >
          <FormattedMessage id="billing.invoice.print.page" />
        </Button>

        {network.online ? (
          <Button busy={printBusy} iconName="receipt" onClick={sendToStation} variant="secondary">
            {intl.formatMessage({
              id: printBusy
                ? 'billing.invoice.print.station.sending'
                : 'billing.invoice.print.station',
            })}
          </Button>
        ) : null}

        {network.online ? (
          <Button busy={downloadBusy} iconName="receipt" onClick={download} variant="secondary">
            {intl.formatMessage({
              id: downloadBusy
                ? 'billing.invoice.print.download.downloading'
                : 'billing.invoice.print.download',
            })}
          </Button>
        ) : null}
      </ButtonGroup>

      {network.online ? (
        <NumericStepper
          decimalPlaces={0}
          description={intl.formatMessage({ id: 'billing.invoice.print.station.copies.hint' })}
          id="invoice-print-copies"
          label={intl.formatMessage({ id: 'billing.invoice.print.station.copies.label' })}
          max={5}
          min={1}
          name="copies"
          onValueChange={setCopies}
          showRangeHint={false}
          value={copies}
          {...(copiesIncomplete
            ? {
                error: intl.formatMessage({
                  id: 'billing.invoice.print.station.copies.outOfRange',
                }),
              }
            : {})}
        />
      ) : null}

      {network.online ? null : (
        <>
          <OfflineBlockedAction
            action={intl.formatMessage({ id: 'billing.invoice.print.offlineAction.station' })}
          />
          <OfflineBlockedAction
            action={intl.formatMessage({ id: 'billing.invoice.print.offlineAction.download' })}
          />
        </>
      )}

      {printJobId === null ? null : (
        <Alert live="polite" tone="success">
          {intl.formatMessage({ id: 'billing.invoice.print.station.sent' }, { jobId: printJobId })}
        </Alert>
      )}
      <BillingProblemAlert failure={printFailure} />
      <BillingProblemAlert failure={downloadFailure} />
    </section>
  )
}
