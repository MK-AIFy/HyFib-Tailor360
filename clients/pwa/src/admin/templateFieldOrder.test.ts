import { describe, expect, it } from 'vitest'
import { aTemplateField } from './testing/fixtures'
import { groupFields, orderedFields, planFieldMove, planGroupMove } from './templateFieldOrder'
import type { TemplateField } from './types'

/** A field with just enough of one to be ordered and grouped. */
function field(key: string, groupName: string, displayOrder: number): TemplateField {
  return aTemplateField({ templateFieldId: `id-${key}`, key, groupName, displayOrder })
}

/** Two groups of two, already contiguous, which is the state a version reaches after one move. */
const CONTIGUOUS = [
  field('chest', 'Bodice', 0),
  field('waist', 'Bodice', 1),
  field('sleeve', 'Sleeve', 2),
  field('cuff', 'Sleeve', 3),
]

function moved(fields: readonly TemplateField[], writes: ReturnType<typeof planFieldMove>) {
  const applied = fields.map((original) => {
    const write = writes.find(
      (candidate) => candidate.field.templateFieldId === original.templateFieldId,
    )
    return write === undefined ? original : { ...original, displayOrder: write.displayOrder }
  })
  return orderedFields(applied).map((one) => one.key)
}

describe('how a version is grouped and ordered', () => {
  it('orders groups by where their first field sits, which is what the server does', () => {
    // `TemplateVersion.InGroupOrder()`. The helper is not exposed through the API, so the client
    // derives the same thing rather than inventing a second opinion about it.
    expect(groupFields(CONTIGUOUS).map((group) => group.name)).toEqual(['Bodice', 'Sleeve'])
  })

  it('breaks a tie alphabetically by key, exactly as the payload does', () => {
    // Equal display orders are legal — nothing makes the column unique — and the server's own
    // ordering is `displayOrder` then `key`.
    const tied = [field('waist', 'Bodice', 0), field('chest', 'Bodice', 0)]
    expect(orderedFields(tied).map((one) => one.key)).toEqual(['chest', 'waist'])
  })

  it('keeps a scattered group together, under the position its first field earned', () => {
    // Nothing stops a group's fields being interleaved with another's; they would still be captured
    // together, so they are shown together rather than split into two headings of the same name.
    const scattered = [
      field('chest', 'Bodice', 0),
      field('sleeve', 'Sleeve', 1),
      field('waist', 'Bodice', 2),
    ]

    expect(groupFields(scattered)).toEqual([
      { name: 'Bodice', fields: [scattered[0], scattered[2]] },
      { name: 'Sleeve', fields: [scattered[1]] },
    ])
  })
})

describe('moving one field within its group', () => {
  it('writes only the two fields whose numbers change on a contiguous version', () => {
    // The whole version is renumbered and the unchanged numbers are subtracted, which is what makes
    // renumbering the version affordable: an adjacent swap costs two writes, not four.
    const writes = planFieldMove(CONTIGUOUS, 'id-waist', 'up')

    expect(writes).toHaveLength(2)
    expect(writes.map((write) => [write.field.key, write.displayOrder])).toEqual([
      ['waist', 0],
      ['chest', 1],
    ])
  })

  it('puts the field where the person asked, and leaves every other group alone', () => {
    expect(moved(CONTIGUOUS, planFieldMove(CONTIGUOUS, 'id-waist', 'up'))).toEqual([
      'waist',
      'chest',
      'sleeve',
      'cuff',
    ])
  })

  it('cannot move a group by moving a field inside it', () => {
    // The reason the whole version is renumbered rather than one group: group order is derived from
    // each group's *first* field, so a scheme that renumbers only within a group lets a within-group
    // move change which group comes first.
    const before = groupFields(CONTIGUOUS).map((group) => group.name)
    const writes = planFieldMove(CONTIGUOUS, 'id-cuff', 'up')
    const applied = CONTIGUOUS.map((original) => {
      const write = writes.find(
        (candidate) => candidate.field.templateFieldId === original.templateFieldId,
      )
      return write === undefined ? original : { ...original, displayOrder: write.displayOrder }
    })

    expect(groupFields(applied).map((group) => group.name)).toEqual(before)
  })

  it('normalises a version whose numbers were never contiguous, once', () => {
    // The expensive case, and it happens once. After it the version is contiguous and every later
    // move costs two writes.
    const sparse = [
      field('chest', 'Bodice', 10),
      field('waist', 'Bodice', 40),
      field('sleeve', 'Sleeve', 90),
    ]
    const writes = planFieldMove(sparse, 'id-waist', 'up')

    expect(writes.map((write) => [write.field.key, write.displayOrder])).toEqual([
      ['waist', 0],
      ['chest', 1],
      ['sleeve', 2],
    ])
  })

  it('removes the ties, so the order on screen is the order stored', () => {
    // A swap of two fields sharing a number would do nothing at all: the tie still breaks
    // alphabetically. Renumbering gives every field a number of its own.
    const tied = [field('waist', 'Bodice', 0), field('chest', 'Bodice', 0)]
    const writes = planFieldMove(tied, 'id-waist', 'up')

    // One write, not two: `waist` is already numbered 0 and only `chest` has to move off it. The
    // subtraction is genuinely minimal, and breaking the tie is what makes the move take effect —
    // a swap of the two numbers would have left them equal and changed nothing at all.
    expect(writes.map((write) => [write.field.key, write.displayOrder])).toEqual([['chest', 1]])
    expect(moved(tied, writes)).toEqual(['waist', 'chest'])
  })

  it.each([
    ['id-chest', 'up' as const],
    ['id-waist', 'down' as const],
  ])('asks for nothing when %s is already at the %s end of its group', (fieldId, direction) => {
    // The control stays on screen and honestly does nothing, rather than disappearing at the
    // boundary and moving every other control under the pointer.
    expect(planFieldMove(CONTIGUOUS, fieldId, direction)).toEqual([])
  })

  it('asks for nothing about a field this version does not have', () => {
    expect(planFieldMove(CONTIGUOUS, 'id-nothing', 'up')).toEqual([])
  })
})

describe('moving a whole group', () => {
  it('moves the block, because a group is where its first field is', () => {
    const writes = planGroupMove(CONTIGUOUS, 'Sleeve', 'up')

    expect(moved(CONTIGUOUS, writes)).toEqual(['sleeve', 'cuff', 'chest', 'waist'])
  })

  it('writes every field of both groups, because every one of them moves', () => {
    expect(planGroupMove(CONTIGUOUS, 'Sleeve', 'up')).toHaveLength(4)
  })

  it.each([
    ['Bodice', 'up' as const],
    ['Sleeve', 'down' as const],
  ])('asks for nothing when %s is already at the %s end', (groupName, direction) => {
    expect(planGroupMove(CONTIGUOUS, groupName, direction)).toEqual([])
  })

  it('asks for nothing about a group this version does not have', () => {
    expect(planGroupMove(CONTIGUOUS, 'Neckline', 'up')).toEqual([])
  })
})
