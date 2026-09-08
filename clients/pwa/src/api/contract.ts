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
  AssignedAccess,
  AuditPage,
  Branch,
  DeadLetteredMessage,
  FeatureFlag,
  Permission,
  Role,
  StaffSummary,
  StaffUser,
  StaffUserPage,
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
