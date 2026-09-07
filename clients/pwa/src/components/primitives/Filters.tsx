import { useId } from 'react'
import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Button } from './Button'
import { Icon } from './Icon'
import { IconButton } from './IconButton'
import './Filters.css'

export interface AppliedFilter {
  /** Stable identifier, handed back to `onRemove`. */
  readonly id: string
  /**
   * The whole filter in words — "Due: this week", "Phase: Finishing", "Branch: Coimbatore".
   *
   * One string rather than a field-and-value pair, because word order differs in Tamil and a
   * component that joined them with a colon would be building a sentence by concatenation, which
   * docs/nfr/accessibility-localisation.md section 10.4 rules out. The caller formats it through the
   * message catalogue with both halves as ICU arguments.
   */
  readonly label: string
}

export interface FiltersProps {
  /** The filter controls themselves — selects, date fields, a search box. */
  readonly children?: ReactNode
  /** What is currently narrowing the list. Rendered as removable chips. */
  readonly applied?: readonly AppliedFilter[]
  readonly onRemove?: (id: string) => void
  readonly onClearAll?: () => void
  /**
   * How many records survived the filters.
   *
   * Announced politely when it changes, which is what 4.1.3 Status Messages asks of a result count
   * that updates without a page load — and it is announced *here*, once, rather than by each control
   * that changed, so a person who ticks three boxes hears three counts rather than nine.
   */
  readonly resultCount?: number
  /** Names the region. Defaults to "Filters". */
  readonly label?: string
  readonly className?: string
}

/**
 * The filter region of a list screen.
 *
 * It owns three things and no more: somewhere to put the controls, the chips saying what is
 * currently applied, and the result count. The controls themselves come from the forms family,
 * because a filter is an ordinary field and inventing a second kind of select for filtering is how a
 * design system ends up with two of everything.
 *
 * The applied chips are not decoration. A filter that is on but invisible is the reason somebody
 * reports that a job has vanished from the queue, so every active filter is stated in words with its
 * own remove control, and there is one button that clears the lot.
 */
export function Filters({
  children,
  applied = [],
  onRemove,
  onClearAll,
  resultCount,
  label,
  className,
}: FiltersProps) {
  const intl = useIntl()
  const headingId = useId()
  const resolvedLabel = label ?? intl.formatMessage({ id: 'primitives.filters.label' })

  return (
    <section className={cx('filters', className)} aria-labelledby={headingId}>
      <div className="filters__header">
        <h2 className="filters__heading" id={headingId}>
          <Icon name="filter" className="filters__heading-icon" />
          {resolvedLabel}
        </h2>
        {onClearAll === undefined || applied.length === 0 ? null : (
          <Button variant="subtle" iconName="close" onClick={onClearAll}>
            {intl.formatMessage({ id: 'primitives.filters.clearAll' })}
          </Button>
        )}
      </div>

      {children === undefined ? null : <div className="filters__controls">{children}</div>}

      <ul
        className="filters__applied"
        aria-label={intl.formatMessage({ id: 'primitives.filters.applied' })}
      >
        {applied.length === 0 ? (
          <li className="filters__none">{intl.formatMessage({ id: 'primitives.filters.none' })}</li>
        ) : (
          applied.map((filter) => (
            <li key={filter.id} className="filters__chip">
              <span className="filters__chip-label">{filter.label}</span>
              {onRemove === undefined ? null : (
                <IconButton
                  className="filters__chip-remove"
                  name="close"
                  size="dense"
                  label={intl.formatMessage(
                    { id: 'primitives.filters.remove' },
                    { filter: filter.label },
                  )}
                  onClick={() => {
                    onRemove(filter.id)
                  }}
                />
              )}
            </li>
          ))
        )}
      </ul>

      {resultCount === undefined ? null : (
        // Polite, never assertive: a count that interrupted would talk over the control that
        // changed it. role="status" carries an implicit aria-live="polite".
        <p className="filters__results" role="status">
          {intl.formatMessage({ id: 'primitives.filters.results' }, { count: resultCount })}
        </p>
      )}
    </section>
  )
}
