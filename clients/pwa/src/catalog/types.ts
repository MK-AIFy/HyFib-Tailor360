/**
 * The catalogue payloads, hand-written and pinned against the published schema.
 *
 * ## Why these are not in `admin/types.ts`
 *
 * The same reason the measurement templates are not: the routes belong to the Catalog module —
 * `/api/v1/catalog`, not `/api/v1/admin` — and only the *audience* is administrative. Putting them
 * beside the account and branch types would suggest a boundary the server does not have.
 *
 * ## Why they are hand-written when a generated type exists
 *
 * The generated types carry no documentation and type every enumeration as a bare `string`, because
 * OpenAPI cannot express "one of these four, and the server will never send a fifth". These carry
 * the notes a screen author reads and the narrower unions the screens branch on, and
 * `api/contract.ts` asserts each one is assignable to the generated shape — so a response that gains
 * a required member, loses one or changes a member's type fails `pnpm typecheck` rather than failing
 * on a shop floor.
 */

/** One version of the catalogue, without its contents. */
export interface CatalogVersionSummary {
  readonly catalogVersionId: string
  readonly versionNumber: number | string
  readonly name: string
  readonly notes: string | null
  /** `Draft`, `Published` or `Retired`. */
  readonly status: string
  readonly createdAt: string
  readonly publishedAt: string | null
  readonly retiredAt: string | null
  /** The version this one was copied from, when it was not started empty. */
  readonly clonedFromVersionId: string | null
}

/**
 * One node of the hierarchy.
 *
 * `isGroupingNode` is on the payload rather than derived, which matters: a grouping node is one that
 * carries no service types of its own and exists to hold children, and working that out on the
 * client would be a second opinion about a fact the server already has.
 */
export interface CatalogCategory {
  readonly categoryId: string
  readonly parentCategoryId: string | null
  readonly code: string
  readonly name: string
  readonly nameTamil: string | null
  readonly description: string | null
  readonly displayOrder: number | string
  readonly isGroupingNode: boolean
  /**
   * The branches this category is offered at. **Empty means offered nowhere** — it is not "offered
   * everywhere", and a screen that read it that way would show an administrator a category their
   * counters cannot order.
   */
  readonly branchIds: readonly string[]
  readonly activeFrom: string | null
  readonly activeTo: string | null
  readonly featureFlagKey: string | null
}

/** One orderable thing, and the five links that decide what ordering it means. */
export interface CatalogServiceType {
  readonly serviceTypeId: string
  readonly categoryId: string
  readonly code: string
  readonly name: string
  readonly nameTamil: string | null
  readonly description: string | null
  readonly displayOrder: number | string
  readonly branchIds: readonly string[]
  readonly activeFrom: string | null
  readonly activeTo: string | null
  /** Link 1: what is measured. */
  readonly measurementTemplateId: string | null
  /** Link 2: how it is made. */
  readonly workflowDefinitionId: string | null
  /** Link 3: what the customer chooses from. */
  readonly designOptionGroupIds: readonly string[]
  /** Link 4: what it costs. */
  readonly priceListItemCode: string | null
  /** Link 5: how it is checked. */
  readonly qcChecklistTemplateId: string | null
  /**
   * Whether the version may be published with links still missing.
   *
   * The escape hatch, and the reason `notOrderable` exists: a service type saved this way publishes
   * and is then visibly not orderable, rather than blocking the whole catalogue.
   */
  readonly allowIncomplete: boolean
  /** Whether a counter may order it today. Derived by the server from the links it has. */
  readonly notOrderable: boolean
  readonly expectedDurationDays: number | string
  readonly intakeWarning: string | null
}

/** A version and everything in it. */
export interface CatalogVersion {
  readonly version: CatalogVersionSummary
  readonly categories: readonly CatalogCategory[]
  readonly serviceTypes: readonly CatalogServiceType[]
  /** The design option groups of every category, in category then display order (#30). */
  readonly designGroups: readonly CatalogDesignGroup[]
  /** The design rules of every category, by number. */
  readonly designRules: readonly CatalogDesignRule[]
}

/** One design option group — a neckline, a sleeve length — as one version holds it (#30). */
export interface CatalogDesignGroup {
  readonly designOptionGroupId: string
  readonly categoryId: string
  /** `lower_snake_case`, unique within the category, fixed once published. */
  readonly code: string
  readonly name: string
  readonly nameTamil: string | null
  /** `SingleChoice` or `MultipleChoice`. */
  readonly selectionMode: string
  readonly required: boolean
  readonly displayOrder: number
  readonly activeFrom: string | null
  readonly activeTo: string | null
  readonly branchIds: readonly string[]
  readonly options: readonly CatalogDesignOption[]
}

/** One choice within a design option group. */
export interface CatalogDesignOption {
  readonly designOptionId: string
  readonly designOptionGroupId: string
  /** `UPPER_SNAKE_CASE`, unique within the group; `NONE` is reserved and selectable. */
  readonly code: string
  readonly name: string
  readonly nameTamil: string | null
  readonly helpText: string
  /** `sheet_key#group_code.OPTION_CODE`, or null until a drawing exists. */
  readonly illustrationKey: string | null
  readonly illustrationAlt: string
  readonly priceListItemCode: string | null
  readonly timeImpactDays: number
  readonly displayOrder: number
  /** False is retirement: the option is still known, no longer offered. */
  readonly active: boolean
}

/** One side of a rule, in the operand grammar of the design options document. */
export interface CatalogDesignOperand {
  readonly groupCode: string | null
  /** `Equals`, `NotEquals`, `In`, `Includes`, `Excludes`, `AnySelection` or `Always`. */
  readonly form: string
  readonly optionCodes: readonly string[]
}

/** One rule between the options of a category. */
export interface CatalogDesignRule {
  readonly designRuleId: string
  readonly categoryId: string
  readonly number: number
  /** `DR-nn`, unique across the catalogue and never re-used. */
  readonly identifier: string
  /** `Requires`, `Excludes`, `RequiresAttachment` or `Note`. */
  readonly type: string
  readonly antecedent: CatalogDesignOperand
  readonly consequent: CatalogDesignOperand | null
  readonly note: string | null
  readonly why: string | null
  /** The rule as the document writes it. */
  readonly statement: string
  /** Whether a violation stops a confirmation. A note never does. */
  readonly blocks: boolean
}

/** One thing publication validation noticed. */
export interface CatalogFinding {
  /** `Error` refuses publication; `Warning` does not. */
  readonly severity: string
  readonly code: string
  readonly message: string
  /** The part of the draft it is about, or null for a finding about the version as a whole. */
  readonly target: string | null
  /** Which registered validator produced it, so a reviewer can tell two similar findings apart. */
  readonly validator: string
}

/** What publication validation found, without changing anything. */
export interface CatalogValidationReport {
  readonly catalogVersionId: string
  readonly publishable: boolean
  readonly errorCount: number | string
  readonly warningCount: number | string
  readonly findings: readonly CatalogFinding[]
}

/** What publication answered with, including the version it superseded. */
export interface CatalogPublication {
  readonly version: CatalogVersionSummary
  readonly supersededVersionId: string | null
  readonly findings: readonly CatalogFinding[]
}

/** A category as the add and edit routes demand it. Every member required, most of them nullable. */
export interface CategoryRequest {
  readonly code: string | null
  readonly name: string | null
  readonly nameTamil: string | null
  readonly description: string | null
  readonly displayOrder: number | string
  readonly parentCategoryId: string | null
  readonly branchIds: readonly string[] | null
  readonly activeFrom: string | null
  readonly activeTo: string | null
  readonly featureFlagKey: string | null
  readonly reason: string | null
}

/** A service type as the add and edit routes demand it. */
export interface ServiceTypeRequest {
  readonly code: string | null
  readonly name: string | null
  readonly nameTamil: string | null
  readonly description: string | null
  readonly displayOrder: number | string
  readonly branchIds: readonly string[] | null
  readonly activeFrom: string | null
  readonly activeTo: string | null
  readonly measurementTemplateId: string | null
  readonly workflowDefinitionId: string | null
  readonly designOptionGroupIds: readonly string[] | null
  readonly priceListItemCode: string | null
  readonly qcChecklistTemplateId: string | null
  readonly allowIncomplete: boolean
  readonly expectedDurationDays: number | string
  readonly intakeWarning: string | null
  readonly reason: string | null
}

/** The label correction a published version admits, which is the only edit it admits at all. */
export interface PresentationRequest {
  readonly name: string | null
  readonly nameTamil: string | null
  readonly description: string | null
  readonly displayOrder: number | string
  readonly reason: string | null
}

/**
 * One thing a counter may order today, flattened.
 *
 * A different shape from `CatalogServiceType` on purpose: this is what `GET /catalog/current`
 * answers, and it is the *counter's* read rather than the administrator's. It carries the category's
 * name and code inline because a counter shows one list rather than a tree, and it carries no
 * branch identifiers at all because the branch is the question rather than an answer — the read is
 * already scoped to the caller's.
 */
export interface OrderableService {
  readonly serviceTypeId: string
  readonly serviceCode: string
  readonly serviceName: string
  readonly categoryId: string
  readonly categoryCode: string
  readonly categoryName: string
  /** `<categoryCode>.<serviceCode>`, which is how a finding and an order both name it. */
  readonly qualifiedReference: string
  readonly measurementTemplateId: string | null
  readonly workflowDefinitionId: string | null
  readonly designOptionGroupIds: readonly string[]
  readonly priceListItemCode: string | null
  readonly qcChecklistTemplateId: string | null
  readonly expectedDurationDays: number | string
  readonly intakeWarning: string | null
}

/** What the caller's branch may order today, and which published version says so. */
export interface OrderableCatalog {
  readonly branchId: string
  /** Null when nothing is published, which is a real state rather than an error. */
  readonly catalogVersionId: string | null
  readonly services: readonly OrderableService[]
}
