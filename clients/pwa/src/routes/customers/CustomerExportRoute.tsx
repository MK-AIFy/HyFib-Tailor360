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
  const [failure, setFailure] = useState<unknown>(null)
  const [generated, setGenerated] = useState<CustomerExport | null>(null)
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID())

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
    setBusy(true)
    setFailure(null)

    void requestCustomerExport({ customerId, reason, idempotencyKey })
      .then((receipt) => {
        if (!live.current) {
          return
        }
        setGenerated(receipt)
        setAsking(false)
        // A new key for the next, separate request. A resend of *this* one must replay rather than
        // make a second copy of somebody's record.
        setIdempotencyKey(crypto.randomUUID())
      })
      .catch((cause: unknown) => {
        if (live.current) {
          setFailure(cause)
        }
      })
      .finally(() => {
        if (live.current) {
          setBusy(false)
        }
      })
  }

  const download = (receipt: CustomerExport) => {
    setDownloading(true)
    setFailure(null)

    void downloadCustomerExport({
      customerId,
      exportId: receipt.exportId,
      documentCode: receipt.documentCode,
    })
      .then(({ blob, fileName }) => {
        saveBlob(blob, fileName ?? `${receipt.documentCode}-${receipt.exportId}.json`)
      })
      .catch((cause: unknown) => {
        if (live.current) {
          setFailure(cause)
        }
      })
      .finally(() => {
        if (live.current) {
          setDownloading(false)
        }
      })
  }

  const gone = failure instanceof ApiError && failure.code === CUSTOMER_EXPORT_EXPIRED_CODE

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
        <AuthProblemAlert failure={asking ? null : failure} />
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
            setFailure(null)
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
            setFailure(null)
          }}
          onConfirm={(outcome) => {
            generate((outcome.reason ?? '').trim())
          }}
          open
          problem={<AuthProblemAlert failure={failure} />}
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

/** What was generated, in terms somebody can repeat to the person who asked for it. */
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

      {online ? (
        <Button busy={busy} iconName="clipboard" onClick={onDownload} variant="secondary">
          {intl.formatMessage({ id: 'customers.export.download' })}
        </Button>
      ) : (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'customers.export.offlineDownload' })}
        />
      )}
    </Alert>
  )
}

/** Through the shared formatters, never at the call site. */
function formatWhen(value: string, intl: IntlShape): string {
  const formatters = isSupportedLocale(intl.locale) ? getFormatters(intl.locale) : getFormatters()
  return formatters.formatDateTime(value)
}
