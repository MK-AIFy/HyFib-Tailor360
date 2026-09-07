import type { StatusKind } from '../components/primitives/statuses'

/**
 * The badge a server-side account status is shown as.
 *
 * A function rather than a lookup object, because the server's set may grow and a screen that threw
 * on an unfamiliar status would refuse to render a list over one row it did not recognise. An unknown
 * status falls back to the closed one, which is the safe direction: showing an account as more
 * restricted than it is loses nobody any access, and the word beside the badge still says what the
 * server said.
 */
export function accountStatusKind(status: string): StatusKind {
  switch (status) {
    case 'Invited':
      return 'invited'
    case 'Active':
      return 'active'
    case 'Suspended':
      return 'suspended'
    default:
      return 'deactivated'
  }
}
