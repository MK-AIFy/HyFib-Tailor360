import { SideNav } from '../navigation/SideNav'
import { ShellFooter, ShellHeader, ShellMain, ShellUtilities } from './ShellParts'
import type { ShellLayoutProps } from './PhoneShell'

/**
 * The tablet layout: a persistent navigation rail beside a master-detail working area.
 *
 * A counter tablet is docked, propped or held in two hands, and in landscape its bottom edge is the
 * furthest point from either thumb — so it gets a rail rather than a bottom bar. The rail is
 * ungrouped: at 768 px four group headings cost more height than they buy in orientation, and a
 * tablet's destination list is short enough to read at a glance.
 *
 * The working area is `MasterDetail`, used by the screen rather than by the shell — measurement
 * capture, the job queue, a job card, stock, a bill. It splits or stacks on the width of its own
 * container, which is what makes docs/nfr/support-matrix.md section 5 true without an orientation
 * check anywhere: portrait gives a container under 768 px and stacks, landscape gives one over it
 * and splits, and 1.3.4 Orientation cannot be broken by a layout that never asks.
 *
 * Help, support and display settings sit in the header, in the same relative position as on the
 * phone (3.2.6), because the rail here has no footer of its own to keep them in.
 */
export function TabletShell({ navigation, children }: ShellLayoutProps) {
  return (
    <>
      <ShellHeader>
        <ShellUtilities placement="bar" />
      </ShellHeader>

      <div className="app-shell__body">
        <SideNav
          className="app-shell__rail"
          items={[
            ...navigation.railItems,
            ...navigation.railSections.flatMap((section) => section.items),
          ]}
        />
        <ShellMain>{children}</ShellMain>
      </div>

      <ShellFooter />
    </>
  )
}
