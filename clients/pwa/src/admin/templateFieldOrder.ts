import type { TemplateField } from './types'

/**
 * What "move this field up" means, and which writes it costs.
 *
 * ## The decision this module implements, and why it is the one
 *
 * `displayOrder` is a single integer per field, **global to the version** — not scoped to a group,
 * not unique and not contiguous. `TemplateField.Check` asserts only that it is non-negative, and the
 * payload is ordered by `displayOrder` then by `key`, so equal numbers break alphabetically.
 *
 * Group order is *derived*, not stored: `TemplateVersion.InGroupOrder()` orders groups by the
 * display order of each group's first field. Nothing exposes that helper through the API, and there
 * is no group entity, no order column and no endpoint that lists, renames or moves a group.
 *
 * Three readings of a move were available, and only one of them is safe:
 *
 *  - **Swap the two numbers.** Two writes, but the sequence stays arbitrary, and a swap between two
 *    fields that happen to share a number does nothing at all — the tie still breaks alphabetically.
 *  - **Renumber the group contiguously.** Predictable within the group, and it steps over the
 *    numbers of fields in other groups. Since group order is derived from each group's *first*
 *    field, a move that changes a group's minimum number silently reorders the groups.
 *  - **Renumber the whole version contiguously.** The only reading under which a within-group move
 *    cannot move a group, and the only one that removes ties, so the order on screen is the order
 *    stored rather than the order stored plus an alphabetical tie-break.
 *
 * This module renumbers the whole version and then **writes only the fields whose number actually
 * changed**. That is what makes the cost acceptable: the first move on a version normalises it, and
 * every move after that on a contiguous version changes exactly the fields between the two
 * positions — two, for an adjacent swap.
 *
 * The decision is recorded in `docs/prd/assumptions-and-open-decisions.md` as **OD-17**, because it
 * is a product decision this slice needed an answer to rather than one somebody had made.
 *
 * ## Why the writes are a list rather than a call
 *
 * There is no reorder endpoint and no bulk field save: nothing accepts a list of identifiers or two
 * changed orders in one request. A move is one full-body `PUT …/fields/{fieldId}` per affected
 * field, **sequentially**, each carrying its own retry key and each needing the `ETag` the previous
 * response returned, because the template's version advances on every write. So this module returns
 * the plan and the screen performs it, which is what lets a failure part-way be reported honestly
 * rather than presented as a move that half happened.
 */

/** One group of fields, in the order the capture wizard will ask for them. */
export interface FieldGroup {
  readonly name: string
  readonly fields: readonly TemplateField[]
}

/**
 * The fields of a version, grouped and ordered as they will be captured.
 *
 * Groups appear in the order their first field does, which is `InGroupOrder()`'s rule; fields within
 * a group are ordered by `displayOrder` then by `key`, which is the server's own ordering, so the
 * screen shows what a capture wizard would show rather than a second opinion about it.
 */
export function groupFields(fields: readonly TemplateField[]): readonly FieldGroup[] {
  const sorted = [...fields].sort(compareFields)
  const groups: FieldGroup[] = []

  for (const field of sorted) {
    const last = groups.at(-1)
    const existing = groups.find((group) => group.name === field.groupName)

    if (last !== undefined && last.name === field.groupName) {
      groups[groups.length - 1] = { name: last.name, fields: [...last.fields, field] }
      continue
    }

    if (existing === undefined) {
      groups.push({ name: field.groupName, fields: [field] })
      continue
    }

    // A group whose fields are not contiguous in the ordering. The server would capture them
    // together, so they are shown together, under the position the group's first field earned.
    groups[groups.indexOf(existing)] = {
      name: existing.name,
      fields: [...existing.fields, field],
    }
  }

  return groups
}

function compareFields(left: TemplateField, right: TemplateField): number {
  const byOrder = Number(left.displayOrder) - Number(right.displayOrder)
  return byOrder !== 0 ? byOrder : left.key.localeCompare(right.key, 'en')
}

/** One field to write, and the number to write on it. */
export interface OrderWrite {
  readonly field: TemplateField
  readonly displayOrder: number
}

/** Which way a move goes. */
export type MoveDirection = 'up' | 'down'

/**
 * The flat sequence a version is renumbered against: every group's fields, in group order.
 *
 * Exported because the screen renders from it and the tests assert against it, and because a
 * sequence derived twice is a sequence that can disagree with itself.
 */
export function orderedFields(fields: readonly TemplateField[]): readonly TemplateField[] {
  return groupFields(fields).flatMap((group) => group.fields)
}

/**
 * The writes that move one field one place within its group.
 *
 * Returns an empty list when the field is already at the end it is being moved towards, so a screen
 * can offer the control and have it honestly do nothing rather than hide it — a control that
 * disappears at the boundary is a control whose position moves under the pointer.
 *
 * @param fields Every field of the version, in any order.
 * @param fieldId The field being moved.
 * @param direction Which way.
 */
export function planFieldMove(
  fields: readonly TemplateField[],
  fieldId: string,
  direction: MoveDirection,
): readonly OrderWrite[] {
  const groups = groupFields(fields)
  const group = groups.find((candidate) =>
    candidate.fields.some((field) => field.templateFieldId === fieldId),
  )

  if (group === undefined) {
    return []
  }

  const at = group.fields.findIndex((field) => field.templateFieldId === fieldId)
  const to = direction === 'up' ? at - 1 : at + 1

  if (to < 0 || to >= group.fields.length) {
    return []
  }

  const reordered = groups.map((candidate) =>
    candidate === group ? { name: group.name, fields: swap(group.fields, at, to) } : candidate,
  )

  return writesFor(reordered)
}

/**
 * The writes that move a whole group one place.
 *
 * A group is moved as a block, because its position *is* the position of its first field — moving
 * one field of it would either take the group with it or split the group, and neither is what
 * somebody dragging a step heading means.
 */
export function planGroupMove(
  fields: readonly TemplateField[],
  groupName: string,
  direction: MoveDirection,
): readonly OrderWrite[] {
  const groups = groupFields(fields)
  const at = groups.findIndex((group) => group.name === groupName)

  if (at < 0) {
    return []
  }

  const to = direction === 'up' ? at - 1 : at + 1

  if (to < 0 || to >= groups.length) {
    return []
  }

  return writesFor(swap(groups, at, to))
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
 * The renumbering, minus the fields that already carry the number they would be given.
 *
 * This subtraction is the whole reason renumbering the version is affordable. A move on a version
 * that is already contiguous touches only the fields between the two positions; the expensive case
 * is the first move on a version whose numbers were never contiguous, and it happens once.
 */
function writesFor(groups: readonly FieldGroup[]): readonly OrderWrite[] {
  const writes: OrderWrite[] = []
  let order = 0

  for (const group of groups) {
    for (const field of group.fields) {
      if (Number(field.displayOrder) !== order) {
        writes.push({ field, displayOrder: order })
      }
      order += 1
    }
  }

  // Ascending by construction, so a partial failure leaves a prefix of the sequence renumbered
  // rather than a scatter: the screen can say which field it stopped at, and every number before
  // that one is already right.
  return writes
}
