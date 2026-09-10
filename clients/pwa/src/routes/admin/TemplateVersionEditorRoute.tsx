import { useMemo, useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useParams } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ApiError } from '../../auth/apiClient'
import type { VersionedResponse } from '../../auth/apiClient'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import type { ConfirmOutcome } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import {
  addTemplateField,
  changeTemplateField,
  readMeasurementTemplate,
  removeTemplateField,
} from '../../admin/templateApi'
import { useAdminResource } from '../../admin/useAdminResource'
import { blankField, fieldToForm, toRequest, validateField } from '../../admin/templateFieldForm'
import type { FieldFormState } from '../../admin/templateFieldForm'
import { TemplateFieldForm } from './TemplateFieldForm'
import {
  formatMeasurementRange,
  unitForBands,
} from '../../design-system/components/forms/measurementRange'
import type { CentimetreDecimals, InchFractionStep } from '../../i18n/units'
import type { MeasurementTemplate, TemplateField } from '../../admin/types'

/**
 * The fields of one draft version: what is measured, and the three acts that change the list.
 *
 * ## Why this is a route of its own
 *
 * #93 deliberately did not earn one. There the selected version is component state, because a
 * version is a thing you *look at*. Here it is a thing you *work in* — a person adds six fields over
 * several minutes, is interrupted, and comes back — so it has an address they can return to, share
 * with a colleague, and reload without losing their place.
 *
 * ## Why every save re-renders from the response
 *
 * All three field routes answer with the **whole** template — every version, every field — and a
 * fresh `ETag`. Saving one label returns the lot. That is not waste: an `If-Match` on any of these
 * is a precondition on the whole template, so the tag the next command must present is the one that
 * came back from this one. Patching a local copy and keeping the old tag would make the second save
 * of a sitting fail with a conflict that blames another administrator who was never there.
 *
 * ## Why the precondition is the tag this screen rendered
 *
 * The same reason as the lifecycle acts on `TemplateDetailRoute`: re-reading immediately before a
 * command would make `If-Match` true by construction, which is precisely what it exists to refuse.
 * The tag held here is the last one the server gave — from the read that painted the screen, or from
 * the command that superseded it — and never one fetched to make the write succeed.
 *
 * ## Why a retry key outlives a refusal
 *
 * `docs/architecture/conventions.md` section 4.3: a retry after a conflict reuses the *same* key.
 * A key minted when the form is submitted would be fresh on the second attempt, so a save whose
 * response was lost rather than refused would add the field twice. The key is forgotten once the
 * command has actually succeeded.
 */
export function TemplateVersionEditorRoute() {
  const intl = useIntl()
  const { templateId, versionId } = useParams()

  const template = useAdminResource(`measurement-template:${templateId ?? ''}`, (signal) =>
    readMeasurementTemplate(templateId ?? '', signal),
  )

  /** The field being edited, or a blank one being added. Null when the form is closed. */
  const [editing, setEditing] = useState<{
    readonly field: TemplateField | null
    readonly form: FieldFormState
  } | null>(null)

  const [removing, setRemoving] = useState<TemplateField | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [submitted, setSubmitted] = useState(0)

  /** The retry key of every command attempted and not yet succeeded, by what it was for. */
  const [keys, setKeys] = useState<Readonly<Record<string, string>>>({})

  /** The tag the last command returned, which supersedes the read until it is re-read. */
  const [held, setHeld] = useState<string | undefined>(undefined)

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

  const hold = (result: VersionedResponse<MeasurementTemplate>): void => {
    setHeld(result.version)
  }

  const value = template.value?.value ?? null
  const precondition = held ?? template.value?.version
  const version = value?.versions.find((row) => row.templateVersionId === versionId) ?? null
  const fields = useMemo(() => [...(version?.fields ?? [])], [version])

  /** The band this field accepts, in the unit the version opens in, or undefined when it has none. */
  const rangeOf = (field: TemplateField): string | undefined => {
    const unit = unitForBands(
      {
        inchFraction: Number(field.inchFraction),
        centimetreDecimals: Number(field.centimetreDecimals),
      },
      version?.defaultDisplayUnit,
    )

    return unit === undefined
      ? undefined
      : formatMeasurementRange(
          intl,
          {
            minimumMillimetres: Number(field.minimumMillimetres),
            maximumMillimetres: Number(field.maximumMillimetres),
          },
          {
            unit,
            step: (Number(field.inchFraction) > 0
              ? Number(field.inchFraction)
              : 8) as InchFractionStep,
            decimals: (Number(field.centimetreDecimals) > 0
              ? Number(field.centimetreDecimals)
              : 1) as CentimetreDecimals,
          },
        )
  }

  const send = async (
    id: string,
    run: (input: {
      readonly version: string
      readonly idempotencyKey: string
    }) => Promise<VersionedResponse<MeasurementTemplate>>,
    done: string,
  ): Promise<void> => {
    if (precondition === undefined) {
      // Fail closed. The read behind this screen always carries a tag, so a missing one means the
      // screen is not showing a state worth acting on — ask for it again rather than send a
      // precondition the server would have to guess at.
      setFailure(new ApiError('The template must be read again.', { status: 409 }))
      return
    }

    setBusy(true)
    setFailure(null)
    setNotice(null)

    try {
      const result = await run({ version: precondition, idempotencyKey: keyFor(id) })
      forget(id)
      hold(result)
      setEditing(null)
      setRemoving(null)
      setNotice(done)
      template.reload()
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setBusy(false)
    }
  }

  const save = (form: FieldFormState): void => {
    if (templateId === undefined || versionId === undefined || version === null) {
      return
    }

    const existing = editing?.field ?? null
    const isNew = existing === null
    const others = fields
      .filter((row) => row.templateFieldId !== existing?.templateFieldId)
      .map((row) => row.key)

    setSubmitted((count) => count + 1)

    if (validateField(form, others, isNew).length > 0) {
      // The form renders them; nothing is sent. Keeping the pending state means the person is not
      // asked to retype anything, and a later successful submit reuses the same retry key.
      setEditing({ field: existing, form })
      return
    }

    // Ordering is #104. A new field goes to the end, which is where somebody adding one expects it;
    // an existing one keeps the place it has, so a save here cannot reorder the version.
    const order = isNew
      ? fields.reduce((highest, row) => Math.max(highest, Number(row.displayOrder)), -1) + 1
      : Number(existing.displayOrder)

    const request = toRequest(form, existing, order)
    const label = request.label

    void send(
      isNew ? `add:${request.key}` : `change:${existing.templateFieldId}`,
      ({ version: tag, idempotencyKey }) =>
        isNew
          ? addTemplateField({
              templateId,
              versionId,
              field: request,
              version: tag,
              idempotencyKey,
            })
          : changeTemplateField({
              templateId,
              versionId,
              fieldId: existing.templateFieldId,
              field: request,
              version: tag,
              idempotencyKey,
            }),
      intl.formatMessage({ id: isNew ? 'admin.field.added' : 'admin.field.changed' }, { label }),
    )
  }

  const remove = (outcome: ConfirmOutcome): void => {
    if (templateId === undefined || versionId === undefined || removing === null) {
      return
    }

    const reason = outcome.reason?.trim() ?? ''
    const field = removing

    void send(
      `remove:${field.templateFieldId}`,
      ({ version: tag, idempotencyKey }) =>
        removeTemplateField({
          templateId,
          versionId,
          fieldId: field.templateFieldId,
          reason: reason === '' ? null : reason,
          version: tag,
          idempotencyKey,
        }),
      intl.formatMessage({ id: 'admin.field.removed' }, { label: field.label }),
    )
  }

  const conflict = failure instanceof ApiError && failure.status === 409

  if (template.loading) {
    return (
      <section>
        <LoadingState what={intl.formatMessage({ id: 'admin.field.editor.loading' })} />
      </section>
    )
  }

  if (value === null) {
    return (
      <section>
        <AuthProblemAlert failure={template.failure} />
      </section>
    )
  }

  const back = (
    <p>
      <Link to={`/admin/templates/${templateId ?? ''}`}>
        <FormattedMessage id="admin.back" />
      </Link>
    </p>
  )

  if (version === null) {
    return (
      <section>
        {back}
        <Alert tone="warning" live="polite">
          <FormattedMessage id="admin.field.editor.notFound" />
        </Alert>
      </section>
    )
  }

  // A published or retired version is immutable in the database — a trigger refuses the write, not a
  // validator — so the controls that would fail are not offered at all. The one that does what the
  // person wants lives on the template screen, and the sentence says so.
  if (version.status !== 'Draft') {
    return (
      <section>
        {back}
        <h2>
          {intl.formatMessage(
            { id: 'admin.field.editor.title' },
            { number: version.versionNumber },
          )}
        </h2>
        <Alert tone="info" live="polite">
          <FormattedMessage id="admin.field.editor.notDraft" />
        </Alert>
      </section>
    )
  }

  return (
    <section>
      {back}

      <h2>
        {intl.formatMessage({ id: 'admin.field.editor.title' }, { number: version.versionNumber })}
      </h2>

      {notice === null ? null : (
        <Alert
          tone="success"
          live="polite"
          onDismiss={() => {
            setNotice(null)
          }}
        >
          {notice}
        </Alert>
      )}

      {conflict ? (
        <Alert
          tone="warning"
          live="assertive"
          title={intl.formatMessage({ id: 'admin.conflict.title' })}
          actions={
            <Button
              variant="secondary"
              onClick={() => {
                setFailure(null)
                setHeld(undefined)
                template.reload()
              }}
            >
              <FormattedMessage id="admin.reload" />
            </Button>
          }
        >
          <FormattedMessage id="admin.conflict.body" />
        </Alert>
      ) : (
        <AuthProblemAlert failure={failure} />
      )}

      {fields.length === 0 ? (
        <EmptyState iconName="ruler" live="polite">
          {intl.formatMessage({ id: 'admin.template.noFields' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'admin.template.fieldsCaption' })}
          rows={fields}
          rowKey={(row) => row.templateFieldId}
          rowLabel={(row) => row.label}
          columns={[
            {
              id: 'label',
              header: intl.formatMessage({ id: 'admin.template.column.label' }),
              primary: true,
              cell: (row: TemplateField) => row.label,
            },
            {
              id: 'key',
              header: intl.formatMessage({ id: 'admin.template.column.key' }),
              cell: (row: TemplateField) => row.key,
            },
            {
              id: 'group',
              header: intl.formatMessage({ id: 'admin.template.column.group' }),
              cell: (row: TemplateField) => row.groupName,
            },
            {
              id: 'unit',
              header: intl.formatMessage({ id: 'admin.template.column.unit' }),
              cell: (row: TemplateField) =>
                intl.formatMessage({
                  id:
                    row.canonicalUnit === 'Count'
                      ? 'admin.field.unit.Count'
                      : row.canonicalUnit === 'None'
                        ? 'admin.field.unit.None'
                        : 'admin.field.unit.Millimetre',
                }),
            },
            {
              id: 'range',
              header: intl.formatMessage({ id: 'admin.template.column.range' }),
              numeric: true,
              hideWhenNarrow: true,
              // The tailor's own unit at the field's own precision (#103). A choice field and a
              // field carrying the no-bounds sentinel both render nothing, because neither has a
              // range to state — and "0–0 mm" would read as a field that accepts only zero.
              cell: (row: TemplateField) => rangeOf(row) ?? '',
            },
            {
              id: 'required',
              header: intl.formatMessage({ id: 'admin.template.column.required' }),
              cell: (row: TemplateField) =>
                intl.formatMessage({
                  id: row.isRequired ? 'admin.template.required' : 'admin.template.optional',
                }),
            },
          ]}
          rowActions={(row: TemplateField) => (
            <>
              <Button
                variant="secondary"
                onClick={() => {
                  setFailure(null)
                  setEditing({ field: row, form: fieldToForm(row) })
                }}
              >
                {intl.formatMessage({ id: 'admin.field.edit' }, { label: row.label })}
              </Button>
              <Button
                variant="danger"
                busy={busy && removing?.templateFieldId === row.templateFieldId}
                onClick={() => {
                  setFailure(null)
                  setRemoving(row)
                }}
              >
                {intl.formatMessage({ id: 'admin.field.remove' }, { label: row.label })}
              </Button>
            </>
          )}
        />
      )}

      <Button
        variant="primary"
        onClick={() => {
          setFailure(null)
          // The group of the last field, so that somebody entering six fields of one step types the
          // step once rather than six times.
          setEditing({ field: null, form: blankField(fields.at(-1)?.groupName ?? '') })
        }}
      >
        <FormattedMessage id="admin.field.add" />
      </Button>

      {editing === null ? null : (
        <TemplateFieldForm
          busy={busy}
          defaultDisplayUnit={version.defaultDisplayUnit}
          existing={editing.field}
          form={editing.form}
          onCancel={() => {
            setEditing(null)
          }}
          onChange={(form) => {
            setEditing({ field: editing.field, form })
          }}
          onSubmit={save}
          otherKeys={fields
            .filter((row) => row.templateFieldId !== editing.field?.templateFieldId)
            .map((row) => row.key)}
          submissionId={submitted}
        />
      )}

      {removing === null ? null : (
        <ConfirmDialog
          open
          tier="reason"
          action={intl.formatMessage({ id: 'admin.field.remove' }, { label: removing.label })}
          title={intl.formatMessage({ id: 'admin.field.remove.title' })}
          confirmLabel={intl.formatMessage({ id: 'admin.field.remove' }, { label: removing.label })}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          busy={busy}
          onConfirm={remove}
          onCancel={() => {
            setRemoving(null)
          }}
        >
          {intl.formatMessage({ id: 'admin.field.remove.body' })}
        </ConfirmDialog>
      )}
    </section>
  )
}
