import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { listBranches } from '../../admin/adminApi'
import { useAdminResource } from '../../admin/useAdminResource'
import { ApiError } from '../../auth/apiClient'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { billingProblemCode, billingProblemMessage } from '../../billing/billingProblems'
import type { GstRegistration, GstRegistrationRequest } from '../../billing/pricingAdminTypes'
import {
  amendGstRegistration,
  listGstRegistrations,
  readGstRegistration,
  recordGstRegistration,
} from '../../billing/pricingConfigApi'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { NetworkStatusBanner } from '../../components/states/NetworkStatusBanner'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { getFormatters } from '../../i18n/formatters'
import { GstRegistrationForm } from './GstRegistrationForm'
import { blankGstRegistrationDraft, draftFromGstRegistration } from './gstRegistrationDraft'
import type { GstRegistrationDraft } from './gstRegistrationDraft'

/** The refusal codes named beside their own field rather than through a page-level alert. */
const GSTIN_CODES = ['billing.gstin-not-well-formed', 'billing.gstin-state-mismatch']
const STATE_CODE_CODES = ['billing.state-code-not-well-formed']
const DATES_CODES = ['billing.dates-not-ordered']
const FIELD_CODES = [...GSTIN_CODES, ...STATE_CODE_CODES, ...DATES_CODES]

/** The stale-`If-Match` refusal, which gets its own "read it again" rather than a bare retry. */
const CONCURRENCY_CODE = 'billing.registration-changed'

/**
 * Every GST registration the organisation has recorded, a record form and an amend form (#237) — the
 * read-and-record surface the pricing administration's other seven client slices hang off.
 *
 * ## Why an amendment re-reads before it edits
 *
 * `ListGstRegistrations` answers no version: only a single `GetGstRegistration` carries the `ETag`
 * an amendment is made against, so opening the form for a known row still reads that row again to
 * hold a precondition that is actually current.
 *
 * ## Why four refusal codes are field errors and the rest are a page-level alert
 *
 * `billing.gstin-not-well-formed`, `billing.gstin-state-mismatch`, `billing.state-code-not-well-formed`
 * and `billing.dates-not-ordered` each name one control; every other refusal this screen can meet — an
 * overlap, a stale tag, an unknown identifier — names the registration as a whole, so it is rendered
 * above the form instead.
 */
export function GstRegistrationsRoute() {
  const intl = useIntl()
  const formatters = getFormatters()
  const network = useNetworkState()

  const registrations = useAdminResource('gst-registrations', listGstRegistrations)
  const branches = useAdminResource('branches-for-pricing', listBranches)

  const branchNamesAvailable = branches.failure === null
  const branchName = (branchId: string): string | undefined =>
    branches.value?.find((branch) => branch.branchId === branchId)?.name
  const branchOptions = (branches.value ?? []).map((branch) => ({
    value: branch.branchId,
    label: `${branch.name} (${branch.code})`,
  }))

  const [editing, setEditing] = useState<{
    readonly existing: GstRegistration | null
    readonly draft: GstRegistrationDraft
    readonly version: string | undefined
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [keys, setKeys] = useState<Readonly<Record<string, string>>>({})

  const keyFor = (id: string): string => {
    const existingKey = keys[id]
    if (existingKey !== undefined) {
      return existingKey
    }
    const minted = crypto.randomUUID()
    setKeys((all) => ({ ...all, [id]: minted }))
    return minted
  }

  const forget = (id: string): void => {
    setKeys((all) => Object.fromEntries(Object.entries(all).filter(([spent]) => spent !== id)))
  }

  const openRecord = (): void => {
    setFailure(null)
    setNotice(null)
    setEditing({ existing: null, draft: blankGstRegistrationDraft(), version: undefined })
  }

  const openAmend = (registration: GstRegistration): void => {
    setFailure(null)
    setNotice(null)
    setEditing({
      existing: registration,
      draft: draftFromGstRegistration(registration),
      version: undefined,
    })
    if (!network.online) {
      return
    }
    void readGstRegistration(registration.gstRegistrationId)
      .then(({ value, version }) => {
        setEditing((current) =>
          current?.existing?.gstRegistrationId === registration.gstRegistrationId
            ? { existing: value, draft: draftFromGstRegistration(value), version }
            : current,
        )
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
  }

  const reread = (): void => {
    const id = editing?.existing?.gstRegistrationId
    if (id === undefined) {
      return
    }
    setFailure(null)
    void readGstRegistration(id)
      .then(({ value, version }) => {
        setEditing({ existing: value, draft: draftFromGstRegistration(value), version })
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
  }

  const submit = (): void => {
    if (editing === null) {
      return
    }
    const { draft, existing } = editing

    const body: GstRegistrationRequest = {
      branchId: draft.branchId.trim(),
      gstin: draft.gstin.trim() === '' ? null : draft.gstin.trim(),
      stateCode: draft.stateCode.trim() === '' ? null : draft.stateCode.trim(),
      legalName: draft.legalName.trim() === '' ? null : draft.legalName.trim(),
      tradeName: draft.tradeName.trim() === '' ? null : draft.tradeName.trim(),
      effectiveFrom: draft.effectiveFrom === '' ? null : draft.effectiveFrom,
      effectiveTo: draft.effectiveTo === '' ? null : draft.effectiveTo,
      reason: draft.reason.trim() === '' ? null : draft.reason.trim(),
    }

    const id = existing === null ? 'record' : `amend:${existing.gstRegistrationId}`
    const idempotencyKey = keyFor(id)

    setBusy(true)
    setFailure(null)

    const write: Promise<GstRegistration> =
      existing === null
        ? recordGstRegistration({ body, idempotencyKey })
        : editing.version === undefined
          ? Promise.reject(new ApiError('The registration must be read again.', { status: 409 }))
          : amendGstRegistration({
              registrationId: existing.gstRegistrationId,
              body,
              version: editing.version,
              idempotencyKey,
            }).then((result) => result.value)

    write
      .then(() => {
        forget(id)
        setEditing(null)
        setNotice(
          intl.formatMessage({
            id: existing === null ? 'pricing.gst.recorded' : 'pricing.gst.amended',
          }),
        )
        registrations.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const code = billingProblemCode(failure)
  const fieldMessageId =
    code !== undefined && FIELD_CODES.includes(code) ? billingProblemMessage(failure) : undefined
  const fieldSentence =
    fieldMessageId === undefined ? undefined : intl.formatMessage({ id: fieldMessageId })
  const gstinError = code !== undefined && GSTIN_CODES.includes(code) ? fieldSentence : undefined
  const stateCodeError =
    code !== undefined && STATE_CODE_CODES.includes(code) ? fieldSentence : undefined
  const effectiveToError =
    code !== undefined && DATES_CODES.includes(code) ? fieldSentence : undefined
  const isConcurrencyFailure = code === CONCURRENCY_CODE

  const rows = registrations.value ?? []
  const recordAction = (
    <Button onClick={openRecord} variant="primary">
      <FormattedMessage id="pricing.gst.record.action" />
    </Button>
  )

  return (
    <section>
      <h2>
        <FormattedMessage id="pricing.gst.title" />
      </h2>
      <p>
        <FormattedMessage id="pricing.gst.body" />
      </p>

      <NetworkStatusBanner />

      {notice === null ? null : (
        <Alert
          live="polite"
          onDismiss={() => {
            setNotice(null)
          }}
          tone="success"
        >
          {notice}
        </Alert>
      )}

      {registrations.failure === null ? null : (
        <>
          <BillingProblemAlert failure={registrations.failure} />
          <Button
            iconName="refresh"
            onClick={() => {
              registrations.reload()
            }}
            variant="secondary"
          >
            <FormattedMessage id="states.error.retry" />
          </Button>
        </>
      )}

      {registrations.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'pricing.gst.loading' })} />
      ) : rows.length === 0 && registrations.failure === null ? (
        <EmptyState
          actions={network.online ? recordAction : null}
          iconName="list"
          live="polite"
          title={intl.formatMessage({ id: 'pricing.gst.empty.title' })}
        >
          {intl.formatMessage({ id: 'pricing.gst.empty' })}
        </EmptyState>
      ) : (
        <>
          <DataTable
            caption={intl.formatMessage({ id: 'pricing.gst.caption' })}
            columns={[
              {
                id: 'branch',
                header: intl.formatMessage({ id: 'pricing.gst.column.branch' }),
                primary: true,
                cell: (row: GstRegistration) => branchName(row.branchId) ?? row.branchId,
              },
              {
                id: 'gstin',
                header: intl.formatMessage({ id: 'pricing.gst.column.gstin' }),
                cell: (row: GstRegistration) => row.gstin,
              },
              {
                id: 'legalName',
                header: intl.formatMessage({ id: 'pricing.gst.column.legalName' }),
                cell: (row: GstRegistration) => row.legalName,
              },
              {
                id: 'tradeName',
                header: intl.formatMessage({ id: 'pricing.gst.column.tradeName' }),
                hideWhenNarrow: true,
                cell: (row: GstRegistration) => row.tradeName ?? '—',
              },
              {
                id: 'effectiveFrom',
                header: intl.formatMessage({ id: 'pricing.gst.column.effectiveFrom' }),
                cell: (row: GstRegistration) => formatters.formatShortDate(row.effectiveFrom),
              },
              {
                id: 'effectiveTo',
                header: intl.formatMessage({ id: 'pricing.gst.column.effectiveTo' }),
                cell: (row: GstRegistration) =>
                  row.effectiveTo === null
                    ? intl.formatMessage({ id: 'pricing.gst.stillInForce' })
                    : formatters.formatShortDate(row.effectiveTo),
              },
            ]}
            rowActions={(row: GstRegistration) => (
              <Button
                aria-label={intl.formatMessage(
                  { id: 'pricing.gst.amend.label' },
                  { gstin: row.gstin },
                )}
                onClick={() => {
                  openAmend(row)
                }}
                variant="secondary"
              >
                {intl.formatMessage({ id: 'pricing.gst.amend' })}
              </Button>
            )}
            rowKey={(row) => row.gstRegistrationId}
            rowLabel={(row) => row.gstin}
            rows={rows}
          />

          {network.online ? (
            recordAction
          ) : (
            <OfflineBlockedAction
              action={intl.formatMessage({ id: 'pricing.gst.record.offlineAction' })}
            />
          )}
        </>
      )}

      {editing === null ? null : !network.online ? (
        <OfflineBlockedAction
          action={intl.formatMessage({
            id:
              editing.existing === null
                ? 'pricing.gst.record.offlineAction'
                : 'pricing.gst.amend.offlineAction',
          })}
        />
      ) : (
        <>
          {fieldSentence === undefined ? <BillingProblemAlert failure={failure} /> : null}
          {isConcurrencyFailure ? (
            <Button onClick={reread} variant="secondary">
              {intl.formatMessage({ id: 'pricing.gst.form.reread' })}
            </Button>
          ) : null}
          <GstRegistrationForm
            branchName={branchName}
            branchNamesAvailable={branchNamesAvailable}
            branchOptions={branchOptions}
            busy={busy}
            controlId={(name) => `gst-${name}`}
            draft={editing.draft}
            existing={editing.existing}
            onCancel={() => {
              setEditing(null)
              setFailure(null)
            }}
            onChange={(draft) => {
              setEditing({ ...editing, draft })
            }}
            onSubmit={submit}
            {...(gstinError === undefined ? {} : { gstinError })}
            {...(stateCodeError === undefined ? {} : { stateCodeError })}
            {...(effectiveToError === undefined ? {} : { effectiveToError })}
          />
        </>
      )}
    </section>
  )
}
