import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useNavigate } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ApiError } from '../../auth/apiClient'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { Card } from '../../components/primitives/Card'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { Select } from '../../design-system/components/forms/Select'
import { TextField } from '../../design-system/components/forms/TextField'
import { readDuplicateCandidates, registerCustomer } from '../../customers/customersApi'
import type { CustomerDetailsInput, DuplicateCandidate } from '../../customers/types'
import './customers.css'

/**
 * Registering a customer, and the duplicate check in between (#26, #182).
 *
 * ## Why the duplicate candidates are read off the failure, not returned as a value
 *
 * The server answers this exact request with a 409 — it is not a separate question asked first and
 * a create asked second, because the candidates depend on the details as typed, and a client-side
 * pre-check would drift from the server's own scoring the moment either one changes. So the create
 * call is made once, and a 409 is not treated as failure so much as an answer that needs a decision
 * before it can be repeated.
 *
 * ## Why the retry after "reviewed" mints a new idempotency key
 *
 * Presenting the candidates and being told to create anyway is a different decision from the one
 * that was refused, not a retry of it — `duplicatesReviewed` changes the body, and reusing the first
 * key would ask the server to treat two different decisions as one request, which it (correctly)
 * refuses as key reuse. Only a byte-identical resend — the person pressing the button again after a
 * timeout, having decided nothing new — reuses a key, and that is the ordinary case this form never
 * has to handle specially: it is what `apiClient` already does for a request already in flight.
 */
export function CustomerCreateRoute() {
  const intl = useIntl()
  const navigate = useNavigate()
  const network = useNetworkState()

  const [details, setDetails] = useState<CustomerDetailsInput>({ language: 'en-IN' })
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [candidates, setCandidates] = useState<readonly DuplicateCandidate[] | null>(null)

  const set = <K extends keyof CustomerDetailsInput>(key: K, value: string) => {
    setDetails((previous) => ({ ...previous, [key]: value === '' ? undefined : value }))
    // A field changed since the candidates were read, so the decision they were shown no longer
    // matches what would be sent. Asking again is what re-scores it against the new details.
    setCandidates(null)
  }

  const submit = async (duplicatesReviewed: boolean): Promise<void> => {
    if ((details.displayName ?? '').trim() === '') {
      setFailure(new ApiError('A name is required.', { status: 400 }))
      return
    }

    setBusy(true)
    setFailure(null)

    try {
      const registered = await registerCustomer({
        details,
        duplicatesReviewed,
        idempotencyKey: crypto.randomUUID(),
      })
      await navigate(`/customers/${registered.customer.customerId}`)
    } catch (cause: unknown) {
      const found = readDuplicateCandidates(cause)
      if (found !== null) {
        setCandidates(found)
      } else {
        setFailure(cause)
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="page customers">
      <h1>
        <FormattedMessage id="customers.create.title" />
      </h1>
      <p className="customers__lede">
        <FormattedMessage id="customers.create.body" />
      </p>

      <AuthProblemAlert failure={failure} />

      <form
        className="customers__form"
        noValidate
        onSubmit={(event) => {
          event.preventDefault()
          void submit(false)
        }}
      >
        <TextField
          autoComplete="name"
          id="customer-name"
          label={intl.formatMessage({ id: 'customers.create.field.displayName' })}
          name="displayName"
          onValueChange={(value) => {
            set('displayName', value)
          }}
          required
          value={details.displayName ?? ''}
        />
        <TextField
          autoComplete="off"
          id="customer-nativeName"
          label={intl.formatMessage({ id: 'customers.create.field.nativeName' })}
          name="nativeName"
          onValueChange={(value) => {
            set('nativeName', value)
          }}
          value={details.nativeName ?? ''}
        />
        <TextField
          autoComplete="tel"
          id="customer-phone"
          label={intl.formatMessage({ id: 'customers.create.field.phone' })}
          name="phone"
          onValueChange={(value) => {
            set('phone', value)
          }}
          type="tel"
          value={details.phone ?? ''}
        />
        <TextField
          autoComplete="off"
          id="customer-alternatePhone"
          label={intl.formatMessage({ id: 'customers.create.field.alternatePhone' })}
          name="alternatePhone"
          onValueChange={(value) => {
            set('alternatePhone', value)
          }}
          type="tel"
          value={details.alternatePhone ?? ''}
        />
        <TextField
          autoComplete="email"
          id="customer-email"
          label={intl.formatMessage({ id: 'customers.create.field.email' })}
          name="email"
          onValueChange={(value) => {
            set('email', value)
          }}
          type="email"
          value={details.email ?? ''}
        />
        <TextField
          autoComplete="street-address"
          id="customer-addressLine"
          label={intl.formatMessage({ id: 'customers.create.field.addressLine' })}
          name="addressLine"
          onValueChange={(value) => {
            set('addressLine', value)
          }}
          value={details.addressLine ?? ''}
        />
        <TextField
          autoComplete="address-level2"
          id="customer-locality"
          label={intl.formatMessage({ id: 'customers.create.field.locality' })}
          name="locality"
          onValueChange={(value) => {
            set('locality', value)
          }}
          value={details.locality ?? ''}
        />
        <TextField
          autoComplete="postal-code"
          id="customer-postcode"
          label={intl.formatMessage({ id: 'customers.create.field.postcode' })}
          name="postcode"
          onValueChange={(value) => {
            set('postcode', value)
          }}
          value={details.postcode ?? ''}
        />
        <Select
          id="customer-language"
          emptyLabel={null}
          label={intl.formatMessage({ id: 'customers.create.field.language' })}
          name="language"
          onValueChange={(value) => {
            set('language', value)
          }}
          options={[
            {
              value: 'en-IN',
              label: intl.formatMessage({ id: 'customers.create.field.language.en-IN' }),
            },
            {
              value: 'ta-IN',
              label: intl.formatMessage({ id: 'customers.create.field.language.ta-IN' }),
            },
          ]}
          value={details.language ?? 'en-IN'}
        />

        {candidates === null ? null : (
          <DuplicateReview
            busy={busy}
            candidates={candidates}
            onCreateAnyway={() => {
              void submit(true)
            }}
          />
        )}

        {network.online ? (
          <Button busy={busy} iconName="plus" size="primary" type="submit" variant="primary">
            {intl.formatMessage({
              id: busy ? 'customers.create.saving' : 'customers.create.action',
            })}
          </Button>
        ) : (
          <OfflineBlockedAction
            action={intl.formatMessage({ id: 'customers.create.offlineAction' })}
          />
        )}
      </form>
    </section>
  )
}

function DuplicateReview({
  candidates,
  busy,
  onCreateAnyway,
}: {
  readonly candidates: readonly DuplicateCandidate[]
  readonly busy: boolean
  readonly onCreateAnyway: () => void
}) {
  const intl = useIntl()

  return (
    <Alert
      live="assertive"
      tone="warning"
      title={intl.formatMessage({ id: 'customers.create.duplicates.title' })}
    >
      <p>
        <FormattedMessage id="customers.create.duplicates.body" />
      </p>

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
            <FormattedMessage id="customers.create.duplicates.open" />
          </Link>
        </Card>
      ))}

      <Button busy={busy} onClick={onCreateAnyway} type="button" variant="secondary">
        {intl.formatMessage({ id: 'customers.create.duplicates.reviewed' })}
      </Button>
      <p className="customers__duplicatesHint">
        <FormattedMessage id="customers.create.duplicates.reviewedHint" />
      </p>
    </Alert>
  )
}
