import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { useNavigate, useSearchParams } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { useAdminResource } from '../../admin/useAdminResource'
import { readCurrentCatalog } from '../../catalog/catalogApi'
import type { OrderableService } from '../../catalog/types'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { RadioGroup } from '../../design-system/components/forms/RadioGroup'
import { Select } from '../../design-system/components/forms/Select'
import { TextField } from '../../design-system/components/forms/TextField'
import { CUSTOMER_SEARCH_MINIMUM_LENGTH, searchCustomers } from '../../customers/customersApi'
import type { CustomerCard } from '../../customers/types'
import { MeasurementProblemAlert } from '../../measurements/MeasurementProblemAlert'
import { startMeasurementDraft } from '../../measurements/measurementsApi'
import './measurements.css'

/**
 * Choosing whom and what to measure, then starting (#123).
 *
 * ## Why the garment is chosen from the catalogue and not from the template list
 *
 * A counter does not hold the template-administration permission and should not need to: what it
 * knows is that the customer wants a blouse. The orderable catalogue (`GET /api/v1/catalog/current`)
 * says which published template each service is measured against, so the choice is the garment and
 * the template follows from it. A service with no template is not offered, because starting a draft
 * against nothing is a refusal the person would only discover after choosing.
 *
 * ## Why starting holds a retry key
 *
 * Starting a draft is a command, and a lost answer retried without a key would ask the server twice.
 * The server would answer with the same open draft either way — a branch has one per customer and
 * template — but the key is what makes that a replay rather than a coincidence.
 *
 * ## The customer may already be chosen
 *
 * Intake (#32b) and the customer record will arrive here with `?customerId=`; the screen honours
 * it and says so, while leaving the search available for a different person.
 */
export function MeasurementStartRoute() {
  const intl = useIntl()
  const navigate = useNavigate()
  const network = useNetworkState()
  const [params] = useSearchParams()

  const catalogue = useAdminResource('orderable-catalogue', (signal) => readCurrentCatalog(signal))

  const [term, setTerm] = useState('')
  const [searching, setSearching] = useState(false)
  const [searchFailure, setSearchFailure] = useState<unknown>(null)
  const [results, setResults] = useState<readonly CustomerCard[] | null>(null)
  const [truncated, setTruncated] = useState(false)
  const [tooShort, setTooShort] = useState(false)

  const [customerId, setCustomerId] = useState<string>(params.get('customerId') ?? '')
  const preselected = params.get('customerId') !== null && results === null
  const [serviceTypeId, setServiceTypeId] = useState<string>(() => {
    const wanted = params.get('templateId')
    return wanted === null ? '' : `template:${wanted}`
  })

  const [incomplete, setIncomplete] = useState(false)
  const [startKey, setStartKey] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)

  const services: readonly OrderableService[] = (catalogue.value?.services ?? []).filter(
    (service) => service.measurementTemplateId !== null,
  )

  /** A garment is chosen by service; a deep link may name the template instead. Either resolves. */
  const templateFor = (choice: string): string | null => {
    if (choice.startsWith('template:')) {
      const templateId = choice.slice('template:'.length)
      return services.some((service) => service.measurementTemplateId === templateId)
        ? templateId
        : null
    }
    return (
      services.find((service) => service.serviceTypeId === choice)?.measurementTemplateId ?? null
    )
  }

  const search = async (): Promise<void> => {
    const wanted = term.trim()
    if (wanted.length < CUSTOMER_SEARCH_MINIMUM_LENGTH) {
      setTooShort(true)
      return
    }

    setTooShort(false)
    setSearching(true)
    setSearchFailure(null)
    try {
      const page = await searchCustomers(wanted)
      setResults(page.customers)
      setTruncated(page.nextCursor !== null)
      // A choice made from the previous list does not survive a new one: the person can no longer
      // see who it was, and starting a draft for somebody not on screen is how the wrong customer
      // gets measured.
      setCustomerId('')
    } catch (cause: unknown) {
      setSearchFailure(cause)
    } finally {
      setSearching(false)
    }
  }

  const start = async (): Promise<void> => {
    const templateId = templateFor(serviceTypeId)
    if (customerId === '' || templateId === null) {
      setIncomplete(true)
      return
    }

    setIncomplete(false)
    setBusy(true)
    setFailure(null)

    // Minted when the person commits, held across a failure, forgotten on success.
    const key = startKey ?? crypto.randomUUID()
    setStartKey(key)

    try {
      const started = await startMeasurementDraft({
        body: { customerId, measurementTemplateId: templateId, reuseFromVersionId: null },
        idempotencyKey: key,
      })
      setStartKey(null)
      await navigate(`/measurements/drafts/${started.value.measurementDraftId}`)
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setBusy(false)
    }
  }

  const cardLabel = (card: CustomerCard): string =>
    card.visibleToCaller
      ? intl.formatMessage(
          { id: 'measurements.start.customer.card' },
          { name: card.displayName, number: card.customerNumber, phone: card.maskedPhone },
        )
      : intl.formatMessage(
          { id: 'measurements.start.customer.masked' },
          { name: card.displayName, number: card.customerNumber },
        )

  return (
    <section className="page measurements">
      <h1>
        <FormattedMessage id="measurements.start.title" />
      </h1>
      <p className="measurements__lede">
        <FormattedMessage id="measurements.start.body" />
      </p>

      <AuthProblemAlert failure={catalogue.failure} />

      {catalogue.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'measurements.start.catalogue.loading' })} />
      ) : catalogue.failure !== null ? null : services.length === 0 ? (
        <EmptyState
          iconName="ruler"
          live="polite"
          title={intl.formatMessage({ id: 'measurements.start.catalogue.empty.title' })}
        >
          {intl.formatMessage({ id: 'measurements.start.catalogue.empty' })}
        </EmptyState>
      ) : (
        <div className="measurements__form">
          {/*
            Its own form, so Enter — and the keyboard's Search key, which is what `enterKeyHint`
            promises — searches. Inside the start form it would have submitted Start instead.
          */}
          <form
            className="measurements__search"
            noValidate
            onSubmit={(event) => {
              event.preventDefault()
              void search()
            }}
          >
            <TextField
              autoComplete="off"
              description={intl.formatMessage(
                { id: 'measurements.start.customer.hint' },
                { minimum: CUSTOMER_SEARCH_MINIMUM_LENGTH },
              )}
              enterKeyHint="search"
              {...(tooShort
                ? {
                    error: intl.formatMessage(
                      { id: 'measurements.start.customer.tooShort' },
                      { minimum: CUSTOMER_SEARCH_MINIMUM_LENGTH },
                    ),
                  }
                : {})}
              id="capture-customer-term"
              label={intl.formatMessage({ id: 'measurements.start.customer.label' })}
              name="term"
              onValueChange={setTerm}
              type="search"
              value={term}
            />
            <Button busy={searching} iconName="search" type="submit" variant="secondary">
              {intl.formatMessage({
                id: searching
                  ? 'measurements.start.customer.searching'
                  : 'measurements.start.customer.search',
              })}
            </Button>
          </form>

          <AuthProblemAlert failure={searchFailure} />

          <form
            className="measurements__form"
            noValidate
            onSubmit={(event) => {
              event.preventDefault()
              void start()
            }}
          >
            {preselected && customerId !== '' ? (
              <Alert live="off" tone="info">
                {intl.formatMessage({ id: 'measurements.start.customer.preselected' })}
              </Alert>
            ) : null}

            {results === null ? null : results.length === 0 ? (
              <Alert live="polite" tone="warning">
                {intl.formatMessage({ id: 'measurements.start.customer.none' })}
              </Alert>
            ) : (
              <>
                <RadioGroup
                  id="capture-customer"
                  label={intl.formatMessage({ id: 'measurements.start.customer.results' })}
                  name="customerId"
                  onValueChange={setCustomerId}
                  options={results.map((card) => ({
                    value: card.customerId,
                    label: cardLabel(card),
                  }))}
                  required
                  value={customerId}
                />
                {truncated ? (
                  <Alert live="polite" tone="info">
                    {intl.formatMessage({ id: 'measurements.start.customer.more' })}
                  </Alert>
                ) : null}
              </>
            )}

            <Select
              emptyLabel={intl.formatMessage({ id: 'measurements.start.garment.choose' })}
              id="capture-garment"
              label={intl.formatMessage({ id: 'measurements.start.garment.label' })}
              name="serviceTypeId"
              onValueChange={setServiceTypeId}
              options={services.map((service) => ({
                value: service.serviceTypeId,
                label: intl.formatMessage(
                  { id: 'measurements.start.garment.option' },
                  { service: service.serviceName, category: service.categoryName },
                ),
              }))}
              required
              value={
                serviceTypeId.startsWith('template:')
                  ? (services.find(
                      (service) =>
                        service.measurementTemplateId === serviceTypeId.slice('template:'.length),
                    )?.serviceTypeId ?? '')
                  : serviceTypeId
              }
            />

            {incomplete ? (
              <Alert live="assertive" tone="danger">
                {intl.formatMessage({ id: 'measurements.start.incomplete' })}
              </Alert>
            ) : null}

            <MeasurementProblemAlert failure={failure} />

            {network.online ? (
              <>
                <p className="measurements__note">
                  {intl.formatMessage({ id: 'measurements.start.resumes' })}
                </p>
                <Button busy={busy} iconName="ruler" size="primary" type="submit" variant="primary">
                  {intl.formatMessage({
                    id: busy ? 'measurements.start.starting' : 'measurements.start.action',
                  })}
                </Button>
              </>
            ) : (
              <OfflineBlockedAction
                action={intl.formatMessage({ id: 'measurements.start.offlineAction' })}
              />
            )}
          </form>
        </div>
      )}
    </section>
  )
}
