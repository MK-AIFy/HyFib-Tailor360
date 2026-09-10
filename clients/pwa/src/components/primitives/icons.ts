/**
 * The icon set.
 *
 * Path data only — no JSX — so that the set can be iterated by a story, asserted by a test and
 * imported by a `.ts` module without pulling React in. `Icon.tsx` is the only thing that renders it.
 *
 * Three rules the shop floor imposes on every glyph here:
 *
 *  1. **An icon never carries meaning alone.** docs/nfr/accessibility-localisation.md section 4.1
 *     puts it plainly: no status is conveyed by colour alone, and every status badge carries an icon
 *     *and* a word. The icon is the fast channel for somebody who already knows the screen; the word
 *     is the one that is correct. Every component in this family that takes an icon also takes text.
 *  2. **Every icon is decorative in the accessibility tree.** `Icon` renders `aria-hidden`, always.
 *     A meaningful icon gets its meaning from the label beside it, never from an `aria-label` on the
 *     glyph, because a duplicated name is the defect checklist item A11Y-52 looks for.
 *  3. **Stroked, not filled, and drawn on a 24 unit grid.** A stroke follows `currentColor`, so an
 *     icon inherits the contrast of the text it sits with — including in the high-contrast sunlight
 *     theme, where a filled shape at a fixed tint would fail 1.4.11 the moment the palette changed.
 *
 * The strokes are set in Icon.css rather than as presentation attributes so that a theme can thicken
 * them; the geometry is here because geometry is not styling.
 */
export const ICON_NAMES = [
  /* Status and feedback */
  'check',
  'check-circle',
  'alert-circle',
  'alert-triangle',
  'info',
  'clock',
  'pause',
  'refresh',
  'package',
  'cloud-off',
  'x-circle',
  'edit',
  'list',
  'play',
  'dot',
  /* Destinations */
  'home',
  'users',
  'ruler',
  'clipboard',
  'layout',
  'scissors',
  'receipt',
  'truck',
  'bar-chart',
  'scan',
  'settings',
  'help',
  /* Controls */
  'chevron-right',
  'chevron-down',
  'chevron-left',
  'chevron-up',
  'filter',
  'close',
  'search',
  'plus',
  'rupee',
  'phone',
  'external-link',
  'menu',
] as const

export type IconName = (typeof ICON_NAMES)[number]

/**
 * The `d` attribute of each icon's single path, on a 24 x 24 grid.
 *
 * One path per icon on purpose: a component that renders exactly one element cannot accidentally
 * lose half a glyph to a CSS rule that targets `svg > *`, and a single path is what keeps the
 * production bundle's icon cost measured in bytes.
 */
export const ICON_PATHS: Record<IconName, string> = {
  check: 'M20 6 9 17l-5-5',
  'check-circle': 'M21.8 10.9A10 10 0 1 1 16 3.2M22 5 12 15.1l-3-3',
  'alert-circle': 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20ZM12 8v4.5M12 16h.01',
  'alert-triangle':
    'M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h16.9a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0ZM12 9v4M12 17h.01',
  info: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20ZM12 16.5V12M12 8h.01',
  clock: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20ZM12 6.5V12l3.5 2',
  pause: 'M10 4.5H6.5v15H10zM17.5 4.5H14v15h3.5z',
  refresh:
    'M20.5 12a8.5 8.5 0 0 1-14.4 6.1L3 15.2M3.5 12a8.5 8.5 0 0 1 14.4-6.1L21 8.8M21 4v4.8h-4.8M3 20v-4.8h4.8',
  package:
    'M21 16V8a2 2 0 0 0-1-1.7l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.7l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16ZM3.3 7 12 12l8.7-5M12 22V12M7.5 4.3l9 5.2',
  'cloud-off': 'M3 3l18 18M9.9 4.2A6 6 0 0 1 20 9a4 4 0 0 1 1.6 7.6M13.5 20H7a5 5 0 0 1-.8-9.9',
  'x-circle': 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20ZM15 9l-6 6M9 9l6 6',
  edit: 'M11 4H4.5a2 2 0 0 0-2 2v13.5a2 2 0 0 0 2 2H18a2 2 0 0 0 2-2V13M18.4 2.6a2.1 2.1 0 0 1 3 3L12 15l-4 1 1-4Z',
  list: 'M8.5 6H21M8.5 12H21M8.5 18H21M3.5 6h.01M3.5 12h.01M3.5 18h.01',
  play: 'M6 3.8v16.4L20 12Z',
  dot: 'M12 16.5a4.5 4.5 0 1 0 0-9 4.5 4.5 0 0 0 0 9Z',
  home: 'M3 10.2 12 3l9 7.2V20a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2ZM9.2 22v-8.4h5.6V22',
  users:
    'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 11.5a4.2 4.2 0 1 0 0-8.5 4.2 4.2 0 0 0 0 8.5ZM22 21v-2a4 4 0 0 0-3-3.9M16.2 3.2a4.2 4.2 0 0 1 0 8.1',
  ruler:
    'M16 2.2 21.8 8 8 21.8 2.2 16ZM7.4 10.6l2.2 2.2M10.6 7.4l2.2 2.2M4.2 13.8l2.2 2.2M13.8 4.2 16 6.4',
  clipboard:
    'M16 4.5h1.8a2 2 0 0 1 2 2V20a2 2 0 0 1-2 2H6.2a2 2 0 0 1-2-2V6.5a2 2 0 0 1 2-2H8M9.2 2.2h5.6a1 1 0 0 1 1 1v2.4a1 1 0 0 1-1 1H9.2a1 1 0 0 1-1-1V3.2a1 1 0 0 1 1-1Z',
  layout: 'M3.5 3.5h17v17h-17ZM3.5 9.2h17M9.2 20.5V9.2',
  scissors:
    'M6.5 9.2a3.2 3.2 0 1 0 0-6.4 3.2 3.2 0 0 0 0 6.4ZM6.5 21.2a3.2 3.2 0 1 0 0-6.4 3.2 3.2 0 0 0 0 6.4ZM20.5 3.5 8.8 15.2M14.2 14.2l6.3 6.3M8.8 8.8 12 12',
  receipt:
    'M4.5 2.5v19l2.3-1.5 2.4 1.5 2.4-1.5 2.4 1.5 2.4-1.5 2.3 1.5v-19L16.4 4 14 2.5 11.6 4 9.2 2.5 6.8 4ZM8.5 8h7M8.5 12h7M8.5 16h4.5',
  truck:
    'M10 17.5h4.5V5.5H2v12h2.5M20 17.5h2v-3.6a4 4 0 0 0-.7-2.2l-1.7-2.5a2 2 0 0 0-1.7-.9H14.5M7.2 20a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5ZM17.5 20a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z',
  'bar-chart': 'M12 20.5V9.5M18 20.5V3.5M6 20.5v-6M3 20.5h18',
  scan: 'M3 7.5V5.5a2 2 0 0 1 2-2h2M17 3.5h2a2 2 0 0 1 2 2v2M21 16.5v2a2 2 0 0 1-2 2h-2M7 20.5H5a2 2 0 0 1-2-2v-2M3 12h18',
  settings:
    'M12 15.2a3.2 3.2 0 1 0 0-6.4 3.2 3.2 0 0 0 0 6.4ZM19.3 14.5a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-2.8 1.2v.2a2 2 0 1 1-4 0v-.1a1.6 1.6 0 0 0-2.8-1.2l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1A1.6 1.6 0 0 0 3.2 14H3a2 2 0 1 1 0-4h.2a1.6 1.6 0 0 0 1.2-2.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.6 1.6 0 0 0 2.8-1.2V3a2 2 0 1 1 4 0v.2a1.6 1.6 0 0 0 2.8 1.2l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0 1.2 2.8H21a2 2 0 1 1 0 4h-.2a1.6 1.6 0 0 0-1.5 1Z',
  help: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20ZM9.2 9.2a3 3 0 0 1 5.8 1c0 2-3 3-3 3M12 17h.01',
  'chevron-right': 'm9.5 18.5 6.5-6.5-6.5-6.5',
  'chevron-down': 'm5.5 9.5 6.5 6.5 6.5-6.5',
  'chevron-up': 'm5.5 14.5 6.5-6.5 6.5 6.5',
  'chevron-left': 'm14.5 5.5-6.5 6.5 6.5 6.5',
  filter: 'M21.5 3.5h-19L10 12.9v6.1l4 2.5v-8.6Z',
  close: 'M18.5 5.5 5.5 18.5M5.5 5.5l13 13',
  search: 'M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16ZM21 21l-4.4-4.4',
  plus: 'M12 4.5v15M4.5 12h15',
  rupee: 'M6.5 3.5h11M6.5 8.5h11M6.5 13.5h4a5 5 0 0 0 0-10M6.5 13.5 15 21.5',
  phone:
    'M21.5 16.9v3a2 2 0 0 1-2.2 2 19.6 19.6 0 0 1-8.5-3 19.3 19.3 0 0 1-6-6 19.6 19.6 0 0 1-3-8.6 2 2 0 0 1 2-2.2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L7.8 9.9a16 16 0 0 0 6 6l1.2-1.2a2 2 0 0 1 2.1-.5c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.6 2.1Z',
  'external-link':
    'M14.5 3.5h6v6M11 13 20.5 3.5M18 13.5V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h5.5',
  menu: 'M3.5 6.5h17M3.5 12h17M3.5 17.5h17',
}
