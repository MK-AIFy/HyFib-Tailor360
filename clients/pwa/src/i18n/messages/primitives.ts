/**
 * Primitive component messages — the words the buttons, badges, alerts, cards, tables, timeline and
 * filters say for themselves.
 *
 * A caller supplies the content; this family supplies the chrome. That split is what makes the
 * primitives translatable without every screen having to remember to translate "Clear all filters".
 *
 * The status words carry more weight than anything else here. docs/nfr/accessibility-localisation.md
 * section 4.1 forbids status conveyed by colour alone, so every one of these is rendered as a word
 * beside an icon — which means a missing translation is not a cosmetic gap but a status a Tamil-
 * reading Tailor cannot read. Section 11.2 lists the custody and status words as glossary-owned and
 * therefore blocked behind the native-speaker review.
 */
export const primitivesEn = {
  /* Buttons and links -------------------------------------------------------------------- */
  'primitives.button.busy': 'Working…',
  'primitives.link.opensInNewTab': 'opens in a new tab',

  /* Alerts ------------------------------------------------------------------------------- */
  'primitives.alert.info': 'Information',
  'primitives.alert.success': 'Done',
  'primitives.alert.warning': 'Warning',
  'primitives.alert.danger': 'Problem',
  'primitives.alert.dismiss': 'Dismiss this message',

  /* Status words. One per StatusKind in src/components/primitives/statuses.ts. --------------- */
  'primitives.status.draft': 'Draft',
  'primitives.status.queued': 'Queued',
  'primitives.status.inProgress': 'In progress',
  'primitives.status.ready': 'Ready',
  'primitives.status.delivered': 'Delivered',
  'primitives.status.dueSoon': 'Due soon',
  'primitives.status.overdue': 'Overdue',
  'primitives.status.held': 'On hold',
  'primitives.status.rework': 'Rework',
  'primitives.status.qcPassed': 'QC passed',
  'primitives.status.qcFailed': 'QC failed',
  'primitives.status.unpaid': 'Unpaid',
  'primitives.status.paid': 'Paid',
  'primitives.status.cancelled': 'Cancelled',
  'primitives.status.notSynced': 'Not yet synced',

  /* Tables and card collections ----------------------------------------------------------- */
  'primitives.table.empty': 'Nothing to show yet.',
  'primitives.table.rowActions': 'Actions for {row}',
  'primitives.table.rowCount': '{count, plural, one {# row} other {# rows}}',
  'primitives.table.scrollHint': 'The table scrolls sideways to show every column.',

  /* Timeline ------------------------------------------------------------------------------ */
  'primitives.timeline.label': 'History',
  'primitives.timeline.by': 'by {actor}',

  /* Filters ------------------------------------------------------------------------------- */
  'primitives.filters.label': 'Filters',
  'primitives.filters.applied': 'Applied filters',
  'primitives.filters.remove': 'Remove filter: {filter}',
  'primitives.filters.clearAll': 'Clear all filters',
  'primitives.filters.none': 'No filters applied',
  'primitives.filters.results': '{count, plural, one {# result} other {# results}}',
} as const

export const primitivesTa: Record<keyof typeof primitivesEn, string> = {
  'primitives.button.busy': 'செயலில்…',
  // not translated — awaiting native-speaker review
  'primitives.link.opensInNewTab': 'opens in a new tab',

  'primitives.alert.info': 'தகவல்',
  'primitives.alert.success': 'முடிந்தது',
  'primitives.alert.warning': 'எச்சரிக்கை',
  'primitives.alert.danger': 'சிக்கல்',
  // not translated — awaiting native-speaker review
  'primitives.alert.dismiss': 'Dismiss this message',

  // not translated — awaiting native-speaker review
  'primitives.status.draft': 'Draft',
  // not translated — awaiting native-speaker review
  'primitives.status.queued': 'Queued',
  // not translated — awaiting native-speaker review
  'primitives.status.inProgress': 'In progress',
  // not translated — awaiting native-speaker review
  'primitives.status.ready': 'Ready',
  // not translated — awaiting native-speaker review
  'primitives.status.delivered': 'Delivered',
  // not translated — awaiting native-speaker review
  'primitives.status.dueSoon': 'Due soon',
  // not translated — awaiting native-speaker review
  'primitives.status.overdue': 'Overdue',
  'primitives.status.held': 'தடை',
  'primitives.status.rework': 'மீண்டும் தையல்',
  // not translated — awaiting native-speaker review
  'primitives.status.qcPassed': 'QC passed',
  // not translated — awaiting native-speaker review
  'primitives.status.qcFailed': 'QC failed',
  // not translated — awaiting native-speaker review
  'primitives.status.unpaid': 'Unpaid',
  // not translated — awaiting native-speaker review
  'primitives.status.paid': 'Paid',
  // not translated — awaiting native-speaker review
  'primitives.status.cancelled': 'Cancelled',
  // not translated — awaiting native-speaker review
  'primitives.status.notSynced': 'Not yet synced',

  // not translated — awaiting native-speaker review
  'primitives.table.empty': 'Nothing to show yet.',
  // not translated — awaiting native-speaker review
  'primitives.table.rowActions': 'Actions for {row}',
  // not translated — awaiting native-speaker review
  'primitives.table.rowCount': '{count, plural, one {# row} other {# rows}}',
  // not translated — awaiting native-speaker review
  'primitives.table.scrollHint': 'The table scrolls sideways to show every column.',

  // not translated — awaiting native-speaker review
  'primitives.timeline.label': 'History',
  // not translated — awaiting native-speaker review
  'primitives.timeline.by': 'by {actor}',

  // not translated — awaiting native-speaker review
  'primitives.filters.label': 'Filters',
  // not translated — awaiting native-speaker review
  'primitives.filters.applied': 'Applied filters',
  // not translated — awaiting native-speaker review
  'primitives.filters.remove': 'Remove filter: {filter}',
  // not translated — awaiting native-speaker review
  'primitives.filters.clearAll': 'Clear all filters',
  // not translated — awaiting native-speaker review
  'primitives.filters.none': 'No filters applied',
  // not translated — awaiting native-speaker review
  'primitives.filters.results': '{count, plural, one {# result} other {# results}}',
}
