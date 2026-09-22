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
import { SegmentedControl } from '../../design-system/components/forms/SegmentedControl'
import { TextField } from '../../design-system/components/forms/TextField'
import {
  CUSTOMER_SEARCH_MINIMUM_LENGTH,
  CUSTOMER_SEARCH_TERM_TOO_SHORT_CODE,
  searchCustomers,
} from '../../customers/customersApi'
import { customerStatusKind } from '../../customers/customerStatus'
import type { CustomerCard, CustomerPage } from '../../customers/types'

/**
 * Which keyboard the search field asks for.
 *
 * Specified by the plan's `#26 [E04-F01]` blueprint and by docs/prd/exceptions.md section 4.1,
 * where the segmented mode is part of *duplicate prevention*: Reception who cannot type a number
 * quickly searches less, and a search not made is how the same person gets registered twice.
 */
type SearchMode = 'phone' | 'name'
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
  /*
   * Which keyboard the field asks for, in the address for the same two reasons as the filter.
   *
   * It is *not* part of the question. The endpoint matches a name, a native-script name, a customer
   * number or the tail of a telephone number whichever mode is showing, so switching raises a
   * different keyboard and changes nothing about what is asked — which is why switching does not
   * re-run the search and cannot leave results disagreeing with the control above them.
   *
   * Defaults to `name`, which is exactly what this field did before the modes existed. Reception
   * starting from a telephone number is plausibly the commoner case at a counter, and #629 records
   * that as the open question rather than settling it here: a default that silently changes the
   * keyboard for everybody is a product decision, not a client one.
   */
  const mode: SearchMode = params.get('mode') === 'phone' ? 'phone' : 'name'

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
    setParams(
      {
        term: wanted,
        ...(withDeactivated ? { withdrawn: 'true' } : {}),
        // Carried rather than rebuilt: a search must not silently drop back to the other keyboard.
        ...(mode === 'phone' ? { mode: 'phone' } : {}),
      },
      { replace: true },
    )
  }

  /*
   * Switching the keyboard, and nothing else.
   *
   * The term and the filter are written back unchanged because `setParams` replaces the whole
   * query, and because losing a typed term to a keyboard change would be its own small betrayal.
   * No search is re-run: the mode is not part of the question.
   */
  const chooseMode = (next: string) => {
    setParams(
      {
        ...(committed.length > 0 ? { term: committed } : {}),
        ...(withdrawn ? { withdrawn: 'true' } : {}),
        ...(next === 'phone' ? { mode: 'phone' } : {}),
      },
      { replace: true },
    )
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
    /*
     * Re-asks what is *in the box*, not what was last committed.
     *
     * Those are the same thing until somebody edits the field without pressing Search, and then
     * they are not: re-running the committed term would put results for the old question under the
     * new one, which is the same "the screen says one thing and shows another" failure this
     * immediate re-ask exists to prevent, only harder to spot because the box looks right.
     */
    const wanted = term.trim()

    // Nothing typed and nothing asked: no question to re-ask and no results to disagree with, so
    // the box simply moves. Complaining about the length of a term nobody has entered would be noise.
    if (wanted.length === 0 && committed.length === 0) {
      setIncludeWithdrawn(on)
      return
    }

    /*
     * The box holds something this search will not run, so the filter stays where it is.
     *
     * Moving it and stopping would leave a ticked "Include deactivated records" above results that
     * were fetched without it — and somebody reads that as "she is deactivated and still not here",
     * which is the one conclusion this filter exists to prevent. The error at the field says why
     * nothing happened; a checkbox that silently disagrees with the list below it would not.
     */
    if (wanted.length < CUSTOMER_SEARCH_MINIMUM_LENGTH) {
      setTooShort(true)
      return
    }

    setIncludeWithdrawn(on)
    setTooShort(false)
    ask(wanted, on)
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
  /*
   * Why the page is empty, when it is empty for a reason other than nobody matching.
   *
   * The page still answers 200 with no results — that is the whole point of carrying the reason in
   * a field — so without reading this the screen would say "Nobody matched", which is a different
   * and wrong answer.
   */
  const refusal = current?.page?.refusal ?? null

  /*
   * The server saying the same thing the field pre-checks.
   *
   * It should not happen, because the form does not submit a term this short. It is reachable from
   * a pasted or bookmarked address, and it is what a drift between the two minimums would look
   * like. Rendering it as the field's own error rather than as a bare empty state is what makes
   * #182's criterion A true: the refusal reads the same wherever it came from, and it points at the
   * field the person has to change.
   */
  const refusedAsTooShort = refusal === CUSTOMER_SEARCH_TERM_TOO_SHORT_CODE

  /*
   * An address carrying a term too short to run — `/customers?term=ab`, pasted or bookmarked.
   *
   * The effect above deliberately does not ask the server for one of these, so no page comes back
   * to carry a refusal, and `tooShort` is false because nobody submitted the form. Without this the
   * screen shows the term in the box, no results and no reason: the one state a search screen must
   * never be in, because "no reason" is read as "no such person".
   *
   * Derived rather than stored, so it follows the address on a reload or a remount instead of
   * depending on somebody having pressed a button earlier in the session.
   *
   * It reads the *field* as well as the address, so that typing a valid term clears it and emptying
   * the box clears it too. Reading the address alone left "Type at least 3 characters" standing
   * against a value that no longer deserved it — an error a person cannot get rid of by fixing what
   * it complains about teaches them to ignore errors.
   */
  const committedIsTooShort =
    committed.length > 0 &&
    committed.length < CUSTOMER_SEARCH_MINIMUM_LENGTH &&
    term.trim().length > 0 &&
    term.trim().length < CUSTOMER_SEARCH_MINIMUM_LENGTH

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
          No `inputMode`, deliberately — see below for why, which is not the reason it once said.

          This field matches a name, a native-script name, a customer number *or* the tail of a
          telephone number — which is what the hint above it says.

          It is **one field, and the specification asks for two.** The plan's
          `### #26 [E04-F01] Customer profiles, consent, search, deduplication, timeline` blueprint
          (docs/IMPLEMENTATION_PLAN.md, from line 1215 — not section 4.6, which is general client
          architecture) and docs/prd/exceptions.md section 4.1 both call for a segmented Phone /
          Name mode: the
          telephone keypad for a number, the text keyboard for a name. That is not implemented here,
          and it is a gap to build rather than a question to answer — #629 tracks it.

          Do not answer it by putting inputMode="tel" on this field. A keypad on a field whose
          commonest input is a name makes most searches worse, which is the reason the specification
          splits the modes rather than switching the keyboard on one field.

          `autoComplete` stays off because this is a shared counter device, and WCAG 1.3.5 Identify
          Input Purpose governs a person's own details, which a staff member searching for a
          customer is not entering.
        */}
        {/*
          Above the field, because it changes what the field is for. The design system's segmented
          control is a fieldset with a legend and native radios, so the group is announced before
          the first option and the arrow keys work — a wedge scanner is a keyboard, and so is
          somebody's thumb.
        */}
        <SegmentedControl
          id="customer-search-mode"
          label={intl.formatMessage({ id: 'customers.search.mode.label' })}
          name="mode"
          onValueChange={chooseMode}
          options={[
            { value: 'name', label: intl.formatMessage({ id: 'customers.search.mode.name' }) },
            { value: 'phone', label: intl.formatMessage({ id: 'customers.search.mode.phone' }) },
          ]}
          value={mode}
        />

        <TextField
          autoComplete="off"
          description={intl.formatMessage(
            { id: 'customers.search.hint' },
            { minimum: CUSTOMER_SEARCH_MINIMUM_LENGTH },
          )}
          enterKeyHint="search"
          /*
            The whole point of the mode, and the reason it is a mode rather than an `inputMode` on
            one field: a keypad is right for a number and wrong for a name, so the screen asks which
            before it decides. `type` stays `search` in both — it is still a search field, and
            changing the type would change the clear affordance under the person's thumb.
          */
          {...(mode === 'phone' ? { inputMode: 'tel' as const } : {})}
          {...(tooShort || refusedAsTooShort || committedIsTooShort
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
      ) : results === null ? null : refusedAsTooShort ? null : refusal !== null ? ( // Said at the field, where the person can act on it. Saying it twice would be worse.
        /*
         * A refusal this build has never heard of — the field is open-ended, so the server may add
         * one without that being a breaking change.
         *
         * "Nobody matched" would be a wrong answer: the reason is unknown, which is not the same as
         * knowing there is nobody. Rendering nothing at all would be the other wrong answer, and the
         * one this screen has already been bitten by twice — a blank area with a term still in the
         * box reads as "no such person" just as loudly as the sentence does. So it says the true
         * thing: the search did not run, and this version cannot say why.
         */
        <EmptyState iconName="alert-triangle" live="polite">
          {intl.formatMessage({ id: 'customers.search.refusedUnknown' })}
        </EmptyState>
      ) : results.length === 0 ? (
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
