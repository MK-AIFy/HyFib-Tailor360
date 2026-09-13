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
import { listBranches } from '../../admin/adminApi'
import { ADMIN_PERMISSIONS } from '../../admin/adminPermissions'
import { useAdminResource } from '../../admin/useAdminResource'
import { useCurrentUser } from '../../auth/useSession'
import {
  addCatalogCategory,
  addCatalogServiceType,
  correctCategoryPresentation,
  correctServiceTypePresentation,
  editCatalogCategory,
  editCatalogServiceType,
  publishCatalogVersion,
  readCatalogVersion,
  removeCatalogCategory,
  removeCatalogServiceType,
  retireCatalogVersion,
  validateCatalogVersion,
} from '../../catalog/catalogApi'
import {
  branchesOutsideParent,
  buildTree,
  findingsForCategory,
  findingsForServiceType,
  flatten,
  missingLinks,
  serviceBranchesOutsideCategory,
  unanchoredCatalogFindings,
} from '../../catalog/catalogTree'
import { CatalogEntryForm } from './CatalogEntryForm'
import { CatalogPresentationForm } from './CatalogPresentationForm'
import type { PresentationDraft } from './CatalogPresentationForm'
import { blankEntry } from '../../catalog/catalogEntry'
import type { CatalogEntryDraft } from '../../catalog/catalogEntry'
import type {
  CatalogCategory,
  CatalogServiceType,
  CatalogValidationReport,
} from '../../catalog/types'
import type { MessageKey } from '../../i18n/en-IN'

/** One row of the flattened tree: a category, or a service type under one. */
interface Row {
  readonly key: string
  readonly kind: 'category' | 'serviceType'
  readonly depth: number
  readonly code: string
  readonly name: string
  readonly category: CatalogCategory
  readonly serviceType: CatalogServiceType | null
}

/**
 * One catalogue version: the hierarchy, what may be changed in it, and what publication would say.
 *
 * ## Why the tree is a table and not a nesting of lists
 *
 * The hierarchy is at most a few levels deep and every node carries the same five facts — code,
 * name, kind, where it is offered, whether it can be ordered. A table states those once as headers
 * and a nested list restates them per node, which is what makes a nested list unreadable at forty
 * nodes. The nesting is carried by the depth of each row and announced with it, rather than by
 * indentation alone.
 *
 * ## Why a published version offers a label correction and nothing else
 *
 * Because that is what the server admits. A published version's structure is what orders were placed
 * against, so changing it would change what an order already placed *meant*. The screen therefore
 * does not offer the controls that would fail; it offers the one that works, and says where the
 * change is actually made — a draft started from this version.
 *
 * ## Why the subset rule is explained here rather than at publication
 *
 * A sub-category cannot be offered where its parent is not, and an empty set means offered nowhere.
 * Both are easy to get wrong across twenty categories, and finding out at publication means working
 * back from a finding to which of the twenty. The row says it where it is true.
 */
export function CatalogVersionEditorRoute() {
  const intl = useIntl()
  const { versionId } = useParams()
  const { permissions } = useCurrentUser()

  const canPublish = permissions.includes(ADMIN_PERMISSIONS.catalogPublish)

  const catalogue = useAdminResource(`catalog-version:${versionId ?? ''}`, (signal) =>
    readCatalogVersion(versionId ?? '', signal),
  )
  const branches = useAdminResource('branches', (signal) => listBranches(signal))

  const [editing, setEditing] = useState<{
    readonly kind: 'category' | 'serviceType'
    readonly existing: Row | null
    readonly draft: CatalogEntryDraft
  } | null>(null)
  const [removing, setRemoving] = useState<Row | null>(null)
  const [correcting, setCorrecting] = useState<{
    readonly row: Row
    readonly draft: PresentationDraft
  } | null>(null)
  const [publishing, setPublishing] = useState(false)
  const [retiring, setRetiring] = useState(false)
  const [busy, setBusy] = useState(false)
  const [checking, setChecking] = useState(false)
  const [validation, setValidation] = useState<CatalogValidationReport | null>(null)
  const [staleReport, setStaleReport] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [keys, setKeys] = useState<Readonly<Record<string, string>>>({})
  const [held, setHeld] = useState<string | undefined>(undefined)

  const value = catalogue.value?.value ?? null
  const precondition = held ?? catalogue.value?.version

  const rows = useMemo<readonly Row[]>(() => {
    if (value === null) {
      return []
    }

    return flatten(buildTree(value)).flatMap((node) => [
      {
        key: `category:${node.category.categoryId}`,
        kind: 'category' as const,
        depth: node.depth,
        code: node.category.code,
        name: node.category.name,
        category: node.category,
        serviceType: null,
      },
      ...node.serviceTypes.map((serviceType) => ({
        key: `serviceType:${serviceType.serviceTypeId}`,
        kind: 'serviceType' as const,
        depth: node.depth + 1,
        code: serviceType.code,
        name: serviceType.name,
        category: node.category,
        serviceType,
      })),
    ])
  }, [value])

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
      // Fail closed: the read behind this screen always carries a tag, so a missing one means the
      // screen is not showing a state worth acting on.
      setFailure(new ApiError('The catalogue must be read again.', { status: 409 }))
      return
    }

    setBusy(true)
    setFailure(null)
    setNotice(null)

    try {
      const result = await run({ version: precondition, idempotencyKey: keyFor(id) })
      forget(id)
      setHeld(result.version)
      setEditing(null)
      setRemoving(null)
      setCorrecting(null)
      setPublishing(false)
      setRetiring(false)
      // Every write moves the version past whatever the last check saw.
      setStaleReport(validation !== null)
      setNotice(done)
      catalogue.reload()
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setBusy(false)
    }
  }

  const check = async (): Promise<void> => {
    if (versionId === undefined) {
      return
    }

    setChecking(true)
    setFailure(null)

    try {
      setValidation(await validateCatalogVersion(versionId))
      setStaleReport(false)
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setChecking(false)
    }
  }

  const save = (): void => {
    if (versionId === undefined || editing === null) {
      return
    }

    const { draft, kind, existing } = editing
    const reason = draft.reason.trim() === '' ? null : draft.reason.trim()
    const branchIds = [...draft.branchIds]

    if (kind === 'category') {
      const body = {
        code: draft.code.trim(),
        name: draft.name.trim(),
        nameTamil: emptyToNull(draft.nameTamil),
        description: emptyToNull(draft.description),
        displayOrder: draft.displayOrder,
        parentCategoryId: draft.parentCategoryId,
        branchIds,
        activeFrom: null,
        activeTo: null,
        featureFlagKey: emptyToNull(draft.featureFlagKey),
        reason,
      }

      void send(
        existing === null ? `add-category:${body.code}` : `edit-category:${existing.key}`,
        ({ version, idempotencyKey }) =>
          existing === null
            ? addCatalogCategory({ versionId, category: body, version, idempotencyKey })
            : editCatalogCategory({
                versionId,
                categoryId: existing.category.categoryId,
                category: body,
                version,
                idempotencyKey,
              }),
        intl.formatMessage({ id: 'catalog.saved' }, { name: body.name }),
      )
      return
    }

    const body = {
      code: draft.code.trim(),
      name: draft.name.trim(),
      nameTamil: emptyToNull(draft.nameTamil),
      description: emptyToNull(draft.description),
      displayOrder: draft.displayOrder,
      branchIds,
      activeFrom: null,
      activeTo: null,
      measurementTemplateId: emptyToNull(draft.measurementTemplateId),
      workflowDefinitionId: emptyToNull(draft.workflowDefinitionId),
      designOptionGroupIds: draft.designOptionGroupIds
        .split(',')
        .map((one: string) => one.trim())
        .filter((one: string) => one !== ''),
      priceListItemCode: emptyToNull(draft.priceListItemCode),
      qcChecklistTemplateId: emptyToNull(draft.qcChecklistTemplateId),
      allowIncomplete: draft.allowIncomplete,
      expectedDurationDays: draft.expectedDurationDays,
      intakeWarning: emptyToNull(draft.intakeWarning),
      reason,
    }

    void send(
      existing === null ? `add-service:${body.code}` : `edit-service:${existing.key}`,
      ({ version, idempotencyKey }) =>
        existing?.serviceType == null
          ? addCatalogServiceType({
              versionId,
              categoryId: editing.draft.parentCategoryId ?? '',
              serviceType: body,
              version,
              idempotencyKey,
            })
          : editCatalogServiceType({
              versionId,
              serviceTypeId: existing.serviceType.serviceTypeId,
              serviceType: body,
              version,
              idempotencyKey,
            }),
      intl.formatMessage({ id: 'catalog.saved' }, { name: body.name }),
    )
  }

  /**
   * The one change a published version admits.
   *
   * Offered on a published version and nowhere else. A draft is edited properly, and the server
   * refuses a correction to one; a retired version is a record of what orders were placed against,
   * and renaming a label on it changes how that record reads for no operational gain — nobody is
   * ordering from it. `docs/prd/category-hierarchy.md` section 7 grants exactly this much editing of
   * a *published* version.
   */
  const correct = (): void => {
    if (versionId === undefined || correcting === null) {
      return
    }

    const { row, draft } = correcting
    const presentation = {
      name: draft.name.trim(),
      nameTamil: emptyToNull(draft.nameTamil),
      description: emptyToNull(draft.description),
      displayOrder: draft.displayOrder,
      // The server requires one — orders are pinned to this version, so the correction is audited
      // with who made it and why. An empty one is sent as empty and refused, rather than silently
      // dropped here, so the refusal reads the same as every other field-level one.
      reason: draft.reason.trim(),
    }

    void send(
      `correct:${row.key}`,
      ({ version, idempotencyKey }) =>
        row.serviceType === null
          ? correctCategoryPresentation({
              versionId,
              categoryId: row.category.categoryId,
              presentation,
              version,
              idempotencyKey,
            })
          : correctServiceTypePresentation({
              versionId,
              serviceTypeId: row.serviceType.serviceTypeId,
              presentation,
              version,
              idempotencyKey,
            }),
      intl.formatMessage({ id: 'catalog.corrected' }, { name: row.name }),
    )
  }

  const remove = (outcome: ConfirmOutcome): void => {
    if (versionId === undefined || removing === null) {
      return
    }

    const row = removing
    const reason = outcome.reason?.trim() ?? ''

    void send(
      `remove:${row.key}`,
      ({ version, idempotencyKey }) =>
        row.serviceType === null
          ? removeCatalogCategory({
              versionId,
              categoryId: row.category.categoryId,
              reason: reason === '' ? null : reason,
              version,
              idempotencyKey,
            })
          : removeCatalogServiceType({
              versionId,
              serviceTypeId: row.serviceType.serviceTypeId,
              reason: reason === '' ? null : reason,
              version,
              idempotencyKey,
            }),
      intl.formatMessage({ id: 'catalog.removed' }, { name: row.name }),
    )
  }

  const publish = (outcome: ConfirmOutcome): void => {
    if (versionId === undefined) {
      return
    }
    const reason = outcome.reason?.trim() ?? ''

    void send(
      'publish',
      ({ version, idempotencyKey }) =>
        publishCatalogVersion({
          versionId,
          reason: reason === '' ? null : reason,
          version,
          idempotencyKey,
        }),
      intl.formatMessage({ id: 'catalog.publish.done' }),
    )
  }

  const retire = (outcome: ConfirmOutcome): void => {
    if (versionId === undefined) {
      return
    }
    const reason = outcome.reason?.trim() ?? ''

    void send(
      'retire',
      ({ version, idempotencyKey }) =>
        retireCatalogVersion({
          versionId,
          reason: reason === '' ? null : reason,
          version,
          idempotencyKey,
        }),
      intl.formatMessage({ id: 'catalog.retire.done' }),
    )
  }

  const conflict = failure instanceof ApiError && failure.status === 409

  /**
   * A refusal is shown inside the dialog that caused it, and nowhere else while that dialog is
   * open. The dialog is modal: an alert rendered behind it sits under the backdrop and outside the
   * focus trap, so a person who confirmed a publication that was refused would see a dialog that
   * appeared to do nothing. Keeping the dialog open is also what keeps the reason they typed, which
   * a retry needs — and the retry key is held across the refusal, so retrying cannot publish twice.
   */
  const dialogOpen = removing !== null || publishing || retiring

  if (catalogue.loading) {
    return (
      <section>
        <LoadingState what={intl.formatMessage({ id: 'catalog.editor.loading' })} />
      </section>
    )
  }

  if (value === null) {
    return (
      <section>
        <AuthProblemAlert failure={catalogue.failure} />
      </section>
    )
  }

  const isDraft = value.version.status === 'Draft'
  const findings = validation?.findings ?? []
  const orphans = unanchoredCatalogFindings(findings, value)
  const branchList = (branches.value ?? []).map((branch) => ({
    branchId: branch.branchId,
    name: branch.name,
  }))
  const branchName = (branchId: string): string =>
    branchList.find((branch) => branch.branchId === branchId)?.name ?? branchId

  const findingsOn = (row: Row): readonly string[] =>
    (row.serviceType === null
      ? findingsForCategory(findings, row.code)
      : findingsForServiceType(findings, row.category.code, row.code)
    ).map((finding) => finding.message)

  const outsideFor = (row: Row): readonly string[] =>
    row.serviceType === null
      ? branchesOutsideParent(row.category, value.categories)
      : serviceBranchesOutsideCategory(row.serviceType, value.categories)

  return (
    <section>
      <p>
        <Link to="/admin/catalog">
          <FormattedMessage id="admin.back" />
        </Link>
      </p>

      <h2>
        {intl.formatMessage({ id: 'catalog.version' }, { number: value.version.versionNumber })}
      </h2>

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

      {conflict && !dialogOpen ? (
        <Alert
          actions={
            <Button
              onClick={() => {
                setFailure(null)
                setHeld(undefined)
                catalogue.reload()
              }}
              variant="secondary"
            >
              <FormattedMessage id="admin.reload" />
            </Button>
          }
          live="assertive"
          title={intl.formatMessage({ id: 'admin.conflict.title' })}
          tone="warning"
        >
          <FormattedMessage id="admin.conflict.body" />
        </Alert>
      ) : (
        <AuthProblemAlert failure={dialogOpen ? null : failure} />
      )}

      {value.version.status === 'Published' ? (
        <Alert live="polite" tone="info">
          <FormattedMessage id="catalog.editor.readOnly" />
        </Alert>
      ) : value.version.status === 'Retired' ? (
        <Alert live="polite" tone="info">
          <FormattedMessage id="catalog.editor.retired" />
        </Alert>
      ) : null}

      {canPublish ? null : (
        <Alert live="off" tone="info">
          <FormattedMessage
            id={
              value.version.status === 'Published'
                ? 'catalog.needsPublish.correction'
                : 'catalog.needsPublish'
            }
          />
        </Alert>
      )}

      {rows.length === 0 ? (
        <EmptyState iconName="list" live="polite">
          {intl.formatMessage({ id: 'catalog.editor.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'catalog.editor.caption' })}
          columns={[
            {
              id: 'code',
              header: intl.formatMessage({ id: 'catalog.column.code' }),
              primary: true,
              // The nesting, said rather than only drawn: indentation alone is not available to a
              // screen-reader user reading a table cell.
              cell: (row: Row) => `${'— '.repeat(row.depth)}${row.code}`,
            },
            {
              id: 'name',
              header: intl.formatMessage({ id: 'catalog.column.name' }),
              cell: (row: Row) => row.name,
            },
            {
              id: 'kind',
              header: intl.formatMessage({ id: 'catalog.column.kind' }),
              cell: (row: Row) =>
                intl.formatMessage({
                  id:
                    row.serviceType !== null
                      ? 'catalog.kind.serviceType'
                      : row.category.isGroupingNode
                        ? 'catalog.kind.grouping'
                        : 'catalog.kind.category',
                }),
            },
            {
              id: 'branches',
              header: intl.formatMessage({ id: 'catalog.column.branches' }),
              cell: (row: Row) => {
                const offered =
                  row.serviceType === null ? row.category.branchIds : row.serviceType.branchIds
                const outside = outsideFor(row)

                const said =
                  offered.length === 0
                    ? intl.formatMessage({ id: 'catalog.branches.none' })
                    : offered.map(branchName).join(', ')

                return outside.length === 0
                  ? said
                  : `${said} — ${intl.formatMessage(
                      { id: 'catalog.branches.outside' },
                      { count: outside.length },
                    )}`
              },
            },
            {
              id: 'orderable',
              header: intl.formatMessage({ id: 'catalog.column.orderable' }),
              cell: (row: Row) => {
                if (row.serviceType === null) {
                  return ''
                }

                const missing = missingLinks(row.serviceType)

                return missing.length === 0
                  ? intl.formatMessage({ id: 'catalog.orderable.yes' })
                  : `${intl.formatMessage(
                      { id: 'catalog.orderable.missing' },
                      { count: missing.length },
                    )} ${missing
                      .map((link) =>
                        intl.formatMessage({ id: `catalog.link.${link}` as MessageKey }),
                      )
                      .join(', ')}`
              },
            },
            {
              id: 'findings',
              header: intl.formatMessage({ id: 'catalog.column.findings' }),
              // Beside the thing it is about, for the reason the measurement-template report gives:
              // a list makes an administrator map a target path onto a tree by eye.
              cell: (row: Row) => findingsOn(row).join(' '),
            },
          ]}
          rowActions={(row: Row) =>
            !isDraft ? (
              <>
                {row.serviceType === null ? (
                  <Link
                    to={`/admin/catalog/${versionId ?? ''}/categories/${row.category.categoryId}/design`}
                  >
                    {intl.formatMessage({ id: 'catalog.design.open' }, { name: row.name })}
                  </Link>
                ) : null}
                {value.version.status === 'Published' && canPublish ? (
                  <Button
                    onClick={() => {
                      setFailure(null)
                      setCorrecting({
                        row,
                        draft: {
                          name: row.name,
                          nameTamil:
                            (row.serviceType === null
                              ? row.category.nameTamil
                              : row.serviceType.nameTamil) ?? '',
                          description:
                            (row.serviceType === null
                              ? row.category.description
                              : row.serviceType.description) ?? '',
                          displayOrder: Number(
                            row.serviceType === null
                              ? row.category.displayOrder
                              : row.serviceType.displayOrder,
                          ),
                          reason: '',
                        },
                      })
                    }}
                    variant="secondary"
                  >
                    {intl.formatMessage({ id: 'catalog.correct' }, { name: row.name })}
                  </Button>
                ) : null}
              </>
            ) : (
              <>
                <Button
                  onClick={() => {
                    setFailure(null)
                    setEditing({
                      kind: row.kind,
                      existing: row,
                      draft: toDraft(row),
                    })
                  }}
                  variant="secondary"
                >
                  {intl.formatMessage(
                    {
                      id:
                        row.serviceType === null
                          ? 'catalog.category.edit'
                          : 'catalog.serviceType.edit',
                    },
                    { name: row.name },
                  )}
                </Button>
                {row.serviceType === null ? (
                  <Link
                    to={`/admin/catalog/${versionId ?? ''}/categories/${row.category.categoryId}/design`}
                  >
                    {intl.formatMessage({ id: 'catalog.design.open' }, { name: row.name })}
                  </Link>
                ) : null}
                {row.serviceType === null ? (
                  <Button
                    onClick={() => {
                      setFailure(null)
                      setEditing({
                        kind: 'serviceType',
                        existing: null,
                        draft: {
                          ...blankEntry(row.category.categoryId),
                          branchIds: [...row.category.branchIds],
                        },
                      })
                    }}
                    variant="secondary"
                  >
                    {intl.formatMessage(
                      { id: 'catalog.serviceType.add' },
                      { name: row.category.name },
                    )}
                  </Button>
                ) : null}
                <Button
                  onClick={() => {
                    setFailure(null)
                    setRemoving(row)
                  }}
                  variant="danger"
                >
                  {intl.formatMessage(
                    {
                      id:
                        row.serviceType === null
                          ? 'catalog.category.remove'
                          : 'catalog.serviceType.remove',
                    },
                    { name: row.name },
                  )}
                </Button>
              </>
            )
          }
          rowKey={(row) => row.key}
          rowLabel={(row) => row.name}
          rows={rows}
        />
      )}

      {isDraft ? (
        <Button
          onClick={() => {
            setFailure(null)
            setEditing({ kind: 'category', existing: null, draft: blankEntry() })
          }}
          variant="primary"
        >
          <FormattedMessage id="catalog.category.add" />
        </Button>
      ) : null}

      <Button
        busy={checking}
        onClick={() => {
          void check()
        }}
        variant="secondary"
      >
        <FormattedMessage id="catalog.check" />
      </Button>

      {validation === null ? null : (
        <section>
          {staleReport ? (
            <Alert live="polite" tone="info">
              <FormattedMessage id="catalog.check.stale" />
            </Alert>
          ) : null}

          {findings.length === 0 ? (
            <Alert live="polite" tone="success">
              <FormattedMessage id="catalog.check.ready" />
            </Alert>
          ) : (
            <Alert live="polite" tone={Number(validation.errorCount) > 0 ? 'warning' : 'info'}>
              <p>
                {intl.formatMessage(
                  { id: 'catalog.check.counts' },
                  {
                    errors: Number(validation.errorCount),
                    warnings: Number(validation.warningCount),
                  },
                )}
              </p>
              <p>
                {intl.formatMessage({
                  id:
                    Number(validation.errorCount) > 0
                      ? 'catalog.check.blocked'
                      : 'catalog.check.warningsOnly',
                })}
              </p>

              <ul>
                {findings.map((finding) => (
                  <li key={`${finding.code}-${finding.target ?? 'version'}`}>
                    {intl.formatMessage({
                      id:
                        finding.severity === 'Error'
                          ? 'catalog.check.finding.error'
                          : 'catalog.check.finding.warning',
                    })}
                    {': '}
                    {finding.message}
                  </li>
                ))}
              </ul>

              {orphans.length > 0 ? (
                <p>
                  <FormattedMessage id="catalog.check.unanchored" />
                </p>
              ) : null}
            </Alert>
          )}
        </section>
      )}

      {isDraft && canPublish ? (
        <Button
          busy={busy && publishing}
          onClick={() => {
            setFailure(null)
            setPublishing(true)
          }}
          variant="primary"
        >
          <FormattedMessage id="catalog.publish" />
        </Button>
      ) : null}

      {value.version.status !== 'Retired' && canPublish ? (
        <Button
          busy={busy && retiring}
          onClick={() => {
            setFailure(null)
            setRetiring(true)
          }}
          variant="danger"
        >
          <FormattedMessage id="catalog.retire" />
        </Button>
      ) : null}

      {editing === null ? null : (
        <CatalogEntryForm
          branches={branchList}
          branchesOutside={editing.existing === null ? [] : outsideFor(editing.existing)}
          busy={busy}
          controlId={(name) => `catalog-${name}`}
          draft={editing.draft}
          existingCode={editing.existing?.code ?? null}
          kind={editing.kind}
          onCancel={() => {
            setEditing(null)
          }}
          onChange={(draft) => {
            setEditing({ ...editing, draft })
          }}
          onSubmit={save}
          parents={value.categories.filter(
            (category) => category.categoryId !== editing.existing?.category.categoryId,
          )}
        />
      )}

      {correcting === null ? null : (
        <CatalogPresentationForm
          busy={busy}
          controlId={(name) => `catalog-${name}`}
          draft={correcting.draft}
          onCancel={() => {
            setCorrecting(null)
          }}
          onChange={(draft) => {
            setCorrecting({ ...correcting, draft })
          }}
          onSubmit={correct}
          subject={correcting.row.name}
        />
      )}

      {removing === null ? null : (
        <ConfirmDialog
          action={intl.formatMessage(
            {
              id:
                removing.serviceType === null
                  ? 'catalog.category.remove'
                  : 'catalog.serviceType.remove',
            },
            { name: removing.name },
          )}
          busy={busy}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          confirmLabel={intl.formatMessage(
            {
              id:
                removing.serviceType === null
                  ? 'catalog.category.remove'
                  : 'catalog.serviceType.remove',
            },
            { name: removing.name },
          )}
          onCancel={() => {
            setRemoving(null)
          }}
          onConfirm={remove}
          open
          problem={<AuthProblemAlert failure={failure} />}
          tier="reason"
          title={intl.formatMessage({ id: 'catalog.remove.title' })}
        >
          {intl.formatMessage({ id: 'catalog.remove.body' })}
        </ConfirmDialog>
      )}

      {publishing ? (
        <ConfirmDialog
          action={intl.formatMessage({ id: 'catalog.publish' })}
          busy={busy}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          confirmLabel={intl.formatMessage({ id: 'catalog.publish' })}
          irreversible
          onCancel={() => {
            setPublishing(false)
          }}
          onConfirm={publish}
          open
          problem={<AuthProblemAlert failure={failure} />}
          tier="reason"
          title={intl.formatMessage({ id: 'catalog.publish.title' })}
        >
          {intl.formatMessage({ id: 'catalog.publish.body' })}
        </ConfirmDialog>
      ) : null}

      {retiring ? (
        <ConfirmDialog
          action={intl.formatMessage({ id: 'catalog.retire' })}
          busy={busy}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          confirmLabel={intl.formatMessage({ id: 'catalog.retire' })}
          irreversible
          onCancel={() => {
            setRetiring(false)
          }}
          onConfirm={retire}
          open
          problem={<AuthProblemAlert failure={failure} />}
          tier="reason"
          title={intl.formatMessage({ id: 'catalog.retire.title' })}
        >
          {intl.formatMessage({ id: 'catalog.retire.body' })}
        </ConfirmDialog>
      ) : null}
    </section>
  )
}

function emptyToNull(value: string): string | null {
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

/** A row as the form holds it, so an edit starts from what is stored. */
function toDraft(row: Row): CatalogEntryDraft {
  const service = row.serviceType

  return {
    ...blankEntry(service === null ? row.category.parentCategoryId : row.category.categoryId),
    code: row.code,
    name: row.name,
    nameTamil: (service === null ? row.category.nameTamil : service.nameTamil) ?? '',
    description: (service === null ? row.category.description : service.description) ?? '',
    displayOrder: Number(service === null ? row.category.displayOrder : service.displayOrder),
    branchIds: [...(service === null ? row.category.branchIds : service.branchIds)],
    featureFlagKey: row.category.featureFlagKey ?? '',
    measurementTemplateId: service?.measurementTemplateId ?? '',
    workflowDefinitionId: service?.workflowDefinitionId ?? '',
    designOptionGroupIds: (service?.designOptionGroupIds ?? []).join(', '),
    priceListItemCode: service?.priceListItemCode ?? '',
    qcChecklistTemplateId: service?.qcChecklistTemplateId ?? '',
    allowIncomplete: service?.allowIncomplete ?? false,
    expectedDurationDays: Number(service?.expectedDurationDays ?? 5),
    intakeWarning: service?.intakeWarning ?? '',
  }
}
