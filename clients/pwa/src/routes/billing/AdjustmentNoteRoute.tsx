import { useState } from 'react'
import type { FormEvent } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useParams } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { ApiError } from '../../auth/apiClient'
import { noteTotalOf, remainingTaxableValueOf } from '../../billing/adjustmentNote'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import {
  downloadNoteDocument,
  getInvoice,
  postCreditNote,
  postDebitNote,
} from '../../billing/billingApi'
import type { AdjustmentNote, AdjustmentNoteLineRequest, Invoice } from '../../billing/types'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import type { ConfirmOutcome } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { NetworkStatusBanner } from '../../components/states/NetworkStatusBanner'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { RadioGroup } from '../../design-system/components/forms/RadioGroup'
import { getFormatters } from '../../i18n/formatters'
import { saveBlob } from '../../downloads/saveBlob'
import './billing.css'

type NoteKind = 'Credit' | 'Debit'

/** One invoice line, and how much of it a note may still move. */
interface EligibleLine {
  readonly garmentJobId: string
  readonly description: string
  /** Set for a credit note only — a debit note carries no ceiling. */
  readonly remaining: number | undefined
}

function eligibleLinesOf(invoice: Invoice, kind: NoteKind): readonly EligibleLine[] {
  return invoice.lines
    .map((line) => ({
      garmentJobId: line.garmentJobId,
      description: line.description,
      remaining:
        kind === 'Credit' ? remainingTaxableValueOf(invoice, line.garmentJobId) : undefined,
    }))
    .filter((line) => line.remaining === undefined || line.remaining > 0)
}

/**
 * Issuing a credit or debit note against a posted invoice — the ordinary way to correct one, and the
 * only route the client offers once posting has closed the door on editing (#354, matrix rows 374 and
 * 375). Reading the invoice needs no version: unlike Post and Discard, this route carries no
 * `If-Match` — nothing on the invoice's own row moves, so the row is locked and re-read inside the
 * transaction instead.
 *
 * A credit note can only move a line's *remaining* taxable value — `remainingTaxableValueOf` mirrors
 * `Invoice.RemainingTaxableValueOf` on the server exactly, so the screen can grey out an already
 * fully-relieved line before ever asking the server. A debit note carries no such ceiling. Either way,
 * nothing computed here is sent as a figure the server trusts: only the typed taxable values go in the
 * request, and the server recomputes taxes, totals and the note number itself.
 */
export function AdjustmentNoteRoute() {
  const intl = useIntl()
  const formatters = getFormatters()
  const network = useNetworkState()
  const { invoiceId } = useParams()

  const resource = useAdminResource<Invoice>(`note:${invoiceId ?? ''}`, async (signal) => {
    const response = await getInvoice(invoiceId ?? '', signal)
    return response.value
  })
  const invoice = resource.value

  const [kind, setKind] = useState<NoteKind>('Credit')
  const [amounts, setAmounts] = useState<Readonly<Record<string, number | undefined>>>({})
  const [incomplete, setIncomplete] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [idempotencyKey, setIdempotencyKey] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [result, setResult] = useState<AdjustmentNote | null>(null)
  const [downloadBusy, setDownloadBusy] = useState(false)
  const [downloadFailure, setDownloadFailure] = useState<unknown>(null)

  const changeKind = (next: string): void => {
    setKind(next === 'Debit' ? 'Debit' : 'Credit')
    setAmounts({})
    setIncomplete(false)
  }

  const linesToPost = (eligible: readonly EligibleLine[]): readonly AdjustmentNoteLineRequest[] =>
    eligible
      .map((line) => ({
        garmentJobId: line.garmentJobId,
        taxableValue: amounts[line.garmentJobId],
      }))
      .filter(
        (candidate): candidate is AdjustmentNoteLineRequest =>
          candidate.taxableValue !== undefined && candidate.taxableValue > 0,
      )

  const eligible = invoice === null ? [] : eligibleLinesOf(invoice, kind)
  const toPost = linesToPost(eligible)
  const total = noteTotalOf(toPost)

  const openConfirmation = (event: FormEvent): void => {
    event.preventDefault()
    if (toPost.length === 0) {
      setIncomplete(true)
      return
    }
    setIncomplete(false)
    setFailure(null)
    setIdempotencyKey(crypto.randomUUID())
    setConfirming(true)
  }

  const cancel = (): void => {
    setConfirming(false)
    setFailure(null)
  }

  const run = (outcome: ConfirmOutcome): void => {
    if (invoice === null || idempotencyKey === null) {
      return
    }
    setBusy(true)
    setFailure(null)

    const body = { lines: toPost, reason: outcome.reason ?? null }
    const post =
      kind === 'Credit'
        ? postCreditNote({ invoiceId: invoice.invoiceId, body, idempotencyKey })
        : postDebitNote({ invoiceId: invoice.invoiceId, body, idempotencyKey })

    post
      .then((note) => {
        setConfirming(false)
        setIdempotencyKey(null)
        setResult(note)
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const download = (): void => {
    if (result === null || invoice === null) {
      return
    }
    setDownloadBusy(true)
    setDownloadFailure(null)
    downloadNoteDocument({
      invoiceId: invoice.invoiceId,
      noteId: result.noteId,
      fallbackFileName: `${result.number}.pdf`,
    })
      .then(({ blob, fileName }) => {
        saveBlob(blob, fileName ?? `${result.number}.pdf`)
      })
      .catch((cause: unknown) => {
        setDownloadFailure(cause)
      })
      .finally(() => {
        setDownloadBusy(false)
      })
  }

  const errorFor = (garmentJobId: string): string | undefined =>
    failure instanceof ApiError &&
    failure.problem?.errors?.[`lines[${garmentJobId}].taxableValue`] !== undefined
      ? intl.formatMessage({ id: 'billing.problem.noteExceedsLine' })
      : undefined

  return (
    <section className="page billing">
      <h1>
        <FormattedMessage id="billing.note.title" />
      </h1>

      <NetworkStatusBanner />

      {resource.failure === null ? null : <BillingProblemAlert failure={resource.failure} />}

      {resource.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'billing.note.loading' })} />
      ) : invoice === null ? null : result !== null ? (
        <Alert
          live="polite"
          title={intl.formatMessage({
            id: kind === 'Credit' ? 'billing.note.kind.credit' : 'billing.note.kind.debit',
          })}
          tone="success"
        >
          <p className="state-line">
            {intl.formatMessage(
              {
                id: kind === 'Credit' ? 'billing.note.posted.credit' : 'billing.note.posted.debit',
              },
              {
                amount: formatters.formatMoney(result.totals.grandTotal),
                invoiceNumber: invoice.invoiceNumber ?? invoice.orderNumber,
                number: result.number,
              },
            )}
          </p>
          <Button busy={downloadBusy} iconName="receipt" onClick={download} variant="secondary">
            {intl.formatMessage({ id: 'billing.note.posted.download' })}
          </Button>
          <BillingProblemAlert failure={downloadFailure} />
          <p>
            <Link to={`/billing/invoices/${invoice.invoiceId}`}>
              <FormattedMessage id="billing.note.posted.viewInvoice" />
            </Link>
          </p>
        </Alert>
      ) : invoice.status !== 'Posted' ? (
        <Alert live="polite" tone="warning">
          {intl.formatMessage({ id: 'billing.problem.invoiceNotPosted' })}
        </Alert>
      ) : invoice.cancelled ? (
        <Alert live="polite" tone="warning">
          {intl.formatMessage({ id: 'billing.problem.invoiceAlreadyCancelled' })}
        </Alert>
      ) : (
        <>
          <RadioGroup
            id="note-kind"
            label={intl.formatMessage({ id: 'billing.note.kind.label' })}
            name="kind"
            onValueChange={changeKind}
            options={[
              { value: 'Credit', label: intl.formatMessage({ id: 'billing.note.kind.credit' }) },
              { value: 'Debit', label: intl.formatMessage({ id: 'billing.note.kind.debit' }) },
            ]}
            value={kind}
          />

          {eligible.length === 0 ? (
            <EmptyState
              iconName="receipt"
              live="polite"
              title={intl.formatMessage({ id: 'billing.note.empty.title' })}
            >
              {intl.formatMessage({ id: 'billing.note.empty' })}
            </EmptyState>
          ) : (
            <form className="billing__form" noValidate onSubmit={openConfirmation}>
              {eligible.map((line) => {
                const hint =
                  line.remaining === undefined
                    ? intl.formatMessage({ id: 'billing.note.line.amount.hint' })
                    : `${intl.formatMessage(
                        { id: 'billing.note.line.remaining' },
                        { amount: formatters.formatMoney(line.remaining) },
                      )} ${intl.formatMessage({ id: 'billing.note.line.amount.hint' })}`
                const error = errorFor(line.garmentJobId)
                const amount = amounts[line.garmentJobId]

                return (
                  <NumericStepper
                    decimalPlaces={2}
                    description={hint}
                    id={`note-line-${line.garmentJobId}`}
                    inputMode="decimal"
                    key={line.garmentJobId}
                    label={intl.formatMessage(
                      { id: 'billing.note.line.amount.label' },
                      { description: line.description },
                    )}
                    min={0}
                    name={`amount-${line.garmentJobId}`}
                    onValueChange={(next) => {
                      setAmounts((previous) => ({ ...previous, [line.garmentJobId]: next }))
                    }}
                    showRangeHint={false}
                    unit={{ symbol: '₹', label: intl.formatMessage({ id: 'units.rupee.label' }) }}
                    {...(line.remaining === undefined ? {} : { max: line.remaining })}
                    {...(amount === undefined ? {} : { value: amount })}
                    {...(error === undefined ? {} : { error })}
                  />
                )
              })}

              <p>
                {intl.formatMessage(
                  { id: 'billing.note.total' },
                  { amount: formatters.formatMoney(total) },
                )}
              </p>

              {incomplete ? (
                <Alert live="assertive" tone="danger">
                  {intl.formatMessage({ id: 'billing.note.incomplete' })}
                </Alert>
              ) : null}

              <BillingProblemAlert failure={failure} />

              {network.online ? (
                <Button iconName="receipt" size="primary" type="submit" variant="primary">
                  {intl.formatMessage({
                    id:
                      kind === 'Credit'
                        ? 'billing.note.submit.credit'
                        : 'billing.note.submit.debit',
                  })}
                </Button>
              ) : (
                <OfflineBlockedAction
                  action={intl.formatMessage({ id: 'billing.note.offlineAction' })}
                />
              )}
            </form>
          )}

          {confirming ? (
            <ConfirmDialog
              action={intl.formatMessage({
                id: kind === 'Credit' ? 'billing.note.submit.credit' : 'billing.note.submit.debit',
              })}
              busy={busy}
              confirmLabel={intl.formatMessage({
                id: kind === 'Credit' ? 'billing.note.submit.credit' : 'billing.note.submit.debit',
              })}
              irreversible
              onCancel={cancel}
              onConfirm={run}
              open
              problem={<BillingProblemAlert failure={failure} />}
              tier="reason"
              title={intl.formatMessage({
                id:
                  kind === 'Credit'
                    ? 'billing.note.confirm.title.credit'
                    : 'billing.note.confirm.title.debit',
              })}
            >
              {intl.formatMessage(
                { id: 'billing.note.confirm.body' },
                {
                  amount: formatters.formatMoney(total),
                  invoiceNumber: invoice.invoiceNumber ?? invoice.orderNumber,
                },
              )}
            </ConfirmDialog>
          ) : null}
        </>
      )}
    </section>
  )
}
