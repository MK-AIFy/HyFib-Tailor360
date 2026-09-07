/**
 * Navigation messages — the bottom bar, the side navigation, the tab set and the help entry.
 *
 * Two groups, and the distinction matters.
 *
 * The **chrome** keys name the navigation landmarks themselves. `navigation.help.label` is the one
 * that is load-bearing: WCAG 3.2.6 Consistent Help and checklist items A11Y-10 and A11Y-85 both ask
 * whether help is in the same place *with the same name* on every screen, and the only way to keep a
 * name identical across every shell is for there to be exactly one string.
 *
 * The **destination** keys name the places a role can go. They live here rather than in a screen's
 * own family because the same word appears in the phone bottom bar, the desktop side navigation and
 * the tablet tab set, and three copies of "Production" is three chances for them to diverge.
 */
export const navigationEn = {
  /* Chrome -------------------------------------------------------------------------------- */
  'navigation.primary': 'Main navigation',
  'navigation.sections': 'Sections',
  'navigation.tabs': 'Sections of this screen',
  'navigation.badge':
    '{count, plural, one {# item needs attention} other {# items need attention}}',
  'navigation.help.label': 'Help',
  'navigation.help.description': 'How this screen works, and how to reach support',
  'navigation.support.label': 'Contact support',

  /* Destinations -------------------------------------------------------------------------- */
  'navigation.destination.home': 'Home',
  'navigation.destination.scan': 'Scan',
  'navigation.destination.customers': 'Customers',
  'navigation.destination.measurements': 'Measurements',
  'navigation.destination.orders': 'Orders',
  'navigation.destination.workboard': 'Workboard',
  'navigation.destination.production': 'Production',
  'navigation.destination.inventory': 'Inventory',
  'navigation.destination.billing': 'Billing',
  'navigation.destination.delivery': 'Delivery',
  'navigation.destination.reports': 'Reports',
  'navigation.destination.settings': 'Settings',
} as const

export const navigationTa: Record<keyof typeof navigationEn, string> = {
  'navigation.primary': 'முதன்மை வழிசெலுத்தல்',
  // not translated — awaiting native-speaker review
  'navigation.sections': 'Sections',
  // not translated — awaiting native-speaker review
  'navigation.tabs': 'Sections of this screen',
  // not translated — awaiting native-speaker review
  'navigation.badge':
    '{count, plural, one {# item needs attention} other {# items need attention}}',
  'navigation.help.label': 'உதவி',
  // not translated — awaiting native-speaker review
  'navigation.help.description': 'How this screen works, and how to reach support',
  // not translated — awaiting native-speaker review
  'navigation.support.label': 'Contact support',

  'navigation.destination.home': 'முகப்பு',
  // not translated — awaiting native-speaker review
  'navigation.destination.scan': 'Scan',
  'navigation.destination.customers': 'வாடிக்கையாளர்கள்',
  'navigation.destination.measurements': 'அளவுகள்',
  // not translated — awaiting native-speaker review
  'navigation.destination.orders': 'Orders',
  // not translated — awaiting native-speaker review
  'navigation.destination.workboard': 'Workboard',
  'navigation.destination.production': 'தையல்',
  // not translated — awaiting native-speaker review
  'navigation.destination.inventory': 'Stock',
  // not translated — awaiting native-speaker review
  'navigation.destination.billing': 'Bill',
  // not translated — awaiting native-speaker review
  'navigation.destination.delivery': 'Delivery',
  // not translated — awaiting native-speaker review
  'navigation.destination.reports': 'Reports',
  // not translated — awaiting native-speaker review
  'navigation.destination.settings': 'Settings',
}
