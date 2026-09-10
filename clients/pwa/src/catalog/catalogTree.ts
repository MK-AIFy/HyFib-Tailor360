import type { CatalogCategory, CatalogFinding, CatalogServiceType, CatalogVersion } from './types'

/**
 * The hierarchy as a tree, and the rules a screen has to explain before the server enforces them.
 *
 * The payload is flat — a list of categories each naming its parent, and a list of service types
 * each naming its category — because that is what a wire format should be. A tree editor needs the
 * shape back, and needs it built the same way every time it is asked for, which is why it is built
 * here once rather than in each screen that renders it.
 */

/** One node, with its children and the service types that hang off it. */
export interface CatalogNode {
  readonly category: CatalogCategory
  readonly children: readonly CatalogNode[]
  readonly serviceTypes: readonly CatalogServiceType[]
  /** How deep it sits, so a flat rendering can indent and announce the level. */
  readonly depth: number
}

/**
 * The tree, parents before children, siblings in display order then by code.
 *
 * ## What happens to a node whose parent is missing
 *
 * It is treated as a root. A category naming a parent the version does not contain is a version
 * mid-edit or one that changed under the reader, and dropping the node would hide a category — and
 * with it every service type beneath — from the person trying to fix exactly that. A cycle is broken
 * the same way, and for the same reason: something visible and wrong beats something invisible.
 */
export function buildTree(version: CatalogVersion): readonly CatalogNode[] {
  const byParent = new Map<string | null, CatalogCategory[]>()
  const known = new Set(version.categories.map((category) => category.categoryId))

  for (const category of version.categories) {
    // A parent that is not in this version cannot be resolved, so the node becomes a root rather
    // than disappearing with everything under it.
    const parent =
      category.parentCategoryId !== null && known.has(category.parentCategoryId)
        ? category.parentCategoryId
        : null

    const siblings = byParent.get(parent) ?? []
    siblings.push(category)
    byParent.set(parent, siblings)
  }

  const servicesByCategory = new Map<string, CatalogServiceType[]>()
  for (const serviceType of version.serviceTypes) {
    const list = servicesByCategory.get(serviceType.categoryId) ?? []
    list.push(serviceType)
    servicesByCategory.set(serviceType.categoryId, list)
  }

  const seen = new Set<string>()

  const build = (parentId: string | null, depth: number): readonly CatalogNode[] =>
    [...(byParent.get(parentId) ?? [])]
      .sort(compare)
      .filter((category) => {
        // A cycle would otherwise recur forever. Visiting each category once breaks it, and the
        // node still appears — under whichever parent reached it first.
        if (seen.has(category.categoryId)) {
          return false
        }
        seen.add(category.categoryId)
        return true
      })
      .map((category) => ({
        category,
        depth,
        children: build(category.categoryId, depth + 1),
        serviceTypes: [...(servicesByCategory.get(category.categoryId) ?? [])].sort(compare),
      }))

  const roots = build(null, 0)

  // Every category appears exactly once, and a cycle has no root to be reached from — so the two
  // nodes of an `a → b → a` pair would otherwise vanish together, which is the one outcome the
  // paragraph above rules out. Whatever the roots did not reach is appended as a root itself.
  const unreached: CatalogNode[] = []

  // The membership test has to be made as each node is taken, not before: promoting one node of a
  // cycle to a root pulls the rest of that cycle in as its children, and a list filtered up front
  // would then add them a second time.
  for (const category of [...version.categories].sort(compare)) {
    if (seen.has(category.categoryId)) {
      continue
    }

    seen.add(category.categoryId)
    unreached.push({
      category,
      depth: 0,
      children: build(category.categoryId, 1),
      serviceTypes: [...(servicesByCategory.get(category.categoryId) ?? [])].sort(compare),
    })
  }

  return [...roots, ...unreached]
}

function compare(
  left: { readonly displayOrder: number | string; readonly code: string },
  right: { readonly displayOrder: number | string; readonly code: string },
): number {
  const byOrder = Number(left.displayOrder) - Number(right.displayOrder)
  return byOrder !== 0 ? byOrder : left.code.localeCompare(right.code, 'en')
}

/** The tree flattened depth-first, which is the order a table renders and a person reads. */
export function flatten(nodes: readonly CatalogNode[]): readonly CatalogNode[] {
  return nodes.flatMap((node) => [node, ...flatten(node.children)])
}

/**
 * Whether a category's branches are a subset of its parent's, which is what publication demands.
 *
 * ## Why the screen says this rather than letting the refusal say it
 *
 * A sub-category cannot be offered where its parent is not — the parent is how a counter reaches it
 * — and **an empty set means offered nowhere**, not offered everywhere. Both are easy to get wrong
 * while setting availability on twenty categories, and discovering it at publication means finding
 * which of the twenty, from a finding, after the fact.
 *
 * The server remains the authority. This is what lets the screen explain the rule where it is being
 * broken instead of after.
 *
 * @returns The branch identifiers this category offers that its parent does not.
 */
export function branchesOutsideParent(
  category: CatalogCategory,
  categories: readonly CatalogCategory[],
): readonly string[] {
  if (category.parentCategoryId === null) {
    return []
  }

  const parent = categories.find((candidate) => candidate.categoryId === category.parentCategoryId)

  if (parent === undefined) {
    return []
  }

  return category.branchIds.filter((branchId) => !parent.branchIds.includes(branchId))
}

/** The same rule for a service type, whose parent is the category it hangs off. */
export function serviceBranchesOutsideCategory(
  serviceType: CatalogServiceType,
  categories: readonly CatalogCategory[],
): readonly string[] {
  const category = categories.find((candidate) => candidate.categoryId === serviceType.categoryId)

  if (category === undefined) {
    return []
  }

  return serviceType.branchIds.filter((branchId) => !category.branchIds.includes(branchId))
}

/**
 * Which of the five links a service type is still missing.
 *
 * The server derives `notOrderable` from exactly this, so the screen does not decide whether the
 * service type is orderable — it says *which* link is why, which the flag alone cannot.
 */
export function missingLinks(serviceType: CatalogServiceType): readonly string[] {
  return [
    serviceType.measurementTemplateId === null ? 'measurementTemplateId' : null,
    serviceType.workflowDefinitionId === null ? 'workflowDefinitionId' : null,
    serviceType.designOptionGroupIds.length === 0 ? 'designOptionGroupIds' : null,
    serviceType.priceListItemCode === null ? 'priceListItemCode' : null,
    serviceType.qcChecklistTemplateId === null ? 'qcChecklistTemplateId' : null,
  ].filter((name): name is string => name !== null)
}

/**
 * Reads a finding's target back to the thing it is about.
 *
 * The shape is `BuiltInCatalogValidator`'s: `categories[<code>].<field>` and
 * `serviceTypes[<categoryCode>.<serviceCode>].<field>` — the same bracketed path the measurement
 * templates use, and the same reason. A service type is qualified by its category because a service
 * code is unique within a category rather than within the version, so the bare code would name two
 * things in a catalogue with a `STITCHING` under both `BLOUSE` and `SALWAR`.
 *
 * One target does not follow the shape at all: `serviceTypes[<serviceCode>].categoryId`, emitted
 * when a service type names a category the version does not contain — the qualifier cannot be built,
 * because the category it would name is the thing that is missing. It is read as a service type
 * whose category is unknown, so the screen can still say which service type is wrong.
 *
 * A target this build cannot read resolves to nothing, which is what lets the screen carry it in the
 * summary rather than drop it: a finding that cannot be anchored is still a refusal.
 */
export interface FindingSubject {
  readonly kind: 'category' | 'serviceType'
  /** The category's code, or a service type's category code when the target carries one. */
  readonly categoryCode: string | null
  /** The service type's own code, for a service-type target. */
  readonly code: string
  /** Which member of it the finding is about, when the target names one. */
  readonly field: string | null
}

const TARGET = /^(categories|serviceTypes)\[([^\]]+)\](?:\.(.+))?$/

export function findingSubject(finding: CatalogFinding): FindingSubject | null {
  if (finding.target === null) {
    return null
  }

  const match = TARGET.exec(finding.target)

  if (match === null) {
    return null
  }

  const inside = match[2] ?? ''
  const field = match[3] ?? null

  if (match[1] === 'categories') {
    return { kind: 'category', categoryCode: inside, code: inside, field }
  }

  // `<categoryCode>.<serviceCode>`, or a bare service code on the one target that cannot qualify.
  const dot = inside.lastIndexOf('.')

  return dot < 0
    ? { kind: 'serviceType', categoryCode: null, code: inside, field }
    : {
        kind: 'serviceType',
        categoryCode: inside.slice(0, dot),
        code: inside.slice(dot + 1),
        field,
      }
}

/** The findings about one category, by its code. */
export function findingsForCategory(
  findings: readonly CatalogFinding[],
  code: string,
): readonly CatalogFinding[] {
  return findings.filter((finding) => {
    const subject = findingSubject(finding)
    return subject !== null && subject.kind === 'category' && subject.code === code
  })
}

/**
 * The findings about one service type.
 *
 * Matched on the category code as well as its own, because a service code is unique within a
 * category rather than within the version — a catalogue with a `STITCHING` under both `BLOUSE` and
 * `SALWAR` would otherwise put one service type's findings on both. The one target that carries no
 * qualifier is matched on the service code alone, which is all it names.
 */
export function findingsForServiceType(
  findings: readonly CatalogFinding[],
  categoryCode: string,
  code: string,
): readonly CatalogFinding[] {
  return findings.filter((finding) => {
    const subject = findingSubject(finding)

    if (subject === null || subject.kind !== 'serviceType' || subject.code !== code) {
      return false
    }

    return subject.categoryCode === null || subject.categoryCode === categoryCode
  })
}

/** The findings no control on screen can carry, which the summary has to state itself. */
export function unanchoredCatalogFindings(
  findings: readonly CatalogFinding[],
  version: CatalogVersion,
): readonly CatalogFinding[] {
  const categoryCodes = new Set(version.categories.map((category) => category.code))
  const serviceCodes = new Set(version.serviceTypes.map((serviceType) => serviceType.code))

  return findings.filter((finding) => {
    const subject = findingSubject(finding)

    if (subject === null) {
      return true
    }

    return subject.kind === 'category'
      ? !categoryCodes.has(subject.code)
      : !serviceCodes.has(subject.code)
  })
}
