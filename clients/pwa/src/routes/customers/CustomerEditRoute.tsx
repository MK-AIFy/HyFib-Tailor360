import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useParams } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ApiError } from '../../auth/apiClient'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { FormErrorSummary } from '../../design-system/components/forms/FormErrorSummary'
import { Select } from '../../design-system/components/forms/Select'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import type { FieldErrorEntry } from '../../design-system/foundations/FieldProps'
import { useAdminResource } from '../../admin/useAdminResource'
import { correctCustomer, readCustomer } from '../../customers/customersApi'
import { CUSTOMER_VERSION_CONFLICT_CODE } from '../../customers/types'
import type { Customer, CustomerDetailsInput } from '../../customers/types'
import './customers.css'

/**
 * Correcting what a customer record says about the person (#26, #182, #582).
 *
 * ## Why the whole record is sent, and why that decides who may use this screen
 *
 * `PUT /api/v1/customers/{id}` is whole-record: the server compares every field it is given against
 * what it holds, records which ones changed, and treats a field it was not given as one being
 * cleared. That is the right shape for a correction — "her name is spelled differently" and "she no
 * longer has that second number" are the same kind of edit — but it has one consequence worth stating
 * plainly, because it is not obvious and it is a data-loss bug if it is missed:
 *
 * **A caller who cannot read the contact fields cannot correct the record at all.** Their `GET`
 * withholds the six contact values (`contactIncluded: false`), so the only thing this screen could
 * put in a `PUT` for them is nothing — which the server reads as "clear her telephone number." It
 * refuses that (`CustomersErrors.ContactChangeForbidden`), so nothing is actually lost; but a form
 * whose save button is guaranteed to fail is a broken screen, not a boundary. So the screen says who
 * can make the correction instead of offering a form that cannot work. Widening this — a sparse
 * `PATCH`, or a `PUT` the server reads as "leave withheld fields alone" — is a server change and a
 * contract change, tracked separately rather than worked around here.
 *
 * ## Why the two required fields are checked here and not left to the server
 *
 * A name and a reason are the two things the endpoint refuses without, and both are things a person
 * can see is missing before a round trip. They are reported as `FieldErrorEntry`s through
 * `FormErrorSummary` and the fields' own `error` prop — the house pattern, and `LoginRoute`'s — so
 * the summary takes focus, names which box is empty and moves focus to it. What they are *not* is a
 * synthetic `ApiError`: `AuthProblemAlert` chooses its sentence from the status and never from the
 * message, so a hand-made 400 would reach the counter as "something went wrong" with nothing
 * attached to the empty field. Everything beyond "this is required" stays the server's opinion —
 * what a name may contain belongs to `CustomerNameNormaliser`, not to a form.
 *
 * ## Why the typed draft survives a conflict reload
 *
 * The reload-then-retry shape is `RoleDetailRoute`'s and `BranchListRoute`'s: a 409 is answered by
 * reading the record again and offering the save again against the version that came back. It
 * differs from `RoleDetailRoute` in one deliberate way — that screen tags its draft with the version
 * it was made against and drops it when the version moves, and this one keeps it. The reason is what
 * the draft *is*. There, it is a set of permission ticks, and the set a colleague just saved is
 * almost certainly the one to start from. Here, it is a name somebody is reading off an identity
 * document with the customer standing in front of them, and throwing that away to show them a
 * spelling they already decided was wrong is the "never discards typed input" rule in the client
 * guide, section 6. The conflict alert stays up after the reload for exactly that reason: the person
 * is the one who reconciles the two, and they cannot do that if the screen quietly picks a winner.
 */
export function CustomerEditRoute() {
  const intl = useIntl()
  const network = useNetworkState()
  const { customerId = '' } = useParams()

  const record = useAdminResource(customerId, (signal) => readCustomer(customerId, signal))
  const customer = record.value?.value ?? null
  const version = record.value?.version

  // Null until the person edits something: the fields render from the record itself until then, so
  // there is no effect copying the record into state and no window where the two disagree.
  const [draft, setDraft] = useState<CustomerDetailsInput | null>(null)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID())
  const [errors, setErrors] = useState<readonly FieldErrorEntry[]>([])
  // Counts submit attempts rather than tracking a boolean, so a second failed submit moves focus
  // back to the summary instead of leaving somebody in the field they just fixed the wrong way.
  const [attempt, setAttempt] = useState(0)

  if (record.value === null && record.loading) {
    return <LoadingState what={intl.formatMessage({ id: 'customers.edit.loading' })} />
  }

  if (customer === null || version === undefined) {
    return (
      <>
        <AuthProblemAlert failure={record.failure} />
        <EmptyState iconName="users" live="polite">
          {intl.formatMessage({ id: 'customers.detail.notFound' })}
        </EmptyState>
      </>
    )
  }

  const back = (
    <p>
      <Link to={`/customers/${customerId}`}>
        <FormattedMessage id="customers.edit.back" />
      </Link>
    </p>
  )

  if (!customer.contactIncluded) {
    return (
      <section className="page customers">
        {back}
        <h1>
          <FormattedMessage id="customers.edit.title" />
        </h1>
        <Alert live="polite" tone="warning">
          <FormattedMessage id="customers.edit.contactWithheld" />
        </Alert>
      </section>
    )
  }

  const details = draft ?? detailsOf(customer)
  const conflict = failure instanceof ApiError && failure.code === CUSTOMER_VERSION_CONFLICT_CODE

  const set = <K extends keyof CustomerDetailsInput>(key: K, value: string) => {
    setDraft({ ...details, [key]: value === '' ? undefined : value })
    setSaved(false)
  }

  const nameError = errors.find((entry) => entry.name === 'displayName')?.message
  const reasonError = errors.find((entry) => entry.name === 'reason')?.message

  const save = () => {
    /*
     * The only client-side validation is "this is required", for the two fields the server refuses
     * without — `LoginRoute`'s rule and for its reason: anything more would be this screen having an
     * opinion about a name, and a name's rules belong to `CustomerNameNormaliser`, not here.
     *
     * They are `FieldErrorEntry`s and not a thrown `ApiError`, because `AuthProblemAlert` renders a
     * sentence chosen from the *status* and never the message — so a synthetic 400 would reach the
     * counter as "something went wrong" with nothing attached to the field that is actually empty.
     */
    const missing: FieldErrorEntry[] = []
    if ((details.displayName ?? '').trim() === '') {
      missing.push({
        name: 'displayName',
        message: intl.formatMessage({ id: 'customers.edit.nameRequired' }),
        controlId: 'customer-name',
      })
    }
    if (reason.trim() === '') {
      missing.push({
        name: 'reason',
        message: intl.formatMessage({ id: 'customers.edit.reasonRequired' }),
        controlId: 'customer-reason',
      })
    }

    setAttempt((previous) => previous + 1)
    setErrors(missing)
    if (missing.length > 0) {
      return
    }

    setBusy(true)
    setFailure(null)
    setSaved(false)

    void correctCustomer({
      customerId,
      details,
      reason: reason.trim(),
      version,
      idempotencyKey,
    })
      .then(() => {
        setSaved(true)
        setReason('')
        setErrors([])
        // The record is authoritative again, so the fields render from it rather than from a draft
        // that is now a copy of it — which is also what makes a second, different correction start
        // from what was actually saved.
        setDraft(null)
        // A new key for the next, separate correction. The guarantee is that a resend of *this* one
        // replays, not that two different corrections collapse into one.
        setIdempotencyKey(crypto.randomUUID())
        record.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  return (
    <section className="page customers">
      {back}

      <h1>
        <FormattedMessage id="customers.edit.title" />
      </h1>
      <p className="customers__lede">
        <FormattedMessage id="customers.edit.body" />
      </p>

      {saved ? (
        <Alert
          live="polite"
          tone="success"
          onDismiss={() => {
            setSaved(false)
          }}
        >
          <FormattedMessage id="customers.edit.saved" />
        </Alert>
      ) : null}

      {conflict ? (
        <Alert
          live="assertive"
          tone="warning"
          title={intl.formatMessage({ id: 'customers.edit.conflict.title' })}
          actions={
            <Button
              variant="secondary"
              onClick={() => {
                setFailure(null)
                record.reload()
              }}
            >
              <FormattedMessage id="customers.edit.conflict.reload" />
            </Button>
          }
        >
          <FormattedMessage id="customers.edit.conflict.body" />
        </Alert>
      ) : (
        <AuthProblemAlert failure={failure} />
      )}

      <FormErrorSummary errors={errors} submissionId={attempt} />

      <form
        className="customers__form"
        noValidate
        onSubmit={(event) => {
          event.preventDefault()
          save()
        }}
      >
        <TextField
          autoComplete="name"
          id="customer-name"
          label={intl.formatMessage({ id: 'customers.create.field.displayName' })}
          name="displayName"
          onValueChange={(value) => {
            set('displayName', value)
          }}
          required
          value={details.displayName ?? ''}
          {...(nameError === undefined ? {} : { error: nameError })}
        />
        <TextField
          autoComplete="off"
          id="customer-nativeName"
          label={intl.formatMessage({ id: 'customers.create.field.nativeName' })}
          name="nativeName"
          onValueChange={(value) => {
            set('nativeName', value)
          }}
          value={details.nativeName ?? ''}
        />
        <TextField
          autoComplete="tel"
          id="customer-phone"
          label={intl.formatMessage({ id: 'customers.create.field.phone' })}
          name="phone"
          onValueChange={(value) => {
            set('phone', value)
          }}
          type="tel"
          value={details.phone ?? ''}
        />
        <TextField
          autoComplete="off"
          id="customer-alternatePhone"
          label={intl.formatMessage({ id: 'customers.create.field.alternatePhone' })}
          name="alternatePhone"
          onValueChange={(value) => {
            set('alternatePhone', value)
          }}
          type="tel"
          value={details.alternatePhone ?? ''}
        />
        <TextField
          autoComplete="email"
          id="customer-email"
          label={intl.formatMessage({ id: 'customers.create.field.email' })}
          name="email"
          onValueChange={(value) => {
            set('email', value)
          }}
          type="email"
          value={details.email ?? ''}
        />
        <TextField
          autoComplete="street-address"
          id="customer-addressLine"
          label={intl.formatMessage({ id: 'customers.create.field.addressLine' })}
          name="addressLine"
          onValueChange={(value) => {
            set('addressLine', value)
          }}
          value={details.addressLine ?? ''}
        />
        <TextField
          autoComplete="address-level2"
          id="customer-locality"
          label={intl.formatMessage({ id: 'customers.create.field.locality' })}
          name="locality"
          onValueChange={(value) => {
            set('locality', value)
          }}
          value={details.locality ?? ''}
        />
        <TextField
          autoComplete="postal-code"
          id="customer-postcode"
          label={intl.formatMessage({ id: 'customers.create.field.postcode' })}
          name="postcode"
          onValueChange={(value) => {
            set('postcode', value)
          }}
          value={details.postcode ?? ''}
        />
        <Select
          emptyLabel={null}
          id="customer-language"
          label={intl.formatMessage({ id: 'customers.create.field.language' })}
          name="language"
          onValueChange={(value) => {
            set('language', value)
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
          value={details.language ?? 'en-IN'}
        />

        <TextArea
          description={intl.formatMessage({ id: 'customers.edit.reasonHint' })}
          id="customer-reason"
          label={intl.formatMessage({ id: 'customers.edit.reason' })}
          name="reason"
          onValueChange={setReason}
          required
          rows={2}
          value={reason}
          {...(reasonError === undefined ? {} : { error: reasonError })}
        />

        {network.online ? (
          <Button busy={busy} iconName="check" size="primary" type="submit" variant="primary">
            {intl.formatMessage({
              id: busy ? 'customers.edit.saving' : 'customers.edit.action',
            })}
          </Button>
        ) : (
          <OfflineBlockedAction
            action={intl.formatMessage({ id: 'customers.edit.offlineAction' })}
          />
        )}
      </form>
    </section>
  )
}

/**
 * The record as the form's fields, which is also exactly what a `PUT` sends.
 *
 * `null` becomes `undefined` rather than an empty string: they mean the same thing to the server —
 * the field is unset — but `undefined` is what `CustomerDetailsInput` says, and keeping the two
 * spellings from mixing is what stops a field the customer never gave being sent back as `""`.
 */
function detailsOf(customer: Customer): CustomerDetailsInput {
  return {
    displayName: customer.displayName,
    ...(customer.nativeName === null ? {} : { nativeName: customer.nativeName }),
    ...(customer.phone === null ? {} : { phone: customer.phone }),
    ...(customer.alternatePhone === null ? {} : { alternatePhone: customer.alternatePhone }),
    ...(customer.email === null ? {} : { email: customer.email }),
    ...(customer.addressLine === null ? {} : { addressLine: customer.addressLine }),
    ...(customer.locality === null ? {} : { locality: customer.locality }),
    ...(customer.postcode === null ? {} : { postcode: customer.postcode }),
    language: customer.language,
  }
}
