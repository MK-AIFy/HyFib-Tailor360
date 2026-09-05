/**
 * The states family.
 *
 * The five states DoD item 7 requires a story for — loading, empty, error, offline and forbidden —
 * plus the two network states plan Section 4.6 names. They are the screens a person meets when
 * nothing has gone to plan, which is why they are a family of their own rather than an afterthought
 * inside each screen: docs/nfr/a11y-checklist.md section 4.12 asks the same three questions of every
 * one of them, and one implementation is how the answers stay the same.
 *
 * There is no barrel at the root of `src/components` on purpose. A consumer imports from
 * `.../components/states`, so a family can be added without every family's change touching one
 * shared index.
 */
export { EmptyState } from './EmptyState'
export type { EmptyStateProps } from './EmptyState'
export { ErrorState } from './ErrorState'
export type { ErrorStateProps } from './ErrorState'
export { Forbidden } from './Forbidden'
export type { ForbiddenProps } from './Forbidden'
export { LoadingState } from './LoadingState'
export type { LoadingStateProps } from './LoadingState'
export { NetworkStatusBanner } from './NetworkStatusBanner'
export type { NetworkStatusBannerProps } from './NetworkStatusBanner'
export { OfflineBlockedAction } from './OfflineBlockedAction'
export type { OfflineBlockedActionProps } from './OfflineBlockedAction'
export { RetryableError } from './RetryableError'
export type { RetryableErrorProps } from './RetryableError'
export { StateRegion } from './StateRegion'
export type { StateRegionProps } from './StateRegion'
export {
  FAILURE_CAUSE_MESSAGES,
  REQUEST_FAILURE_CAUSES,
  failureCauseForStatus,
  plainLanguageDetail,
} from './problemDetails'
export type { ProblemDetails, RequestFailureCause } from './problemDetails'
export { STATE_LIVENESS, STATE_TONES } from './stateVariants'
export type { StateHeadingLevel, StateLiveness, StateTone } from './stateVariants'
export { useNetworkState } from './useNetworkState'
export type { NetworkState } from './useNetworkState'
