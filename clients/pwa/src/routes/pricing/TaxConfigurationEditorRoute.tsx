import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useParams } from 'react-router'
import { ApiError } from '../../auth/apiClient'
import type { VersionedResponse } from '../../auth/apiClient'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import {
  billingProblemCode,
  billingProblemField,
  billingProblemMessage,
} from '../../billing/billingProblems'
import {
  addTaxCode,
  describeTaxConfigurationVersion,
  editTaxCode,
  readTaxConfigurationVersion,
  removeTaxCode,
} from '../../billing/pricingConfigApi'
import type { DescribeTaxConfigurationRequest, TaxCode } from '../../billing/pricingAdminTypes'
import { useAdminResource } from '../../admin/useAdminResource'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import type { ConfirmOutcome } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { Icon } from '../../components/primitives/Icon'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { NetworkStatusBanner } from '../../components/states/NetworkStatusBanner'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { DateField } from '../../design-system/components/forms/DateField'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import { getFormatters } from '../../i18n/formatters'
import { TaxCodeForm } from './TaxCodeForm'
import type { RateFieldErrors } from './TaxCodeForm'
import {
  RATE_LABEL_KEY,
  blankTaxCodeDraft,
  draftFromTaxCode,
  rateComponentAtIndex,
  taxCodeRequestFrom,
} from './taxCodeDraft'
import type { RateComponent, TaxCodeDraft } from './taxCodeDraft'

const VERSION_CHANGED_CODE = 'billing.version-changed'
const RATE_FIELD_SHAPE = /^rates\[(\d+)\]\.(kind|ratePercent)$/

/**
 * A field the client itself refuses to leave unchosen, phrased as the same `ApiError` a server
 * refusal would be — so it renders through the identical field-error path as one, and a screen
 * never needs a second vocabulary for "you have not answered this yet".
 */
function requiredFieldFailure(field: string): ApiError {
  return new ApiError('A required value was not supplied.', {
    status: 400,
    problem: {
      code: 'billing.value-required',
      errors: { [field]: ['A required value was not supplied.'] },
    },
  })
}

interface VersionDraft {
  readonly name: string
  readonly notes: string
  readonly effectiveFrom: string
  readonly reason: string
}

/**
 * One tax configuration version: its own details, and the tax codes it carries (E09-F01-5).
 *
 * ## Why there is no validate control and no publish control here
 *
 * Checking a version and publishing it are E09-F01-5b, which also owns the shared findings list. A
 * half-built findings list this issue would have to render and that one would then rewrite is worse
 * than none, so the screen says in one sentence that both arrive next rather than leaving their
 * absence looking like an oversight.
 *
 * ## Why a published or retired version offers no editing control at all
 *
 * The server refuses every write against one (`billing.version-not-editable`): a published version
 * is what every invoice since was calculated on, and changing it would change what an order already
 * priced *meant*. The screen therefore offers no control that would end in that refusal — only the
 * sentence that says where the change is actually made, a draft cloned from it on the register.
 */
export function TaxConfigurationEditorRoute() {
  const intl = useIntl()
  const formatters = getFormatters()
  const { versionId } = useParams()
  const network = useNetworkState()

  const data = useAdminResource(`tax-configuration-version:${versionId ?? ''}`, (signal) =>
    readTaxConfigurationVersion(versionId ?? '', signal),
  )

  const [held, setHeld] = useState<string | undefined>(undefined)
  const [editingVersion, setEditingVersion] = useState<VersionDraft | null>(null)
  const [editingCode, setEditingCode] = useState<{
    readonly existing: TaxCode | null
    readonly draft: TaxCodeDraft
  } | null>(null)
  const [removingCode, setRemovingCode] = useState<TaxCode | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [keys, setKeys] = useState<Readonly<Record<string, string>>>({})

  const value = data.value?.value ?? null
  const precondition = held ?? data.value?.version

  const keyFor = (id: string): string => {
    const existing = keys[id]
    if (existing !== undefined) {
      return existing
    }
    const minted = crypto.randomUUID()
    setKeys((all) => ({ ...all, [id]: minted }))
    return minted
  }

  const forget = (id: string): void => {
    setKeys((all) => Object.fromEntries(Object.entries(all).filter(([spent]) => spent !== id)))
  }

  const send = async (
    id: string,
    run: (input: {
      readonly version: string
      readonly idempotencyKey: string
    }) => Promise<VersionedResponse<unknown>>,
    done: string,
  ): Promise<void> => {
    if (precondition === undefined) {
      setFailure(new ApiError('The version must be read again.', { status: 409 }))
      return
    }

    setBusy(true)
    setFailure(null)
    setNotice(null)

    try {
      const result = await run({ version: precondition, idempotencyKey: keyFor(id) })
      forget(id)
      setHeld(result.version)
      setEditingVersion(null)
      setEditingCode(null)
      setRemovingCode(null)
      setNotice(done)
      data.reload()
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setBusy(false)
    }
  }

  const reread = (): void => {
    setFailure(null)
    setHeld(undefined)
    data.reload()
  }

  const openDescribe = (): void => {
    if (value === null) {
      return
    }
    setFailure(null)
    setEditingCode(null)
    setRemovingCode(null)
    setEditingVersion({
      name: value.version.name,
      notes: value.version.notes ?? '',
      effectiveFrom: value.version.effectiveFrom,
      reason: '',
    })
  }

  const saveVersion = (): void => {
    if (versionId === undefined || editingVersion === null) {
      return
    }

    const body: DescribeTaxConfigurationRequest = {
      name: editingVersion.name.trim(),
      notes: editingVersion.notes.trim() === '' ? null : editingVersion.notes.trim(),
      effectiveFrom: editingVersion.effectiveFrom === '' ? null : editingVersion.effectiveFrom,
      reason: editingVersion.reason.trim() === '' ? null : editingVersion.reason.trim(),
    }

    void send(
      'describe',
      ({ version, idempotencyKey }) =>
        describeTaxConfigurationVersion({ versionId, body, version, idempotencyKey }),
      intl.formatMessage({ id: 'pricing.tax.editor.saved' }),
    )
  }

  const openAddCode = (): void => {
    setFailure(null)
    setEditingVersion(null)
    setRemovingCode(null)
    setEditingCode({ existing: null, draft: blankTaxCodeDraft() })
  }

  const openEditCode = (code: TaxCode): void => {
    setFailure(null)
    setEditingVersion(null)
    setRemovingCode(null)
    setEditingCode({ existing: code, draft: draftFromTaxCode(code) })
  }

  const saveCode = (): void => {
    if (versionId === undefined || editingCode === null) {
      return
    }
    const { draft, existing } = editingCode

    if (draft.kind === '') {
      setFailure(requiredFieldFailure('kind'))
      return
    }
    if (draft.active === '') {
      setFailure(requiredFieldFailure('active'))
      return
    }

    const body = taxCodeRequestFrom(draft)
    const id = existing === null ? `add-code:${body.code ?? ''}` : `edit-code:${existing.taxCodeId}`

    void send(
      id,
      ({ version, idempotencyKey }) =>
        existing === null
          ? addTaxCode({ versionId, code: body, version, idempotencyKey })
          : editTaxCode({
              versionId,
              taxCodeId: existing.taxCodeId,
              code: body,
              version,
              idempotencyKey,
            }),
      intl.formatMessage({ id: 'pricing.tax.code.saved' }, { code: body.code ?? '' }),
    )
  }

  const openRemoveCode = (code: TaxCode): void => {
    setFailure(null)
    setEditingVersion(null)
    setEditingCode(null)
    setRemovingCode(code)
  }

  const removeCode = (outcome: ConfirmOutcome): void => {
    if (versionId === undefined || removingCode === null) {
      return
    }
    const code = removingCode
    const reason = outcome.reason?.trim() ?? ''

    void send(
      `remove-code:${code.taxCodeId}`,
      ({ version, idempotencyKey }) =>
        removeTaxCode({
          versionId,
          taxCodeId: code.taxCodeId,
          reason: reason === '' ? null : reason,
          version,
          idempotencyKey,
        }),
      intl.formatMessage({ id: 'pricing.tax.code.removed' }, { code: code.code }),
    )
  }

  const isConcurrencyFailure = billingProblemCode(failure) === VERSION_CHANGED_CODE

  const fieldSentence = (open: boolean, field: string): string | undefined => {
    if (!open || billingProblemField(failure) !== field) {
      return undefined
    }
    const id = billingProblemMessage(failure)
    return id === undefined ? undefined : intl.formatMessage({ id })
  }

  const nameError = fieldSentence(editingVersion !== null, 'name')
  const notesError = fieldSentence(editingVersion !== null, 'notes')
  const effectiveFromError = fieldSentence(editingVersion !== null, 'effectiveFrom')
  const versionReasonError = fieldSentence(editingVersion !== null, 'reason')
  const versionFieldMatched =
    nameError !== undefined ||
    notesError !== undefined ||
    effectiveFromError !== undefined ||
    versionReasonError !== undefined

  const codeError = fieldSentence(editingCode !== null, 'code')
  const descriptionError = fieldSentence(editingCode !== null, 'description')
  const classificationError = fieldSentence(editingCode !== null, 'classification')
  const kindError = fieldSentence(editingCode !== null, 'kind')
  const activeError = fieldSentence(editingCode !== null, 'active')
  const codeReasonError = fieldSentence(editingCode !== null, 'reason')

  const rateErrors: RateFieldErrors = (() => {
    if (editingCode === null) {
      return {}
    }
    const field = billingProblemField(failure)
    const match = field === undefined ? null : RATE_FIELD_SHAPE.exec(field)
    if (match?.[1] === undefined) {
      return {}
    }
    const component = rateComponentAtIndex(editingCode.draft, Number(match[1]))
    const id = billingProblemMessage(failure)
    if (component === undefined || id === undefined) {
      return {}
    }
    return { [component]: intl.formatMessage({ id }) }
  })()

  const codeFieldMatched =
    codeError !== undefined ||
    descriptionError !== undefined ||
    classificationError !== undefined ||
    kindError !== undefined ||
    activeError !== undefined ||
    codeReasonError !== undefined ||
    Object.keys(rateErrors).length > 0

  if (data.loading) {
    return (
      <section>
        <LoadingState what={intl.formatMessage({ id: 'pricing.tax.editor.loading' })} />
      </section>
    )
  }

  if (value === null) {
    return (
      <section>
        <BillingProblemAlert failure={data.failure} />
      </section>
    )
  }

  const isDraft = value.version.status === 'Draft'
  const rateSummary = (code: TaxCode): string =>
    code.rates.length === 0
      ? intl.formatMessage({ id: 'pricing.tax.editor.rates.none' })
      : code.rates
          .map((rate) => {
            const key = RATE_LABEL_KEY[rate.kind as RateComponent]
            const label = key === undefined ? rate.kind : intl.formatMessage({ id: key })
            return `${label} ${formatters.formatPercent(rate.ratePercent)}`
          })
          .join(', ')

  return (
    <section>
      <p>
        <Link to="/admin/tax-configuration">
          <FormattedMessage id="admin.back" />
        </Link>
      </p>

      <h2>
        {intl.formatMessage(
          { id: 'pricing.tax.editor.heading' },
          { number: value.version.versionNumber },
        )}
      </h2>

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

      {value.version.status === 'Published' ? (
        <Alert live="polite" tone="info">
          <FormattedMessage id="pricing.tax.editor.readOnly" />
        </Alert>
      ) : value.version.status === 'Retired' ? (
        <Alert live="polite" tone="info">
          <FormattedMessage id="pricing.tax.editor.retired" />
        </Alert>
      ) : null}

      <section>
        <h3>{intl.formatMessage({ id: 'pricing.tax.editor.detailsTitle' })}</h3>
        <dl>
          <dt>{intl.formatMessage({ id: 'pricing.tax.editor.form.name' })}</dt>
          <dd>{value.version.name}</dd>
          <dt>{intl.formatMessage({ id: 'pricing.tax.editor.form.notes' })}</dt>
          <dd>{value.version.notes ?? ''}</dd>
          <dt>{intl.formatMessage({ id: 'pricing.tax.editor.form.effectiveFrom' })}</dt>
          <dd>{formatters.formatShortDate(value.version.effectiveFrom)}</dd>
        </dl>

        {isDraft && editingVersion === null ? (
          network.online ? (
            <Button onClick={openDescribe} variant="secondary">
              {intl.formatMessage({ id: 'pricing.tax.editor.edit' })}
            </Button>
          ) : (
            <OfflineBlockedAction
              action={intl.formatMessage({ id: 'pricing.tax.editor.form.offlineAction' })}
            />
          )
        ) : null}

        {editingVersion === null ? null : !network.online ? (
          <OfflineBlockedAction
            action={intl.formatMessage({ id: 'pricing.tax.editor.form.offlineAction' })}
          />
        ) : (
          <form
            aria-label={intl.formatMessage({ id: 'pricing.tax.editor.detailsTitle' })}
            onSubmit={(event) => {
              event.preventDefault()
              saveVersion()
            }}
          >
            {versionFieldMatched ? null : <BillingProblemAlert failure={failure} />}
            {isConcurrencyFailure ? (
              <Button onClick={reread} variant="secondary">
                {intl.formatMessage({ id: 'pricing.tax.editor.reread' })}
              </Button>
            ) : null}

            <TextField
              id="tax-version-name"
              label={intl.formatMessage({ id: 'pricing.tax.editor.form.name' })}
              name="name"
              onValueChange={(next) => {
                setEditingVersion({ ...editingVersion, name: next })
              }}
              required
              value={editingVersion.name}
              {...(nameError === undefined ? {} : { error: nameError })}
            />

            <TextArea
              id="tax-version-notes"
              label={intl.formatMessage({ id: 'pricing.tax.editor.form.notes' })}
              name="notes"
              onValueChange={(next) => {
                setEditingVersion({ ...editingVersion, notes: next })
              }}
              value={editingVersion.notes}
              {...(notesError === undefined ? {} : { error: notesError })}
            />

            <DateField
              id="tax-version-effectiveFrom"
              label={intl.formatMessage({ id: 'pricing.tax.editor.form.effectiveFrom' })}
              name="effectiveFrom"
              onValueChange={(next) => {
                setEditingVersion({ ...editingVersion, effectiveFrom: next })
              }}
              required
              value={editingVersion.effectiveFrom}
              {...(effectiveFromError === undefined ? {} : { error: effectiveFromError })}
            />

            <TextArea
              id="tax-version-reason"
              label={intl.formatMessage({ id: 'pricing.tax.editor.form.reason' })}
              name="reason"
              onValueChange={(next) => {
                setEditingVersion({ ...editingVersion, reason: next })
              }}
              value={editingVersion.reason}
              {...(versionReasonError === undefined ? {} : { error: versionReasonError })}
            />

            <Button busy={busy} type="submit" variant="primary">
              {intl.formatMessage({ id: 'pricing.tax.editor.form.save' })}
            </Button>
            <Button
              onClick={() => {
                setEditingVersion(null)
                setFailure(null)
              }}
              type="button"
              variant="secondary"
            >
              <FormattedMessage id="admin.cancel" />
            </Button>
          </form>
        )}
      </section>

      {value.taxCodes.length === 0 ? (
        <EmptyState iconName="list" live="polite">
          {intl.formatMessage({ id: 'pricing.tax.editor.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'pricing.tax.editor.caption' })}
          columns={[
            {
              id: 'code',
              header: intl.formatMessage({ id: 'pricing.tax.editor.column.code' }),
              primary: true,
              cell: (row: TaxCode) => row.code,
            },
            {
              id: 'description',
              header: intl.formatMessage({ id: 'pricing.tax.editor.column.description' }),
              cell: (row: TaxCode) => row.description,
            },
            {
              id: 'classification',
              header: intl.formatMessage({ id: 'pricing.tax.editor.column.classification' }),
              cell: (row: TaxCode) => row.classification,
            },
            {
              id: 'kind',
              header: intl.formatMessage({ id: 'pricing.tax.editor.column.kind' }),
              cell: (row: TaxCode) =>
                intl.formatMessage({
                  id:
                    row.kind === 'Goods'
                      ? 'pricing.tax.editor.kind.Goods'
                      : 'pricing.tax.editor.kind.Services',
                }),
            },
            {
              id: 'active',
              header: intl.formatMessage({ id: 'pricing.tax.editor.column.active' }),
              cell: (row: TaxCode) => (
                <span className="pricing-tax-code__status">
                  <Icon name={row.active ? 'check-circle' : 'x-circle'} />
                  {intl.formatMessage({
                    id: row.active
                      ? 'pricing.tax.editor.active.yes'
                      : 'pricing.tax.editor.active.no',
                  })}
                </span>
              ),
            },
            {
              id: 'rates',
              header: intl.formatMessage({ id: 'pricing.tax.editor.column.rates' }),
              cell: (row: TaxCode) => rateSummary(row),
            },
          ]}
          rowActions={(row: TaxCode) =>
            isDraft ? (
              <>
                <Button
                  onClick={() => {
                    openEditCode(row)
                  }}
                  variant="secondary"
                >
                  {intl.formatMessage({ id: 'pricing.tax.code.edit' }, { code: row.code })}
                </Button>
                <Button
                  onClick={() => {
                    openRemoveCode(row)
                  }}
                  variant="danger"
                >
                  {intl.formatMessage({ id: 'pricing.tax.code.remove' }, { code: row.code })}
                </Button>
              </>
            ) : null
          }
          rowKey={(row) => row.taxCodeId}
          rowLabel={(row) => row.code}
          rows={value.taxCodes}
        />
      )}

      {isDraft ? (
        network.online ? (
          <Button onClick={openAddCode} variant="primary">
            <FormattedMessage id="pricing.tax.code.add" />
          </Button>
        ) : (
          <OfflineBlockedAction
            action={intl.formatMessage({ id: 'pricing.tax.code.add.offlineAction' })}
          />
        )
      ) : null}

      <Alert live="off" tone="info">
        <FormattedMessage id="pricing.tax.editor.publishComingSoon" />
      </Alert>

      {editingCode === null ? null : !network.online ? (
        <OfflineBlockedAction
          action={intl.formatMessage({
            id:
              editingCode.existing === null
                ? 'pricing.tax.code.add.offlineAction'
                : 'pricing.tax.code.edit.offlineAction',
          })}
        />
      ) : (
        <>
          {codeFieldMatched ? null : <BillingProblemAlert failure={failure} />}
          {isConcurrencyFailure ? (
            <Button onClick={reread} variant="secondary">
              {intl.formatMessage({ id: 'pricing.tax.editor.reread' })}
            </Button>
          ) : null}
          <TaxCodeForm
            busy={busy}
            controlId={(name) => `tax-code-${name}`}
            draft={editingCode.draft}
            existing={editingCode.existing}
            onCancel={() => {
              setEditingCode(null)
              setFailure(null)
            }}
            onChange={(draft) => {
              setEditingCode({ ...editingCode, draft })
            }}
            onSubmit={saveCode}
            rateErrors={rateErrors}
            {...(codeError === undefined ? {} : { codeError })}
            {...(descriptionError === undefined ? {} : { descriptionError })}
            {...(classificationError === undefined ? {} : { classificationError })}
            {...(kindError === undefined ? {} : { kindError })}
            {...(activeError === undefined ? {} : { activeError })}
            {...(codeReasonError === undefined ? {} : { reasonError: codeReasonError })}
          />
        </>
      )}

      {removingCode === null ? null : !network.online ? (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'pricing.tax.code.remove.offlineAction' })}
        />
      ) : (
        <ConfirmDialog
          action={intl.formatMessage(
            { id: 'pricing.tax.code.remove' },
            { code: removingCode.code },
          )}
          busy={busy}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          confirmLabel={intl.formatMessage(
            { id: 'pricing.tax.code.remove' },
            { code: removingCode.code },
          )}
          onCancel={() => {
            setRemovingCode(null)
            setFailure(null)
          }}
          onConfirm={removeCode}
          open
          problem={<BillingProblemAlert failure={failure} />}
          tier="reason"
          title={intl.formatMessage({ id: 'pricing.tax.code.remove.title' })}
        >
          {intl.formatMessage({ id: 'pricing.tax.code.remove.body' })}
        </ConfirmDialog>
      )}
    </section>
  )
}
