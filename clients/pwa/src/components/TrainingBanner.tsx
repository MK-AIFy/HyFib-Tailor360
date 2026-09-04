import { FormattedMessage } from 'react-intl'
import { isProductionEnvironment, useVersion } from '../app/version'

/**
 * The persistent "TRAINING — not real data" banner.
 *
 * It is shown on every screen whenever GET /api/version reports an environment other than
 * 'production', so that a member of staff can never mistake a training or staging device for the real
 * shop (implementation plan #20). It is a status region, not an alert: it announces politely once and
 * does not interrupt, and it cannot be dismissed.
 *
 * While the version is loading, and if the request fails, nothing is rendered — claiming "training"
 * on a production device would be as harmful as the reverse, so the banner states only what the
 * server has actually confirmed.
 */
export function TrainingBanner() {
  const version = useVersion()

  if (version.status !== 'ready' || isProductionEnvironment(version.info.environment)) {
    return null
  }

  return (
    <div className="training-banner" role="status">
      <strong className="training-banner__title">
        <FormattedMessage id="banner.training.title" />
      </strong>{' '}
      <span className="training-banner__detail">
        <FormattedMessage
          id="banner.training.detail"
          values={{ environment: version.info.environment }}
        />
      </span>
    </div>
  )
}
