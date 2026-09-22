import { useEffect, useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useLocation, useSearchParams } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { Card } from '../../components/primitives/Card'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { StatusBadge } from '../../components/primitives/StatusBadge'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { TextField } from '../../design-system/components/forms/TextField'
import { CUSTOMER_SEARCH_MINIMUM_LENGTH, searchCustomers } from '../../customers/customersApi'
import { customerStatusKind } from '../../customers/customerStatus'
import type { CustomerCard, CustomerPage } from '../../customers/types'
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

  /*
   * The search that has actually been made lives in the address, not in this component.
   *
   * Two reasons, and the second is the one that made it necessary. A search is a thing somebody
   * should be able to reload, bookmark and send to a colleague — that is ordinary. But this screen
   * is also the list pane of `CustomersLayoutRoute`, and when the panes cannot both fit,
   * `MasterDetail` takes the list *out of the DOM* while the record is open. Anything held in
   * component state dies there, so on a phone every record somebody opened used to cost them their
   * search. The address survives the unmount; state does not.
   */
  const [params, setParams] = useSearchParams()
  const committed = params.get('term') ?? ''
  // In the address beside the term, and for the same reason: this pane is unmounted when the record
  // takes the screen, and a filter that reset itself on the way back would quietly change what the
  // next search means.
  const withdrawn = params.get('withdrawn') === 'true'

  const [term, setTerm] = useState(committed)
  const [includeWithdrawn, setIncludeWithdrawn] = useState(withdrawn)
  const [tooShort, setTooShort] = useState(false)
  /**
   * The answer, tagged with the term it answers.
   *
   * One piece of state rather than four, for `useAdminResource`'s reason: nothing is set
   * synchronously in the effect body, so there is no cascading render on the way in, and "is it
   * searching" is *derived* from whether the answer on hand is the one the address is asking for
   * rather than tracked as a state that can disagree with it.
   */
  const [answer, setAnswer] = useState<{
    readonly term: string
    readonly withdrawn: boolean
    readonly page: CustomerPage | null
    readonly failure: unknown
  } | null>(null)

  /**
   * Writes the question into the address. The effect below is what asks it, so a reload or a remount
   * asks the same question rather than showing an empty screen.
   */
  const ask = (wanted: string, withDeactivated: boolean) => {
    setParams(withDeactivated ? { term: wanted, withdrawn: 'true' } : { term: wanted }, {
      replace: true,
    })
  }

  const submit = () => {
    const wanted = term.trim()
    if (wanted.length < CUSTOMER_SEARCH_MINIMUM_LENGTH) {
      setTooShort(true)
      return
    }
    setTooShort(false)
    ask(wanted, includeWithdrawn)
  }

  /*
   * Turning the filter on re-asks at once, when there is a question to re-ask.
   *
   * Leaving it until the next press of Search would put a ticked box above results that were
   * fetched without it — the screen saying one thing and showing another. The reading somebody takes
   * from that is "she is not here", which is the one conclusion this filter exists to prevent.
   */
  const toggleDeactivated = (on: boolean) => {
    setIncludeWithdrawn(on)
    if (committed.length >= CUSTOMER_SEARCH_MINIMUM_LENGTH) {
      ask(committed, on)
    }
  }

  // The read, once per committed term, cancelled if the term changes or the pane goes away. Written
  // here rather than through `useAdminResource` because this one is conditional: a term shorter than
  // the minimum is not a request, and a hook that always reads would have to be told to lie.
  useEffect(() => {
    if (committed.length < CUSTOMER_SEARCH_MINIMUM_LENGTH) {
      return
    }

    const controller = new AbortController()
    let cancelled = false

    void searchCustomers(committed, { includeDeactivated: withdrawn, signal: controller.signal })
      .then((page) => {
        if (!cancelled) {
          setAnswer({ term: committed, withdrawn, page, failure: null })
        }
      })
      .catch((cause: unknown) => {
        if (!cancelled && !(cause instanceof DOMException && cause.name === 'AbortError')) {
          setAnswer({ term: committed, withdrawn, page: null, failure: cause })
        }
      })

    return () => {
      cancelled = true
      controller.abort()
    }
  }, [committed, withdrawn])

  const asked = committed.length >= CUSTOMER_SEARCH_MINIMUM_LENGTH
  const current =
    answer !== null && answer.term === committed && answer.withdrawn === withdrawn ? answer : null
  const searching = asked && current === null
  const failure = current?.failure ?? null
  const results = current?.page?.customers ?? null
  const truncated = current?.page?.nextCursor !== undefined && current?.page?.nextCursor !== null

  return (
    <section className="page customers">
      {/*
        An `h2`: this is a pane of the customers screen, not a page of its own, and
        `CustomersLayoutRoute` owns the `h1` so that the outline is the same whether or not the
        record is beside it (#616).
      */}
      <h2>
        <FormattedMessage id="customers.search.title" />
      </h2>
      <p className="customers__lede">
        <FormattedMessage id="customers.search.body" />
      </p>

      <form
        className="customers__search"
        noValidate
        onSubmit={(event) => {
          event.preventDefault()
          submit()
        }}
      >
        {/*
          No `inputMode`, deliberately, and #614 records the decision as still open.

          This field matches a name, a native-script name, a customer number *or* the tail of a
          telephone number — which is what the hint above it says. #182 asks for it to be "phone
          keypad optimised", and a `tel` keypad on a field whose commonest input is a name would make
          most searches worse to serve the one that the server already makes cheap by matching a
          partial number. `autoComplete` stays off because this is a shared counter device.

          That is a reading of #182, not an answer to it, so the question and its three options are
          recorded in docs/prd/assumptions-and-open-decisions.md section 4 (#620). Alphabetic stands
          until somebody decides; change this field when that row changes, not before.
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
        {/*
          Off by default, matching the server: a search is nearly always somebody starting a new
          order, and a withdrawn record is exactly the one not to offer for that. It is here at all
          because a record nobody can find is a record nobody can put back — withdrawing one would
          otherwise be a one-way door with a button labelled as if it were not.
        */}
        <Checkbox
          description={intl.formatMessage({ id: 'customers.search.withdrawnHint' })}
          id="customer-search-withdrawn"
          label={intl.formatMessage({ id: 'customers.search.withdrawn' })}
          name="withdrawn"
          onValueChange={toggleDeactivated}
          value={includeWithdrawn}
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
            want the document outline to say what this section is, and an h3 here is what lets each
            result render as an h4 without skipping a level — the create-card below is the only
            other h3 in this pane, so the two read as siblings under the pane's own h2.
          */}
          <h3>
            <FormattedMessage id="customers.search.results" />
          </h3>
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
        headingLevel={3}
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
  /*
   * The record's address keeps the search that found it.
   *
   * Without this, opening a result navigates to a bare `/customers/<id>`, the committed term goes
   * with it and the list beside the record empties — which is the same failure as losing the search
   * on a phone, wearing a different hat. The search is in the address, so every link that stays on
   * this screen has to carry it.
   */
  const { search } = useLocation()

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
      title={<Link to={{ pathname: `/customers/${card.customerId}`, search }}>{label}</Link>}
      headingLevel={4}
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
