import { SideNav } from '../navigation/SideNav'
import { ShellFooter, ShellHeader, ShellMain, ShellUtilities } from './ShellParts'
import type { ShellLayoutProps } from './PhoneShell'

/**
 * The desktop layout: a grouped navigation rail and a dense working area.
 *
 * The back-office desktops — billing, reconciliation, reports, administration — are keyboard-driven,
 * so the rail is a permanent list rather than a menu that opens. A navigation that has to be opened
 * before it can be tabbed puts an extra keystroke in front of every move a Cashier makes all day,
 * and it makes 2.4.5 Multiple Ways and 3.2.3 Consistent Navigation the responsibility of each screen
 * instead of the shell's.
 *
 * The rail is grouped here, unlike the tablet's: a desktop role reaches every destination it has,
 * which is eight for an Owner, and eight ungrouped rows is a list to read rather than a map to scan.
 * The group headings are real `h2`s inside the rail, so heading navigation works.
 *
 * Help, support and display settings sit at the foot of the rail — the same slot on every desktop
 * screen, which is what 3.2.6 Consistent Help and checklist items A11Y-10 and A11Y-85 ask for.
 *
 * **Density is a preference, not a layout.** "Dense tables and dashboards" is what the #50 blueprint
 * asks of this shell, and it is delivered by the `data-density` attribute the display preferences
 * write on `<html>`, not by a rule that only applies here. That matters because the compact 32 px
 * control class of docs/nfr/accessibility-localisation.md section 5 is desktop-only, and the rule in
 * themes.css that restores 44 px on a coarse pointer is what enforces it — a touchscreen laptop is
 * inside the support matrix, and its user gets full-size targets whatever the preference says.
 */
export function DesktopShell({ navigation, children }: ShellLayoutProps) {
  return (
    <>
      <ShellHeader />

      <div className="app-shell__body">
        <SideNav
          className="app-shell__rail"
          items={navigation.railItems}
          sections={navigation.railSections}
          footer={<ShellUtilities placement="side" showSupport />}
        />
        <ShellMain>{children}</ShellMain>
      </div>

      <ShellFooter />
    </>
  )
}
