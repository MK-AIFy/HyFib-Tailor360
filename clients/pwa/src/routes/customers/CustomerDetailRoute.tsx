import { FormattedMessage, useIntl } from 'react-intl'
import type { IntlShape } from 'react-intl'
import { Link, useParams } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { StatusBadge } from '../../components/primitives/StatusBadge'
import { readCustomer } from '../../customers/customersApi'
import { CUSTOMERS_PERMISSIONS } from '../../customers/customersPermissions'
import { useSession } from '../../auth/useSession'
import { customerStatusKind } from '../../customers/customerStatus'
import { useAdminResource } from '../../admin/useAdminResource'
import './customers.css'

/**
 * One customer record, read-only (#26, #182).
 *
 * Correcting a record, and the optimistic-concurrency conflict that comes with it, is #582's own
 * screen (`CustomerEditRoute`); this one links to it for a caller holding `customers.update`, and
 * shows no link to a screen that would only refuse the person who opened it. The timeline (#583),
 * the duplicate-review and merge screen (#584)
 * and consent (#585) are separate units for the same reason: each is real complexity of its own, and
 * none of it is needed to answer "is this the record I found."
 *
 * ## Why the edit link reads the session rather than requiring one
 *
 * `useSession` and not `useCurrentUser`: this screen reads a record, and a link to the correction
 * form is an affordance on top of that, not the reason the screen exists. `useCurrentUser` throws
 * when no account has arrived yet, which would turn a session still in flight into a blank screen
 * for the record underneath — so the link is simply absent until the permissions are known, which is
 * also the right default if they never are. The server re-checks `customers.update` on the request
 * regardless; this only decides whether somebody is offered a door they cannot open.
 *
 * ## Why the contact fields are shown exactly as the server sent them
 *
 * `contactIncluded` distinguishes a field withheld by permission from a customer who has not given
 * one — the difference between "you may not see this" and "there is nothing to see." Rendering the
 * two identically, as blank, is the one thing `field-visibility.md` says a client must not do, so
 * this screen names which case it is rather than picking a placeholder that reads either way.
 */
export function CustomerDetailRoute() {
  const intl = useIntl()
  const { customerId = '' } = useParams()
  const { user } = useSession()

  const record = useAdminResource(customerId, (signal) => readCustomer(customerId, signal))
  const customer = record.value?.value ?? null

  if (record.value === null && record.loading) {
    return <LoadingState what={intl.formatMessage({ id: 'customers.detail.loading' })} />
  }

  if (customer === null) {
    return (
      <>
        <AuthProblemAlert failure={record.failure} />
        <EmptyState iconName="users" live="polite">
          {intl.formatMessage({ id: 'customers.detail.notFound' })}
        </EmptyState>
      </>
    )
  }

  return (
    <section className="page customers">
      <p>
        <Link to="/customers">
          <FormattedMessage id="customers.detail.back" />
        </Link>
      </p>

      <h1>{customer.displayName}</h1>
      {customer.nativeName === null ? null : (
        <p className="customers__lede">{customer.nativeName}</p>
      )}

      <p className="customers__number">
        <FormattedMessage
          id="customers.detail.number"
          values={{ number: customer.customerNumber }}
        />
        {' · '}
        <StatusBadge status={customerStatusKind(customer.status)} />
      </p>

      {user?.permissions.includes(CUSTOMERS_PERMISSIONS.update) === true ? (
        <p>
          <Link to={`/customers/${customer.customerId}/edit`}>
            <FormattedMessage id="customers.detail.correct" />
          </Link>
        </p>
      ) : null}

      <dl className="customers__detailGrid">
        <dt>
          <FormattedMessage id="customers.create.field.phone" />
        </dt>
        <dd>
          {contactCell(
            customer.contactIncluded,
            customer.phone,
            intl.formatMessage({ id: 'customers.detail.notGiven' }),
          )}
        </dd>

        <dt>
          <FormattedMessage id="customers.create.field.alternatePhone" />
        </dt>
        <dd>
          {contactCell(
            customer.contactIncluded,
            customer.alternatePhone,
            intl.formatMessage({ id: 'customers.detail.notGiven' }),
          )}
        </dd>

        <dt>
          <FormattedMessage id="customers.create.field.email" />
        </dt>
        <dd>
          {contactCell(
            customer.contactIncluded,
            customer.email,
            intl.formatMessage({ id: 'customers.detail.notGiven' }),
          )}
        </dd>

        <dt>
          <FormattedMessage id="customers.create.field.addressLine" />
        </dt>
        <dd>
          {contactCell(
            customer.contactIncluded,
            [customer.addressLine, customer.locality, customer.postcode]
              .filter(Boolean)
              .join(', ') || null,
            intl.formatMessage({ id: 'customers.detail.notGiven' }),
          )}
        </dd>

        <dt>
          <FormattedMessage id="customers.detail.language" />
        </dt>
        <dd>{languageLabel(customer.language, intl)}</dd>
      </dl>

      {customer.contactIncluded ? null : (
        <p className="customers__hint">
          <FormattedMessage id="customers.detail.contactWithheld" />
        </p>
      )}

      {customer.aliases.length === 0 ? null : (
        <>
          <h2>
            <FormattedMessage id="customers.detail.aliases" />
          </h2>
          <ul>
            {customer.aliases.map((alias) => (
              <li key={`${alias.kind}:${alias.value}`}>{alias.value}</li>
            ))}
          </ul>
        </>
      )}
    </section>
  )
}

/** A withheld field and one the customer never gave read alike as blank; they must not read alike here. */
function contactCell(included: boolean, value: string | null, notGiven: string): string {
  if (!included) {
    return '—'
  }
  return value ?? notGiven
}

/** The two languages the server sends today, plus the code itself for one it does not name yet. */
function languageLabel(language: string, intl: IntlShape): string {
  switch (language) {
    case 'en-IN':
      return intl.formatMessage({ id: 'customers.create.field.language.en-IN' })
    case 'ta-IN':
      return intl.formatMessage({ id: 'customers.create.field.language.ta-IN' })
    default:
      return language
  }
}
