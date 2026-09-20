import type { StatusKind } from '../components/primitives/statuses'

/**
 * The badge a server-side customer status is shown as.
 *
 * A function rather than a lookup object, for the same reason `accountStatusKind` is one: the
 * server's set may grow, and a screen that threw on an unfamiliar status would refuse to render a
 * whole list over one row it did not recognise. An unknown status falls back to `deactivated`, the
 * safe direction — it shows the record as more restricted than it may be, never less.
 */
export function customerStatusKind(status: string): StatusKind {
  return status === 'Active' ? 'active' : 'deactivated'
}
