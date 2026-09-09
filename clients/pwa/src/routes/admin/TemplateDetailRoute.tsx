import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useParams } from 'react-router'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ApiError } from '../../auth/apiClient'
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
import type { TemplateField, TemplateVersion } from '../../admin/types'
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
export function TemplateDetailRoute() {
  const intl = useIntl()
  const { templateId } = useParams()
  const { permissions } = useCurrentUser()

  const canPublish = permissions.includes(ADMIN_PERMISSIONS.templatesPublish)

  const template = useAdminResource(`measurement-template:${templateId ?? ''}`, (signal) =>
    readMeasurementTemplate(templateId ?? '', signal),
  )

  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [pending, setPending] = useState<{
    readonly action: TemplateLifecycleAction
    readonly version: TemplateVersion
    readonly idempotencyKey: string
  } | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)

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

  const run = (outcome: ConfirmOutcome) => {
    if (pending === null || templateId === undefined) {
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
    const version = template.value?.version

    if (version === undefined) {
      // Fail closed. The read behind this screen always carries a tag, so a missing one means the
      // screen is not showing a state worth acting on — ask for it again rather than send a
      // precondition the server would have to guess at. The conflict alert offers exactly that.
      setPending(null)
      setFailure(new ApiError('The template must be read again.', { status: 409 }))
      return
    }

    setBusy(true)
    setFailure(null)

    void commandTemplateVersion({
      templateId,
      versionId: pending.version.templateVersionId,
      action: pending.action,
      reason: needsReason ? reason : null,
      version,
      idempotencyKey: pending.idempotencyKey,
    })
      .then(() => {
        setNotice(
          intl.formatMessage(
            { id: 'admin.template.done' },
            { number: pending.version.versionNumber },
          ),
        )
        template.reload()
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
        setPending(null)
      })
  }

  const clone = (from: TemplateVersion) => {
    if (templateId === undefined) {
      return
    }

    setBusy(true)
    setFailure(null)

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
      idempotencyKey: crypto.randomUUID(),
    })
      .then((result) => {
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
        setBusy(false)
      })
  }

  const conflict = failure instanceof ApiError && failure.status === 409
  const selfApproval =
    failure instanceof ApiError && failure.code === 'measurements.submitter-cannot-publish'

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
                intl.formatMessage({ id: `admin.template.status.${row.status}` as MessageKey }),
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
                busy={busy && selected?.templateVersionId === row.templateVersionId}
                onClick={() => {
                  setSelectedId(row.templateVersionId)
                }}
              >
                {intl.formatMessage(
                  { id: 'admin.templates.version' },
                  { number: row.versionNumber },
                )}
              </Button>
              {actionsFor(row).map((action) => (
                <Button
                  key={action}
                  variant={action === 'retire' || action === 'return' ? 'danger' : 'primary'}
                  busy={busy && pending?.version.templateVersionId === row.templateVersionId}
                  onClick={() => {
                    setPending({ action, version: row, idempotencyKey: crypto.randomUUID() })
                  }}
                >
                  {intl.formatMessage({ id: `admin.template.action.${action}` as MessageKey })}
                </Button>
              ))}
              {row.status === 'Published' || row.status === 'Retired' ? (
                <Button
                  variant="secondary"
                  busy={busy}
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
                  // Millimetres, deliberately. Rendering these the way a tailor reads them is #94,
                  // and a half-conversion here would be a number nobody can check.
                  cell: (row: TemplateField) =>
                    row.canonicalUnit === 'None'
                      ? ''
                      : intl.formatMessage(
                          { id: 'admin.template.millimetres' },
                          { from: row.minimumMillimetres, to: row.maximumMillimetres },
                        ),
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
