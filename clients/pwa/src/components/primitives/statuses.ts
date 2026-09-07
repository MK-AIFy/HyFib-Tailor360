import type { MessageKey } from '../../i18n/en-IN'
import type { Tone } from '../../design-system/foundations/types'
import type { IconName } from './icons'

/**
 * The statuses a screen may show, and how each one is presented.
 *
 * A closed union rather than an open `{ tone, icon, label }` prop, because an open one would let a
 * screen invent a status with a colour and no word — which is precisely the failure
 * docs/nfr/accessibility-localisation.md section 4.1 forbids: "**No status is conveyed by colour
 * alone.** Every status badge carries an icon and a word — overdue, held, ready, unpaid, rework".
 * Closing the union means the word comes from the message catalogue by construction, so a status
 * cannot ship untranslated and cannot ship unnamed.
 *
 * Two rules govern the table below.
 *
 *  1. **Neighbouring statuses do not share a glyph.** Paid and unpaid, QC passed and QC failed,
 *     cancelled and QC failed all appear in the same lists, so each takes a different shape. If the
 *     only difference between two badges were their colour, the word would be doing all the work and
 *     the icon would be decoration pretending to be information.
 *  2. **The tone is chosen after the shape, not before.** Tone picks a colour pair; it never carries
 *     the meaning. Reading this table in greyscale must still tell you which status is which.
 *
 * The vocabulary comes from docs/prd/glossary.md — ready state, hold, rework, overdue, dispatch —
 * which is also why section 11.2 lists these as glossary-owned and blocked behind the native-speaker
 * review before Tamil can be switched on.
 */
export const STATUS_KINDS = [
  'draft',
  'queued',
  'in-progress',
  'ready',
  'delivered',
  'due-soon',
  'overdue',
  'held',
  'rework',
  'qc-passed',
  'qc-failed',
  'unpaid',
  'paid',
  'cancelled',
  'not-synced',
] as const

export type StatusKind = (typeof STATUS_KINDS)[number]

export interface StatusPresentation {
  readonly tone: Tone
  readonly icon: IconName
  readonly messageKey: MessageKey
}

export const STATUS_PRESENTATION: Record<StatusKind, StatusPresentation> = {
  draft: { tone: 'neutral', icon: 'edit', messageKey: 'primitives.status.draft' },
  queued: { tone: 'neutral', icon: 'list', messageKey: 'primitives.status.queued' },
  'in-progress': { tone: 'info', icon: 'play', messageKey: 'primitives.status.inProgress' },
  ready: { tone: 'success', icon: 'package', messageKey: 'primitives.status.ready' },
  delivered: { tone: 'success', icon: 'truck', messageKey: 'primitives.status.delivered' },
  'due-soon': { tone: 'warning', icon: 'clock', messageKey: 'primitives.status.dueSoon' },
  overdue: { tone: 'danger', icon: 'alert-triangle', messageKey: 'primitives.status.overdue' },
  held: { tone: 'warning', icon: 'pause', messageKey: 'primitives.status.held' },
  rework: { tone: 'warning', icon: 'refresh', messageKey: 'primitives.status.rework' },
  'qc-passed': { tone: 'success', icon: 'check', messageKey: 'primitives.status.qcPassed' },
  'qc-failed': { tone: 'danger', icon: 'x-circle', messageKey: 'primitives.status.qcFailed' },
  unpaid: { tone: 'warning', icon: 'rupee', messageKey: 'primitives.status.unpaid' },
  paid: { tone: 'success', icon: 'check-circle', messageKey: 'primitives.status.paid' },
  cancelled: { tone: 'neutral', icon: 'close', messageKey: 'primitives.status.cancelled' },
  'not-synced': { tone: 'info', icon: 'cloud-off', messageKey: 'primitives.status.notSynced' },
}
