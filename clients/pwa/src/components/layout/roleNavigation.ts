import { useIntl } from 'react-intl'
import type { IconName } from '../primitives/icons'
import type { NavigationItem, NavigationSection } from '../navigation/navigationItems'
import { MAX_BOTTOM_NAV_ITEMS } from '../navigation/navigationItems'
import type { JourneyRole } from '../../design-system/foundations/types'

/**
 * Where each role can go, and what its one primary action is.
 *
 * ## Why the shell owns this and the navigation components do not
 *
 * `BottomNav`, `SideNav` and `Tabs` render a list of destinations. *Which* list is a question about
 * the person holding the device, and the answer has to be identical in all three or 3.2.3 Consistent
 * Navigation is broken the moment somebody rotates a tablet. So there is one table, here, and the
 * three shells read it.
 *
 * ## The rules the table obeys
 *
 *  - **Home is first for every role.** Checklist item A11Y-10 asks whether the navigation is in the
 *    same place with the same names as on the previous screen; a first destination that moves per
 *    role is the same failure across a shift on a shared device.
 *  - **At most five destinations in the bottom bar** (`MAX_BOTTOM_NAV_ITEMS`). At the 320 px reflow
 *    floor a sixth 44 px target with a legible label does not fit, and shrinking the targets to make
 *    it fit is the trade docs/nfr/accessibility-localisation.md section 5 refuses outright.
 *  - **The desktop rail shows everything the role can reach**, grouped. A desktop has the room, and
 *    2.4.5 Multiple Ways is easier to satisfy with a visible list than with a menu.
 *  - **Every role has at most one primary action**, and on a phone it is the floating action button.
 *    For the four shop-floor roles that action is Scan, which is what the #50 blueprint means by a
 *    scanner-first phone layout; for the others it is the one thing they came to the device to do.
 *
 * The eight roles are the journey roles of `A11Y-RJ-01`..`A11Y-RJ-08`, not the RACI columns — see
 * the note on `JOURNEY_ROLES` in the foundations.
 */

/** Every place the shell knows how to send somebody. */
export const DESTINATION_IDS = [
  'home',
  'scan',
  'customers',
  'measurements',
  'orders',
  'workboard',
  'production',
  'inventory',
  'billing',
  'delivery',
  'reports',
  'settings',
] as const

export type DestinationId = (typeof DESTINATION_IDS)[number]

interface DestinationDescriptor {
  readonly href: string
  readonly icon: IconName
  /** The catalogue key. The word itself is owned by the navigation family, so all three shells agree. */
  readonly messageId: string
  /** Matches the path exactly. Only the root needs it, or every screen would be "current". */
  readonly end?: boolean
}

export const DESTINATIONS: Record<DestinationId, DestinationDescriptor> = {
  home: { href: '/', icon: 'home', messageId: 'navigation.destination.home', end: true },
  scan: { href: '/scan', icon: 'scan', messageId: 'navigation.destination.scan' },
  customers: { href: '/customers', icon: 'users', messageId: 'navigation.destination.customers' },
  measurements: {
    href: '/measurements',
    icon: 'ruler',
    messageId: 'navigation.destination.measurements',
  },
  orders: { href: '/orders', icon: 'clipboard', messageId: 'navigation.destination.orders' },
  workboard: { href: '/workboard', icon: 'layout', messageId: 'navigation.destination.workboard' },
  production: {
    href: '/production',
    icon: 'scissors',
    messageId: 'navigation.destination.production',
  },
  inventory: { href: '/inventory', icon: 'package', messageId: 'navigation.destination.inventory' },
  billing: { href: '/billing', icon: 'receipt', messageId: 'navigation.destination.billing' },
  delivery: { href: '/delivery', icon: 'truck', messageId: 'navigation.destination.delivery' },
  reports: { href: '/reports', icon: 'bar-chart', messageId: 'navigation.destination.reports' },
  settings: { href: '/settings', icon: 'settings', messageId: 'navigation.destination.settings' },
}

/** The one action a role reaches for with a garment in the other hand. */
export interface PrimaryAction {
  readonly id: string
  readonly href: string
  readonly icon: IconName
  readonly messageId: string
}

export const PRIMARY_ACTIONS = {
  scan: { id: 'scan', href: '/scan', icon: 'scan', messageId: 'layout.action.scan' },
  newOrder: {
    id: 'newOrder',
    href: '/orders/new',
    icon: 'plus',
    messageId: 'layout.action.newOrder',
  },
  capture: {
    id: 'capture',
    href: '/measurements/new',
    icon: 'ruler',
    messageId: 'layout.action.capture',
  },
  takePayment: {
    id: 'takePayment',
    href: '/billing/new',
    icon: 'rupee',
    messageId: 'layout.action.takePayment',
  },
} as const satisfies Record<string, PrimaryAction>

export type PrimaryActionId = keyof typeof PRIMARY_ACTIONS

interface RoleNavigationDescriptor {
  /** Everything the role can reach, in rail order. */
  readonly destinations: readonly DestinationId[]
  /** The subset the phone bottom bar shows, in bar order. Never more than five. */
  readonly bottomBar: readonly DestinationId[]
  /** The floating action on a phone, or null for a role whose work is reading. */
  readonly primaryAction: PrimaryActionId | null
}

export const ROLE_NAVIGATION: Record<JourneyRole, RoleNavigationDescriptor> = {
  reception: {
    destinations: ['home', 'customers', 'measurements', 'orders', 'billing', 'settings'],
    bottomBar: ['home', 'customers', 'measurements', 'orders', 'billing'],
    primaryAction: 'newOrder',
  },
  'measurement-staff': {
    destinations: ['home', 'customers', 'measurements', 'orders'],
    bottomBar: ['home', 'customers', 'measurements', 'orders'],
    primaryAction: 'capture',
  },
  tailor: {
    destinations: ['home', 'scan', 'production', 'orders'],
    bottomBar: ['home', 'scan', 'production', 'orders'],
    primaryAction: 'scan',
  },
  'tailor-master': {
    destinations: ['home', 'scan', 'workboard', 'production', 'orders', 'reports'],
    bottomBar: ['home', 'scan', 'workboard', 'production', 'orders'],
    primaryAction: 'scan',
  },
  inventory: {
    destinations: ['home', 'scan', 'inventory', 'orders'],
    bottomBar: ['home', 'scan', 'inventory', 'orders'],
    primaryAction: 'scan',
  },
  cashier: {
    destinations: ['home', 'customers', 'orders', 'billing', 'reports'],
    bottomBar: ['home', 'customers', 'orders', 'billing', 'reports'],
    primaryAction: 'takePayment',
  },
  delivery: {
    destinations: ['home', 'scan', 'delivery', 'orders'],
    bottomBar: ['home', 'scan', 'delivery', 'orders'],
    primaryAction: 'scan',
  },
  owner: {
    destinations: [
      'home',
      'workboard',
      'orders',
      'customers',
      'inventory',
      'billing',
      'reports',
      'settings',
    ],
    bottomBar: ['home', 'workboard', 'orders', 'billing', 'reports'],
    primaryAction: null,
  },
}

/**
 * How the rail groups the destinations.
 *
 * Order matters and is the same for every role, so a person who moves between roles on a shared
 * desktop finds Billing in the same group in the same place. A group with nothing in it for this
 * role is dropped rather than shown empty.
 */
const SECTION_ORDER = [
  {
    id: 'work',
    messageId: 'layout.section.work',
    members: ['scan', 'workboard', 'production', 'measurements', 'delivery', 'inventory'],
  },
  { id: 'customers', messageId: 'layout.section.customers', members: ['customers', 'orders'] },
  { id: 'money', messageId: 'layout.section.money', members: ['billing', 'reports'] },
  { id: 'manage', messageId: 'layout.section.manage', members: ['settings'] },
] as const satisfies readonly {
  id: string
  messageId: string
  members: readonly DestinationId[]
}[]

/** A count to show beside a destination, keyed by destination id. */
export type NavigationBadges = Partial<Record<DestinationId, number>>

/** Everything a shell needs to draw its navigation for one role. */
export interface RoleNavigation {
  /** For `BottomNav`. */
  readonly bottomBar: readonly NavigationItem[]
  /** For `SideNav`'s ungrouped list — Home, which belongs to no group. */
  readonly railItems: readonly NavigationItem[]
  /** For `SideNav`'s grouped lists. */
  readonly railSections: readonly NavigationSection[]
  /** The floating action, already translated, or null. */
  readonly primaryAction: (PrimaryAction & { readonly label: string }) | null
}

/**
 * Builds one role's navigation, with every label taken from the catalogue.
 *
 * A hook rather than a function because the labels are translated, and translation is context. The
 * table above stays pure and is asserted by `roleNavigation.test.ts` without rendering anything.
 */
export function useRoleNavigation(
  role: JourneyRole,
  badges: NavigationBadges = {},
): RoleNavigation {
  const intl = useIntl()
  const descriptor = ROLE_NAVIGATION[role]

  const toItem = (id: DestinationId): NavigationItem => {
    const destination = DESTINATIONS[id]
    const count = badges[id]
    return {
      id,
      label: intl.formatMessage({ id: destination.messageId }),
      href: destination.href,
      icon: destination.icon,
      ...(destination.end === undefined ? {} : { end: destination.end }),
      ...(count === undefined ? {} : { badgeCount: count }),
    }
  }

  const reachable = new Set<DestinationId>(descriptor.destinations)

  const railSections: NavigationSection[] = SECTION_ORDER.map((section) => ({
    id: section.id,
    label: intl.formatMessage({ id: section.messageId }),
    items: section.members.filter((id) => reachable.has(id)).map(toItem),
  })).filter((section) => section.items.length > 0)

  const action =
    descriptor.primaryAction === null ? null : PRIMARY_ACTIONS[descriptor.primaryAction]

  return {
    bottomBar: descriptor.bottomBar.slice(0, MAX_BOTTOM_NAV_ITEMS).map(toItem),
    railItems: descriptor.destinations.filter((id) => id === 'home').map(toItem),
    railSections,
    primaryAction:
      action === null ? null : { ...action, label: intl.formatMessage({ id: action.messageId }) },
  }
}
