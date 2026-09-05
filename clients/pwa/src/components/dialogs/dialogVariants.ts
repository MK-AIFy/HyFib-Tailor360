import type { ShellKind } from '../../design-system/foundations/types'

/**
 * How a dialog presents itself.
 *
 * A `.ts` sibling rather than part of the component, for the reason the primitives family gives:
 * React Fast Refresh cannot refresh a module that exports both a component and a value, and a
 * variant list is data a story and a test both want without importing anything that renders.
 *
 *   auto    a sheet on a phone, a centred dialog on a tablet or a desktop. The default, and the
 *           blueprint's rule: a phone dialog belongs at the bottom of the screen, in thumb reach,
 *           because the other hand is holding a garment
 *   sheet   forced to the bottom sheet, whatever the width
 *   centre  forced to the centred dialog
 *   drawer  a panel on the inline edge, full height — filters and detail panes on a desktop, where
 *           the list behind it stays visible and in place
 */
export const DIALOG_PRESENTATIONS = ['auto', 'sheet', 'centre', 'drawer'] as const

export type DialogPresentation = (typeof DIALOG_PRESENTATIONS)[number]

/** What a dialog actually renders as, once `auto` has been resolved against the shell. */
export type ResolvedDialogPresentation = Exclude<DialogPresentation, 'auto'>

/** The heading level a dialog title renders as. A dialog is a new context, so `h2` is the default. */
export type DialogHeadingLevel = 2 | 3 | 4

/**
 * Resolves `auto` against the shell the screen is in.
 *
 * A pure function, and separate from the component, because this one line is the blueprint's rule
 * about phones — a dialog on a phone is a bottom sheet, in thumb reach, at the end of the screen the
 * hand is already near — and a rule stated as a function is a rule a test can hold to.
 */
export function resolveDialogPresentation(
  presentation: DialogPresentation,
  shellKind: ShellKind,
): ResolvedDialogPresentation {
  if (presentation !== 'auto') {
    return presentation
  }
  return shellKind === 'phone' ? 'sheet' : 'centre'
}
