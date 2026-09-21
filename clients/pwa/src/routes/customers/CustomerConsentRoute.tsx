import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import type { IntlShape } from 'react-intl'
import { Link, useParams } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ApiError } from '../../auth/apiClient'
import { useSession } from '../../auth/useSession'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { Card } from '../../components/primitives/Card'
import { Icon } from '../../components/primitives/Icon'
import type { IconName } from '../../components/primitives/icons'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { Select } from '../../design-system/components/forms/Select'
import { TextField } from '../../design-system/components/forms/TextField'
import { TimeField } from '../../design-system/components/forms/TimeField'
import { useAdminResource } from '../../admin/useAdminResource'
import {
  readCommunicationPreferences,
  readConsent,
  recordConsent,
  replaceCommunicationPreferences,
} from '../../customers/customersApi'
import { CUSTOMERS_PERMISSIONS } from '../../customers/customersPermissions'
import { COMMUNICATION_CHANNELS } from '../../customers/types'
import type { ConsentPurpose } from '../../customers/types'
import { getFormatters } from '../../i18n/formatters'
import { isSupportedLocale } from '../../i18n/locales'
import './customers.css'

/**
 * What a customer has agreed to, and how she wants to be reached (#26, #182, #585).
 *
 * Its own address rather than a third tab on the record, unlike the history: `Tabs` activates on
 * arrow-key focus and its own doc comment justifies that on the grounds that nothing is fetched by
 * arrowing across. The history already made that untrue for two tabs, where the cost is the one
 * request a click would have made anyway; a third tab that fetches two more would be a keyboard user
 * paying for screens they did not ask for. This is also the screen somebody is sent straight to.
 *
 * ## An answer is appended, never edited
 *
 * Withdrawing is a `Withdrawn` answer and agreeing again is another `Granted` one, so the evidence
 * that she once withdrew survives her changing her mind. That is why this screen offers "record what
 * she said" and never "change what she said", and why every previous answer stays on the page: the
 * history is the record, and the current status is only its first row.
 *
 * ## The wording version is never sent
 *
 * A consent record names the version of the words the customer was read. The client cannot name one,
 * because a client that could would be able to record an answer against words she was never read —
 * so the server reads it from the register, and a purpose with no published wording cannot be
 * answered at all. `canBeAnswered` is that decision, already taken; this screen shows why rather than
 * recomputing it from `isRetired`, which is only one of the two reasons.
 *
 * ## Quiet hours are both ends or neither
 *
 * They are wall-clock times at the branch and may run backwards over midnight, which is the ordinary
 * case — 21:00 to 08:00 is a quiet night, not an error. So nothing here reorders them or complains
 * that the end is before the start; the only rule enforced is the one the server states, that one
 * end without the other is not a preference.
 */
export function CustomerConsentRoute() {
  const intl = useIntl()
  const network = useNetworkState()
  const { user } = useSession()
  const { customerId = '' } = useParams()

  const consent = useAdminResource(customerId, (signal) => readConsent(customerId, signal))
  const preferences = useAdminResource(customerId, (signal) =>
    readCommunicationPreferences(customerId, signal),
  )

  const mayRecord = user?.permissions.includes(CUSTOMERS_PERMISSIONS.update) === true

  if (consent.value === null && consent.loading) {
    return <LoadingState what={intl.formatMessage({ id: 'customers.consent.loading' })} />
  }

  if (consent.value === null) {
    return (
      <>
        <AuthProblemAlert failure={consent.failure} />
        <EmptyState iconName="users" live="polite">
          {intl.formatMessage({ id: 'customers.detail.notFound' })}
        </EmptyState>
      </>
    )
  }

  return (
    <section className="page customers">
      <p>
        <Link to={`/customers/${customerId}`}>
          <FormattedMessage id="customers.edit.back" />
        </Link>
      </p>

      <h1>
        <FormattedMessage id="customers.consent.title" />
      </h1>
      <p className="customers__lede">
        <FormattedMessage id="customers.consent.body" />
      </p>

      {consent.value.purposes.length === 0 ? (
        <EmptyState iconName="clipboard" live="polite">
          {intl.formatMessage({ id: 'customers.consent.empty' })}
        </EmptyState>
      ) : (
        consent.value.purposes.map((purpose) => (
          <PurposeCard
            customerId={customerId}
            key={purpose.key}
            mayRecord={mayRecord}
            online={network.online}
            onRecorded={() => {
              consent.reload()
            }}
            purpose={purpose}
          />
        ))
      )}

      <PreferencesForm
        customerId={customerId}
        mayRecord={mayRecord}
        online={network.online}
        preferences={preferences}
      />
    </section>
  )
}

/** One purpose: where she stands, what she has said before, and what can be recorded now. */
function PurposeCard({
  customerId,
  purpose,
  mayRecord,
  online,
  onRecorded,
}: {
  readonly customerId: string
  readonly purpose: ConsentPurpose
  readonly mayRecord: boolean
  readonly online: boolean
  readonly onRecorded: () => void
}) {
  const intl = useIntl()
  const [source, setSource] = useState('')
  const [busy, setBusy] = useState<string | null>(null)
  const [failure, setFailure] = useState<unknown>(null)
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID())

  const record = (decision: string) => {
    if (source.trim() === '') {
      setFailure(new ApiError('A source is required.', { status: 400 }))
      return
    }

    setBusy(decision)
    setFailure(null)

    void recordConsent({
      customerId,
      purposeKey: purpose.key,
      decision,
      source: source.trim(),
      idempotencyKey,
    })
      .then(() => {
        setSource('')
        // A new key for the next, separate answer. The guarantee is that a resend of *this* answer
        // replays, not that two different answers collapse into one.
        setIdempotencyKey(crypto.randomUUID())
        onRecorded()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(null)
      })
  }

  const sourceError =
    failure instanceof ApiError && failure.status === 400 && source.trim() === ''
      ? intl.formatMessage({ id: 'customers.consent.sourceRequired' })
      : undefined

  return (
    <Card headingLevel={2} title={purpose.name}>
      <p className="customers__consentStatus">
        <Icon name={statusIcon(purpose.status)} />{' '}
        {intl.formatMessage({ id: statusMessage(purpose.status) })}
      </p>

      {purpose.description === null ? null : <p>{purpose.description}</p>}

      {purpose.canBeAnswered ? null : (
        <Alert live="off" tone="info">
          <FormattedMessage
            id={purpose.isRetired ? 'customers.consent.retired' : 'customers.consent.noWording'}
          />
        </Alert>
      )}

      {purpose.answers.length === 0 ? null : (
        <>
          <h3>
            <FormattedMessage id="customers.consent.answers" />
          </h3>
          <ul>
            {purpose.answers.map((answer) => (
              <li key={answer.recordId}>
                <FormattedMessage
                  id="customers.consent.answer"
                  values={{
                    decision: intl.formatMessage({ id: statusMessage(answer.decision) }),
                    when: formatWhen(answer.recordedAt, intl),
                    version: answer.wordingVersion,
                    source: answer.source,
                  }}
                />
              </li>
            ))}
          </ul>
        </>
      )}

      {!mayRecord || !purpose.canBeAnswered ? null : (
        <>
          <AuthProblemAlert failure={sourceError === undefined ? failure : null} />

          <TextField
            autoComplete="off"
            description={intl.formatMessage({ id: 'customers.consent.sourceHint' })}
            id={`consent-source-${purpose.key}`}
            label={intl.formatMessage({ id: 'customers.consent.source' })}
            name="source"
            onValueChange={(value) => {
              setSource(value)
              setFailure(null)
            }}
            required
            value={source}
            {...(sourceError === undefined ? {} : { error: sourceError })}
          />

          {online ? (
            <div className="customers__consentActions">
              <Button
                busy={busy === 'Granted'}
                iconName="check"
                onClick={() => {
                  record('Granted')
                }}
                variant="primary"
              >
                {intl.formatMessage({ id: 'customers.consent.grant' })}
              </Button>
              <Button
                busy={busy === 'Declined'}
                iconName="x-circle"
                onClick={() => {
                  record('Declined')
                }}
                variant="secondary"
              >
                {intl.formatMessage({ id: 'customers.consent.decline' })}
              </Button>
              {purpose.status === 'Granted' ? (
                <Button
                  busy={busy === 'Withdrawn'}
                  iconName="close"
                  onClick={() => {
                    record('Withdrawn')
                  }}
                  variant="secondary"
                >
                  {intl.formatMessage({ id: 'customers.consent.withdraw' })}
                </Button>
              ) : null}
            </div>
          ) : (
            <OfflineBlockedAction
              action={intl.formatMessage({ id: 'customers.consent.offlineAction' })}
            />
          )}
        </>
      )}
    </Card>
  )
}

/** How she wants to be reached. Whole-state, so the trail reads as a state rather than a diff. */
function PreferencesForm({
  customerId,
  preferences,
  mayRecord,
  online,
}: {
  readonly customerId: string
  readonly preferences: ReturnType<
    typeof useAdminResource<Awaited<ReturnType<typeof readCommunicationPreferences>>>
  >
  readonly mayRecord: boolean
  readonly online: boolean
}) {
  const intl = useIntl()
  const current = preferences.value
  const [draft, setDraft] = useState<{
    readonly channels: readonly string[]
    readonly language: string
    readonly start: string
    readonly end: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID())

  if (current === null) {
    return (
      <>
        <h2>
          <FormattedMessage id="customers.preferences.title" />
        </h2>
        {preferences.loading ? (
          <LoadingState what={intl.formatMessage({ id: 'customers.preferences.loading' })} />
        ) : (
          <AuthProblemAlert failure={preferences.failure} />
        )}
      </>
    )
  }

  const values = draft ?? {
    channels: current.allowedChannels,
    language: current.language,
    start: (current.quietHoursStart ?? '').slice(0, 5),
    end: (current.quietHoursEnd ?? '').slice(0, 5),
  }

  const halfAnHour = (values.start === '') !== (values.end === '')
  const quietError = halfAnHour
    ? intl.formatMessage({ id: 'customers.preferences.quietHoursBothEnds' })
    : undefined

  const save = () => {
    if (halfAnHour) {
      return
    }

    setBusy(true)
    setSaved(false)
    setFailure(null)

    void replaceCommunicationPreferences({
      customerId,
      allowedChannels: values.channels,
      language: values.language,
      quietHoursStart: values.start === '' ? null : `${values.start}:00`,
      quietHoursEnd: values.end === '' ? null : `${values.end}:00`,
      // Omitted, not `*`, when no preference exists yet: there is no version of a row that does not
      // exist, and the server requires the header's absence rather than a wildcard.
      version: current.hasBeenRecorded ? (current.version ?? undefined) : undefined,
      idempotencyKey,
    })
      .then(() => {
        setSaved(true)
        setDraft(null)
        setIdempotencyKey(crypto.randomUUID())
        preferences.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const toggle = (channel: string, on: boolean) => {
    setSaved(false)
    setDraft({
      ...values,
      channels: on
        ? [...values.channels, channel]
        : values.channels.filter((held) => held !== channel),
    })
  }

  return (
    <>
      <h2>
        <FormattedMessage id="customers.preferences.title" />
      </h2>
      <p className="customers__hint">
        <FormattedMessage id="customers.preferences.body" />
      </p>

      {saved ? (
        <Alert
          live="polite"
          onDismiss={() => {
            setSaved(false)
          }}
          tone="success"
        >
          <FormattedMessage id="customers.preferences.saved" />
        </Alert>
      ) : null}

      <AuthProblemAlert failure={failure} />

      <fieldset className="customers__channels">
        <legend>
          <FormattedMessage id="customers.preferences.channels" />
        </legend>
        {COMMUNICATION_CHANNELS.map((channel) => (
          <Checkbox
            disabled={!mayRecord}
            id={`channel-${channel}`}
            key={channel}
            label={intl.formatMessage({ id: `customers.preferences.channel.${channel}` })}
            name={channel}
            onValueChange={(on) => {
              toggle(channel, on)
            }}
            value={values.channels.includes(channel)}
          />
        ))}
      </fieldset>
      {values.channels.length === 0 ? (
        <p className="customers__hint">
          <FormattedMessage id="customers.preferences.noChannels" />
        </p>
      ) : null}

      <Select
        disabled={!mayRecord}
        emptyLabel={null}
        id="preferences-language"
        label={intl.formatMessage({ id: 'customers.create.field.language' })}
        name="language"
        onValueChange={(value) => {
          setSaved(false)
          setDraft({ ...values, language: value })
        }}
        options={[
          {
            value: 'en-IN',
            label: intl.formatMessage({ id: 'customers.create.field.language.en-IN' }),
          },
          {
            value: 'ta-IN',
            label: intl.formatMessage({ id: 'customers.create.field.language.ta-IN' }),
          },
        ]}
        value={values.language}
      />

      <TimeField
        description={intl.formatMessage({ id: 'customers.preferences.quietHoursHint' })}
        disabled={!mayRecord}
        id="preferences-quiet-start"
        label={intl.formatMessage({ id: 'customers.preferences.quietHoursStart' })}
        name="quietHoursStart"
        onValueChange={(value) => {
          setSaved(false)
          setDraft({ ...values, start: value })
        }}
        value={values.start}
        {...(quietError === undefined ? {} : { error: quietError })}
      />
      <TimeField
        disabled={!mayRecord}
        id="preferences-quiet-end"
        label={intl.formatMessage({ id: 'customers.preferences.quietHoursEnd' })}
        name="quietHoursEnd"
        onValueChange={(value) => {
          setSaved(false)
          setDraft({ ...values, end: value })
        }}
        value={values.end}
      />

      {!mayRecord ? null : online ? (
        <Button busy={busy} iconName="check" onClick={save} variant="primary">
          {intl.formatMessage({ id: 'customers.preferences.save' })}
        </Button>
      ) : (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'customers.preferences.offlineAction' })}
        />
      )}
    </>
  )
}

/** The glyph for a consent status. Shapes differ, so the row reads in greyscale. */
function statusIcon(status: string): IconName {
  switch (status) {
    case 'Granted':
      return 'check-circle'
    case 'Declined':
      return 'x-circle'
    case 'Withdrawn':
      return 'close'
    default:
      return 'help'
  }
}

/**
 * The word for a consent status.
 *
 * Not `StatusBadge`: that renders the word its own shared catalogue holds for a `StatusKind`, and
 * the nearest kinds would say "Active" and "Cancelled" where this screen has to say "She agreed" and
 * "She said no". Borrowing a badge whose word is wrong would be colour carrying the meaning, which
 * is the one thing the client guide forbids outright.
 */
function statusMessage(
  status: string,
):
  | 'customers.consent.status.Granted'
  | 'customers.consent.status.Declined'
  | 'customers.consent.status.Withdrawn'
  | 'customers.consent.status.NeverAsked' {
  switch (status) {
    case 'Granted':
      return 'customers.consent.status.Granted'
    case 'Declined':
      return 'customers.consent.status.Declined'
    case 'Withdrawn':
      return 'customers.consent.status.Withdrawn'
    default:
      return 'customers.consent.status.NeverAsked'
  }
}

/** When an answer was given, through the shared formatters rather than at the call site. */
function formatWhen(value: string, intl: IntlShape): string {
  const formatters = isSupportedLocale(intl.locale) ? getFormatters(intl.locale) : getFormatters()
  return formatters.formatDateTime(value)
}
