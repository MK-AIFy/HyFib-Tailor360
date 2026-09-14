import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useSearchParams } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import type { VersionedResponse } from '../../auth/apiClient'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { createInvoiceDraft } from '../../billing/billingApi'
import { InvoiceDocumentView } from '../../billing/InvoiceDocumentView'
import type { Invoice } from '../../billing/types'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { NetworkStatusBanner } from '../../components/states/NetworkStatusBanner'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import './billing.css'

/**
 * Raising an invoice for an order (#345): reads `orderId` and `calculation` from the query string,
 * drafts the invoice at once, and shows the server's own lines and totals for review before the
 * person opens it to post or discard.
 *
 * Nothing here re-prices anything — the lines shown are the calculation's, exactly as
 * `CreateInvoiceDraft` returns them.
 *
 * ## Why the two parameters are read from the query string rather than picked on screen
 *
 * Orders' job-card screen (#204) is where a real "Raise invoice" link will be placed, once #201
 * stores a calculation snapshot for a confirmed order to draft from. Neither exists yet, so this
 * route is reached by its parameters until then — built by hand today, by a link tomorrow.
 */
export function InvoiceDraftRoute() {
  const intl = useIntl()
  const [params] = useSearchParams()
  const orderId = params.get('orderId') ?? ''
  const calculationReference = params.get('calculation') ?? ''

  if (orderId === '' || calculationReference === '') {
    return (
      <section className="page billing">
        <h1>{intl.formatMessage({ id: 'billing.invoice.draft.title' })}</h1>
        <EmptyState
          iconName="receipt"
          title={intl.formatMessage({ id: 'billing.invoice.draft.missingParams.title' })}
        >
          {intl.formatMessage({ id: 'billing.invoice.draft.missingParams' })}
        </EmptyState>
      </section>
    )
  }

  return <InvoiceDraft calculationReference={calculationReference} orderId={orderId} />
}

/** What one attempt to draft resolved to. `skipped` never reached the network at all. */
type DraftAttempt =
  | { readonly kind: 'skipped' }
  | { readonly kind: 'created'; readonly response: VersionedResponse<Invoice> }

function InvoiceDraft({
  orderId,
  calculationReference,
}: {
  readonly orderId: string
  readonly calculationReference: string
}) {
  const intl = useIntl()
  const network = useNetworkState()

  // Minted once for the life of this screen and held across every retry of the same draft attempt:
  // a retry — including the one apiClient replays after an in-place re-authentication — must reach
  // the server as the identical request, or a slow first attempt and its retry could both succeed
  // as two drafts of the same order.
  const [idempotencyKey] = useState(() => crypto.randomUUID())

  const resource = useAdminResource<DraftAttempt>(
    `invoice-draft:${orderId}:${calculationReference}:${network.online ? 'on' : 'off'}`,
    async (signal): Promise<DraftAttempt> => {
      // Billing is online-only (clients/pwa/CLAUDE.md section 6): the read itself must never reach
      // the network while offline, not merely hide a button that would have. Encoding `network.online`
      // in the key above is what makes this run again, with the same key, the moment it changes.
      if (!network.online) {
        return { kind: 'skipped' }
      }
      const response = await createInvoiceDraft({
        body: { orderId, calculationReference, garmentJobIds: null, reason: null },
        idempotencyKey,
        signal,
      })
      return { kind: 'created', response }
    },
  )

  const created = resource.value?.kind === 'created' ? resource.value.response : null

  return (
    <section className="page billing">
      <h1>{intl.formatMessage({ id: 'billing.invoice.draft.title' })}</h1>

      <NetworkStatusBanner />

      {!network.online ? (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'billing.invoice.draft.offlineAction' })}
        />
      ) : resource.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'billing.invoice.draft.creating' })} />
      ) : resource.failure !== null ? (
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
      ) : created !== null ? (
        <section aria-labelledby="invoice-draft-review-heading">
          <h2 id="invoice-draft-review-heading">
            <FormattedMessage id="billing.invoice.draft.review" />
          </h2>
          <InvoiceDocumentView invoice={created.value} />
          <Link to={`/billing/invoices/${created.value.invoiceId}`}>
            {intl.formatMessage({ id: 'billing.invoice.draft.open' })}
          </Link>
        </section>
      ) : null}
    </section>
  )
}
