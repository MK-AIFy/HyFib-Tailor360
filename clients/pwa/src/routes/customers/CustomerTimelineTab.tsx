import { useEffect, useRef, useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import type { IntlShape } from 'react-intl'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { Timeline } from '../../components/primitives/Timeline'
import type { TimelineEntry as TimelineRow } from '../../components/primitives/Timeline'
import type { IconName } from '../../components/primitives/icons'
import { useAdminResource } from '../../admin/useAdminResource'
import { readCustomerTimeline } from '../../customers/customersApi'
import type { CustomerTimelineEntry } from '../../customers/types'
import { getFormatters } from '../../i18n/formatters'
import { isSupportedLocale } from '../../i18n/locales'
import './customers.css'

/**
 * One customer's history, merged across every module that recorded part of it (#26, #182, #583).
 *
 * ## Why a gap in the history is louder than the history
 *
 * `unavailableSources` names a module that could not answer. It is rendered as an alert above the
 * list rather than as a footnote below it, because of what the list is used for: somebody reads a
 * customer's history to decide whether this is the third time a garment has come back. An empty
 * stretch that is actually a failed source tells them "no, it is the first", which is a worse answer
 * than no answer. So the gap is stated before anything that could be misread as completeness.
 *
 * It is `live="off"`, which is the default and is the point: the gap is known before the screen has
 * rendered, so it is read in document order like any other heading, and `Alert`'s own guidance is
 * that a live region here would announce it a second time. Being *above* the list is what makes it
 * reach a screen-reader user, not an `aria-live` attribute.
 *
 * ## Why paging is a button and never a scroll
 *
 * `nextCursor` is followed only when somebody presses "Show older". The client guide's rule is that
 * nothing re-orders or auto-advances under the reader's hands, and an audit trail is the worst place
 * to break it — an infinite scroll that loads while somebody is reading moves the entry they were
 * looking at. Pages are appended, so what has been read stays where it was read.
 *
 * ## Why `kind` chooses an icon and never a sentence
 *
 * The server sends `title` already written in the shop's words. This screen maps `kind` to an icon
 * only, and falls through to a neutral dot for one it does not recognise. Building the sentence here
 * from the dotted kind would mean a module could not add an entry type without a client release, and
 * would put the wording of a consent withdrawal in two places at once.
 */
export function CustomerTimelineTab({ customerId }: { readonly customerId: string }) {
  const intl = useIntl()

  // The pages already followed, oldest request first. The first page comes from the resource below;
  // this holds only what "Show older" has added, so a reload of the record starts the history over
  // rather than appending the same entries twice.
  const [older, setOlder] = useState<readonly CustomerTimelineEntry[]>([])
  const [cursor, setCursor] = useState<string | null>(null)
  const [followed, setFollowed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  // Empty until a page has actually been appended, so the region is mounted and *then* given its
  // text — which is the only way a polite region is announced reliably. See the note below.
  const [announcement, setAnnouncement] = useState('')

  /*
   * The request "Show older" has in flight, or null.
   *
   * It does two jobs that `busy` cannot. It is the re-entrancy guard: `busy` is state, so a second
   * call in the same tick would see the old value and start a duplicate request, and because pages
   * are *appended* a duplicate request is a permanently duplicated page rather than a flicker. And
   * it is what aborts on unmount — `Tabs` mounts only the selected panel, so switching back to the
   * record while a page is loading tears this component down mid-request. `useAdminResource` does
   * both of these for the first page; this is the same discipline for the ones after it, rather
   * than a hand-rolled fetch that quietly reintroduces what that hook exists to prevent.
   */
  const inFlight = useRef<AbortController | null>(null)

  useEffect(
    () => () => {
      inFlight.current?.abort()
      inFlight.current = null
    },
    [],
  )

  const first = useAdminResource(customerId, (signal) =>
    readCustomerTimeline({ customerId }, signal),
  )
  const page = first.value

  if (page === null && first.loading) {
    return <LoadingState what={intl.formatMessage({ id: 'customers.timeline.loading' })} />
  }

  if (page === null) {
    return <AuthProblemAlert failure={first.failure} />
  }

  const entries = [...page.entries, ...older]
  // The first page's cursor until "Show older" has been pressed, then whatever the last page gave.
  const next = followed ? cursor : page.nextCursor

  const showOlder = () => {
    if (next === null || inFlight.current !== null) {
      return
    }

    const controller = new AbortController()
    inFlight.current = controller
    setBusy(true)
    setFailure(null)

    void readCustomerTimeline({ customerId, cursor: next }, controller.signal)
      .then((result) => {
        setOlder((previous) => [...previous, ...result.entries])
        setCursor(result.nextCursor)
        setFollowed(true)
        setAnnouncement(
          intl.formatMessage(
            { id: 'customers.timeline.olderAdded' },
            { count: result.entries.length },
          ),
        )
      })
      .catch((cause: unknown) => {
        // An abort is this component being torn down, not a failure to report to somebody who is no
        // longer looking at the screen.
        if (!(cause instanceof DOMException && cause.name === 'AbortError')) {
          setFailure(cause)
        }
      })
      .finally(() => {
        if (inFlight.current === controller) {
          inFlight.current = null
        }
        setBusy(false)
      })
  }

  return (
    <div className="customers__timeline">
      {page.unavailableSources.length === 0 ? null : (
        <Alert
          live="off"
          tone="warning"
          title={intl.formatMessage({ id: 'customers.timeline.partial.title' })}
        >
          <FormattedMessage
            id="customers.timeline.partial.body"
            values={{
              count: page.unavailableSources.length,
              sources: page.unavailableSources.map((s) => sourceLabel(s, intl)).join(', '),
            }}
          />
        </Alert>
      )}

      <AuthProblemAlert failure={failure} />

      {entries.length === 0 ? (
        <EmptyState iconName="clock" live="polite">
          {intl.formatMessage({ id: 'customers.timeline.empty' })}
        </EmptyState>
      ) : (
        <Timeline
          entries={entries.map((entry) => toRow(entry, intl))}
          label={intl.formatMessage({ id: 'customers.timeline.label' })}
        />
      )}

      {/*
       * The one live region on this screen, and it is empty on first paint deliberately: a polite
       * region that mounts already holding its text is announced inconsistently, which is the same
       * reasoning `FormErrorSummary` records for not using one at all. This one is created empty and
       * given its sentence when a page is appended, which is the case `aria-live` is actually
       * reliable for — and it is the only confirmation a screen-reader user gets that "Show older"
       * did anything, since the rail itself is deliberately not live (an audit trail must hold still).
       */}
      <span aria-live="polite" className="visually-hidden" role="status">
        {announcement}
      </span>

      {next === null ? null : (
        <Button busy={busy} iconName="chevron-down" onClick={showOlder} variant="secondary">
          {intl.formatMessage({
            id: busy ? 'customers.timeline.loadingOlder' : 'customers.timeline.older',
          })}
        </Button>
      )}
    </div>
  )
}

/** One wire entry as the `Timeline` primitive's row. */
function toRow(entry: CustomerTimelineEntry, intl: IntlShape): TimelineRow {
  // `intl.locale` is a string as far as react-intl is concerned, and the pseudo-locale story proves
  // it can be one this application does not format in. The guard is what keeps that a fallback to
  // the default rather than a cast that would hand `Intl` a tag it cannot build a formatter for.
  const formatters = isSupportedLocale(intl.locale) ? getFormatters(intl.locale) : getFormatters()
  const when = formatters.formatRelativeTime(entry.occurredAt)
  const detail = detailOf(entry, intl)

  return {
    id: entry.entryId,
    title: entry.title,
    icon: iconFor(entry.kind),
    absoluteTime: when.absolute,
    dateTime: entry.occurredAt,
    relativeTime: when.relative,
    // Null is the system, not a missing name, so it is named rather than left blank — "by —" reads
    // as a bug, and an audit trail that cannot say who did something is not one.
    actor: entry.actorDisplayName ?? intl.formatMessage({ id: 'customers.timeline.actor.system' }),
    ...(detail === null ? {} : { detail }),
  }
}

/** The longer text under an entry: what was written, and what the reason was or why it is not shown. */
function detailOf(entry: CustomerTimelineEntry, intl: IntlShape) {
  const reason = reasonLine(entry, intl)
  if (entry.detail === null && reason === null) {
    return null
  }

  return (
    <>
      {entry.detail === null ? null : <p>{entry.detail}</p>}
      {reason === null ? null : <p className="customers__timelineReason">{reason}</p>}
    </>
  )
}

/**
 * The reason, the fact that there is one nobody may read, or nothing.
 *
 * The three cases are the whole point of the server sending `reasonPermission` beside `reason`: a
 * reason withheld and a reason never given are different facts about the record, and a reader
 * deciding whether somebody explained themselves needs to be able to tell them apart.
 */
function reasonLine(entry: CustomerTimelineEntry, intl: IntlShape): string | null {
  if (entry.reason !== null) {
    return intl.formatMessage({ id: 'customers.timeline.reason' }, { reason: entry.reason })
  }
  if (entry.reasonPermission !== null) {
    return intl.formatMessage({ id: 'customers.timeline.reasonWithheld' })
  }
  return null
}

/**
 * The glyph for a kind, matched on its leading segments so a module can add a kind without a client
 * release. An unrecognised kind gets the neutral dot, which is the `Timeline` primitive's default.
 */
function iconFor(kind: string): IconName {
  if (kind.startsWith('customers.consent')) return 'check-circle'
  if (kind.startsWith('customers.preferences')) return 'settings'
  if (kind.startsWith('customers.export')) return 'clipboard'
  if (kind.startsWith('customers.merge')) return 'users'
  if (kind.startsWith('customers.measurement')) return 'ruler'
  if (kind.startsWith('customers')) return 'edit'
  if (kind.startsWith('orders')) return 'scissors'
  if (kind.startsWith('billing')) return 'receipt'
  if (kind.startsWith('custody')) return 'scan'
  if (kind.startsWith('delivery')) return 'truck'
  return 'dot'
}

/**
 * A module's name in the shop's words, falling back to the key itself for one not yet named.
 *
 * A switch rather than a computed message id, because the set of sources grows on the server and a
 * computed id would either fail the catalogue's `MessageKey` check or silently render the raw key as
 * though it were a sentence. Falling back to the key is deliberate and visible: "part of this
 * history could not be loaded (custody)" is still useful to whoever is asked about it, and the
 * missing translation shows up as the odd word out rather than as nothing at all.
 */
function sourceLabel(source: string, intl: IntlShape): string {
  switch (source) {
    case 'customers':
      return intl.formatMessage({ id: 'customers.timeline.source.customers' })
    case 'orders':
      return intl.formatMessage({ id: 'customers.timeline.source.orders' })
    case 'billing':
      return intl.formatMessage({ id: 'customers.timeline.source.billing' })
    case 'custody':
      return intl.formatMessage({ id: 'customers.timeline.source.custody' })
    default:
      return source
  }
}
