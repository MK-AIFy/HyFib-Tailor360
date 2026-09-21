import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useParams } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ApiError } from '../../auth/apiClient'
import { useSession } from '../../auth/useSession'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { Card } from '../../components/primitives/Card'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { useAdminResource } from '../../admin/useAdminResource'
import { mergeCustomers, readCustomer, readDuplicateReview } from '../../customers/customersApi'
import { CUSTOMERS_PERMISSIONS } from '../../customers/customersPermissions'
import {
  CUSTOMER_MERGED_RECORD_CHANGED_CODE,
  CUSTOMER_VERSION_CONFLICT_CODE,
} from '../../customers/types'
import type { CustomerMergeOutcome, DuplicateCandidate } from '../../customers/types'
import './customers.css'

/**
 * Reviewing who might be the same person, and folding one record into another (#26, #182, #584).
 *
 * This is the only irreversible operation on a customer record, and every decision below follows
 * from that one fact rather than from taste.
 *
 * ## The direction is stated, never implied
 *
 * The record in the address **survives**; a card is folded *into* it. That is the server's shape —
 * the survivor is the path and the folded record is the body, deliberately, so that a precondition
 * can never be sent for one record and a merge performed on another — and it is the single thing a
 * reader must not have to infer. So the survivor is named in the heading, named again in every
 * button, and named a third time in the confirmation, and the phrase typed to confirm is the
 * *folded* record's customer number: somebody who has not read which record they are destroying
 * cannot type it.
 *
 * ## Both records are read immediately before the merge, not when the list was drawn
 *
 * The endpoint takes two preconditions: `If-Match` for the survivor and `mergedCustomerVersion` for
 * the record being folded in. What a manager approves is a pair, and an `If-Match` alone protects
 * only the half that carries on. So the candidate's version is fetched when the merge is confirmed
 * rather than when the list was drawn, which keeps the window between reading a record and
 * destroying it as short as this screen can make it.
 *
 * The two halves refuse differently and are never treated alike: a stale survivor is
 * `customers.version-conflict` and sends the reader back to the survivor, a stale folded record is
 * `customers.merged-record-changed` and sends them to the other one. Rendering both as "something
 * changed" would tell somebody to go and look at the wrong record.
 *
 * ## Reading is Reception's, merging is a manager's
 *
 * The duplicates list is gated on `customers.read`, because preparing the decision is what Reception
 * does before asking somebody to take it. The merge control appears only for `customers.merge`, and
 * the server additionally demands a fresh proof of identity — which `apiClient` answers in place, so
 * this screen never raises a second dialog over its own confirmation.
 */
export function CustomerMergeRoute() {
  const intl = useIntl()
  const network = useNetworkState()
  const { user } = useSession()
  const { customerId = '' } = useParams()

  const survivor = useAdminResource(customerId, (signal) => readCustomer(customerId, signal))
  const review = useAdminResource(customerId, (signal) => readDuplicateReview(customerId, signal))

  const [chosen, setChosen] = useState<DuplicateCandidate | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [outcome, setOutcome] = useState<CustomerMergeOutcome | null>(null)
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID())

  const record = survivor.value?.value ?? null
  const version = survivor.value?.version

  if (record === null && survivor.loading) {
    return <LoadingState what={intl.formatMessage({ id: 'customers.detail.loading' })} />
  }

  if (record === null || version === undefined) {
    return (
      <>
        <AuthProblemAlert failure={survivor.failure} />
        <EmptyState iconName="users" live="polite">
          {intl.formatMessage({ id: 'customers.detail.notFound' })}
        </EmptyState>
      </>
    )
  }

  const mayMerge = user?.permissions.includes(CUSTOMERS_PERMISSIONS.merge) === true
  const candidates = review.value?.candidates ?? []

  const merge = (candidate: DuplicateCandidate, reason: string) => {
    setBusy(true)
    setFailure(null)

    // The folded record's version is read here rather than carried from the list, so the pair being
    // merged is the pair as it stands at the moment of the decision. See the note above.
    void readCustomer(candidate.customer.customerId)
      .then(async (folded) => {
        if (folded.version === undefined) {
          /*
           * No `ETag` on the record about to be destroyed. `mergedCustomerVersion` is required and
           * must be a concrete version — `*` is refused by the server precisely because there is no
           * such thing as "any version" of a record somebody approved destroying — so there is
           * nothing safe to send. Refusing here, rather than sending an empty precondition and
           * letting the server decide, is the difference between an explained stop and a 400 that
           * reads like a bug on the last screen anybody wants one on.
           */
          throw new ApiError('The folded record carried no version.', { status: 428 })
        }

        return await mergeCustomers({
          customerId,
          mergedCustomerId: candidate.customer.customerId,
          mergedCustomerVersion: folded.version,
          reason,
          version,
          idempotencyKey,
        })
      })
      .then((result) => {
        setOutcome(result)
        setChosen(null)
        // A new key for the next, separate decision — not for a retry of this one, which must
        // replay rather than merge a second pair.
        setIdempotencyKey(crypto.randomUUID())
        survivor.reload()
        review.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  return (
    <section className="page customers">
      <p>
        <Link to={`/customers/${customerId}`}>
          <FormattedMessage id="customers.edit.back" />
        </Link>
      </p>

      <h1>
        <FormattedMessage id="customers.merge.title" values={{ name: record.displayName }} />
      </h1>
      <p className="customers__lede">
        <FormattedMessage
          id="customers.merge.body"
          values={{ name: record.displayName, number: record.customerNumber }}
        />
      </p>

      {outcome === null ? null : <MergeOutcome outcome={outcome} />}

      {chosen === null ? <AuthProblemAlert failure={failure} /> : null}

      {review.value === null && review.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'customers.merge.loading' })} />
      ) : null}
      <AuthProblemAlert failure={review.failure} />

      {review.value !== null && candidates.length === 0 ? (
        <EmptyState iconName="users" live="polite">
          {intl.formatMessage({ id: 'customers.merge.empty' })}
        </EmptyState>
      ) : null}

      {candidates.map((candidate) => (
        <Card
          key={candidate.customer.customerId}
          headingLevel={2}
          title={candidate.customer.displayName}
          meta={intl.formatMessage({
            id: `customers.create.duplicates.confidence.${candidate.confidence}`,
          })}
        >
          <p>{candidate.customer.customerNumber}</p>
          <ul>
            {candidate.reasons.map((reason) => (
              <li key={reason}>{reason}</li>
            ))}
          </ul>
          <Link to={`/customers/${candidate.customer.customerId}`}>
            <FormattedMessage id="customers.merge.open" />
          </Link>

          {!mayMerge ? null : network.online ? (
            <Button
              iconName="users"
              onClick={() => {
                setFailure(null)
                setChosen(candidate)
              }}
              variant="secondary"
            >
              {intl.formatMessage(
                { id: 'customers.merge.action' },
                { number: candidate.customer.customerNumber, name: record.displayName },
              )}
            </Button>
          ) : (
            <OfflineBlockedAction
              action={intl.formatMessage({ id: 'customers.merge.offlineAction' })}
            />
          )}
        </Card>
      ))}

      {chosen === null ? null : (
        <ConfirmDialog
          action={intl.formatMessage(
            { id: 'customers.merge.confirm.action' },
            { number: chosen.customer.customerNumber, name: record.displayName },
          )}
          busy={busy}
          confirmLabel={intl.formatMessage(
            { id: 'customers.merge.confirm.label' },
            { number: chosen.customer.customerNumber },
          )}
          irreversible
          onCancel={() => {
            setChosen(null)
            setFailure(null)
          }}
          onConfirm={(result) => {
            merge(chosen, (result.reason ?? '').trim())
          }}
          open
          problem={<MergeProblem failure={failure} />}
          tier="typed"
          title={intl.formatMessage(
            { id: 'customers.merge.confirm.title' },
            {
              merged: chosen.customer.displayName,
              survivor: record.displayName,
            },
          )}
          // The phrase is the *folded* record's number, not the survivor's: somebody who has not
          // read which record they are destroying cannot type it.
          typedPhrase={chosen.customer.customerNumber}
        >
          <FormattedMessage
            id="customers.merge.confirm.body"
            values={{
              merged: chosen.customer.displayName,
              mergedNumber: chosen.customer.customerNumber,
              survivor: record.displayName,
              survivorNumber: record.customerNumber,
            }}
          />
        </ConfirmDialog>
      )}
    </section>
  )
}

/**
 * The refusal, inside the dialog that caused it.
 *
 * The two 409s are told apart because they send the reader to different records — the survivor
 * changed, or the record about to be folded in changed — and "something changed" would send somebody
 * to look at the wrong one. Everything else falls through to the shared problem sentence.
 */
function MergeProblem({ failure }: { readonly failure: unknown }) {
  const intl = useIntl()

  if (failure instanceof ApiError && failure.code === CUSTOMER_VERSION_CONFLICT_CODE) {
    return (
      <Alert live="assertive" tone="warning">
        {intl.formatMessage({ id: 'customers.merge.conflict.survivor' })}
      </Alert>
    )
  }

  if (failure instanceof ApiError && failure.code === CUSTOMER_MERGED_RECORD_CHANGED_CODE) {
    return (
      <Alert live="assertive" tone="warning">
        {intl.formatMessage({ id: 'customers.merge.conflict.merged' })}
      </Alert>
    )
  }

  return <AuthProblemAlert failure={failure} />
}

/** What the merge actually did, in numbers somebody can check against what they expected. */
function MergeOutcome({ outcome }: { readonly outcome: CustomerMergeOutcome }) {
  const intl = useIntl()

  return (
    <Alert
      live="polite"
      tone="success"
      title={intl.formatMessage({ id: 'customers.merge.done.title' })}
    >
      <p>
        <FormattedMessage
          id="customers.merge.done.body"
          values={{
            number: outcome.mergedCustomerNumber,
            survivor: outcome.customer.displayName,
          }}
        />
      </p>
      <ul>
        <li>
          <FormattedMessage
            id="customers.merge.done.aliases"
            values={{ count: outcome.aliasesRecorded }}
          />
        </li>
        <li>
          <FormattedMessage
            id="customers.merge.done.repointed"
            values={{ count: outcome.recordsRepointed }}
          />
        </li>
        <li>
          <FormattedMessage
            id="customers.merge.done.branches"
            values={{ count: outcome.visibilityBranchesAdded }}
          />
        </li>
      </ul>
    </Alert>
  )
}
