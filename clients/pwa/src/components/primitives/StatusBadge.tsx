import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Icon } from './Icon'
import { STATUS_PRESENTATION } from './statuses'
import type { StatusKind } from './statuses'
import './StatusBadge.css'

export interface StatusBadgeProps {
  readonly status: StatusKind
  /**
   * A short qualifier shown after the status word — "4 days", "since 12-09-2026", a hold reason
   * code. Always formatted by the caller through the `formatters` module: this component never
   * turns a value into text, because a date formatted twice is a date formatted two ways.
   */
  readonly detail?: string
  /**
   * Larger type and a heavier weight, for the one status a shop-floor screen is really about — the
   * job's phase on a job card, the ready state on a scan result. 1.4.6 (adopted, AAA) asks for 7:1
   * on that text, which the token pairs already deliver in every theme.
   */
  readonly prominent?: boolean
  readonly className?: string
}

/**
 * A status, as an icon and a word.
 *
 * Never as a colour. That is the whole design: the badge renders a glyph whose shape differs from
 * its neighbours, the status word from the message catalogue, and a colour pair chosen last. Read it
 * in greyscale, at 200% zoom, in the high-contrast sunlight theme, or with the icon failing to load,
 * and the status still reads (1.4.1 Use of Colour; the per-status Storybook story the criterion's
 * verification row asks for is `AllStatuses` in StatusBadge.stories.tsx).
 *
 * It is not a live region. A status that *changes* has to be announced by whatever changed it — the
 * scan result, the save, the queue update — through the polite region 4.1.3 requires; a badge that
 * announced itself on every re-render would talk over the screen it sits on.
 */
export function StatusBadge({ status, detail, prominent = false, className }: StatusBadgeProps) {
  const intl = useIntl()
  const presentation = STATUS_PRESENTATION[status]

  return (
    <span
      className={cx('status-badge', className)}
      data-status={status}
      data-tone={presentation.tone}
      data-prominent={prominent ? 'true' : undefined}
    >
      <Icon name={presentation.icon} className="status-badge__icon" />
      <span className="status-badge__label">
        {intl.formatMessage({ id: presentation.messageKey })}
      </span>
      {detail === undefined ? null : <span className="status-badge__detail">{detail}</span>}
    </span>
  )
}
