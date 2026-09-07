/**
 * The layout family: the application shell and the three role-optimised layouts.
 *
 * This is the family that turns the design system into a screen. It owns the decision of which
 * layout a container is wide enough for, the WCAG 2.2 mechanics that only a shell can implement —
 * `scroll-padding-bottom` matched to the bottom bar, the bar hidden while the virtual keyboard is
 * open (2.4.11), the route announcer that moves focus to the page heading (4.1.3) — and the
 * persistent status regions that a scan result, the sync state and an autosave are published into.
 *
 * A family barrel rather than one root index, for the same reason every other family has one: a
 * single index that every family had to be added to would be the one file every change touches.
 */
export { AppShell, DEFAULT_JOURNEY_ROLE } from './AppShell'
export type { AppShellProps } from './AppShell'
export { DesktopShell } from './DesktopShell'
export { DisplayPreferencesPanel } from './DisplayPreferencesPanel'
export type { DisplayPreferencesPanelProps } from './DisplayPreferencesPanel'
export { MasterDetail } from './MasterDetail'
export type { MasterDetailArrangement, MasterDetailProps } from './MasterDetail'
export { PhoneShell } from './PhoneShell'
export type { ShellLayoutProps } from './PhoneShell'
export { PrimaryActionFab } from './PrimaryActionFab'
export type { PrimaryActionFabProps } from './PrimaryActionFab'
export { PAGE_HEADING_ATTRIBUTE, RouteAnnouncer } from './RouteAnnouncer'
export type { RouteAnnouncerProps } from './RouteAnnouncer'
export {
  DESTINATIONS,
  DESTINATION_IDS,
  PRIMARY_ACTIONS,
  ROLE_NAVIGATION,
  useRoleNavigation,
} from './roleNavigation'
export type {
  DestinationId,
  NavigationBadges,
  PrimaryAction,
  PrimaryActionId,
  RoleNavigation,
} from './roleNavigation'
export { ShellStatusContext } from './shellStatus'
export type {
  ScanOutcome,
  ScanStatusMessage,
  ShellStatusValue,
  SyncStatusMessage,
} from './shellStatus'
export { ShellStatusProvider } from './ShellStatusProvider'
export type { ShellStatusProviderProps } from './ShellStatusProvider'
export { ShellStatusRegions } from './ShellStatusRegions'
export type { ShellStatusRegionsProps } from './ShellStatusRegions'
export {
  DISPLAY_SETTINGS_HREF,
  ShellFooter,
  ShellHeader,
  ShellMain,
  ShellUtilities,
} from './ShellParts'
export { TabletShell } from './TabletShell'
export { useElementWidth } from './useElementWidth'
export { useShellKind } from './useShellKind'
export { useShellStatus } from './useShellStatus'
export { KEYBOARD_THRESHOLD_PX, useVirtualKeyboard } from './useVirtualKeyboard'
export type { VirtualKeyboardState } from './useVirtualKeyboard'
