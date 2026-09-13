import type { CatalogDesignGroup, CatalogDesignOption } from './types'

/**
 * What "move this group up" and "move this option up" mean, and which writes they cost (#141).
 *
 * The same reading `admin/templateFieldOrder.ts` gives measurement fields, for the same reason:
 * `displayOrder` is a plain integer with no reorder endpoint and no bulk save, so a move is one
 * full-body `PUT` per row whose number actually changes. Unlike a template field's version-wide
 * order, a design group's scope is already its category — it is one of the category's own groups —
 * and an option's scope is already its group, so there is no cross-scope renumbering to reason about
 * here: each function renumbers exactly the list it was handed, contiguously, and returns only the
 * rows that changed.
 */

export type MoveDirection = 'up' | 'down'

/** One design group to write, and the number to write on it. */
export interface GroupOrderWrite {
  readonly group: CatalogDesignGroup
  readonly displayOrder: number
}

/** One design option to write, and the number to write on it. */
export interface OptionOrderWrite {
  readonly option: CatalogDesignOption
  readonly displayOrder: number
}

function orderedBy<T extends { readonly displayOrder: number; readonly code: string }>(
  items: readonly T[],
): readonly T[] {
  return [...items].sort(
    (left, right) =>
      left.displayOrder - right.displayOrder || left.code.localeCompare(right.code, 'en'),
  )
}

function swap<T>(items: readonly T[], left: number, right: number): readonly T[] {
  const copy = [...items]
  const a = copy[left]
  const b = copy[right]

  if (a === undefined || b === undefined) {
    return items
  }

  copy[left] = b
  copy[right] = a
  return copy
}

/**
 * The writes that move one design group one place within the list it belongs to (a category's
 * groups, in caller-supplied scope).
 *
 * Returns an empty list when the group is already at the end it is being moved towards, so a screen
 * can offer the control and have it honestly do nothing rather than hide it at the boundary.
 */
export function planDesignGroupMove(
  groups: readonly CatalogDesignGroup[],
  designOptionGroupId: string,
  direction: MoveDirection,
): readonly GroupOrderWrite[] {
  const ordered = orderedBy(groups)
  const at = ordered.findIndex((group) => group.designOptionGroupId === designOptionGroupId)
  const to = direction === 'up' ? at - 1 : at + 1

  if (at < 0 || to < 0 || to >= ordered.length) {
    return []
  }

  const writes: GroupOrderWrite[] = []

  swap(ordered, at, to).forEach((group, index) => {
    if (group.displayOrder !== index) {
      writes.push({ group, displayOrder: index })
    }
  })

  return writes
}

/** The writes that move one design option one place within the group it belongs to. */
export function planDesignOptionMove(
  options: readonly CatalogDesignOption[],
  designOptionId: string,
  direction: MoveDirection,
): readonly OptionOrderWrite[] {
  const ordered = orderedBy(options)
  const at = ordered.findIndex((option) => option.designOptionId === designOptionId)
  const to = direction === 'up' ? at - 1 : at + 1

  if (at < 0 || to < 0 || to >= ordered.length) {
    return []
  }

  const writes: OptionOrderWrite[] = []

  swap(ordered, at, to).forEach((option, index) => {
    if (option.displayOrder !== index) {
      writes.push({ option, displayOrder: index })
    }
  })

  return writes
}
