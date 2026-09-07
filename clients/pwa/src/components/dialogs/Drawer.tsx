import { cx } from '../../design-system/foundations/cx'
import { Dialog } from './Dialog'
import type { DialogProps } from './Dialog'

export interface DrawerProps extends Omit<DialogProps, 'presentation'> {
  /**
   * Which edge it comes from. Defaults to the inline end — the trailing edge, which is the right in
   * an English or Tamil layout and would follow the writing direction if a right-to-left locale were
   * ever added. Filters belong on the end; navigation on the start.
   */
  readonly side?: 'inline-start' | 'inline-end'
}

/**
 * A full-height panel on one edge, with the screen behind it still visible.
 *
 * The shape earns its place on a desktop and a tablet, where a filter panel or a detail pane can
 * open without covering the list a person is working through — they keep their place in a queue of
 * two hundred rows, which a centred dialog takes away. It is still a modal: the list behind is
 * `inert` while it is open, because a filter panel that can be tabbed past is a filter panel whose
 * Apply button nobody finds.
 *
 * On a phone, prefer `Dialog` — its `auto` presentation gives a sheet, which is where a thumb is.
 */
export function Drawer({ side = 'inline-end', className, ...rest }: DrawerProps) {
  return (
    <Dialog
      {...rest}
      className={cx(
        side === 'inline-start' ? 'dialog--drawer-start' : 'dialog--drawer-end',
        className,
      )}
      presentation="drawer"
    />
  )
}
