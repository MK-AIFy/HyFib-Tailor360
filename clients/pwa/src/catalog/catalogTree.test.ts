import { describe, expect, it } from 'vitest'
import {
  branchesOutsideParent,
  buildTree,
  findingSubject,
  findingsForCategory,
  findingsForServiceType,
  flatten,
  missingLinks,
  serviceBranchesOutsideCategory,
  unanchoredCatalogFindings,
} from './catalogTree'
import {
  COIMBATORE,
  ERODE,
  aCatalogFinding,
  aCatalogVersion,
  aCategory,
  aServiceType,
} from './testing/fixtures'

const PARENT = aCategory({ categoryId: 'id-blouse', code: 'BLOUSE', displayOrder: 0 })
const CHILD = aCategory({
  categoryId: 'id-aari',
  parentCategoryId: 'id-blouse',
  code: 'AARI',
  name: 'Aari work',
  displayOrder: 0,
})
const SIBLING = aCategory({
  categoryId: 'id-salwar',
  code: 'SALWAR',
  name: 'Salwar kameez',
  displayOrder: 1,
})

describe('the hierarchy as a tree', () => {
  it('nests a child under its parent and orders siblings by display order', () => {
    const tree = buildTree(
      aCatalogVersion({ categories: [SIBLING, CHILD, PARENT], serviceTypes: [] }),
    )

    expect(tree.map((node) => node.category.code)).toEqual(['BLOUSE', 'SALWAR'])
    expect(tree[0]?.children.map((node) => node.category.code)).toEqual(['AARI'])
    expect(tree[0]?.children[0]?.depth).toBe(1)
  })

  it('breaks a tie on the code, so the order does not depend on which read arrived', () => {
    const tree = buildTree(
      aCatalogVersion({
        categories: [
          aCategory({ categoryId: 'b', code: 'ZEBRA', displayOrder: 0 }),
          aCategory({ categoryId: 'a', code: 'APPLE', displayOrder: 0 }),
        ],
        serviceTypes: [],
      }),
    )

    expect(tree.map((node) => node.category.code)).toEqual(['APPLE', 'ZEBRA'])
  })

  it('hangs each service type off its category, in order', () => {
    const first = aServiceType({ serviceTypeId: 's1', code: 'PATTERN', displayOrder: 1 })
    const second = aServiceType({ serviceTypeId: 's2', code: 'READY', displayOrder: 0 })
    const tree = buildTree(
      aCatalogVersion({ categories: [aCategory()], serviceTypes: [first, second] }),
    )

    expect(tree[0]?.serviceTypes.map((one) => one.code)).toEqual(['READY', 'PATTERN'])
  })

  it('shows a category whose parent is not in this version, rather than hiding it', () => {
    // A version mid-edit, or one that changed under the reader. Dropping the node would hide a
    // category — and every service type beneath it — from the person trying to fix exactly that.
    const orphan = aCategory({ categoryId: 'id-lost', parentCategoryId: 'id-gone', code: 'LOST' })
    const tree = buildTree(aCatalogVersion({ categories: [orphan], serviceTypes: [] }))

    expect(tree.map((node) => node.category.code)).toEqual(['LOST'])
    expect(tree[0]?.depth).toBe(0)
  })

  it('does not recur forever on a cycle, and still shows both nodes', () => {
    const left = aCategory({ categoryId: 'a', parentCategoryId: 'b', code: 'A' })
    const right = aCategory({ categoryId: 'b', parentCategoryId: 'a', code: 'B' })
    const tree = buildTree(aCatalogVersion({ categories: [left, right], serviceTypes: [] }))

    // Something visible and wrong beats something invisible: both appear, under whichever parent
    // reached them first.
    expect(flatten(tree)).toHaveLength(2)
  })

  it.each([
    ['a plain hierarchy', [PARENT, CHILD, SIBLING]],
    [
      'a cycle with no root at all',
      [
        aCategory({ categoryId: 'a', parentCategoryId: 'b', code: 'A' }),
        aCategory({ categoryId: 'b', parentCategoryId: 'a', code: 'B' }),
      ],
    ],
    [
      'a cycle hanging off a real root',
      [
        PARENT,
        aCategory({ categoryId: 'a', parentCategoryId: 'b', code: 'A' }),
        aCategory({ categoryId: 'b', parentCategoryId: 'a', code: 'B' }),
      ],
    ],
    [
      'a parent that is not in the version',
      [aCategory({ categoryId: 'x', parentCategoryId: 'nowhere', code: 'X' })],
    ],
  ])('shows every category exactly once — %s', (_named, categories) => {
    // The invariant the tree exists to keep. Breaking it in either direction is a real failure: a
    // category shown twice is an editor with two of the same row, and one shown never is a category
    // an administrator cannot reach to fix.
    const shown = flatten(buildTree(aCatalogVersion({ categories, serviceTypes: [] })))
    const ids = shown.map((node) => node.category.categoryId)

    expect(ids).toHaveLength(categories.length)
    expect(new Set(ids).size).toBe(categories.length)
  })

  it('flattens depth-first, which is the order a table renders and a person reads', () => {
    const tree = buildTree(
      aCatalogVersion({ categories: [PARENT, CHILD, SIBLING], serviceTypes: [] }),
    )

    expect(flatten(tree).map((node) => node.category.code)).toEqual(['BLOUSE', 'AARI', 'SALWAR'])
  })
})

describe('the subset rule, said before publication refuses it', () => {
  it('accepts a child offered where its parent is', () => {
    const parent = aCategory({ categoryId: 'p', branchIds: [COIMBATORE, ERODE] })
    const child = aCategory({ categoryId: 'c', parentCategoryId: 'p', branchIds: [COIMBATORE] })

    expect(branchesOutsideParent(child, [parent, child])).toEqual([])
  })

  it('names the branch a child is offered at and its parent is not', () => {
    // The parent is how a counter reaches the child, so the child cannot be offered beyond it.
    const parent = aCategory({ categoryId: 'p', branchIds: [COIMBATORE] })
    const child = aCategory({
      categoryId: 'c',
      parentCategoryId: 'p',
      branchIds: [COIMBATORE, ERODE],
    })

    expect(branchesOutsideParent(child, [parent, child])).toEqual([ERODE])
  })

  it('holds a root category to nothing, because it has no parent to be inside', () => {
    expect(branchesOutsideParent(aCategory({ branchIds: [ERODE] }), [])).toEqual([])
  })

  it('applies the same rule to a service type against its category', () => {
    const category = aCategory({ categoryId: 'c', branchIds: [COIMBATORE] })
    const service = aServiceType({ categoryId: 'c', branchIds: [COIMBATORE, ERODE] })

    expect(serviceBranchesOutsideCategory(service, [category])).toEqual([ERODE])
  })

  it('says nothing about a category the version does not contain', () => {
    // The subset rule needs both sides. A missing parent is its own finding, not this one.
    expect(serviceBranchesOutsideCategory(aServiceType({ categoryId: 'gone' }), [])).toEqual([])
  })

  it('treats an empty set as offered nowhere, which is a subset of anything', () => {
    // Empty is *not* "offered everywhere" — a screen that read it that way would show an
    // administrator a category their counters cannot order.
    const parent = aCategory({ categoryId: 'p', branchIds: [COIMBATORE] })
    const child = aCategory({ categoryId: 'c', parentCategoryId: 'p', branchIds: [] })

    expect(branchesOutsideParent(child, [parent, child])).toEqual([])
  })
})

describe('which of the five links a service type is missing', () => {
  it('says none for one that is fully linked', () => {
    expect(missingLinks(aServiceType())).toEqual([])
  })

  it.each([
    ['measurementTemplateId', { measurementTemplateId: null }],
    ['workflowDefinitionId', { workflowDefinitionId: null }],
    ['designOptionGroupIds', { designOptionGroupIds: [] }],
    ['priceListItemCode', { priceListItemCode: null }],
    ['qcChecklistTemplateId', { qcChecklistTemplateId: null }],
  ])('names %s when it is the one missing', (name, overrides) => {
    // The server derives `notOrderable` from exactly this; the screen says *which* link is why,
    // which the flag alone cannot.
    expect(missingLinks(aServiceType(overrides))).toEqual([name])
  })

  it('names every missing link, not the first', () => {
    expect(
      missingLinks(aServiceType({ measurementTemplateId: null, priceListItemCode: null })),
    ).toEqual(['measurementTemplateId', 'priceListItemCode'])
  })
})

describe('reading a finding back to the thing it is about', () => {
  it('reads a category target', () => {
    expect(findingSubject(aCatalogFinding({ target: 'categories[BLOUSE].branchIds' }))).toEqual({
      kind: 'category',
      categoryCode: 'BLOUSE',
      code: 'BLOUSE',
      field: 'branchIds',
    })
  })

  it('reads a service-type target, qualified by its category', () => {
    // A service code is unique within a category rather than within the version, so the bare code
    // would name two things in a catalogue with a STITCHING under both BLOUSE and SALWAR.
    expect(findingSubject(aCatalogFinding())).toEqual({
      kind: 'serviceType',
      categoryCode: 'BLOUSE',
      code: 'PATTERN',
      field: 'priceListItemCode',
    })
  })

  it('reads the one target that cannot be qualified', () => {
    // `serviceTypes[<code>].categoryId`, emitted when a service type names a category the version
    // does not contain — the qualifier cannot be built, because that category is what is missing.
    expect(findingSubject(aCatalogFinding({ target: 'serviceTypes[PATTERN].categoryId' }))).toEqual(
      { kind: 'serviceType', categoryCode: null, code: 'PATTERN', field: 'categoryId' },
    )
  })

  it('has nothing for a finding about the version as a whole', () => {
    expect(findingSubject(aCatalogFinding({ target: null }))).toBeNull()
  })

  it('gives up honestly on a shape it cannot read', () => {
    expect(findingSubject(aCatalogFinding({ target: 'somethingElse' }))).toBeNull()
  })
})

describe('putting a finding beside the thing that caused it', () => {
  it('collects the findings about one category', () => {
    const mine = aCatalogFinding({ target: 'categories[BLOUSE].branchIds' })
    const theirs = aCatalogFinding({ target: 'categories[SALWAR].branchIds' })

    expect(findingsForCategory([mine, theirs], 'BLOUSE')).toEqual([mine])
  })

  it('does not put one service type findings on another that shares its code', () => {
    // The reason the target is qualified at all.
    const blouse = aCatalogFinding({ target: 'serviceTypes[BLOUSE.STITCHING].priceListItemCode' })
    const salwar = aCatalogFinding({ target: 'serviceTypes[SALWAR.STITCHING].priceListItemCode' })

    expect(findingsForServiceType([blouse, salwar], 'BLOUSE', 'STITCHING')).toEqual([blouse])
    expect(findingsForServiceType([blouse, salwar], 'SALWAR', 'STITCHING')).toEqual([salwar])
  })

  it('matches the unqualified target on the service code alone, which is all it names', () => {
    const lost = aCatalogFinding({ target: 'serviceTypes[STITCHING].categoryId' })

    expect(findingsForServiceType([lost], 'BLOUSE', 'STITCHING')).toEqual([lost])
  })
})

describe('the findings no control on screen can carry', () => {
  const version = aCatalogVersion()

  it('carries a finding about the version as a whole', () => {
    const orphan = aCatalogFinding({ target: null })
    expect(unanchoredCatalogFindings([orphan], version)).toEqual([orphan])
  })

  it('carries a finding about something this version does not contain', () => {
    const stale = aCatalogFinding({ target: 'categories[GONE].branchIds' })
    expect(unanchoredCatalogFindings([stale], version)).toEqual([stale])
  })

  it('carries a target shape this build cannot read, because it is still a refusal', () => {
    const unknown = aCatalogFinding({ target: 'workflows[X].y' })
    expect(unanchoredCatalogFindings([unknown], version)).toEqual([unknown])
  })

  it('leaves a finding that can be anchored to its row', () => {
    expect(unanchoredCatalogFindings([aCatalogFinding()], version)).toEqual([])
  })
})
