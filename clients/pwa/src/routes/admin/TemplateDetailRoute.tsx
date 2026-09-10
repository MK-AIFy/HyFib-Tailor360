import { useState } from 'react'
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
  TEMPLATE_ACTIONS_NEEDING_PUBLISH,
  TEMPLATE_ACTIONS_NEEDING_REASON,
  commandTemplateVersion,
  readMeasurementTemplate,
  startTemplateDraft,
} from '../../admin/templateApi'
import type { TemplateLifecycleAction } from '../../admin/templateApi'
import { ADMIN_PERMISSIONS } from '../../admin/adminPermissions'
import { useAdminResource } from '../../admin/useAdminResource'
import { useCurrentUser } from '../../auth/useSession'
import type { MeasurementTemplate, TemplateField, TemplateVersion } from '../../admin/types'
import {
  formatMeasurementRange,
  unitForBands,
} from '../../design-system/components/forms/measurementRange'
import type { CentimetreDecimals, InchFractionStep } from '../../i18n/units'
import type { MessageKey } from '../../i18n/en-IN'

/**
 * One measurement template: its versions, what each contains, and the acts that move one along.
 *
 * ## Why the acts are offered per version rather than per screen
 *
 * A template has four versions in four different states more often than not — one published, one
 * being drafted to replace it, and two retired. "Publish" means nothing without saying which, and a
 * screen-level action bar would have to disable itself constantly. Offering only what a *version*
 * admits also means the screen never shows a control the server would refuse.
 *
 * ## Why a published version offers a draft rather than an edit
 *
 * It is not a courtesy. A published version is immutable in the database — a trigger refuses the
 * write, not a validator — because a measurement renders through the version it was captured under,
 * and editing one would silently rewrite what a customer's stored measurements mean. So the control
 * that would fail is not offered; the one that does the thing the person actually wants is.
 *
 * ## Why the precondition is the version that was rendered
 *
 * Every lifecycle command sends the `ETag` of the read that painted this screen, not one taken just
 * before the command. Re-reading would make the precondition true by construction: it would fetch
 * whatever another administrator has since made of the template and act on that, which is exactly
 * what `If-Match` exists to refuse. Sending the rendered tag means a template somebody else changed
 * answers `409 measurements.version-changed`, and the conflict alert offers the re-read — so the
 * person approves what they actually looked at, twice, rather than once.
 *
 * ## Why the acts are also filtered by what this administrator holds
 *
 * `submit` needs `catalog.templates.edit`; returning, approving, publishing and retiring need
 * `catalog.templates.publish`. The two travel together in the Owner and Admin system roles, but a
 * shop may define a custom role holding either alone, so the pair cannot be assumed. A control that
 * would be refused is not offered — and, because a screen that silently drops four buttons reads as
 * a broken screen, the reason is said in words instead.
 *
 * ## Why the selected version is component state and not in the URL
 *
 * A deep link to a version would be useful and this deliberately does not have one yet: the reader
 * arrives from the list wanting the live one, and the fields of every version are already on the
 * page. When the editor arrives (#94) the version becomes a thing you are working *in* rather than
 * looking *at*, and that is the change that earns a route of its own.
 */
/** An act the administrator has committed to, and the retry key it will keep until it succeeds. */
interface PendingCommand {
  readonly action: TemplateLifecycleAction
  readonly version: TemplateVersion
  readonly idempotencyKey: string
}

/**
 * The four states this release knows, looked up rather than interpolated into a message key.
 *
 * `TemplateVersion.status` is typed `string` on purpose — `admin/types.ts` records why — so a server
 * that gains a state must make the screen say something honest rather than render `react-intl`'s
 * fallback, which is the key itself.
 */
const STATUS_MESSAGES: Readonly<Record<string, MessageKey>> = {
  Draft: 'admin.template.status.Draft',
  InReview: 'admin.template.status.InReview',
  Published: 'admin.template.status.Published',
  Retired: 'admin.template.status.Retired',
}

export function TemplateDetailRoute() {
  const intl = useIntl()

  /**
   * The band this field accepts, or undefined when it declares none.
   *
   * Undefined for a choice field, which has no bands at all, and for a numeric field carrying the
   * `ValidationBands.None` sentinel — `0 / 0`, which is "any measurement" rather than "only zero".
   */
  const rangeOf = (field: TemplateField): string | undefined => {
    const unit = unitForBands(
      {
        inchFraction: Number(field.inchFraction),
        centimetreDecimals: Number(field.centimetreDecimals),
      },
      selected?.defaultDisplayUnit,
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
  const { templateId } = useParams()
  const { permissions } = useCurrentUser()

  const canPublish = permissions.includes(ADMIN_PERMISSIONS.templatesPublish)

  const template = useAdminResource(`measurement-template:${templateId ?? ''}`, (signal) =>
    readMeasurementTemplate(templateId ?? '', signal),
  )

  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [pending, setPending] = useState<PendingCommand | null>(null)
  const [busy, setBusy] = useState(false)
  const [cloning, setCloning] = useState<string | null>(null)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)

  /**
   * The retry key of every command that has been attempted and has not yet succeeded, by the act and
   * the version it was for.
   *
   * `docs/architecture/conventions.md` section 4.3 is explicit: a retry after a conflict reuses the
   * *same* `Idempotency-Key`. Minting one when the confirmation opens would give a second attempt a
   * key the server has never seen, so a command whose response was lost rather than refused would
   * happen twice. The key is forgotten once the command has actually succeeded.
   */
  const [keys, setKeys] = useState<Readonly<Record<string, string>>>({})

  /**
   * The entity tag the last command returned, and the read it superseded.
   *
   * `template.reload()` is not awaited, so for one round trip the rendered value still carries the
   * tag the command has already consumed. Sending that one would answer 409 and blame another
   * administrator for this screen's own previous command. Holding the read it was taken against —
   * rather than clearing the tag from an effect — is what makes it expire on its own: the moment a
   * fresh read lands, `template.value` is a different object and the held tag stops applying.
   */
  const [settled, setSettled] = useState<{
    readonly tag: string
    readonly against: VersionedResponse<MeasurementTemplate> | null
  } | null>(null)

  /** The precondition to send: what the last command produced, else what the screen is showing. */
  const precondition =
    settled !== null && settled.against === template.value ? settled.tag : template.value?.version

  const value = template.value?.value ?? null

  // Newest first, which is the order somebody looks for a version in: the one being worked on is
  // the one they came for, and the retired ones are history below it.
  const versions = [...(value?.versions ?? [])].sort(
    (left, right) => Number(right.versionNumber) - Number(left.versionNumber),
  )

  const selected =
    versions.find((version) => version.templateVersionId === selectedId) ??
    versions.find((version) => version.templateVersionId === value?.publishedVersionId) ??
    versions[0] ??
    null

  /** What this version's state admits. A published one admits retiring; a retired one admits nothing. */
  const admits = (version: TemplateVersion): readonly TemplateLifecycleAction[] => {
    if (version.status === 'Draft') return ['submit']
    if (version.status === 'InReview') {
      return version.isApproved ? ['return', 'publish'] : ['return', 'approve']
    }
    if (version.status === 'Published') return ['retire']

    return []
  }

  /** …and of those, the ones this administrator may actually carry out. */
  const actionsFor = (version: TemplateVersion): readonly TemplateLifecycleAction[] =>
    canPublish
      ? admits(version)
      : admits(version).filter((action) => !TEMPLATE_ACTIONS_NEEDING_PUBLISH.includes(action))

  /** True when a version on this screen admits something this administrator is not allowed to do. */
  const withheld = versions.some((version) => admits(version).length > actionsFor(version).length)

  /** The retry key for one act on one version, minted once and held until that act succeeds. */
  const keyFor = (id: string): string => {
    const held = keys[id]

    if (held !== undefined) {
      return held
    }

    const minted = crypto.randomUUID()
    setKeys((all) => ({ ...all, [id]: minted }))

    return minted
  }

  const forget = (id: string) => {
    setKeys(({ [id]: _spent, ...rest }) => rest)
  }

  /** Remembers the tag a command returned, against the read it has just superseded. */
  const hold = (result: VersionedResponse<MeasurementTemplate>) => {
    setSettled(
      result.version === undefined ? null : { tag: result.version, against: template.value },
    )
  }

  /** Opens the confirmation for an act, carrying the retry key any earlier attempt at it left. */
  const ask = (action: TemplateLifecycleAction, version: TemplateVersion) => {
    setPending({
      action,
      version,
      idempotencyKey: keyFor(`${version.templateVersionId}:${action}`),
    })
  }

  /**
   * Sends one lifecycle command. The shared transport handles step-up recovery while this pending
   * decision stays in place, preserving its body, retry key and precondition for the single replay.
   */
  const send = async (
    attempt: PendingCommand,
    reason: string | null,
    version: string,
  ): Promise<void> => {
    if (templateId === undefined) {
      return
    }

    const id = `${attempt.version.templateVersionId}:${attempt.action}`

    setBusy(true)
    setFailure(null)
    setNotice(null)

    try {
      const result = await commandTemplateVersion({
        templateId,
        versionId: attempt.version.templateVersionId,
        action: attempt.action,
        reason,
        version,
        idempotencyKey: attempt.idempotencyKey,
      })

      forget(id)
      hold(result)
      setPending(null)
      setNotice(
        intl.formatMessage(
          { id: 'admin.template.done' },
          { number: attempt.version.versionNumber },
        ),
      )
      template.reload()
    } catch (cause: unknown) {
      setFailure(cause)
      setPending(null)
    } finally {
      setBusy(false)
    }
  }

  const run = (outcome: ConfirmOutcome) => {
    if (pending === null) {
      return
    }

    const needsReason = TEMPLATE_ACTIONS_NEEDING_REASON.includes(pending.action)
    const reason = outcome.reason?.trim() ?? ''

    if (needsReason && reason === '') {
      setFailure(new ApiError('A reason is required.', { status: 400 }))
      return
    }

    // The entity tag of the read this screen is showing: the precondition is what the administrator
    // reviewed. See the note above on why this is not re-read here.
    const version = precondition

    if (version === undefined) {
      // Fail closed. The read behind this screen always carries a tag, so a missing one means the
      // screen is not showing a state worth acting on — ask for it again rather than send a
      // precondition the server would have to guess at. The conflict alert offers exactly that.
      setPending(null)
      setFailure(new ApiError('The template must be read again.', { status: 409 }))
      return
    }

    void send(pending, needsReason ? reason : null, version)
  }

  const clone = (from: TemplateVersion) => {
    if (templateId === undefined) {
      return
    }

    const id = `${from.templateVersionId}:clone`
    const idempotencyKey = keyFor(id)

    setCloning(from.templateVersionId)
    setFailure(null)
    setNotice(null)

    // The server numbers a new version from the highest that exists, not from the one being copied
    // (`MeasurementTemplate.NextVersionNumber`). Naming it after the source would call the fourth
    // version of a template "Version 2" whenever somebody drafts from a superseded one, and the name
    // is stored rather than derived, so it would stay wrong for the life of the version.
    const next =
      versions.reduce((highest, version) => Math.max(highest, Number(version.versionNumber)), 0) + 1

    void startTemplateDraft({
      templateId,
      name: intl.formatMessage({ id: 'admin.templates.version' }, { number: next }),
      notes: null,
      defaultDisplayUnit: from.defaultDisplayUnit,
      cloneFromVersionId: from.templateVersionId,
      idempotencyKey,
    })
      .then((result) => {
        forget(id)
        hold(result)
        template.reload()
        setSelectedId(
          [...result.value.versions].sort(
            (left, right) => Number(right.versionNumber) - Number(left.versionNumber),
          )[0]?.templateVersionId ?? null,
        )
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setCloning(null)
      })
  }

  const conflict = failure instanceof ApiError && failure.status === 409
  const selfApproval =
    failure instanceof ApiError && failure.code === 'measurements.submitter-cannot-publish'

  // Publication runs the validation first and refuses on any error it finds. The findings themselves
  // are a screen of their own (#96); until then this says which refusal it was, because the generic
  // sentence for a 400 is "the reason is not clear" and the reason is entirely clear.
  const validationRefused =
    failure instanceof ApiError && failure.code === 'measurements.publish-validation-failed'

  if (template.loading) {
    return (
      <section>
        <LoadingState what={intl.formatMessage({ id: 'admin.template.loading' })} />
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

  return (
    <section>
      <p>
        <Link to="/admin/templates">
          <FormattedMessage id="admin.back" />
        </Link>
      </p>

      <h2>{value.name}</h2>

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

      {selfApproval ? (
        <Alert tone="warning" live="assertive">
          <FormattedMessage id="admin.template.selfApproval" />
        </Alert>
      ) : validationRefused ? (
        <Alert tone="warning" live="assertive">
          <FormattedMessage id="admin.template.validationRefused" />
        </Alert>
      ) : conflict ? (
        <Alert
          tone="warning"
          live="assertive"
          title={intl.formatMessage({ id: 'admin.conflict.title' })}
          actions={
            <Button
              variant="secondary"
              onClick={() => {
                setFailure(null)
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
        <AuthProblemAlert failure={failure ?? template.failure} />
      )}

      <h3>
        <FormattedMessage id="admin.template.versions" />
      </h3>

      {withheld ? (
        <Alert tone="info" live="off">
          <FormattedMessage id="admin.template.needsPublish" />
        </Alert>
      ) : null}

      {versions.length === 0 ? (
        <EmptyState iconName="ruler" live="polite">
          {intl.formatMessage({ id: 'admin.template.noVersions' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'admin.template.versionsCaption' })}
          rows={versions}
          rowKey={(row) => row.templateVersionId}
          rowLabel={(row) =>
            intl.formatMessage({ id: 'admin.templates.version' }, { number: row.versionNumber })
          }
          columns={[
            {
              id: 'version',
              header: intl.formatMessage({ id: 'admin.template.column.version' }),
              primary: true,
              numeric: true,
              cell: (row: TemplateVersion) => String(row.versionNumber),
            },
            {
              id: 'status',
              header: intl.formatMessage({ id: 'admin.template.column.status' }),
              cell: (row: TemplateVersion) =>
                intl.formatMessage({
                  id: STATUS_MESSAGES[row.status] ?? 'admin.template.status.unknown',
                }),
            },
            {
              id: 'name',
              header: intl.formatMessage({ id: 'admin.template.column.name' }),
              cell: (row: TemplateVersion) => row.name,
            },
            {
              id: 'fields',
              header: intl.formatMessage({ id: 'admin.template.column.fields' }),
              numeric: true,
              cell: (row: TemplateVersion) =>
                intl.formatMessage(
                  { id: 'admin.template.fields' },
                  { count: row.fields?.length ?? 0 },
                ),
            },
          ]}
          rowActions={(row: TemplateVersion) => (
            <>
              <Button
                variant="secondary"
                onClick={() => {
                  setSelectedId(row.templateVersionId)
                }}
              >
                {intl.formatMessage(
                  { id: 'admin.templates.version' },
                  { number: row.versionNumber },
                )}
              </Button>
              {row.status === 'Draft' ? (
                <Link to={`/admin/templates/${templateId ?? ''}/versions/${row.templateVersionId}`}>
                  <FormattedMessage id="admin.field.editor.open" />
                </Link>
              ) : null}
              {actionsFor(row).map((action) => (
                <Button
                  key={action}
                  variant={action === 'retire' || action === 'return' ? 'danger' : 'primary'}
                  busy={busy && pending?.version.templateVersionId === row.templateVersionId}
                  onClick={() => {
                    ask(action, row)
                  }}
                >
                  {intl.formatMessage({ id: `admin.template.action.${action}` as MessageKey })}
                </Button>
              ))}
              {row.status === 'Published' || row.status === 'Retired' ? (
                <Button
                  variant="secondary"
                  busy={cloning === row.templateVersionId}
                  onClick={() => {
                    clone(row)
                  }}
                >
                  <FormattedMessage id="admin.template.clone" />
                </Button>
              ) : null}
            </>
          )}
        />
      )}

      {selected === null ? null : (
        <>
          <h3>
            {intl.formatMessage(
              { id: 'admin.templates.version' },
              { number: selected.versionNumber },
            )}
          </h3>

          {selected.status === 'Published' ? (
            <Alert tone="info" live="off">
              <FormattedMessage id="admin.template.readOnly" />
            </Alert>
          ) : selected.status === 'InReview' ? (
            <Alert tone="info" live="off">
              <FormattedMessage
                id={
                  selected.isApproved
                    ? 'admin.template.approved'
                    : 'admin.template.awaitingApproval'
                }
              />
            </Alert>
          ) : null}

          {(selected.fields ?? []).length === 0 ? (
            <EmptyState iconName="ruler" live="polite">
              {intl.formatMessage({ id: 'admin.template.noFields' })}
            </EmptyState>
          ) : (
            <DataTable
              caption={intl.formatMessage({ id: 'admin.template.fieldsCaption' })}
              rows={selected.fields ?? []}
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
                  cell: (row: TemplateField) => row.canonicalUnit,
                },
                {
                  id: 'range',
                  header: intl.formatMessage({ id: 'admin.template.column.range' }),
                  numeric: true,
                  hideWhenNarrow: true,
                  // In the tailor's own unit at the field's own precision (#103). Millimetres are
                  // never shown to staff, so a bound quoted in them cannot be checked against the
                  // tape in anybody's hand — which is the only reason to show it.
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
            />
          )}
        </>
      )}

      {pending === null ? null : (
        <ConfirmDialog
          open
          tier={TEMPLATE_ACTIONS_NEEDING_REASON.includes(pending.action) ? 'reason' : 'confirm'}
          action={pending.action}
          title={intl.formatMessage({
            id: `admin.template.action.${pending.action}.title` as MessageKey,
          })}
          confirmLabel={intl.formatMessage({
            id: `admin.template.action.${pending.action}` as MessageKey,
          })}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          irreversible={pending.action === 'publish' || pending.action === 'retire'}
          busy={busy}
          onConfirm={run}
          onCancel={() => {
            setPending(null)
          }}
        >
          {intl.formatMessage({
            id: `admin.template.action.${pending.action}.body` as MessageKey,
          })}
        </ConfirmDialog>
      )}
    </section>
  )
}
