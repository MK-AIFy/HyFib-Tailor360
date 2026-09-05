import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link } from 'react-router'
import { useVersion } from '../app/version'
import { Card } from '../components/primitives/Card'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { isInstalledDisplayMode } from './installPlatform'
import './install.css'

export interface BuildDetailsProps {
  /** Reads the endpoint again. The About screen supplies it by remounting this component. */
  readonly onRetry: () => void
}

/**
 * The build description, in its three states.
 *
 * A separate component so the About screen can force a re-read by changing its `key`. `useVersion`
 * fetches once per mount and exposes no refetch; remounting is therefore the honest retry, and it is
 * a great deal less code than a second data-fetching path — which section 6 of
 * `clients/pwa/CLAUDE.md` forbids outright. When the shared query cache arrives with #53's generated
 * client, this component keeps its shape and loses the key.
 */
export function BuildDetails({ onRetry }: BuildDetailsProps) {
  const intl = useIntl()
  const version = useVersion()

  if (version.status === 'loading') {
    return <LoadingState what={intl.formatMessage({ id: 'about.loading.what' })} />
  }

  if (version.status === 'error') {
    return (
      <ErrorState onRetry={onRetry} title={intl.formatMessage({ id: 'about.unavailable.title' })}>
        <FormattedMessage id="about.unavailable.body" />
      </ErrorState>
    )
  }

  const { info } = version

  return (
    <Card headingLevel={2} title={intl.formatMessage({ id: 'about.details.label' })}>
      {/*
       * A description list rather than a table: these are four label-and-value pairs, not a grid,
       * and a screen reader announces a definition list as pairs without the row-and-column
       * arithmetic a one-column table forces on it.
       */}
      <dl className="about__details">
        <dt>
          <FormattedMessage id="about.version.label" />
        </dt>
        <dd>{info.version}</dd>

        <dt>
          <FormattedMessage id="about.build.label" />
        </dt>
        {/*
         * A build hash is read out character by character when it is dictated over a telephone, so
         * it is set in the tabular figures rather than the body face, where 0 and O are one shape.
         */}
        <dd className="about__value--code">{info.buildHash}</dd>

        <dt>
          <FormattedMessage id="about.environment.label" />
        </dt>
        <dd>{info.environment}</dd>

        <dt>
          <FormattedMessage id="about.displayMode.label" />
        </dt>
        <dd>
          <FormattedMessage
            id={
              isInstalledDisplayMode() ? 'about.displayMode.installed' : 'about.displayMode.browser'
            }
          />
        </dd>
      </dl>
    </Card>
  )
}

/**
 * The About screen: which build is on this device, and how to report a problem with it.
 *
 * ## Why it exists
 *
 * Because "it is broken" from a counter tablet is unactionable without knowing which build the
 * tablet is running, and because the shell's footer shows the version in a place nobody reads aloud
 * over a telephone. `GET /api/version` is anonymous by design — the shell reads it before a session
 * exists — so this screen works even when the person cannot sign in, which is exactly when it is
 * needed most.
 *
 * ## What it deliberately does not show
 *
 * Nothing beyond the version, the build hash, the environment name and how the page was opened. The
 * endpoint itself returns nothing else: `VersionEndpoints.cs` records that it discloses "only the
 * build hash and the environment name — never configuration, hostnames or dependency versions", and
 * a support screen that grew a list of feature flags or a user agent would be a disclosure surface
 * on an anonymous page.
 *
 * The support paragraph tells the person not to put a customer's details in the message they send.
 * That is not decoration: the fastest route to personal data leaving the shop is a well-meaning
 * screenshot of a measurement screen attached to a support request.
 */
export function AboutRoute() {
  const intl = useIntl()
  const [attempt, setAttempt] = useState(0)

  return (
    <section className="page about">
      <h1>
        <FormattedMessage id="about.title" />
      </h1>
      <p>
        <FormattedMessage id="about.body" />
      </p>

      <BuildDetails
        key={attempt}
        onRetry={() => {
          setAttempt((previous) => previous + 1)
        }}
      />

      <Card headingLevel={2} title={intl.formatMessage({ id: 'about.support.title' })}>
        <p>
          <FormattedMessage id="about.support.body" />
        </p>
      </Card>

      <p>
        <Link to="/install">
          <FormattedMessage id="about.install.link" />
        </Link>
      </p>
    </section>
  )
}
