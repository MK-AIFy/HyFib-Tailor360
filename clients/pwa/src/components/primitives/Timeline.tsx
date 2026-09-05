import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Icon } from './Icon'
import type { IconName } from './icons'
import { STATUS_PRESENTATION } from './statuses'
import type { StatusKind } from './statuses'
import './Timeline.css'

export interface TimelineEntry {
  readonly id: string
  /** What happened, in words. "Cutting complete", "Payment received", "Scanned in at Finishing". */
  readonly title: ReactNode
  /**
   * The status this entry put the record into, if any. Supplies the glyph and the tone; the icon
   * shapes differ from one another, so the rail reads in greyscale.
   */
  readonly status?: StatusKind
  /** An explicit glyph, when the entry is not a status change. Ignored if `status` is given. */
  readonly icon?: IconName
  /**
   * The instant, formatted by the caller through the `formatters` module — `04-09-2026 04:30 PM`,
   * in the branch's timezone. Always rendered: docs/nfr/accessibility-localisation.md section 12
   * requires the absolute value to be available wherever a relative one is shown.
   */
  readonly absoluteTime: string
  /** The machine-readable instant for `<time datetime>`, ISO 8601. */
  readonly dateTime?: string
  /** "3 hours ago". An addition to the absolute time, never a replacement for it. */
  readonly relativeTime?: string
  /** Who did it. Rendered as "by Kavitha R", from the message catalogue so the word order is Tamil-safe. */
  readonly actor?: string
  /** Anything more — a reason, a note, a measurement that changed. */
  readonly detail?: ReactNode
}

export interface TimelineProps {
  readonly entries: readonly TimelineEntry[]
  /** Names the list. Defaults to "History". */
  readonly label?: string
  readonly className?: string
}

/**
 * The history of a record, newest first or oldest first as the caller pleases.
 *
 * An ordered list, because it is one: the sequence is the meaning, and `<ol>` is what tells a screen
 * reader "item 3 of 11" as somebody walks a custody chain looking for the handover that went wrong.
 * The connecting rail is drawn with a pseudo-element on the list item, so it is decoration a screen
 * reader never meets, and the marker for each entry is the status glyph rather than a bullet.
 *
 * Nothing here is a live region and nothing here animates: an audit trail that reordered or faded
 * under a reader would fail 2.2.2 Pause, Stop, Hide, which checklist item A11Y-74 tests by asking
 * whether the screen holds still.
 */
export function Timeline({ entries, label, className }: TimelineProps) {
  const intl = useIntl()

  return (
    <ol
      className={cx('timeline', className)}
      aria-label={label ?? intl.formatMessage({ id: 'primitives.timeline.label' })}
    >
      {entries.map((entry) => {
        const presentation =
          entry.status === undefined ? undefined : STATUS_PRESENTATION[entry.status]
        const iconName: IconName = presentation?.icon ?? entry.icon ?? 'dot'

        return (
          <li
            key={entry.id}
            className="timeline__entry"
            data-tone={presentation === undefined ? 'neutral' : presentation.tone}
          >
            <span className="timeline__marker">
              <Icon name={iconName} />
            </span>
            <div className="timeline__content">
              <p className="timeline__title">{entry.title}</p>
              <p className="timeline__meta">
                {entry.dateTime === undefined ? (
                  <span className="timeline__time">{entry.absoluteTime}</span>
                ) : (
                  <time className="timeline__time" dateTime={entry.dateTime}>
                    {entry.absoluteTime}
                  </time>
                )}
                {entry.relativeTime === undefined ? null : (
                  <span className="timeline__relative">{entry.relativeTime}</span>
                )}
                {entry.actor === undefined ? null : (
                  <span className="timeline__actor">
                    {intl.formatMessage({ id: 'primitives.timeline.by' }, { actor: entry.actor })}
                  </span>
                )}
              </p>
              {entry.detail === undefined ? null : (
                <div className="timeline__detail">{entry.detail}</div>
              )}
            </div>
          </li>
        )
      })}
    </ol>
  )
}
