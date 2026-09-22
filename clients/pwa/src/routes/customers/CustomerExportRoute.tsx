import { useEffect, useRef, useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import type { IntlShape } from 'react-intl'
import { Link, useParams } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ApiError } from '../../auth/apiClient'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { useAdminResource } from '../../admin/useAdminResource'
import {
  downloadCustomerExport,
  readCustomer,
  requestCustomerExport,
} from '../../customers/customersApi'
import { CUSTOMER_EXPORT_EXPIRED_CODE } from '../../customers/types'
import type { CustomerExport } from '../../customers/types'
import { saveBlob } from '../../downloads/saveBlob'
import { getFormatters } from '../../i18n/formatters'
import { isSupportedLocale } from '../../i18n/locales'
import './customers.css'

/**
 * Answering a subject-access request (#26, #182, #619).
 *
 * Until this screen there was no way to answer one from the application at all: the endpoints were
 * built, audited and tested, and reachable only by somebody who could call the API by hand. A right
 * of access has a statutory clock on it, and "the endpoint exists" is not "the shop can answer her".
 *
 * ## Why it is two steps and not a link
 *
 * Generating produces a **receipt** — what was made, how big it is, when the copy stops working. The
 * document itself is fetched separately, through the transport, as bytes. It is never given a URL,
 * because the download route re-authorises the caller, re-checks the organisation and the expiry,
 * and writes the read to the audit trail against the customer, none of which an address somebody
 * could copy out of a browser bar would do (`CLAUDE.md` section 4, rule 9, and
 * `docs/nfr/data-classification.md` section 10, which lists an export download among the reads that
 * are audited explicitly).
 *
 * The saved file is named after the export, never after the person. A customer's name in a downloads
 * folder is personal data in a place nobody is auditing — rule 8, and the reason the server picks the
 * name rather than the screen.
 *
 * ## Why generating asks first
 *
 * Generating destroys any earlier export for the same customer, so at most one copy of a person's
 * record exists outside the record at a time. That is the right default and it is a surprise if you
 * meet it by accident: somebody answering a second request would silently break the download they
 * gave out for the first. So the confirmation says so, and the screen says afterwards how many
 * copies it replaced.
 *
 * It is the confirm-with-reason tier rather than the typed tier. An export is significant and
 * recoverable — it can be generated again — and the reason is what the audit trail keeps, which for
 * a disclosure of everything the shop holds about a named person is the part that matters.
 */
export function CustomerExportRoute() {
  const intl = useIntl()
  const network = useNetworkState()
  const { customerId = '' } = useParams()

  const record = useAdminResource(customerId, (signal) => readCustomer(customerId, signal))
  const customer = record.value?.value ?? null

  const [asking, setAsking] = useState(false)
  const [busy, setBusy] = useState(false)
  const [downloading, setDownloading] = useState(false)
  /*
   * Two failures, not one.
   *
   * Generating and downloading fail for different reasons, are shown in different places — one
   * inside the confirmation, one on the page — and only the download can report that the copy has
   * gone. Sharing a slot worked only because the control that opens the dialog happened to clear it
   * first, which is an invariant nothing enforces and the next edit would quietly break.
   */
  const [generateFailure, setGenerateFailure] = useState<unknown>(null)
  const [downloadFailure, setDownloadFailure] = useState<unknown>(null)
  const [generated, setGenerated] = useState<CustomerExport | null>(null)
  /**
   * The attempt in hand: the reason that was sent, and the key it was sent under.
   *
   * The key belongs to the *request*, and the request is the reason — so pressing confirm again
   * after a timeout replays, and confirming a corrected reason does not. Rotating only on success
   * is not enough, which is the mistake this repeats from #584: the dialog stays open on a failure
   * with the text still editable, so "same key, different body" is one keystroke and one click away
   * on an operation that destroys the previous copy and writes the reason to the audit trail.
   */
  const [attempt, setAttempt] = useState<{ readonly reason: string; readonly key: string } | null>(
    null,
  )

  // Both calls are writes as far as the trail is concerned — one generates a copy, the other is an
  // audited read of somebody's personal data — so neither is abandoned when the screen goes away.
  // Only this component's own state updates are guarded.
  const live = useRef(true)
  useEffect(() => {
    live.current = true
    return () => {
      live.current = false
    }
  }, [])

  if (customer === null && record.loading) {
    return <LoadingState what={intl.formatMessage({ id: 'customers.detail.loading' })} />
  }

  if (customer === null) {
    return (
      <>
        <AuthProblemAlert failure={record.failure} />
        <EmptyState iconName="users" live="polite">
          {intl.formatMessage({ id: 'customers.detail.notFound' })}
        </EmptyState>
      </>
    )
  }

  const generate = (reason: string) => {
    if (busy) {
      return
    }

    // Same reason as the attempt that failed: a retry, and it keeps its key so the server replays.
    // Different reason: a different request, and it gets its own.
    const key = attempt !== null && attempt.reason === reason ? attempt.key : crypto.randomUUID()
    setAttempt({ reason, key })
    setBusy(true)
    setGenerateFailure(null)

    void requestCustomerExport({ customerId, reason, idempotencyKey: key })
      .then((receipt) => {
        if (!live.current) {
          return
        }
        setGenerated(receipt)
        setAsking(false)
        // Done with: the next export is a new request whatever its reason says.
        setAttempt(null)
      })
      .catch((cause: unknown) => {
        if (live.current) {
          setGenerateFailure(cause)
        }
      })
      .finally(() => {
        if (live.current) {
          setBusy(false)
        }
      })
  }

  const download = (receipt: CustomerExport) => {
    if (downloading) {
      return
    }

    setDownloading(true)
    setDownloadFailure(null)

    void downloadCustomerExport({
      customerId,
      exportId: receipt.exportId,
      documentCode: receipt.documentCode,
    })
      .then(({ blob, fileName }) => {
        // Deliberately not guarded by `live`, unlike everything below it. The bytes are already
        // fetched and the server has already written the read to the trail — the disclosure has
        // happened — and `saveBlob` touches the document rather than React state. Skipping it
        // because the person navigated away would throw away a file they asked for and that is
        // already recorded as delivered.
        saveBlob(blob, fileName ?? `${receipt.documentCode}-${receipt.exportId}.json`)
      })
      .catch((cause: unknown) => {
        if (!live.current) {
          return
        }
        setDownloadFailure(cause)
        if (cause instanceof ApiError && cause.code === CUSTOMER_EXPORT_EXPIRED_CODE) {
          // There is nothing left to offer a download of, so stop offering one. Leaving the button
          // up would let somebody press it at a dead copy until they worked out why.
          setGenerated(null)
        }
      })
      .finally(() => {
        if (live.current) {
          setDownloading(false)
        }
      })
  }

  const gone =
    downloadFailure instanceof ApiError && downloadFailure.code === CUSTOMER_EXPORT_EXPIRED_CODE

  return (
    <section className="page customers">
      <p>
        <Link to={`/customers/${customerId}`}>
          <FormattedMessage id="customers.edit.back" />
        </Link>
      </p>

      <h1>
        <FormattedMessage id="customers.export.title" values={{ name: customer.displayName }} />
      </h1>
      <p className="customers__lede">
        <FormattedMessage id="customers.export.body" />
      </p>

      <h2>
        <FormattedMessage id="customers.export.contains" />
      </h2>
      <ul>
        <li>
          <FormattedMessage id="customers.export.contains.profile" />
        </li>
        <li>
          <FormattedMessage id="customers.export.contains.consent" />
        </li>
        <li>
          <FormattedMessage id="customers.export.contains.preferences" />
        </li>
      </ul>
      {/*
        What it leaves out, stated as plainly as what it holds. Somebody handing this to a customer
        is answering for its completeness, and finding out later that the images were never in it is
        the wrong moment.
      */}
      <p className="customers__hint">
        <FormattedMessage id="customers.export.excludes" />
      </p>

      {gone ? (
        <Alert
          live="assertive"
          tone="warning"
          title={intl.formatMessage({ id: 'customers.export.gone.title' })}
        >
          <FormattedMessage id="customers.export.gone.body" />
        </Alert>
      ) : (
        <AuthProblemAlert failure={downloadFailure} />
      )}

      {generated === null ? null : (
        <ExportReceipt
          busy={downloading}
          online={network.online}
          onDownload={() => {
            download(generated)
          }}
          receipt={generated}
        />
      )}

      {network.online ? (
        <Button
          busy={busy}
          iconName="clipboard"
          onClick={() => {
            setGenerateFailure(null)
            setAsking(true)
          }}
          size="primary"
          variant="primary"
        >
          {intl.formatMessage({
            id: generated === null ? 'customers.export.action' : 'customers.export.again',
          })}
        </Button>
      ) : (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'customers.export.offlineAction' })}
        />
      )}

      {!asking ? null : (
        <ConfirmDialog
          action={intl.formatMessage({ id: 'customers.export.confirm.action' })}
          busy={busy}
          confirmLabel={intl.formatMessage({ id: 'customers.export.confirm.label' })}
          onCancel={() => {
            setAsking(false)
            setGenerateFailure(null)
          }}
          onConfirm={(outcome) => {
            generate((outcome.reason ?? '').trim())
          }}
          open
          problem={<AuthProblemAlert failure={generateFailure} />}
          tier="reason"
          title={intl.formatMessage(
            { id: 'customers.export.confirm.title' },
            { name: customer.displayName },
          )}
        >
          <FormattedMessage id="customers.export.confirm.body" />
        </ConfirmDialog>
      )}
    </section>
  )
}

/**
 * What was generated, in terms somebody can repeat to the person who asked for it.
 *
 * The download goes in the alert's `actions` slot rather than its content, which is the contract
 * every other caller follows: the content is the message and is inside a polite live region, and a
 * primary control folded into a region meant to be read out is a control whose presence is tied to
 * whatever re-announces the message.
 */
function ExportReceipt({
  receipt,
  busy,
  online,
  onDownload,
}: {
  readonly receipt: CustomerExport
  readonly busy: boolean
  readonly online: boolean
  readonly onDownload: () => void
}) {
  const intl = useIntl()

  return (
    <Alert
      actions={
        online ? (
          <Button busy={busy} iconName="clipboard" onClick={onDownload} variant="secondary">
            {intl.formatMessage({ id: 'customers.export.download' })}
          </Button>
        ) : (
          <OfflineBlockedAction
            action={intl.formatMessage({ id: 'customers.export.offlineDownload' })}
          />
        )
      }
      live="polite"
      tone="success"
      title={intl.formatMessage({ id: 'customers.export.ready.title' })}
    >
      <dl className="customers__detailGrid">
        <dt>
          <FormattedMessage id="customers.export.generatedAt" />
        </dt>
        <dd>{formatWhen(receipt.generatedAt, intl)}</dd>
        <dt>
          <FormattedMessage id="customers.export.expiresAt" />
        </dt>
        <dd>{formatWhen(receipt.expiresAt, intl)}</dd>
        <dt>
          <FormattedMessage id="customers.export.classification" />
        </dt>
        <dd>{receipt.classification}</dd>
      </dl>

      {receipt.supersededCount === 0 ? null : (
        <p>
          <FormattedMessage
            id="customers.export.superseded"
            values={{ count: receipt.supersededCount }}
          />
        </p>
      )}
    </Alert>
  )
}

/** Through the shared formatters, never at the call site. */
function formatWhen(value: string, intl: IntlShape): string {
  const formatters = isSupportedLocale(intl.locale) ? getFormatters(intl.locale) : getFormatters()
  return formatters.formatDateTime(value)
}
