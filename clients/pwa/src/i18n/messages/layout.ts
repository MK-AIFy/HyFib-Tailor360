/**
 * Layout messages — the application shell, the three role-optimised layouts, the persistent status
 * regions and the display preferences.
 *
 * Three groups, and each is here rather than in a screen's own family for a reason.
 *
 * The **shell** keys name the parts of the chrome that exist on every screen: the master-detail
 * panes, the phone primary action, the side-navigation group headings. A screen never renames them,
 * because 3.2.3 Consistent Navigation and checklist item A11Y-10 both ask whether the shell is the
 * same as it was on the previous screen, and a per-screen override is how that stops being true.
 *
 * The **status** keys name what the persistent regions announce. They lead every message with a word
 * — "Scan accepted", "Not sent yet" — because the region carries a colour and an icon as well, and
 * docs/nfr/accessibility-localisation.md section 4.1 forbids either of those carrying the meaning on
 * its own.
 *
 * The **display** keys are the theme and text-size preferences. Their option labels say what the
 * setting is *for* — "High contrast — for sunlight", "150% — largest" — because a person who needs
 * one of them is choosing under exactly the conditions that make the choice hard.
 */
export const layoutEn = {
  /* Shell ---------------------------------------------------------------------------------- */
  'layout.settings.display': 'Display settings',
  'layout.settings.displayBody':
    'Choose how this application looks. The change takes effect at once and applies to every screen.',
  'layout.documentTitle': '{page} — HyFib Tailor360',
  'layout.route.announcement': 'Navigated to {page}',

  /* Master and detail ---------------------------------------------------------------------- */
  'layout.masterDetail.list': 'List',
  'layout.masterDetail.detail': 'Details',
  'layout.masterDetail.back': 'Back to the list',
  'layout.masterDetail.empty': 'Choose an item from the list to see it here.',

  /* The phone primary action ---------------------------------------------------------------- */
  'layout.action.scan': 'Scan',
  'layout.action.newOrder': 'New order',
  'layout.action.capture': 'Capture measurement',
  'layout.action.takePayment': 'Take payment',

  /* Side-navigation groups ------------------------------------------------------------------ */
  'layout.section.work': 'Work',
  'layout.section.customers': 'Customers and orders',
  'layout.section.money': 'Money',
  'layout.section.manage': 'Manage',

  /* The persistent status regions ----------------------------------------------------------- */
  'layout.status.scanAccepted': 'Scan accepted',
  'layout.status.scanRejected': 'Scan rejected',
  'layout.status.sync': 'Sync',
  'layout.status.autosave': 'Draft',
  'layout.status.dismiss': 'Dismiss this message',

  /* Display preferences --------------------------------------------------------------------- */
  'layout.display.theme': 'Theme',
  'layout.display.themeDescription':
    'High contrast is for reading the screen in sunlight at the counter.',
  'layout.display.theme.system': 'Follow the device',
  'layout.display.theme.light': 'Light',
  'layout.display.theme.dark': 'Dark',
  'layout.display.theme.contrast': 'High contrast — for sunlight',
  'layout.display.textSize': 'Text size',
  'layout.display.textSizeDescription':
    'This is separate from the browser zoom, and both can be used together.',
  'layout.display.textSize.100': '100% — standard',
  'layout.display.textSize.125': '125% — larger',
  'layout.display.textSize.150': '150% — largest',
  'layout.display.density': 'Row spacing',
  'layout.display.densityDescription':
    'Compact fits more rows on a desktop screen. Controls stay full size on a touchscreen.',
  'layout.display.density.comfortable': 'Comfortable',
  'layout.display.density.compact': 'Compact — desktop only',
  'layout.display.sample': 'Sample: job J-CBE01-2627-000512-01 is due on 14-09-2026.',
  'layout.display.storage': 'These settings are stored on this device.',
} as const

export const layoutTa: Record<keyof typeof layoutEn, string> = {
  // not translated — awaiting native-speaker review
  'layout.settings.display': 'Display settings',
  // not translated — awaiting native-speaker review
  'layout.settings.displayBody':
    'Choose how this application looks. The change takes effect at once and applies to every screen.',
  'layout.documentTitle': '{page} — HyFib Tailor360',
  // not translated — awaiting native-speaker review
  'layout.route.announcement': 'Navigated to {page}',

  // not translated — awaiting native-speaker review
  'layout.masterDetail.list': 'List',
  // not translated — awaiting native-speaker review
  'layout.masterDetail.detail': 'Details',
  'layout.masterDetail.back': 'பட்டியலுக்குத் திரும்பு',
  // not translated — awaiting native-speaker review
  'layout.masterDetail.empty': 'Choose an item from the list to see it here.',

  // not translated — awaiting native-speaker review
  'layout.action.scan': 'Scan',
  // not translated — awaiting native-speaker review
  'layout.action.newOrder': 'New order',
  // not translated — awaiting native-speaker review
  'layout.action.capture': 'Capture measurement',
  // not translated — awaiting native-speaker review
  'layout.action.takePayment': 'Take payment',

  // not translated — awaiting native-speaker review
  'layout.section.work': 'Work',
  // not translated — awaiting native-speaker review
  'layout.section.customers': 'Customers and orders',
  // not translated — awaiting native-speaker review
  'layout.section.money': 'Money',
  // not translated — awaiting native-speaker review
  'layout.section.manage': 'Manage',

  // not translated — awaiting native-speaker review
  'layout.status.scanAccepted': 'Scan accepted',
  // not translated — awaiting native-speaker review
  'layout.status.scanRejected': 'Scan rejected',
  // not translated — awaiting native-speaker review
  'layout.status.sync': 'Sync',
  // not translated — awaiting native-speaker review
  'layout.status.autosave': 'Draft',
  // not translated — awaiting native-speaker review
  'layout.status.dismiss': 'Dismiss this message',

  // not translated — awaiting native-speaker review
  'layout.display.theme': 'Theme',
  // not translated — awaiting native-speaker review
  'layout.display.themeDescription':
    'High contrast is for reading the screen in sunlight at the counter.',
  // not translated — awaiting native-speaker review
  'layout.display.theme.system': 'Follow the device',
  // not translated — awaiting native-speaker review
  'layout.display.theme.light': 'Light',
  // not translated — awaiting native-speaker review
  'layout.display.theme.dark': 'Dark',
  // not translated — awaiting native-speaker review
  'layout.display.theme.contrast': 'High contrast — for sunlight',
  // not translated — awaiting native-speaker review
  'layout.display.textSize': 'Text size',
  // not translated — awaiting native-speaker review
  'layout.display.textSizeDescription':
    'This is separate from the browser zoom, and both can be used together.',
  // not translated — awaiting native-speaker review
  'layout.display.textSize.100': '100% — standard',
  // not translated — awaiting native-speaker review
  'layout.display.textSize.125': '125% — larger',
  // not translated — awaiting native-speaker review
  'layout.display.textSize.150': '150% — largest',
  // not translated — awaiting native-speaker review
  'layout.display.density': 'Row spacing',
  // not translated — awaiting native-speaker review
  'layout.display.densityDescription':
    'Compact fits more rows on a desktop screen. Controls stay full size on a touchscreen.',
  // not translated — awaiting native-speaker review
  'layout.display.density.comfortable': 'Comfortable',
  // not translated — awaiting native-speaker review
  'layout.display.density.compact': 'Compact — desktop only',
  // not translated — awaiting native-speaker review
  'layout.display.sample': 'Sample: job J-CBE01-2627-000512-01 is due on 14-09-2026.',
  // not translated — awaiting native-speaker review
  'layout.display.storage': 'These settings are stored on this device.',
}
