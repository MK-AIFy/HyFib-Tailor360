import type { CatalogDesignOperand, DesignPickerGroup } from './types'

/**
 * Resolves an operand's codes to the names the picker shows, for the sentence a rule's effect is
 * explained with (#142). Pure and locale-agnostic; the caller joins the names with
 * `Intl.ListFormat` and wraps them in a localised message — this only resolves what a code means.
 */

function groupOf(
  groups: readonly DesignPickerGroup[],
  groupCode: string | null,
): DesignPickerGroup | undefined {
  return groupCode === null ? undefined : groups.find((group) => group.code === groupCode)
}

/** The group's own name, or its code when the group cannot be resolved. */
export function operandGroupName(
  groups: readonly DesignPickerGroup[],
  operand: CatalogDesignOperand,
): string | null {
  if (operand.groupCode === null) {
    return null
  }
  return groupOf(groups, operand.groupCode)?.name ?? operand.groupCode
}

/** The option names an operand's codes resolve to, in the order given — falling back to the code. */
export function operandOptionNames(
  groups: readonly DesignPickerGroup[],
  operand: CatalogDesignOperand,
): readonly string[] {
  const group = groupOf(groups, operand.groupCode)
  return operand.optionCodes.map(
    (code) => group?.options.find((option) => option.code === code)?.name ?? code,
  )
}

/** One option's own name within a group, or its code when it cannot be resolved. */
export function optionName(
  groups: readonly DesignPickerGroup[],
  groupCode: string,
  optionCode: string,
): string {
  return (
    groupOf(groups, groupCode)?.options.find((option) => option.code === optionCode)?.name ??
    optionCode
  )
}
