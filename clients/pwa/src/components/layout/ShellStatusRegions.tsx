import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Alert } from '../primitives/Alert'
import { useShellStatus } from './useShellStatus'
import './ShellStatusRegions.css'

export interface ShellStatusRegionsProps {
  readonly className?: string
}

/**
 * The shell's four live regions, in the same slot on every screen.
 *
 * ## Why four containers and not one
 *
 * A live region announces a **change**, and only if the container was already in the accessibility
 * tree when the change happened. Mounting a region at the moment the message arrives is the
 * commonest way a correct-looking implementation announces nothing at all, and it is silent in
 * exactly the case that matters — the first scan of a shift.
 *
 * So all four containers are mounted from the first paint and stay mounted, empty. They carry no
 * padding, border or margin while they are empty, so an empty region occupies no space and shifts
 * nothing; `display: none` is not used, because that would take them out of the tree again and
 * reintroduce the bug.
 *
 * There are four rather than three because politeness cannot be changed on a live element and be
 * relied upon: a rejected scan needs `role="alert"` and an accepted one needs `role="status"`, so
 * each has its own container and the message goes into the right one.
 *
 * ## Why the visible message is not itself a live region
 *
 * The `Alert` inside each container renders with `live="off"`. The container is the live region; the
 * alert is what stays on the screen afterwards. If both were live the message would be announced
 * twice, which is the chatter checklist item A11Y-43 fails a screen for.
 */
export function ShellStatusRegions({ className }: ShellStatusRegionsProps) {
  const intl = useIntl()
  const { scan, sync, autosave, announceScan } = useShellStatus()

  const dismissScan = () => {
    announceScan(null)
  }

  return (
    <div className={cx('shell-status', className)}>
      {/*
       * Rejected scans first, and assertive. The garment has already moved on: a rejection that
       * waits its turn behind whatever was being read is heard too late to act on
       * (docs/nfr/accessibility-localisation.md section 6, checklist item A11Y-41).
       */}
      <div className="shell-status__region" role="alert" data-channel="scan-rejected">
        {scan?.outcome === 'rejected' ? (
          <Alert
            tone="danger"
            title={intl.formatMessage({ id: 'layout.status.scanRejected' })}
            onDismiss={dismissScan}
          >
            {scan.message}
          </Alert>
        ) : null}
      </div>

      <div className="shell-status__region" role="status" data-channel="scan-accepted">
        {scan?.outcome === 'accepted' ? (
          <Alert
            tone="success"
            title={intl.formatMessage({ id: 'layout.status.scanAccepted' })}
            onDismiss={dismissScan}
          >
            {scan.message}
          </Alert>
        ) : null}
      </div>

      {/*
       * Sync carries no dismiss control on purpose. Section 8.3 calls the network state persistent
       * and non-dismissible: a queued scan that the person has dismissed is a queued scan they have
       * forgotten, and #51 blocks sign-out while the queue is not empty.
       */}
      <div className="shell-status__region" role="status" data-channel="sync">
        {sync === null ? null : (
          <Alert tone={sync.tone} title={intl.formatMessage({ id: 'layout.status.sync' })}>
            {sync.message}
          </Alert>
        )}
      </div>

      <div className="shell-status__region" role="status" data-channel="autosave">
        {autosave === null ? null : (
          <Alert tone="info" title={intl.formatMessage({ id: 'layout.status.autosave' })}>
            {autosave}
          </Alert>
        )}
      </div>
    </div>
  )
}
