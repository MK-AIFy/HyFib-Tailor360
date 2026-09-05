/**
 * The navigation family.
 *
 * Three shells' worth of navigation driven by one list of destinations — the phone bottom bar, the
 * desktop rail and the within-screen tab strip — plus the help entry that 3.2.6 requires to sit in
 * the same place with the same name on every one of them.
 */
export { BottomNav } from './BottomNav'
export type { BottomNavProps } from './BottomNav'
export { HELP_HREF, HelpEntry, SUPPORT_HREF } from './HelpEntry'
export type { HelpEntryPlacement, HelpEntryProps } from './HelpEntry'
export { NavBadge } from './NavBadge'
export type { NavBadgeProps } from './NavBadge'
export { MAX_BOTTOM_NAV_ITEMS } from './navigationItems'
export type { NavigationItem, NavigationSection } from './navigationItems'
export { SideNav } from './SideNav'
export type { SideNavProps } from './SideNav'
export { Tabs } from './Tabs'
export type { TabItem, TabsProps } from './Tabs'
export { useVirtualKeyboardOpen } from './useVirtualKeyboardOpen'
