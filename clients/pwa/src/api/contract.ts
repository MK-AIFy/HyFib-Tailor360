import type { components, operations } from './schema'
import type {
  AuthenticatorEnrolment,
  CurrentUser,
  MultiFactorResult,
  PasskeyChallenge,
  PasskeySummary,
  RecoveryCodeSheet,
  RecoveryCompleted,
  SessionDevice,
  SignInResult,
  SignOutEverywhereResult,
} from '../auth/types'
import type {
  CatalogPublication,
  CatalogValidationReport,
  CatalogVersion,
  CatalogVersionSummary,
  CategoryRequest,
  OrderableCatalog,
  PresentationRequest,
  ServiceTypeRequest,
} from '../catalog/types'
import type { CustomerCard, CustomerPage } from '../customers/types'
import type {
  ConfirmMeasurementsRequest,
  MeasurementCaptureTemplate,
  MeasurementCheck,
  MeasurementComparison,
  MeasurementDraft,
  MeasurementSheet,
  MeasurementSummary,
  MeasurementVersion,
  MeasurementVersionTemplate,
  SaveMeasurementSectionRequest,
  StartMeasurementDraftRequest,
} from '../measurements/types'
import type {
  AssignedAccess,
  AuditPage,
  Branch,
  DeadLetteredMessage,
  FeatureFlag,
  MeasurementTemplate,
  Permission,
  Role,
  StaffSummary,
  StaffUser,
  StaffUserPage,
  TemplateFieldRequest,
  TemplateValidation,
} from '../admin/types'

/**
 * The published API contract, in TypeScript.
 *
 * `schema.d.ts` beside this file is generated from `docs/api/openapi.v1.json` by `pnpm generate:api`.
 * **Do not edit it.** Regenerate it in the same commit as the endpoint change that moved it;
 * `pnpm generate:api:check` fails when the committed file and the committed document have drifted, and
 * CI runs that check.
 *
 * ## Why this file exists as well
 *
 * A generated schema on its own proves nothing: nothing imports it, so it can be wrong for months. The
 * assertions below are what give it teeth. They pin each hand-written payload type in `auth/types.ts`
 * against the schema the server publishes, at compile time and at no runtime cost — so an endpoint
 * whose response gains a required member, loses one, or changes a member's type fails
 * `pnpm typecheck` here rather than failing on a shop floor.
 *
 * The hand-written types are kept, rather than replaced by the generated ones, because they carry the
 * documentation a screen author reads and the narrower unions the client actually branches on
 * (`SignInStep`, `MfaEnrolmentState`). The generated schema types every one of those as a bare
 * `string`, since OpenAPI cannot express "one of these five, and the server will never send a sixth".
 * So the direction of the check matters: **the hand-written type must be assignable to the generated
 * one**, which catches a member that was removed, renamed or retyped, while still permitting the
 * client's narrowing.
 */

/** The response body of one documented operation. */
type Response200<TOperation extends keyof operations> = operations[TOperation] extends {
  responses: { 200: { content: { 'application/json': infer TBody } } }
}
  ? TBody
  : never

/**
 * The request body of one documented operation.
 *
 * Requests need pinning as much as responses do, and until now nothing here pinned one. A
 * hand-written request type is the more dangerous of the two: a response that has drifted shows up
 * as a screen rendering `undefined`, whereas a request that has drifted is a `400` a person
 * discovers by failing to save something they typed. `TemplateFieldRequest` has nineteen members,
 * every one of them required by the schema even where nullable, which is precisely the shape that
 * rots quietly.
 */
type RequestBody<TOperation extends keyof operations> = operations[TOperation] extends {
  requestBody: { content: { 'application/json': infer TBody } }
}
  ? TBody
  : never

/**
 * The same type with every member and array made read-only.
 *
 * The generated types are mutable, because JSON is; the hand-written ones are `readonly` throughout,
 * because a payload is a value the screen renders and never edits in place. Without this the two would
 * be unassignable for a reason that has nothing to do with the contract, and the assertions below would
 * have to be weakened to the point of proving nothing.
 */
type Immutable<T> = T extends readonly (infer TItem)[]
  ? readonly Immutable<TItem>[]
  : T extends object
    ? { readonly [TKey in keyof T]: Immutable<T[TKey]> }
    : T

/**
 * Fails to compile unless `TMine` is assignable to `TContract`.
 *
 * It is a type, not a function, so it disappears entirely at build time — this whole module compiles to
 * nothing and is read by the type checker alone. Each alias is exported so that it counts as used;
 * an unexported one would be reported as dead code rather than as the assertion it is.
 */
export type Conforms<TMine extends TContract, TContract> = TMine

export type SignInConforms = Conforms<SignInResult, Immutable<Response200<'SignIn'>>>

export type PasskeySignInConforms = Conforms<
  SignInResult,
  Immutable<Response200<'CompletePasskeyAssertion'>>
>

export type MultiFactorConforms = Conforms<
  MultiFactorResult,
  Immutable<Response200<'AnswerMultiFactorChallenge'>>
>

export type CurrentUserConforms = Conforms<CurrentUser, Immutable<Response200<'GetCurrentUser'>>>

export type SessionDeviceConforms = Conforms<
  SessionDevice,
  Immutable<components['schemas']['SessionPayload']>
>

export type SignOutEverywhereConforms = Conforms<
  SignOutEverywhereResult,
  Immutable<Response200<'SignOutEverywhere'>>
>

export type EnrolmentConforms = Conforms<
  AuthenticatorEnrolment,
  Immutable<Response200<'BeginMultiFactorEnrolment'>>
>

export type RecoveryCodesConforms = Conforms<
  RecoveryCodeSheet,
  Immutable<Response200<'ConfirmMultiFactorEnrolment'>>
>

export type RecoveryCompletedConforms = Conforms<
  RecoveryCompleted,
  Immutable<Response200<'ConfirmPasswordRecovery'>>
>

export type PasskeySummaryConforms = Conforms<
  PasskeySummary,
  Immutable<components['schemas']['PasskeyPayload']>
>

export type PasskeyChallengeConforms = Conforms<
  PasskeyChallenge,
  Immutable<Response200<'BeginPasskeyAssertion'>>
>

/*
 * The administration surface (#25).
 *
 * Same direction as the assertions above — the hand-written type must be assignable to the generated
 * one — so a payload member that is renamed, retyped or removed on the server fails here rather than
 * on an administrator's screen. The enum-like members (`status`, `reach`, `scope`, `mfaEnrolment`)
 * stay `string` on both sides: the client offers its own narrowed constants for the values it
 * branches on, but a server that gained a value would otherwise fail to type rather than fail to
 * render, and the screen's job is to show an unfamiliar status, not to refuse it.
 */

export type StaffSummaryConforms = Conforms<
  StaffSummary,
  Immutable<components['schemas']['StaffSummaryPayload']>
>

export type StaffUserConforms = Conforms<StaffUser, Immutable<Response200<'GetStaffUser'>>>

export type StaffPageConforms = Conforms<StaffUserPage, Immutable<Response200<'ListStaffUsers'>>>

export type AssignedAccessConforms = Conforms<
  AssignedAccess,
  Immutable<Response200<'GetStaffUserAccess'>>
>

export type BranchConforms = Conforms<Branch, Immutable<Response200<'GetBranch'>>>

export type RoleConforms = Conforms<Role, Immutable<Response200<'GetRole'>>>

export type PermissionConforms = Conforms<
  Permission,
  Immutable<components['schemas']['PermissionPayload']>
>

export type FeatureFlagConforms = Conforms<FeatureFlag, Immutable<Response200<'GetFeatureFlag'>>>

export type AuditPageConforms = Conforms<AuditPage, Immutable<Response200<'ReadAuditTrail'>>>

export type DeadLetterConforms = Conforms<
  DeadLetteredMessage,
  Immutable<components['schemas']['DeadLetteredMessagePayload']>
>

/* Measurement templates (#27, #93) ------------------------------------------------------------ */

/**
 * These live under `/api/v1/customers/`, not `/api/v1/admin/` — the routes belong to the Customers
 * module even though an administrator is the only person who uses them.
 *
 * `MeasurementTemplate` is pinned against the read, but the same payload is what all eleven writes
 * answer with, so pinning it once covers every one of them.
 */
export type MeasurementTemplateConforms = Conforms<
  MeasurementTemplate,
  Immutable<Response200<'GetMeasurementTemplate'>>
>

export type TemplateValidationConforms = Conforms<
  TemplateValidation,
  Immutable<Response200<'ValidateMeasurementTemplateVersion'>>
>

/**
 * The write half (#102).
 *
 * Adding and changing share one body, so pinning it once covers both routes; the assertion is what
 * turns "the server grew a required member" from a refusal on a shop floor into a compile error
 * here. The direction is the same as every other assertion in this file — the hand-written type must
 * be assignable to the generated one — which is what makes a member the server requires and this
 * type omits fail to compile.
 */
export type TemplateFieldRequestConforms = Conforms<
  TemplateFieldRequest,
  Immutable<RequestBody<'AddMeasurementTemplateField'>>
>

/** The same body on the change route, asserted separately so a divergence between them is caught. */
export type TemplateFieldChangeRequestConforms = Conforms<
  TemplateFieldRequest,
  Immutable<RequestBody<'ChangeMeasurementTemplateField'>>
>

/* The catalogue (#29, #85) --------------------------------------------------------------------- */

/**
 * Fifteen routes under `/api/v1/catalog`, published with #29's backend and unused by any client
 * until now. The reads and the writes are both pinned, for the reason the measurement-template
 * request is: a drifted response renders `undefined`, and a drifted request is a `400` a person
 * meets after filling in a form.
 */
export type CatalogVersionConforms = Conforms<
  CatalogVersion,
  Immutable<Response200<'GetCatalogVersion'>>
>

export type CurrentCatalogConforms = Conforms<
  OrderableCatalog,
  Immutable<Response200<'GetCurrentCatalog'>>
>

export type CatalogVersionSummaryConforms = Conforms<
  CatalogVersionSummary,
  Immutable<components['schemas']['CatalogVersionSummaryPayload']>
>

export type CatalogValidationConforms = Conforms<
  CatalogValidationReport,
  Immutable<Response200<'ValidateCatalogVersion'>>
>

export type CatalogPublicationConforms = Conforms<
  CatalogPublication,
  Immutable<Response200<'PublishCatalogVersion'>>
>

export type CategoryRequestConforms = Conforms<
  CategoryRequest,
  Immutable<RequestBody<'AddCatalogCategory'>>
>

export type CategoryEditRequestConforms = Conforms<
  CategoryRequest,
  Immutable<RequestBody<'EditCatalogCategory'>>
>

export type ServiceTypeRequestConforms = Conforms<
  ServiceTypeRequest,
  Immutable<RequestBody<'AddCatalogServiceType'>>
>

export type ServiceTypeEditRequestConforms = Conforms<
  ServiceTypeRequest,
  Immutable<RequestBody<'EditCatalogServiceType'>>
>

export type PresentationRequestConforms = Conforms<
  PresentationRequest,
  Immutable<RequestBody<'CorrectCatalogCategoryPresentation'>>
>

/* The customer search and the measurement capture (#26, #121, #123). ------------------------- */

export type CustomerCardConforms = Conforms<
  CustomerCard,
  Immutable<components['schemas']['CustomerCardPayload']>
>

export type CustomerPageConforms = Conforms<CustomerPage, Immutable<Response200<'SearchCustomers'>>>

export type MeasurementDraftConforms = Conforms<
  MeasurementDraft,
  Immutable<Response200<'GetMeasurementDraft'>>
>

export type MeasurementCaptureTemplateConforms = Conforms<
  MeasurementCaptureTemplate,
  Immutable<Response200<'GetMeasurementDraftTemplate'>>
>

export type MeasurementCheckConforms = Conforms<
  MeasurementCheck,
  Immutable<Response200<'CheckMeasurementDraft'>>
>

export type MeasurementVersionConforms = Conforms<
  MeasurementVersion,
  Immutable<components['schemas']['MeasurementVersionPayload']>
>

export type StartMeasurementDraftRequestConforms = Conforms<
  StartMeasurementDraftRequest,
  Immutable<RequestBody<'StartMeasurementDraft'>>
>

export type SaveMeasurementSectionRequestConforms = Conforms<
  SaveMeasurementSectionRequest,
  Immutable<RequestBody<'SaveMeasurementSection'>>
>

export type ConfirmMeasurementsRequestConforms = Conforms<
  ConfirmMeasurementsRequest,
  Immutable<RequestBody<'ConfirmMeasurements'>>
>

/* Reuse, comparison and the sheet (#124). ------------------------------------------------------ */

export type MeasurementSummaryConforms = Conforms<
  MeasurementSummary,
  Immutable<components['schemas']['MeasurementSummaryPayload']>
>

export type MeasurementComparisonConforms = Conforms<
  MeasurementComparison,
  Immutable<Response200<'CompareMeasurements'>>
>

export type MeasurementSheetConforms = Conforms<
  MeasurementSheet,
  Immutable<Response200<'ReadMeasurementSheet'>>
>

export type MeasurementVersionTemplateConforms = Conforms<
  MeasurementVersionTemplate,
  Immutable<Response200<'GetMeasurementVersionTemplate'>>
>

export type MeasurementListConforms = Conforms<
  readonly MeasurementSummary[],
  Immutable<Response200<'ListCustomerMeasurements'>>
>
