import { useEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { BREAKPOINTS } from '../../design-system/foundations/breakpoints'
import { cx } from '../../design-system/foundations/cx'
import { Button } from '../primitives/Button'
import { useElementWidth } from './useElementWidth'
import './MasterDetail.css'

/** How the two panes are arranged. `split` shows both; `stacked` shows one at a time. */
export type MasterDetailArrangement = 'split' | 'stacked'

export interface MasterDetailProps {
  /** The list pane. Always present. */
  readonly list: ReactNode
  /** The detail pane, or undefined when nothing is selected. */
  readonly detail?: ReactNode
  /**
   * Whether the detail is what the person is looking at. Only meaningful when stacked, where the
   * two panes cannot both be on screen; when split, both panes are shown regardless.
   */
  readonly detailOpen?: boolean
  /** Called by the Back control. Required for the stacked arrangement to be escapable. */
  readonly onCloseDetail?: () => void
  /** Names the list landmark — "Orders", "Jobs due today". Defaults to "List". */
  readonly listLabel?: string
  /** Names the detail landmark — "Order J-CBE01-…". Defaults to "Details". */
  readonly detailLabel?: string
  /** Forces an arrangement. For stories and tests only; the real decision is the measured width. */
  readonly arrangement?: MasterDetailArrangement
  readonly className?: string
}

/**
 * A list beside the thing it selects, or one at a time when there is not room for both.
 *
 * This is the counter tablet's working pattern — measurement capture, the job queue, a job card,
 * stock, a bill — and it is the pattern the #50 blueprint names for the tablet layout.
 *
 * ## Where the threshold comes from
 *
 * The arrangement is decided by **this component's own container width**, not the window and not the
 * device, and it splits at 768 CSS px of container. That number is what makes the arrangement match
 * docs/nfr/support-matrix.md section 5 without a device check anywhere: a counter tablet held in
 * portrait has a viewport of 768 px, and after the shell's padding and its navigation rail the
 * container is nearer 540 px, so it stacks — which is the "stacked single column" the matrix
 * specifies for portrait. The same tablet turned to landscape gives the container about 790 px, so
 * it splits, which is the "master-detail" the matrix specifies for landscape. Nothing anywhere reads
 * the orientation, so 1.3.4 Orientation cannot be broken by accident.
 *
 * ## Landmarks and focus
 *
 * Each pane is a named `region`. Two unnamed regions would be exactly what checklist item A11Y-06
 * fails a screen for — "region, region" tells a returning user nothing — so the names say what is
 * inside them and default to the two catalogue strings when a screen does not supply better.
 *
 * When stacked, opening the detail replaces the list, so focus is moved into the detail pane;
 * closing it returns focus to the list pane. Without that, a phone user who has just opened a job
 * card is still focused on a link that is no longer on the screen, and the next `Tab` starts from the
 * top of the shell (checklist items A11Y-64 and A11Y-66). When split, both panes are on screen and
 * nothing moves, because nothing has been replaced.
 */
export function MasterDetail({
  list,
  detail,
  detailOpen = false,
  onCloseDetail,
  listLabel,
  detailLabel,
  arrangement,
  className,
}: MasterDetailProps) {
  const intl = useIntl()
  const containerRef = useRef<HTMLDivElement>(null)
  const listRef = useRef<HTMLElement>(null)
  const detailRef = useRef<HTMLElement>(null)
  const width = useElementWidth(containerRef)

  const resolved: MasterDetailArrangement =
    arrangement ?? (width >= BREAKPOINTS.md ? 'split' : 'stacked')
  const showDetail = resolved === 'split' || detailOpen

  const listName = listLabel ?? intl.formatMessage({ id: 'layout.masterDetail.list' })
  const detailName = detailLabel ?? intl.formatMessage({ id: 'layout.masterDetail.detail' })

  const previousPane = useRef<'list' | 'detail' | null>(null)

  useEffect(() => {
    if (resolved !== 'stacked') {
      previousPane.current = null
      return
    }

    const current = detailOpen ? 'detail' : 'list'
    // Only on a change: a re-render for any other reason must not steal focus back (A11Y-21).
    if (previousPane.current !== null && previousPane.current !== current) {
      const target = current === 'detail' ? detailRef.current : listRef.current
      target?.focus()
    }
    previousPane.current = current
  }, [detailOpen, resolved])

  return (
    <div className={cx('master-detail', className)} data-arrangement={resolved} ref={containerRef}>
      {resolved === 'split' || !detailOpen ? (
        <section className="master-detail__list" aria-label={listName} ref={listRef} tabIndex={-1}>
          {list}
        </section>
      ) : null}

      {showDetail ? (
        <section
          className="master-detail__detail"
          aria-label={detailName}
          ref={detailRef}
          tabIndex={-1}
        >
          {resolved === 'stacked' && onCloseDetail !== undefined ? (
            <p className="master-detail__back">
              <Button
                variant="subtle"
                iconName="chevron-left"
                iconPosition="leading"
                onClick={onCloseDetail}
              >
                {intl.formatMessage({ id: 'layout.masterDetail.back' })}
              </Button>
            </p>
          ) : null}
          {detail ?? (
            <p className="master-detail__empty">
              {intl.formatMessage({ id: 'layout.masterDetail.empty' })}
            </p>
          )}
        </section>
      ) : null}
    </div>
  )
}
