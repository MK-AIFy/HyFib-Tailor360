import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { readAuditTrail } from '../../admin/adminApi'
import { useAdminResource } from '../../admin/useAdminResource'
import type { AuditEntry } from '../../admin/types'

/**
 * What was changed, by whom, and why.
 *
 * ## Why before and after are behind a disclosure rather than in the row
 *
 * The trail is read to answer a question — usually "who did this to my account" — and the answer is
 * in the summary, the actor and the reason. The serialised states are the evidence behind that
 * answer, wanted for one entry in fifty, and putting them in every row buries the fifty in the one.
 * A `<details>` is the right control because it is the browser's own: it is keyboard-operable and
 * announced as an expandable region without a line of JavaScript.
 *
 * ## Why the trail is never cached
 *
 * The server marks it `no-store` and the transport asks for no cache, because every entry names a
 * person and says what they did. A shared counter browser must not hold a copy after the auditor has
 * walked away.
 *
 * ## Why "show older entries" is a button and not an infinite scroll
 *
 * The trail is appended to while it is being read. Paging is keyset on a monotonic sequence rather
 * than by offset — an offset would shift under every page and silently skip the entries an
 * investigation needed — and a control the reader presses keeps their place, which a list that grows
 * under their hands does not.
 */
export function AuditTrailRoute() {
  const intl = useIntl()

  const [action, setAction] = useState('')
  const [applied, setApplied] = useState('')
  const [cursors, setCursors] = useState<readonly string[]>([])

  const cursor = cursors.at(-1)

  const page = useAdminResource(`${applied}|${cursor ?? ''}`, (signal) =>
    readAuditTrail({
      ...(applied === '' ? {} : { action: applied }),
      ...(cursor === undefined ? {} : { cursor }),
      signal,
    }),
  )

  const [seen, setSeen] = useState<readonly AuditEntry[]>([])

  // The pages are concatenated as they arrive rather than replacing what is on screen: an auditor
  // reading down the trail should not lose the entries above when they ask for the ones below.
  const entries =
    cursor === undefined ? (page.value?.entries ?? []) : [...seen, ...(page.value?.entries ?? [])]

  return (
    <section>
      <h2>
        <FormattedMessage id="admin.audit.title" />
      </h2>

      <form
        className="admin__toolbar"
        onSubmit={(event) => {
          event.preventDefault()
          setCursors([])
          setSeen([])
          setApplied(action)
        }}
      >
        <div className="admin__field">
          <label htmlFor="audit-action">
            <FormattedMessage id="admin.audit.filter.action" />
          </label>
          <input
            id="audit-action"
            type="text"
            value={action}
            onChange={(event) => {
              setAction(event.target.value)
            }}
          />
        </div>
        <Button type="submit" iconName="search">
          <FormattedMessage id="admin.audit.filter.apply" />
        </Button>
      </form>

      <AuthProblemAlert failure={page.failure} />

      {page.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'admin.audit.loading' })} />
      ) : entries.length === 0 ? (
        <EmptyState iconName="list" live="polite">
          {intl.formatMessage({ id: 'admin.audit.empty' })}
        </EmptyState>
      ) : (
        <ol className="admin__checkList">
          {entries.map((entry) => (
            <li key={String(entry.sequence)}>
              <article>
                <h3>{entry.summary}</h3>
                <p className="admin__hint">
                  {intl.formatDate(entry.occurredAt, {
                    dateStyle: 'medium',
                    timeStyle: 'short',
                  })}
                  {' · '}
                  {entry.actorDisplayName}
                  {entry.correlationId === null
                    ? null
                    : ` · ${intl.formatMessage(
                        { id: 'admin.audit.correlation' },
                        { correlationId: entry.correlationId },
                      )}`}
                </p>
                <p>{entry.reason ?? intl.formatMessage({ id: 'admin.audit.noReason' })}</p>
                {entry.before === null && entry.after === null ? null : (
                  <details>
                    <summary>{intl.formatMessage({ id: 'admin.audit.details' })}</summary>
                    <dl className="admin__detailGrid">
                      <dt>
                        <FormattedMessage id="admin.audit.before" />
                      </dt>
                      <dd>
                        <code>{entry.before ?? '—'}</code>
                      </dd>
                      <dt>
                        <FormattedMessage id="admin.audit.after" />
                      </dt>
                      <dd>
                        <code>{entry.after ?? '—'}</code>
                      </dd>
                    </dl>
                  </details>
                )}
              </article>
            </li>
          ))}
        </ol>
      )}

      {page.value?.nextCursor == null ? null : (
        <div className="admin__actions">
          <Button
            variant="secondary"
            onClick={() => {
              setSeen(entries)
              setCursors((previous) => [...previous, page.value?.nextCursor ?? ''])
            }}
          >
            <FormattedMessage id="admin.audit.more" />
          </Button>
        </div>
      )}
    </section>
  )
}
