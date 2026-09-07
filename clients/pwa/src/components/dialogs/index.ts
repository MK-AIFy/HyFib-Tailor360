/**
 * The dialogs family: modals, sheets, drawers, the three-tier confirmation and the undo bar.
 *
 * Everything here is about the moment before something irreversible happens, or the moment just
 * after something reversible did. docs/nfr/accessibility-localisation.md section 8.4 pairs the two
 * deliberately — non-sensitive field actions get an undo afterwards, sensitive ones ask first — and
 * the tiers in `confirmTiers.ts` are what decides which.
 *
 * There is no barrel at the root of `src/components` on purpose. A consumer imports from
 * `.../components/dialogs`, so a family can be added without every family's change touching one
 * shared index.
 */
export { BottomSheet } from './BottomSheet'
export type { BottomSheetProps } from './BottomSheet'
export { ConfirmDialog } from './ConfirmDialog'
export type { ConfirmDialogProps, ConfirmOutcome } from './ConfirmDialog'
export { Dialog } from './Dialog'
export type { DialogProps } from './Dialog'
export { Drawer } from './Drawer'
export type { DrawerProps } from './Drawer'
export { UndoBar } from './UndoBar'
export type { UndoBarProps } from './UndoBar'

export { CONFIRM_TIERS, resolveConfirmTier } from './confirmTiers'
export type { ConfirmTier, ResolvedConfirmTier } from './confirmTiers'
export { DIALOG_PRESENTATIONS, resolveDialogPresentation } from './dialogVariants'
export type {
  DialogHeadingLevel,
  DialogPresentation,
  ResolvedDialogPresentation,
} from './dialogVariants'
export { focusableElements } from './focusable'
export { DIALOG_ROOT_ATTRIBUTE, useDialogBehaviour } from './useDialogBehaviour'
export type { DialogBehaviour, DialogBehaviourOptions } from './useDialogBehaviour'
export { useViewportShellKind } from './useViewportShellKind'
export { UNDO_WINDOW_MS, useUndoWindow } from './useUndoWindow'
export type { UndoWindow, UndoWindowOptions } from './useUndoWindow'
