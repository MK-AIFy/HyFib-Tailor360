/**
 * The message families, composed into one catalogue per language.
 *
 * A component family owns exactly one file in this directory and adds one line to each of the two
 * objects below. Nothing else in the internationalisation layer has to change, which is what lets
 * the design-system families be built independently of one another.
 *
 * Adding a family:
 *   1. create `messages/<family>.ts` exporting `<family>En` and `<family>Ta`, following the pattern
 *      and the rules documented at the top of `messages/shell.ts`;
 *   2. add it to both spreads here.
 *
 * The type gate is in en-IN.ts: `MessageCatalogue` is derived from the English composition, and the
 * Tamil composition is annotated with it, so a key present in one language and missing from the
 * other fails `pnpm typecheck`.
 */
import { dialogsEn, dialogsTa } from './dialogs'
import { formsEn, formsTa } from './forms'
import { installEn, installTa } from './install'
import { layoutEn, layoutTa } from './layout'
import { navigationEn, navigationTa } from './navigation'
import { primitivesEn, primitivesTa } from './primitives'
import { shellEn, shellTa } from './shell'
import { statesEn, statesTa } from './states'
import { unitsEn, unitsTa } from './units'

export const messagesEn = {
  ...layoutEn,
  ...navigationEn,
  ...primitivesEn,
  ...shellEn,
  ...formsEn,
  ...installEn,
  ...statesEn,
  ...dialogsEn,
  ...unitsEn,
} as const

export const messagesTa = {
  ...layoutTa,
  ...navigationTa,
  ...primitivesTa,
  ...shellTa,
  ...formsTa,
  ...installTa,
  ...statesTa,
  ...dialogsTa,
  ...unitsTa,
}
