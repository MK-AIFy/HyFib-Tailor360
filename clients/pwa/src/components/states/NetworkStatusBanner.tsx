import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Button } from '../primitives/Button'
import { Icon } from '../primitives/Icon'
import { useNetworkState } from './useNetworkState'
import type { NetworkState } from './useNetworkState'
import './states.css'

export interface NetworkStatusBannerProps {
  /**
   * The connection state. Omit it and the banner reads the device itself; pass one from a shell
   * that already holds it, or from a story or a test that needs to show a state the browser is not
   * in.
   */
  readonly state?: NetworkState
  /**
   * Extra detail belonging to the connection — the queued count and the "N scans not yet sent" line
   * that #51 adds. Rendered inside the same region so it is announced with it rather than after it.
   */
  readonly children?: ReactNode
  readonly className?: string
}

/**
 * The connection, stated on the screen and kept there.
 *
 * Three rules, and each of them is a rule rather than a preference:
 *
 *  - **It is never a toast.** docs/nfr/accessibility-localisation.md section 6 forbids one for sync
 *    state outright: it disappears before a person holding a garment has read it, and a screen
 *    reader user may never hear it at all.
 *  - **The offline message cannot be dismissed.** Section 8.3 calls it "a persistent,
 *    non-dismissible network banner", and checklist item A11Y-OF-01 asks whether it stays until the
 *    connection returns. There is deliberately no `onDismiss` on this component.
 *  - **The return is stated, not merely implied.** A banner that silently vanishes tells nobody
 *    anything. The connection coming back renders its own message, which the person dismisses when
 *    they have seen it — and which has no timer on it.
 *
 * The live region is rendered on every screen whether or not there is anything to say. That is
 * deliberate: a live region that is inserted into the page at the moment its content appears is the
 * commonest reason an announcement is missed, because the region has to exist before it changes.
 * When there is nothing to say the region holds no content and takes no space.
 */
export function NetworkStatusBanner({ state, children, className }: NetworkStatusBannerProps) {
  const intl = useIntl()
  const detected = useNetworkState()
  const { online, restored, acknowledgeRestored } = state ?? detected

  const status = !online ? 'offline' : restored ? 'restored' : 'clear'

  return (
    <div
      aria-label={intl.formatMessage({ id: 'states.network.label' })}
      className={cx('network-status-banner', className)}
      data-status={status}
      role="status"
    >
      {status === 'clear' ? null : (
        <div className="network-status-banner__body">
          <Icon
            className="network-status-banner__icon"
            name={status === 'offline' ? 'cloud-off' : 'check-circle'}
          />
          <div className="network-status-banner__text">
            <strong className="network-status-banner__title">
              {intl.formatMessage({
                id:
                  status === 'offline'
                    ? 'states.network.offline.title'
                    : 'states.network.restored.title',
              })}
            </strong>{' '}
            <span>
              {intl.formatMessage({
                id:
                  status === 'offline'
                    ? 'states.network.offline.body'
                    : 'states.network.restored.body',
              })}
            </span>
            {children}
          </div>
          {status === 'restored' ? (
            <Button
              className="network-status-banner__dismiss"
              onClick={acknowledgeRestored}
              variant="subtle"
            >
              {intl.formatMessage({ id: 'states.network.restored.dismiss' })}
            </Button>
          ) : null}
        </div>
      )}
    </div>
  )
}
