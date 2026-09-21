import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { Card } from '../../components/primitives/Card'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { StatusBadge } from '../../components/primitives/StatusBadge'
import { TextField } from '../../design-system/components/forms/TextField'
import { CUSTOMER_SEARCH_MINIMUM_LENGTH, searchCustomers } from '../../customers/customersApi'
import { customerStatusKind } from '../../customers/customerStatus'
import type { CustomerCard } from '../../customers/types'
import './customers.css'

/**
 * Finding a customer before creating one (#26, #182).
 *
 * ## Why this is not a picker
 *
 * `MeasurementStartRoute` already searches customers, as a step inside a bigger flow, and answers
 * with a radio choice. This screen is the destination itself: a result opens the customer's own
 * record, and "none of these" is a first-class action rather than a fallback nobody was shown. The
 * two do not share a component for that reason — they share only `searchCustomers`, the API call and
 * the minimum-length rule underneath it, so the two screens can never disagree about how short a
 * search may be.
 *
 * ## The masked card, and why opening it is still offered
 *
 * A result from a branch the caller is not assigned to comes back with `visibleToCaller: false` — a
 * masked disambiguation card, enough to tell two people apart and not enough to be a contact list.
 * It is still a real link: opening it is what `customers.customer.opened-at-branch` records, adding
 * the caller's branch to the record's visibility, which is the server-side action `branch-scenarios.md`
 * section 3.1 calls "a customer served at a second branch." The screen never unmasks anything itself.
 */
export function CustomerSearchRoute() {
  const intl = useIntl()

  const [term, setTerm] = useState('')
  const [searching, setSearching] = useState(false)
  const [tooShort, setTooShort] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [results, setResults] = useState<readonly CustomerCard[] | null>(null)
  const [truncated, setTruncated] = useState(false)

  const search = async (): Promise<void> => {
    const wanted = term.trim()
    if (wanted.length < CUSTOMER_SEARCH_MINIMUM_LENGTH) {
      setTooShort(true)
      return
    }

    setTooShort(false)
    setSearching(true)
    setFailure(null)

    try {
      const page = await searchCustomers(wanted)
      setResults(page.customers)
      setTruncated(page.nextCursor !== null)
    } catch (cause: unknown) {
      setFailure(cause)
      setResults(null)
    } finally {
      setSearching(false)
    }
  }

  return (
    <section className="page customers">
      <h1>
        <FormattedMessage id="customers.search.title" />
      </h1>
      <p className="customers__lede">
        <FormattedMessage id="customers.search.body" />
      </p>

      <form
        className="customers__search"
        noValidate
        onSubmit={(event) => {
          event.preventDefault()
          void search()
        }}
      >
        {/*
          No `inputMode`, deliberately, and #614 records the decision as still open.

          This field matches a name, a native-script name, a customer number *or* the tail of a
          telephone number — which is what the hint above it says. #182 asks for it to be "phone
          keypad optimised", and a `tel` keypad on a field whose commonest input is a name would make
          most searches worse to serve the one that the server already makes cheap by matching a
          partial number. `autoComplete` stays off because this is a shared counter device.
        */}
        <TextField
          autoComplete="off"
          description={intl.formatMessage(
            { id: 'customers.search.hint' },
            { minimum: CUSTOMER_SEARCH_MINIMUM_LENGTH },
          )}
          enterKeyHint="search"
          {...(tooShort
            ? {
                error: intl.formatMessage(
                  { id: 'customers.search.tooShort' },
                  { minimum: CUSTOMER_SEARCH_MINIMUM_LENGTH },
                ),
              }
            : {})}
          id="customer-search-term"
          label={intl.formatMessage({ id: 'customers.search.label' })}
          name="term"
          onValueChange={setTerm}
          type="search"
          value={term}
        />
        <Button busy={searching} iconName="search" type="submit" variant="primary">
          {intl.formatMessage({
            id: searching ? 'customers.search.searching' : 'customers.search.action',
          })}
        </Button>
      </form>

      <AuthProblemAlert failure={failure} />

      {searching && results === null ? (
        <LoadingState what={intl.formatMessage({ id: 'customers.search.loading' })} />
      ) : results === null ? null : results.length === 0 ? (
        <EmptyState iconName="users" live="polite">
          {intl.formatMessage({ id: 'customers.search.empty' })}
        </EmptyState>
      ) : (
        <>
          {/*
            A real heading, not an aria-label on the list: 1.3.1 and the heading-order rule both
            want the document outline to say what this section is, and an h2 here is what lets each
            result render as an h3 without skipping a level — the create-card below is the only
            other h2 on the screen, so the two read as siblings under the page's own h1.
          */}
          <h2>
            <FormattedMessage id="customers.search.results" />
          </h2>
          <div className="customers__results" role="list">
            {results.map((card) => (
              <div key={card.customerId} role="listitem">
                <ResultCard card={card} />
              </div>
            ))}
          </div>
          {truncated ? (
            <Alert live="polite" tone="info">
              <FormattedMessage id="customers.search.more" />
            </Alert>
          ) : null}
        </>
      )}

      <Card
        className="customers__createCard"
        title={
          <Link to="/customers/new">
            {intl.formatMessage({ id: 'customers.search.createNew' })}
          </Link>
        }
        headingLevel={2}
      >
        <p>
          <FormattedMessage id="customers.search.createNewHint" />
        </p>
      </Card>
    </section>
  )
}

function ResultCard({ card }: { readonly card: CustomerCard }) {
  const intl = useIntl()

  const label = card.visibleToCaller
    ? intl.formatMessage(
        { id: 'customers.search.card' },
        { name: card.displayName, number: card.customerNumber, phone: card.maskedPhone },
      )
    : intl.formatMessage(
        { id: 'customers.search.masked' },
        { name: card.displayName, number: card.customerNumber },
      )

  return (
    <Card
      title={<Link to={`/customers/${card.customerId}`}>{label}</Link>}
      headingLevel={3}
      meta={<StatusBadge status={customerStatusKind(card.status)} />}
    >
      {card.visibleToCaller ? null : (
        <p className="customers__hint">
          <FormattedMessage id="customers.search.maskedHint" />
        </p>
      )}
    </Card>
  )
}
