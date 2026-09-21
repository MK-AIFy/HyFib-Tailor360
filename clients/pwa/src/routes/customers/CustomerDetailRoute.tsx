import { FormattedMessage, useIntl } from 'react-intl'
import type { IntlShape } from 'react-intl'
import { Link, useParams } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { StatusBadge } from '../../components/primitives/StatusBadge'
import { Tabs } from '../../components/navigation/Tabs'
import { readCustomer } from '../../customers/customersApi'
import { CUSTOMERS_PERMISSIONS } from '../../customers/customersPermissions'
import { useSession } from '../../auth/useSession'
import { customerStatusKind } from '../../customers/customerStatus'
import type { Customer } from '../../customers/types'
import { CustomerTimelineTab } from './CustomerTimelineTab'
import { useAdminResource } from '../../admin/useAdminResource'
import './customers.css'

/**
 * One customer record: the details, and the history behind them (#26, #182, #583).
 *
 * Correcting a record, and the optimistic-concurrency conflict that comes with it, is #582's own
 * screen (`CustomerEditRoute`); this one links to it for a caller holding `customers.update`, and
 * shows no link to a screen that would only refuse the person who opened it. The duplicate-review
 * and merge screen (#584) and consent (#585) are separate units for the same reason: each is real
 * complexity of its own, and none of it is needed to answer "is this the record I found."
 *
 * ## The history is a tab, and it is fetched when the tab is opened
 *
 * `Tabs` mounts only the selected panel, so opening "History" is what asks for the timeline. Its own
 * doc comment argues for activation-follows-focus on the grounds that "nothing is fetched by
 * arrowing across", which this screen is the first to make untrue — with two tabs, arrowing costs at
 * most the one request that clicking would have made anyway, and paying it on the arrow rather than
 * on a second keypress is not a bargain worth an extra keystroke for every keyboard user. If a third
 * tab arrives that is expensive to load, that is the point to revisit, not this one.
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

      {/*
       * Offered on `customers.read`, because reading who might be a duplicate is what Reception does
       * before asking a manager to merge — the merge control itself lives on that screen and appears
       * only for `customers.merge`.
       */}
      <p>
        <Link to={`/customers/${customer.customerId}/duplicates`}>
          <FormattedMessage id="customers.detail.duplicates" />
        </Link>
      </p>

      {user?.permissions.includes(CUSTOMERS_PERMISSIONS.readConsent) === true ? (
        <p>
          <Link to={`/customers/${customer.customerId}/consent`}>
            <FormattedMessage id="customers.detail.consent" />
          </Link>
        </p>
      ) : null}

      <Tabs
        items={[
          {
            id: 'record',
            label: intl.formatMessage({ id: 'customers.detail.tab.record' }),
            icon: 'users',
            panel: <RecordPanel customer={customer} intl={intl} />,
          },
          {
            id: 'history',
            label: intl.formatMessage({ id: 'customers.detail.tab.history' }),
            icon: 'clock',
            /*
             * Keyed on the customer, so moving from one record to another with the history open
             * starts a fresh one. React Router re-renders this route in place when `:customerId`
             * changes rather than remounting it, and the history tab holds the pages it has already
             * followed — without the key those pages would survive the change and this person's
             * record would be shown above the last person's history.
             */
            panel: (
              <CustomerTimelineTab key={customer.customerId} customerId={customer.customerId} />
            ),
          },
        ]}
        label={intl.formatMessage({ id: 'customers.detail.tabs' })}
      />
    </section>
  )
}

/**
 * The record itself, as the first tab's panel.
 *
 * Split out when the history tab arrived (#583), not because this screen grew too long but because
 * `Tabs` mounts only the selected panel — so the panel has to be a component for the unselected one
 * to cost nothing. The header above the tabs is deliberately not part of either panel: the person's
 * name, number and status are what the screen is about, and they should not disappear when somebody
 * looks at the history.
 */
function RecordPanel({
  customer,
  intl,
}: {
  readonly customer: Customer
  readonly intl: IntlShape
}) {
  return (
    <>
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
    </>
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
