import { FormattedMessage } from 'react-intl'
import { Link } from 'react-router'
import { Icon } from '../../components/primitives/Icon'
import './measurements.css'

/**
 * The Measurements destination.
 *
 * The queue of garments waiting to be measured arrives with intake (#32b); until then the
 * destination is the door to the one journey it owns, so that the primary action on the phone shell
 * and the rail entry on the desktop both land somewhere that says what it is.
 */
export function MeasurementsHomeRoute() {
  return (
    <section className="page measurements">
      <h1>
        <FormattedMessage id="measurements.title" />
      </h1>
      <p className="measurements__lede">
        <FormattedMessage id="measurements.body" />
      </p>
      <p>
        <Link className="button" data-size="primary" data-variant="primary" to="/measurements/new">
          <Icon name="ruler" />
          <span className="button__label">
            <FormattedMessage id="measurements.start" />
          </span>
        </Link>
      </p>
    </section>
  )
}
