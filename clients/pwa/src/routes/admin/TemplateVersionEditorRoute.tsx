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
  validateTemplateVersion,
} from '../../admin/templateApi'
import { findingsForField, parseTarget } from '../../admin/templateFindings'
import { TemplateCapturePreview } from './TemplateCapturePreview'
import { TemplateValidationReport } from './TemplateValidationReport'
import { TemplateVersionCompare } from './TemplateVersionCompare'
import { useAdminResource } from '../../admin/useAdminResource'
import { blankField, fieldToForm, toRequest, validateField } from '../../admin/templateFieldForm'
import type { FieldFormState } from '../../admin/templateFieldForm'
import { groupFields, planFieldMove, planGroupMove } from '../../admin/templateFieldOrder'
import type { FieldGroup, MoveDirection, OrderWrite } from '../../admin/templateFieldOrder'
import { TemplateFieldForm } from './TemplateFieldForm'
import {
  formatMeasurementRange,
  unitForBands,
} from '../../design-system/components/forms/measurementRange'
import type { CentimetreDecimals, InchFractionStep } from '../../i18n/units'
import type {
  MeasurementTemplate,
  TemplateField,
  TemplateFinding,
  TemplateValidation,
} from '../../admin/types'

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
  const [moving, setMoving] = useState(false)
  /** How far a move has got, while it is running. */
  const [progress, setProgress] = useState<{
    readonly done: number
    readonly total: number
  } | null>(null)
  /** How far a move got before it stopped, which is a prefix of the renumbering and is saved. */
  const [partial, setPartial] = useState<{ readonly done: number; readonly total: number } | null>(
    null,
  )
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [submitted, setSubmitted] = useState(0)

  /** The retry key of every command attempted and not yet succeeded, by what it was for. */
  const [keys, setKeys] = useState<Readonly<Record<string, string>>>({})

  /** The tag the last command returned, which supersedes the read until it is re-read. */
  const [held, setHeld] = useState<string | undefined>(undefined)
  const [validation, setValidation] = useState<TemplateValidation | null>(null)
  const [checking, setChecking] = useState(false)
  /** True once a write has landed since the report was computed. */
  const [staleReport, setStaleReport] = useState(false)
  const [compareAgainst, setCompareAgainst] = useState<string | null>(null)

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
  /** The groups as the capture wizard will ask for them, which is the order the screen renders. */
  const groups = useMemo(() => groupFields(fields), [fields])

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
      setStaleReport(validation !== null)
      setNotice(done)
      template.reload()
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setBusy(false)
    }
  }

  /**
   * Applies one move: a sequence of full-body writes, one field at a time.
   *
   * ## Why it is a loop rather than a call
   *
   * There is no reorder endpoint and no bulk field save. Nothing accepts a list of identifiers or
   * two changed orders in one request, so a move is one `PUT …/fields/{fieldId}` per affected field,
   * **sequentially** — each carrying its own retry key, and each needing the `ETag` the previous
   * response returned, because the template's version advances on every write. Firing them together
   * would make all but the first fail their precondition.
   *
   * ## Why a failure part-way is reported rather than hidden
   *
   * The writes are ascending, so what has been saved when one fails is a *prefix* of the new
   * sequence: the numbers before the failure are right and the ones after it are unchanged. That is
   * a real state of the template, not a corrupt one — but it is not the state the person asked for,
   * and a screen that retried silently or said nothing would leave them believing a move happened.
   * So it says how far it got, in those words, and offers the reload that shows where the order
   * actually stands.
   */
  const applyMove = async (writes: readonly OrderWrite[], announce: string): Promise<void> => {
    if (templateId === undefined || versionId === undefined || writes.length === 0) {
      return
    }

    let tag = precondition

    if (tag === undefined) {
      setFailure(new ApiError('The template must be read again.', { status: 409 }))
      return
    }

    setMoving(true)
    setFailure(null)
    setNotice(null)
    setPartial(null)
    setProgress({ done: 0, total: writes.length })

    let done = 0

    try {
      for (const write of writes) {
        const id = `order:${write.field.templateFieldId}:${String(write.displayOrder)}`
        const result = await changeTemplateField({
          templateId,
          versionId,
          fieldId: write.field.templateFieldId,
          // The whole field, with only its number changed. There is no PATCH, and echoing the read
          // is what stops a reorder quietly resetting a bound or a rule it does not own.
          field: toRequest(fieldToForm(write.field), write.field, write.displayOrder),
          version: tag,
          idempotencyKey: keyFor(id),
        })

        forget(id)
        setHeld(result.version)
        done += 1
        setProgress({ done, total: writes.length })

        if (result.version === undefined) {
          // Every one of these routes answers with an `ETag`, so a response without one is a
          // response this screen cannot build the next precondition from. Stopping here reports a
          // real prefix; carrying on would send the tag the previous write already consumed.
          throw new ApiError('The template must be read again.', { status: 409 })
        }

        tag = result.version
      }

      setStaleReport(validation !== null)
      setNotice(announce)
      template.reload()
    } catch (cause: unknown) {
      // Honest about how far it got. A prefix of the renumbering is saved and correct; the rest is
      // untouched, and the person needs to see where the order stands before deciding again.
      setPartial({ done, total: writes.length })
      setFailure(cause)
    } finally {
      setMoving(false)
      setProgress(null)
    }
  }

  const moveField = (field: TemplateField, direction: MoveDirection, group: FieldGroup): void => {
    const writes = planFieldMove(fields, field.templateFieldId, direction)

    if (writes.length === 0) {
      // The control stays on screen at the boundary rather than disappearing and moving every other
      // control under the pointer, so pressing it says why nothing happened.
      setNotice(
        intl.formatMessage(
          { id: direction === 'up' ? 'admin.field.order.atStart' : 'admin.field.order.atEnd' },
          { label: field.label },
        ),
      )
      return
    }

    const at = group.fields.indexOf(field)
    void applyMove(
      writes,
      intl.formatMessage(
        { id: 'admin.field.order.moved' },
        {
          label: field.label,
          position: (direction === 'up' ? at - 1 : at + 1) + 1,
          total: group.fields.length,
          group: group.name,
        },
      ),
    )
  }

  const moveGroup = (name: string, direction: MoveDirection, at: number, total: number): void => {
    const writes = planGroupMove(fields, name, direction)

    if (writes.length === 0) {
      setNotice(
        intl.formatMessage(
          {
            id:
              direction === 'up'
                ? 'admin.field.order.groupAtStart'
                : 'admin.field.order.groupAtEnd',
          },
          { name },
        ),
      )
      return
    }

    void applyMove(
      writes,
      intl.formatMessage(
        { id: 'admin.field.order.groupMoved' },
        { name, position: (direction === 'up' ? at - 1 : at + 1) + 1, total },
      ),
    )
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

  /**
   * Runs the publish checks without changing anything, and holds what they found.
   *
   * The report is held rather than re-fetched on every render, and marked stale the moment a field
   * is written: it was computed against a state the screen has since moved past, and a stale report
   * that looked current would let somebody fix a finding, see it still listed, and fix it twice.
   */
  const check = async (): Promise<void> => {
    if (templateId === undefined || versionId === undefined) {
      return
    }

    setChecking(true)
    setFailure(null)

    try {
      setValidation(await validateTemplateVersion({ templateId, versionId }))
      setStaleReport(false)
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setChecking(false)
    }
  }

  /** The label of the field a finding is about, or null when it is about nothing on screen. */
  const labelForFinding = (finding: TemplateFinding): string | null => {
    const { fieldKey } = parseTarget(finding.target)
    if (fieldKey === null) {
      return null
    }
    return fields.find((field) => field.key === fieldKey)?.label ?? null
  }

  /** Opens the field a finding is about, which is where it can actually be corrected. */
  const goToFinding = (finding: TemplateFinding): void => {
    const { fieldKey } = parseTarget(finding.target)
    const field = fields.find((candidate) => candidate.key === fieldKey)

    if (field === undefined) {
      return
    }

    setFailure(null)
    setEditing({ field, form: fieldToForm(field) })
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

      {progress === null ? null : (
        <Alert tone="info" live="polite">
          {intl.formatMessage(
            { id: 'admin.field.order.working' },
            { done: progress.done, total: progress.total },
          )}
        </Alert>
      )}

      {partial === null ? null : (
        <Alert
          tone="warning"
          live="assertive"
          actions={
            <Button
              variant="secondary"
              onClick={() => {
                setPartial(null)
                setFailure(null)
                setHeld(undefined)
                template.reload()
              }}
            >
              <FormattedMessage id="admin.reload" />
            </Button>
          }
        >
          {intl.formatMessage(
            { id: 'admin.field.order.partial' },
            { done: partial.done, total: partial.total },
          )}
        </Alert>
      )}

      {conflict && partial === null ? (
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
        <>
          <p>{intl.formatMessage({ id: 'admin.field.order.explain' })}</p>
          {groups.map((group, groupIndex) => (
            <section key={group.name}>
              <h3>{intl.formatMessage({ id: 'admin.field.order.group' }, { name: group.name })}</h3>

              <Button
                iconName="chevron-up"
                busy={moving}
                onClick={() => {
                  moveGroup(group.name, 'up', groupIndex, groups.length)
                }}
                variant="secondary"
              >
                {intl.formatMessage({ id: 'admin.field.order.groupUp' }, { name: group.name })}
              </Button>
              <Button
                iconName="chevron-down"
                busy={moving}
                onClick={() => {
                  moveGroup(group.name, 'down', groupIndex, groups.length)
                }}
                variant="secondary"
              >
                {intl.formatMessage({ id: 'admin.field.order.groupDown' }, { name: group.name })}
              </Button>

              <DataTable
                caption={intl.formatMessage(
                  { id: 'admin.field.order.caption' },
                  { name: group.name },
                )}
                rows={group.fields}
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
                    id: 'position',
                    header: intl.formatMessage({ id: 'admin.template.column.position' }),
                    numeric: true,
                    // Where the field sits in its step, in words. A person moving a field with the
                    // keyboard needs to be able to read the position back, and a bare display order —
                    // which is global to the version and not contiguous until something renumbers it —
                    // is not that.
                    cell: (row: TemplateField) =>
                      intl.formatMessage(
                        { id: 'admin.field.order.position' },
                        {
                          position: group.fields.indexOf(row) + 1,
                          total: group.fields.length,
                          group: group.name,
                        },
                      ),
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
                  {
                    id: 'findings',
                    header: intl.formatMessage({ id: 'admin.version.check' }),
                    // The anchoring, which the issue calls the feature: the message sits beside
                    // the field that caused it, so an administrator reads which control is wrong
                    // rather than mapping a target path onto a form by eye.
                    cell: (row: TemplateField) =>
                      findingsForField(validation?.findings ?? [], row.key)
                        .map((finding) => finding.message)
                        .join(' '),
                  },
                ]}
                rowActions={(row: TemplateField) => (
                  <>
                    <Button
                      iconName="chevron-up"
                      busy={moving}
                      variant="secondary"
                      onClick={() => {
                        moveField(row, 'up', group)
                      }}
                    >
                      {intl.formatMessage({ id: 'admin.field.order.up' }, { label: row.label })}
                    </Button>
                    <Button
                      iconName="chevron-down"
                      busy={moving}
                      variant="secondary"
                      onClick={() => {
                        moveField(row, 'down', group)
                      }}
                    >
                      {intl.formatMessage({ id: 'admin.field.order.down' }, { label: row.label })}
                    </Button>
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
            </section>
          ))}
        </>
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

      <Button
        busy={checking}
        onClick={() => {
          void check()
        }}
        variant="secondary"
      >
        <FormattedMessage id="admin.version.check" />
      </Button>

      {validation === null ? null : (
        <TemplateValidationReport
          knownKeys={fields.map((field) => field.key)}
          labelFor={labelForFinding}
          onGoTo={goToFinding}
          stale={staleReport}
          validation={validation}
        />
      )}

      <TemplateCapturePreview version={version} />

      <TemplateVersionCompare
        againstId={compareAgainst}
        controlId={(name) => `editor-${name}`}
        onAgainstChange={setCompareAgainst}
        others={value.versions.filter((one) => one.templateVersionId !== version.templateVersionId)}
        version={version}
      />

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
