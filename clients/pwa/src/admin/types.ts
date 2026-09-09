/**
 * The administration contract, as the client sees it.
 *
 * These mirror the payloads `/api/v1/admin/*` returns. Each is pinned against the generated schema in
 * `src/api/contract.ts`, so a server field that is renamed, retyped or removed fails `pnpm typecheck`
 * here rather than failing on somebody's screen.
 *
 * ## Nulls, not optionals
 *
 * A field the server may answer with `null` is typed `T | null`. The application compiles with
 * `exactOptionalPropertyTypes` and JSON `null` is a value; modelling it as an absent property would
 * make every read a lie the type checker cannot catch.
 *
 * ## What a version is for
 *
 * Every editable thing carries a `version`. It is the value the next edit sends back in `If-Match`,
 * and the reason a screen can tell "somebody else changed this while you had it open" from "your
 * change failed". It is opaque: never parsed, never compared for ordering, only presented back.
 */

/** Where an account is in its life. */
export const STAFF_STATUSES = ['Invited', 'Active', 'Suspended', 'Deactivated'] as const

export type StaffStatus = (typeof STAFF_STATUSES)[number]

/** How far a role is meant to reach. */
export const ROLE_REACHES = ['Branch', 'Organisation'] as const

export type RoleReach = (typeof ROLE_REACHES)[number]

/** What a permission is about. */
export const PERMISSION_SCOPES = ['Branch', 'Organisation', 'NotBranchOwned'] as const

export type PermissionScope = (typeof PERMISSION_SCOPES)[number]

/** Whether a branch is trading. */
export const BRANCH_STATUSES = ['Open', 'Closed'] as const

export type BranchStatus = (typeof BRANCH_STATUSES)[number]

/**
 * One staff account as a list row.
 *
 * Two things it deliberately does not carry, and both are the point. **No address**: the list matches
 * names only, and returning addresses would answer "is this email a member of staff" for anybody who
 * reached the endpoint. **No version**: a row cannot be acted on straight from the list, because the
 * version an edit presents has to be the one the row carries *now*, not the one a table has been
 * showing for five minutes. Every command therefore reads the account first, which is also what makes
 * `If-Match` mean something.
 */
export interface StaffSummary {
  readonly userId: string
  readonly displayName: string
  readonly userName: string
  readonly status: string
  readonly mfaEnrolment: string
  readonly homeBranchId: string | null
  readonly roleKeys: readonly string[]
  readonly lastSignInAt: string | null
  readonly createdAt: string
}

/** One staff account as the detail screen shows it, with the version an edit must present. */
export interface StaffUser {
  readonly userId: string
  readonly displayName: string
  readonly userName: string
  /** The address invitations and security alerts go to. Personal data: this screen and nowhere else. */
  readonly email: string
  readonly status: string
  readonly mfaEnrolment: string
  readonly homeBranchId: string | null
  readonly lastSignInAt: string | null
  readonly createdAt: string
  readonly version: string
}

/** One page of staff accounts, paged by keyset. */
export interface StaffUserPage {
  readonly users: readonly StaffSummary[]
  /** Where to continue from, or null at the end. Opaque: never parsed. */
  readonly nextCursor: string | null
}

/** A branch an account works in, and whether it is the one their screens open on. */
export interface BranchAssignment {
  readonly branchId: string
  readonly isPrimary: boolean
}

/** What one account may do and where. */
export interface AssignedAccess {
  readonly roleKeys: readonly string[]
  readonly branches: readonly BranchAssignment[]
  readonly version: string
}

/** One branch in the register. */
export interface Branch {
  readonly branchId: string
  /** Set once when the branch is opened and never again: it is in every document number it produces. */
  readonly code: string
  readonly name: string
  readonly timeZoneId: string
  readonly status: string
  readonly statusReason: string | null
  readonly addressLine1: string | null
  readonly addressLine2: string | null
  readonly city: string | null
  readonly state: string | null
  readonly postalCode: string | null
  readonly contactPhone: string | null
  readonly contactEmail: string | null
  readonly gstRegistrationReference: string | null
  readonly version: string
}

/** One role, with what it grants and how many people hold it. */
export interface Role {
  readonly roleId: string
  readonly key: string
  readonly name: string
  readonly description: string
  readonly reach: string
  /** True when this release ships the role, in which case it cannot be deleted. */
  readonly isSystem: boolean
  readonly assignedByDefault: boolean
  readonly permissionKeys: readonly string[]
  readonly holders: number
  readonly updatedAt: string
  readonly updatedBy: string | null
  readonly version: string
}

/**
 * One permission the application declares.
 *
 * The three flags are shown on the role screen because they change what the administrator is
 * deciding: granting a role a permission marked for step-up means everybody holding that role will be
 * asked to re-authenticate before using it.
 */
export interface Permission {
  readonly key: string
  readonly description: string
  readonly module: string
  readonly scope: string
  readonly requiresMfa: boolean
  readonly requiresStepUp: boolean
  readonly requiresReason: boolean
}

/** One feature flag or module toggle. */
export interface FeatureFlag {
  readonly key: string
  readonly enabled: boolean
  readonly reason: string | null
  readonly updatedAt: string
  readonly updatedBy: string | null
  readonly revision: number
  /** Roughly how long the change takes to reach every node, which the screen tells the operator. */
  readonly propagationSeconds: number
  readonly version: string
}

/** One entry in the audit trail. */
export interface AuditEntry {
  /**
   * The monotonic position of the entry in the trail, and the value paging keys on.
   *
   * Typed to accept a string as well as a number because the server may serialise a 64-bit sequence
   * either way, and a client that assumed one would silently read `NaN` from the other. It is only
   * ever compared and passed back, never arithmetic.
   */
  readonly sequence: number | string
  readonly occurredAt: string
  readonly action: string
  readonly entityType: string
  readonly entityId: string
  readonly actorId: string | null
  /** Who acted, as a name. Personal data: this screen and the trail, never a log line. */
  readonly actorDisplayName: string
  readonly branchId: string | null
  readonly correlationId: string | null
  readonly reason: string | null
  readonly summary: string
  /** Redacted prior state, serialised. Null for a creation. */
  readonly before: string | null
  /** Redacted resulting state. Null for a deletion. */
  readonly after: string | null
}

/** One page of the trail, newest first, paged by keyset on the sequence. */
export interface AuditPage {
  readonly entries: readonly AuditEntry[]
  readonly nextCursor: string | null
}

/** One outbox message that exhausted its delivery attempts. */
export interface DeadLetteredMessage {
  readonly id: string
  /**
   * The schema whose outbox holds it. Every module has its own, so this is what tells an operator
   * where the message actually is.
   */
  readonly module: string
  readonly aggregateId: string
  readonly eventType: string
  readonly schemaVersion: number
  readonly occurredAt: string
  readonly deadLetteredAt: string | null
  readonly attemptCount: number
  /** The failure it was given up on. The dispatcher never puts a payload value here. */
  readonly lastError: string | null
  readonly correlationId: string | null
}

/**
 * Where a measurement-template version is in its life (#27).
 *
 * The interface members below keep `string` rather than this union, deliberately and for the same
 * reason `Branch.status` does: a server that gains a state should fail to *render* — which a screen
 * can say something honest about — rather than fail to type, which nobody sees until a build.
 */
export const TEMPLATE_VERSION_STATUSES = ['Draft', 'InReview', 'Published', 'Retired'] as const

export type TemplateVersionStatus = (typeof TEMPLATE_VERSION_STATUSES)[number]

/** One choice a field offers, when it is chosen rather than measured. */
export interface TemplateChoiceOption {
  readonly code: string
  readonly label: string
  readonly labelTamil: string | null
  readonly displayOrder: number | string
}

/** One clause of a visibility rule. Clauses are joined by *or*. */
export interface TemplateRuleClause {
  /** Where the operand is read from: `Field` or `DesignSelection`. */
  readonly scope: string
  /** The field key, or the design option-group code. */
  readonly name: string
  /** `IsAnyOf` or `Excludes`. */
  readonly operator: string
  readonly values: readonly string[]
}

/** When a field is shown, in the shape the update accepts. */
export interface TemplateRule {
  /** `ShownWhen` or `HiddenWhen`. */
  readonly effect: string
  readonly anyOf: readonly TemplateRuleClause[]
}

/**
 * One field of a version.
 *
 * ## Two pairs, and which half to read
 *
 * `rule` is the visibility rule rendered as an English sentence and `optionCodes` is the choices
 * without their labels. Both are superseded by `ruleDefinition` and `options`, and both are still
 * served only because removing a field inside a major version breaks every client reading it
 * (`docs/architecture/conventions.md` section 5.2). **Read `ruleDefinition` and `options`.** The
 * sentence is for showing a person; it cannot be sent back, because the update expects the clauses.
 *
 * Bounds and thresholds are millimetres. Rendering them in the unit a tailor thinks in is #94's
 * work, and doing half of it here would be worse than not doing it.
 */
export interface TemplateField {
  readonly templateFieldId: string
  /** What captured values are filed under. Stable: a rename is a removal and an addition. */
  readonly key: string
  readonly label: string
  readonly labelTamil: string | null
  readonly groupName: string
  readonly displayOrder: number | string
  /** `Millimetre`, `Count` or `None`. */
  readonly canonicalUnit: string
  readonly displayUnits: readonly string[]
  /** The inch step as a denominator: 8 means eighths. Zero for no inch display. */
  readonly inchFraction: number | string
  readonly centimetreDecimals: number | string
  readonly isRequired: boolean
  readonly minimumMillimetres: number | string
  readonly maximumMillimetres: number | string
  readonly warnBelowMillimetres: number | string | null
  readonly warnAboveMillimetres: number | string | null
  readonly helpText: string
  /** The sheet and callout together, as `<diagram_key>#<field_key>`. */
  readonly diagramReference: string | null
  readonly diagramAlt: string | null
  /** The bundled sheet as an update must send it back. */
  readonly diagramKey: string | null
  /** The uploaded diagram as an update must send it back (#31). */
  readonly diagramMediaId: string | null
  /** Superseded by `ruleDefinition`. A sentence to show, never to send. */
  readonly rule: string | null
  readonly ruleDefinition: TemplateRule | null
  /** Superseded by `options`. Codes without their labels. */
  readonly optionCodes: readonly string[]
  readonly options: readonly TemplateChoiceOption[]
}

/** One version of a template. `fields` is null on the list, which does not carry them. */
export interface TemplateVersion {
  readonly templateVersionId: string
  readonly versionNumber: number | string
  readonly name: string
  readonly notes: string | null
  readonly status: string
  /** The unit the capture wizard opens in, as this version declares it. */
  readonly defaultDisplayUnit: string
  /** Whether a second administrator has approved it. Publication needs this. */
  readonly isApproved: boolean
  readonly publishedAt: string | null
  readonly retiredAt: string | null
  readonly fields: readonly TemplateField[] | null
}

/**
 * A measurement template and every version of it.
 *
 * There is no `version` member: the concurrency token for this aggregate travels in the `ETag` of
 * the read, not in the body, and the screens hold it beside the value rather than inside it.
 */
export interface MeasurementTemplate {
  readonly measurementTemplateId: string
  /** The stable machine key a catalogue service type points at, such as `MT_BLOUSE_PATTERN`. */
  readonly code: string
  readonly name: string
  readonly description: string | null
  readonly publishedVersionId: string | null
  readonly versions: readonly TemplateVersion[]
}

/** One thing publish validation noticed. `target` names the control it is about. */
export interface TemplateFinding {
  /** `Error` refuses publication; `Warning` does not. */
  readonly severity: string
  readonly code: string
  readonly message: string
  readonly target: string
}

/** What publish validation found. */
export interface TemplateValidation {
  readonly templateVersionId: string
  readonly isReadyToPublish: boolean
  readonly findings: readonly TemplateFinding[]
}
