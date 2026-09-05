import { Dialog } from './Dialog'
import type { DialogProps } from './Dialog'

export type BottomSheetProps = Omit<DialogProps, 'presentation'>

/**
 * A modal that always arrives from the bottom edge, whatever the width.
 *
 * `Dialog` already chooses a sheet on a phone, so this exists for the cases where the shape is the
 * point rather than the width: a picker over a tablet's master-detail layout, where a centred dialog
 * would cover both panes, or a set of actions belonging to a row the person is looking at near the
 * bottom of a long list. On a desktop it stays a sheet, which is a deliberate and visible choice
 * rather than a layout that forgot to adapt.
 *
 * Everything else — focus, `inert`, Escape, the returned focus, the gesture-bar clearance under the
 * footer — is `Dialog`'s, and is documented there.
 */
export function BottomSheet(props: BottomSheetProps) {
  return <Dialog {...props} presentation="sheet" />
}
