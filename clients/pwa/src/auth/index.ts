/**
 * The authentication family.
 *
 * Everything the rest of the application needs to know about who is signed in, and nothing about how
 * the session is carried — which is the point. The session is an `httpOnly` cookie, the transport
 * puts the anti-forgery header on every write, and a screen that wants to know who the caller is
 * asks `useSession` rather than reading anything.
 *
 * A family barrel rather than one root index, for the reason every other family here has one: a
 * single index every family had to be added to would be the one file every change touches.
 *
 * `apiClient` is exported because it is the transport every later module's data layer builds on;
 * `antiforgery` is not, because nothing outside this family should be handling a token at all.
 */
export { ApiError, apiRequest, setSessionChallengeHandler } from './apiClient'
export type {
  ApiProblem,
  ApiRequestOptions,
  HttpMethod,
  SessionChallengeHandler,
  SessionState,
} from './apiClient'
export { AuthProblemAlert } from './AuthProblemAlert'
export type { AuthProblemAlertProps } from './AuthProblemAlert'
export { authProblemMessage, hasFieldErrors } from './authProblems'
export type { AuthProblemMessage } from './authProblems'
export { AFTER_SIGN_IN, AUTH_ROUTES, RECOVERY_TOKEN_PARAM, redirectTargetFrom } from './authRoutes'
export { CopyButton } from './CopyButton'
export type { CopyButtonProps } from './CopyButton'
export {
  STEP_UP_FRESHNESS_SECONDS,
  hasExpired,
  isStepUpFresh,
  millisecondsUntilWarning,
  secondsRemaining,
  sessionDeadline,
  warningDue,
} from './expiry'
export { FactorFields } from './FactorFields'
export type { FactorFieldsProps } from './FactorFields'
export { isPasskeySupported, PasskeyCancelled, PasskeyUnsupported } from './passkeys'
export { QrCode } from './QrCode'
export type { QrCodeProps } from './QrCode'
export { ReauthenticationDialog } from './ReauthenticationDialog'
export type { ReauthenticationDialogProps } from './ReauthenticationDialog'
export { RecoveryCodes } from './RecoveryCodes'
export type { RecoveryCodesProps } from './RecoveryCodes'
export { RequireSession } from './RequireSession'
export type { RequireSessionProps } from './RequireSession'
export { SessionContext } from './sessionContext'
export type {
  ReauthenticationReason,
  ReauthenticationRequest,
  SessionStatus,
  SessionValue,
} from './sessionContext'
export { SessionExpiryDialog } from './SessionExpiryDialog'
export type { SessionExpiryDialogProps } from './SessionExpiryDialog'
export { SessionProvider } from './SessionProvider'
export type { SessionProviderProps } from './SessionProvider'
export { useCurrentUser, useSession, useStepUp } from './useSession'
export { usePasskeySignIn } from './usePasskeySignIn'
export type { PasskeySignIn } from './usePasskeySignIn'
export type {
  AccountPreferences,
  AccountSecurity,
  AuthenticatorEnrolment,
  ChallengeFactor,
  CurrentUser,
  FactorAvailability,
  MfaEnrolmentState,
  MultiFactorResult,
  PasskeyChallenge,
  PasskeySummary,
  RecoveryCodeSheet,
  RecoveryCompleted,
  SessionDevice,
  SessionExpiry,
  SignInResult,
  SignInStep,
  SignOutEverywhereResult,
} from './types'
