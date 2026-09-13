import { useMemo, useState } from 'react'
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
import { listBranches } from '../../admin/adminApi'
import { ADMIN_PERMISSIONS } from '../../admin/adminPermissions'
import { useAdminResource } from '../../admin/useAdminResource'
import { useCurrentUser } from '../../auth/useSession'
import {
  addCatalogDesignGroup,
  addCatalogDesignOption,
  addCatalogDesignRule,
  correctCatalogDesignGroupPresentation,
  correctCatalogDesignOptionPresentation,
  editCatalogDesignGroup,
  editCatalogDesignOption,
  editCatalogDesignRule,
  readCatalogVersion,
  removeCatalogDesignGroup,
  removeCatalogDesignOption,
  removeCatalogDesignRule,
  validateCatalogVersion,
} from '../../catalog/catalogApi'
import { findingsForDesignGroup, findingsForDesignRule } from '../../catalog/catalogTree'
import type { MoveDirection } from '../../catalog/designOrder'
import { planDesignGroupMove, planDesignOptionMove } from '../../catalog/designOrder'
import type {
  CatalogDesignGroup,
  CatalogDesignOption,
  CatalogDesignRule,
  CatalogValidationReport,
  DesignGroupRequest,
  DesignOptionRequest,
} from '../../catalog/types'
import type { DesignGroupDraft } from '../../catalog/designGroupEntry'
import { blankDesignGroupDraft } from '../../catalog/designGroupEntry'
import type { DesignOptionDraft } from '../../catalog/designOptionEntry'
import { blankDesignOptionDraft } from '../../catalog/designOptionEntry'
import type { DesignRuleFormDraft } from '../../catalog/designRuleEntry'
import { blankDesignRuleDraft } from '../../catalog/designRuleEntry'
import { DesignGroupForm } from './DesignGroupForm'
import type { DesignGroupPresentationDraft } from './DesignGroupPresentationForm'
import { DesignGroupPresentationForm } from './DesignGroupPresentationForm'
import { DesignOptionForm } from './DesignOptionForm'
import type { DesignOptionPresentationDraft } from './DesignOptionPresentationForm'
import { DesignOptionPresentationForm } from './DesignOptionPresentationForm'
import { DesignRuleForm } from './DesignRuleForm'
import { IllustrationPreview } from './IllustrationPreview'

type Editing =
  | {
      readonly kind: 'group'
      readonly existing: CatalogDesignGroup | null
      readonly draft: DesignGroupDraft
    }
  | {
      readonly kind: 'option'
      readonly groupId: string
      readonly existing: CatalogDesignOption | null
      readonly draft: DesignOptionDraft
    }
  | {
      readonly kind: 'rule'
      readonly existing: CatalogDesignRule | null
      readonly draft: DesignRuleFormDraft
    }

type Removing =
  | { readonly kind: 'group'; readonly item: CatalogDesignGroup }
  | { readonly kind: 'option'; readonly item: CatalogDesignOption }
  | { readonly kind: 'rule'; readonly item: CatalogDesignRule }

type Correcting =
  | {
      readonly kind: 'group'
      readonly item: CatalogDesignGroup
      readonly draft: DesignGroupPresentationDraft
    }
  | {
      readonly kind: 'option'
      readonly item: CatalogDesignOption
      readonly draft: DesignOptionPresentationDraft
    }

/** One row of the group/option table: a group, or an option under one. */
interface Row {
  readonly key: string
  readonly kind: 'group' | 'option'
  readonly depth: number
  readonly code: string
  readonly name: string
  readonly group: CatalogDesignGroup
  readonly option: CatalogDesignOption | null
}

function groupRequestFor(group: CatalogDesignGroup, displayOrder: number): DesignGroupRequest {
  return {
    code: group.code,
    name: group.name,
    nameTamil: group.nameTamil,
    selectionMode: group.selectionMode,
    required: group.required,
    displayOrder,
    activeFrom: group.activeFrom,
    activeTo: group.activeTo,
    branchIds: [...group.branchIds],
    reason: null,
  }
}

function optionRequestFor(option: CatalogDesignOption, displayOrder: number): DesignOptionRequest {
  return {
    code: option.code,
    name: option.name,
    nameTamil: option.nameTamil,
    helpText: option.helpText,
    illustrationKey: option.illustrationKey,
    illustrationAlt: option.illustrationAlt,
    priceListItemCode: option.priceListItemCode,
    timeImpactDays: option.timeImpactDays,
    displayOrder,
    active: option.active,
    reason: null,
  }
}

function draftFromGroup(group: CatalogDesignGroup): DesignGroupDraft {
  return {
    code: group.code,
    name: group.name,
    nameTamil: group.nameTamil ?? '',
    selectionMode: group.selectionMode,
    required: group.required,
    displayOrder: group.displayOrder,
    branchIds: [...group.branchIds],
    reason: '',
  }
}

function draftFromOption(option: CatalogDesignOption): DesignOptionDraft {
  return {
    code: option.code,
    name: option.name,
    nameTamil: option.nameTamil ?? '',
    helpText: option.helpText,
    illustrationKey: option.illustrationKey ?? '',
    illustrationAlt: option.illustrationAlt,
    priceListItemCode: option.priceListItemCode ?? '',
    timeImpactDays: option.timeImpactDays,
    displayOrder: option.displayOrder,
    active: option.active,
    reason: '',
  }
}

function draftFromRule(rule: CatalogDesignRule): DesignRuleFormDraft {
  return {
    type: rule.type,
    antecedent: {
      groupCode: rule.antecedent.groupCode,
      form: rule.antecedent.form,
      optionCodes: [...rule.antecedent.optionCodes],
    },
    hasConsequent: rule.consequent !== null,
    consequent:
      rule.consequent === null
        ? { groupCode: null, form: 'Always', optionCodes: [] }
        : {
            groupCode: rule.consequent.groupCode,
            form: rule.consequent.form,
            optionCodes: [...rule.consequent.optionCodes],
          },
    note: rule.note ?? '',
    why: rule.why ?? '',
    reason: '',
  }
}

function emptyToNull(value: string): string | null {
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

/**
 * Groups, options and rules on one category of a draft, and their words on a published one (#141).
 *
 * ## Why this is its own address rather than a section of the version screen
 *
 * A category's own groups and rules are a screen's worth of controls the moment there are more than
 * a handful of options — the picker, the rule editor and the validation report each deserve the room
 * `CatalogVersionEditorRoute`'s single tree table already fills with categories and service types.
 * Splitting it here is the same reasoning the measurement templates give their own version editor its
 * own address (#102): a working set survives a reload and can be shared with a colleague.
 *
 * ## Why groups and options share one table
 *
 * The same reason the version screen puts categories and service types in one: an option's identity
 * is its group, the way a service type's is its category, and a nested list of options under each
 * group restates five facts per option that a table states once.
 */
export function CatalogDesignRoute() {
  const intl = useIntl()
  const { versionId, categoryId } = useParams()
  const { permissions } = useCurrentUser()

  const canPublish = permissions.includes(ADMIN_PERMISSIONS.catalogPublish)

  const catalogue = useAdminResource(`catalog-version:${versionId ?? ''}`, (signal) =>
    readCatalogVersion(versionId ?? '', signal),
  )
  const branches = useAdminResource('branches', (signal) => listBranches(signal))

  const [editing, setEditing] = useState<Editing | null>(null)
  const [removing, setRemoving] = useState<Removing | null>(null)
  const [correcting, setCorrecting] = useState<Correcting | null>(null)
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

  const category =
    value?.categories.find((candidate) => candidate.categoryId === categoryId) ?? null

  const groups = useMemo<readonly CatalogDesignGroup[]>(
    () =>
      value === null || category === null
        ? []
        : [...value.designGroups.filter((group) => group.categoryId === category.categoryId)].sort(
            (left, right) =>
              left.displayOrder - right.displayOrder || left.code.localeCompare(right.code, 'en'),
          ),
    [value, category],
  )

  const rules = useMemo<readonly CatalogDesignRule[]>(
    () =>
      value === null || category === null
        ? []
        : [...value.designRules.filter((rule) => rule.categoryId === category.categoryId)].sort(
            (left, right) => left.number - right.number,
          ),
    [value, category],
  )

  const rows = useMemo<readonly Row[]>(
    () =>
      groups.flatMap((group) => [
        {
          key: `group:${group.designOptionGroupId}`,
          kind: 'group' as const,
          depth: 0,
          code: group.code,
          name: group.name,
          group,
          option: null,
        },
        ...group.options.map((option) => ({
          key: `option:${option.designOptionId}`,
          kind: 'option' as const,
          depth: 1,
          code: option.code,
          name: option.name,
          group,
          option,
        })),
      ]),
    [groups],
  )

  const send = async (
    id: string,
    run: (input: { readonly version: string; readonly idempotencyKey: string }) => Promise<{
      readonly version: string | undefined
    }>,
    done: string,
  ): Promise<void> => {
    if (precondition === undefined) {
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

  const moveGroup = async (group: CatalogDesignGroup, direction: MoveDirection): Promise<void> => {
    const plan = planDesignGroupMove(groups, group.designOptionGroupId, direction)

    if (plan.length === 0) {
      setNotice(
        intl.formatMessage({
          id: direction === 'up' ? 'catalog.design.order.atStart' : 'catalog.design.order.atEnd',
        }),
      )
      return
    }

    if (versionId === undefined) {
      return
    }

    let cursor = precondition
    setBusy(true)
    setFailure(null)
    setNotice(null)

    try {
      for (const write of plan) {
        if (cursor === undefined) {
          throw new ApiError('The catalogue must be read again.', { status: 409 })
        }
        const id = `move-group:${write.group.designOptionGroupId}`
        const result = await editCatalogDesignGroup({
          versionId,
          designOptionGroupId: write.group.designOptionGroupId,
          group: groupRequestFor(write.group, write.displayOrder),
          version: cursor,
          idempotencyKey: keyFor(id),
        })
        forget(id)
        cursor = result.version
      }
      setHeld(cursor)
      setStaleReport(validation !== null)
      catalogue.reload()
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setBusy(false)
    }
  }

  const moveOption = async (
    group: CatalogDesignGroup,
    option: CatalogDesignOption,
    direction: MoveDirection,
  ): Promise<void> => {
    const plan = planDesignOptionMove(group.options, option.designOptionId, direction)

    if (plan.length === 0) {
      setNotice(
        intl.formatMessage({
          id: direction === 'up' ? 'catalog.design.order.atStart' : 'catalog.design.order.atEnd',
        }),
      )
      return
    }

    if (versionId === undefined) {
      return
    }

    let cursor = precondition
    setBusy(true)
    setFailure(null)
    setNotice(null)

    try {
      for (const write of plan) {
        if (cursor === undefined) {
          throw new ApiError('The catalogue must be read again.', { status: 409 })
        }
        const id = `move-option:${write.option.designOptionId}`
        const result = await editCatalogDesignOption({
          versionId,
          designOptionId: write.option.designOptionId,
          option: optionRequestFor(write.option, write.displayOrder),
          version: cursor,
          idempotencyKey: keyFor(id),
        })
        forget(id)
        cursor = result.version
      }
      setHeld(cursor)
      setStaleReport(validation !== null)
      catalogue.reload()
    } catch (cause: unknown) {
      setFailure(cause)
    } finally {
      setBusy(false)
    }
  }

  const saveGroup = (): void => {
    if (versionId === undefined || category === null || editing?.kind !== 'group') {
      return
    }
    const { draft, existing } = editing
    const body: DesignGroupRequest = {
      code: draft.code.trim(),
      name: draft.name.trim(),
      nameTamil: emptyToNull(draft.nameTamil),
      selectionMode: draft.selectionMode,
      required: draft.required,
      displayOrder: draft.displayOrder,
      activeFrom: null,
      activeTo: null,
      branchIds: [...draft.branchIds],
      reason: emptyToNull(draft.reason),
    }

    void send(
      existing === null ? `add-group:${body.code}` : `edit-group:${existing.designOptionGroupId}`,
      ({ version, idempotencyKey }) =>
        existing === null
          ? addCatalogDesignGroup({
              versionId,
              categoryId: category.categoryId,
              group: body,
              version,
              idempotencyKey,
            })
          : editCatalogDesignGroup({
              versionId,
              designOptionGroupId: existing.designOptionGroupId,
              group: body,
              version,
              idempotencyKey,
            }),
      intl.formatMessage({ id: 'catalog.saved' }, { name: body.name ?? '' }),
    )
  }

  const saveOption = (): void => {
    if (versionId === undefined || editing?.kind !== 'option') {
      return
    }
    const { draft, existing, groupId } = editing
    const body: DesignOptionRequest = {
      code: draft.code.trim(),
      name: draft.name.trim(),
      nameTamil: emptyToNull(draft.nameTamil),
      helpText: draft.helpText.trim(),
      illustrationKey: emptyToNull(draft.illustrationKey),
      illustrationAlt: draft.illustrationAlt.trim(),
      priceListItemCode: emptyToNull(draft.priceListItemCode),
      timeImpactDays: draft.timeImpactDays,
      displayOrder: draft.displayOrder,
      active: draft.active,
      reason: emptyToNull(draft.reason),
    }

    void send(
      existing === null
        ? `add-option:${groupId}:${body.code}`
        : `edit-option:${existing.designOptionId}`,
      ({ version, idempotencyKey }) =>
        existing === null
          ? addCatalogDesignOption({
              versionId,
              designOptionGroupId: groupId,
              option: body,
              version,
              idempotencyKey,
            })
          : editCatalogDesignOption({
              versionId,
              designOptionId: existing.designOptionId,
              option: body,
              version,
              idempotencyKey,
            }),
      intl.formatMessage({ id: 'catalog.saved' }, { name: body.name ?? '' }),
    )
  }

  const saveRule = (): void => {
    if (versionId === undefined || category === null || editing?.kind !== 'rule') {
      return
    }
    const { draft, existing } = editing
    const body = {
      type: draft.type,
      antecedent: {
        groupCode: draft.antecedent.groupCode,
        form: draft.antecedent.form,
        optionCodes: [...draft.antecedent.optionCodes],
      },
      consequent: draft.hasConsequent
        ? {
            groupCode: draft.consequent.groupCode,
            form: draft.consequent.form,
            optionCodes: [...draft.consequent.optionCodes],
          }
        : null,
      note: emptyToNull(draft.note),
      why: emptyToNull(draft.why),
      reason: emptyToNull(draft.reason),
    }

    void send(
      existing === null ? `add-rule:${category.categoryId}` : `edit-rule:${existing.designRuleId}`,
      ({ version, idempotencyKey }) =>
        existing === null
          ? addCatalogDesignRule({
              versionId,
              categoryId: category.categoryId,
              rule: body,
              version,
              idempotencyKey,
            })
          : editCatalogDesignRule({
              versionId,
              designRuleId: existing.designRuleId,
              rule: body,
              version,
              idempotencyKey,
            }),
      intl.formatMessage({ id: 'catalog.design.rule.saved' }),
    )
  }

  const correct = (): void => {
    if (versionId === undefined || correcting === null) {
      return
    }

    if (correcting.kind === 'group') {
      const { item, draft } = correcting
      void send(
        `correct-group:${item.designOptionGroupId}`,
        ({ version, idempotencyKey }) =>
          correctCatalogDesignGroupPresentation({
            versionId,
            designOptionGroupId: item.designOptionGroupId,
            presentation: {
              name: draft.name.trim(),
              nameTamil: emptyToNull(draft.nameTamil),
              displayOrder: draft.displayOrder,
              reason: draft.reason.trim(),
            },
            version,
            idempotencyKey,
          }),
        intl.formatMessage({ id: 'catalog.corrected' }, { name: item.name }),
      )
      return
    }

    const { item, draft } = correcting
    void send(
      `correct-option:${item.designOptionId}`,
      ({ version, idempotencyKey }) =>
        correctCatalogDesignOptionPresentation({
          versionId,
          designOptionId: item.designOptionId,
          presentation: {
            name: draft.name.trim(),
            nameTamil: emptyToNull(draft.nameTamil),
            helpText: draft.helpText.trim(),
            illustrationAlt: draft.illustrationAlt.trim(),
            displayOrder: draft.displayOrder,
            reason: draft.reason.trim(),
          },
          version,
          idempotencyKey,
        }),
      intl.formatMessage({ id: 'catalog.corrected' }, { name: item.name }),
    )
  }

  const remove = (outcome: ConfirmOutcome): void => {
    if (versionId === undefined || removing === null) {
      return
    }
    const reason = outcome.reason?.trim() ?? ''
    const reasonOrNull = reason === '' ? null : reason

    if (removing.kind === 'group') {
      const { item } = removing
      void send(
        `remove-group:${item.designOptionGroupId}`,
        ({ version, idempotencyKey }) =>
          removeCatalogDesignGroup({
            versionId,
            designOptionGroupId: item.designOptionGroupId,
            reason: reasonOrNull,
            version,
            idempotencyKey,
          }),
        intl.formatMessage({ id: 'catalog.removed' }, { name: item.name }),
      )
      return
    }

    if (removing.kind === 'option') {
      const { item } = removing
      void send(
        `remove-option:${item.designOptionId}`,
        ({ version, idempotencyKey }) =>
          removeCatalogDesignOption({
            versionId,
            designOptionId: item.designOptionId,
            reason: reasonOrNull,
            version,
            idempotencyKey,
          }),
        intl.formatMessage({ id: 'catalog.removed' }, { name: item.name }),
      )
      return
    }

    const { item } = removing
    void send(
      `remove-rule:${item.designRuleId}`,
      ({ version, idempotencyKey }) =>
        removeCatalogDesignRule({
          versionId,
          designRuleId: item.designRuleId,
          reason: reasonOrNull,
          version,
          idempotencyKey,
        }),
      intl.formatMessage({ id: 'catalog.design.rule.removed' }, { identifier: item.identifier }),
    )
  }

  const conflict = failure instanceof ApiError && failure.status === 409
  const dialogOpen = removing !== null

  if (catalogue.loading) {
    return (
      <section>
        <LoadingState what={intl.formatMessage({ id: 'catalog.design.loading' })} />
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

  if (category === null) {
    return (
      <section>
        <Alert live="polite" tone="warning">
          <FormattedMessage id="catalog.design.categoryNotFound" />
        </Alert>
      </section>
    )
  }

  const isDraft = value.version.status === 'Draft'
  const isPublished = value.version.status === 'Published'
  const findings = validation?.findings ?? []

  const findingsOnGroup = (group: CatalogDesignGroup): readonly string[] =>
    findingsForDesignGroup(findings, category.code, group.code).map((finding) => finding.message)

  const findingsOnRule = (rule: CatalogDesignRule): readonly string[] =>
    findingsForDesignRule(findings, rule.identifier).map((finding) => finding.message)

  return (
    <section>
      <p>
        <Link to={`/admin/catalog/${versionId ?? ''}`}>
          <FormattedMessage id="admin.back" />
        </Link>
      </p>

      <h2>{intl.formatMessage({ id: 'catalog.design.title' }, { name: category.name })}</h2>

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

      {isPublished ? (
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
            id={isPublished ? 'catalog.needsPublish.correction' : 'catalog.needsPublish'}
          />
        </Alert>
      )}

      {rows.length === 0 ? (
        <EmptyState iconName="list" live="polite">
          {intl.formatMessage({ id: 'catalog.design.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'catalog.design.caption' }, { name: category.name })}
          columns={[
            {
              id: 'code',
              header: intl.formatMessage({ id: 'catalog.column.code' }),
              primary: true,
              cell: (row: Row) => `${'— '.repeat(row.depth)}${row.code}`,
            },
            {
              id: 'name',
              header: intl.formatMessage({ id: 'catalog.column.name' }),
              cell: (row: Row) => row.name,
            },
            {
              id: 'detail',
              header: intl.formatMessage({ id: 'catalog.design.column.detail' }),
              cell: (row: Row) =>
                row.option === null
                  ? intl.formatMessage(
                      { id: `catalog.design.group.selectionMode.${row.group.selectionMode}` },
                      {},
                    )
                  : row.option.active
                    ? intl.formatMessage({ id: 'catalog.design.option.active' })
                    : intl.formatMessage({ id: 'catalog.design.option.retired' }),
            },
            {
              id: 'illustration',
              header: intl.formatMessage({ id: 'catalog.design.column.illustration' }),
              cell: (row: Row) =>
                row.option === null ? (
                  ''
                ) : (
                  <IllustrationPreview
                    alt={row.option.illustrationAlt}
                    illustrationKey={row.option.illustrationKey}
                  />
                ),
            },
            {
              id: 'findings',
              header: intl.formatMessage({ id: 'catalog.column.findings' }),
              cell: (row: Row) => (row.option === null ? findingsOnGroup(row.group).join(' ') : ''),
            },
          ]}
          rowActions={(row: Row) => {
            if (row.option === null) {
              const group = row.group
              return (
                <>
                  {isDraft ? (
                    <>
                      <Button
                        onClick={() => {
                          void moveGroup(group, 'up')
                        }}
                        variant="secondary"
                      >
                        {intl.formatMessage(
                          { id: 'catalog.design.order.up' },
                          { label: group.name },
                        )}
                      </Button>
                      <Button
                        onClick={() => {
                          void moveGroup(group, 'down')
                        }}
                        variant="secondary"
                      >
                        {intl.formatMessage(
                          { id: 'catalog.design.order.down' },
                          { label: group.name },
                        )}
                      </Button>
                      <Button
                        onClick={() => {
                          setFailure(null)
                          setEditing({
                            kind: 'option',
                            groupId: group.designOptionGroupId,
                            existing: null,
                            draft: blankDesignOptionDraft(),
                          })
                        }}
                        variant="secondary"
                      >
                        {intl.formatMessage(
                          { id: 'catalog.design.option.add' },
                          { name: group.name },
                        )}
                      </Button>
                      <Button
                        onClick={() => {
                          setFailure(null)
                          setEditing({
                            kind: 'group',
                            existing: group,
                            draft: draftFromGroup(group),
                          })
                        }}
                        variant="secondary"
                      >
                        {intl.formatMessage(
                          { id: 'catalog.design.group.edit' },
                          { name: group.name },
                        )}
                      </Button>
                      <Button
                        onClick={() => {
                          setFailure(null)
                          setRemoving({ kind: 'group', item: group })
                        }}
                        variant="danger"
                      >
                        {intl.formatMessage(
                          { id: 'catalog.design.group.remove' },
                          { name: group.name },
                        )}
                      </Button>
                    </>
                  ) : isPublished && canPublish ? (
                    <Button
                      onClick={() => {
                        setFailure(null)
                        setCorrecting({
                          kind: 'group',
                          item: group,
                          draft: {
                            name: group.name,
                            nameTamil: group.nameTamil ?? '',
                            displayOrder: group.displayOrder,
                            reason: '',
                          },
                        })
                      }}
                      variant="secondary"
                    >
                      {intl.formatMessage({ id: 'catalog.correct' }, { name: group.name })}
                    </Button>
                  ) : null}
                </>
              )
            }

            const option = row.option
            return isDraft ? (
              <>
                <Button
                  onClick={() => {
                    void moveOption(row.group, option, 'up')
                  }}
                  variant="secondary"
                >
                  {intl.formatMessage({ id: 'catalog.design.order.up' }, { label: option.name })}
                </Button>
                <Button
                  onClick={() => {
                    void moveOption(row.group, option, 'down')
                  }}
                  variant="secondary"
                >
                  {intl.formatMessage({ id: 'catalog.design.order.down' }, { label: option.name })}
                </Button>
                <Button
                  onClick={() => {
                    setFailure(null)
                    setEditing({
                      kind: 'option',
                      groupId: row.group.designOptionGroupId,
                      existing: option,
                      draft: draftFromOption(option),
                    })
                  }}
                  variant="secondary"
                >
                  {intl.formatMessage({ id: 'catalog.design.option.edit' }, { name: option.name })}
                </Button>
                <Button
                  onClick={() => {
                    setFailure(null)
                    setRemoving({ kind: 'option', item: option })
                  }}
                  variant="danger"
                >
                  {intl.formatMessage(
                    { id: 'catalog.design.option.remove' },
                    { name: option.name },
                  )}
                </Button>
              </>
            ) : isPublished && canPublish ? (
              <Button
                onClick={() => {
                  setFailure(null)
                  setCorrecting({
                    kind: 'option',
                    item: option,
                    draft: {
                      name: option.name,
                      nameTamil: option.nameTamil ?? '',
                      helpText: option.helpText,
                      illustrationAlt: option.illustrationAlt,
                      displayOrder: option.displayOrder,
                      reason: '',
                    },
                  })
                }}
                variant="secondary"
              >
                {intl.formatMessage({ id: 'catalog.correct' }, { name: option.name })}
              </Button>
            ) : null
          }}
          rowKey={(row) => row.key}
          rowLabel={(row) => row.name}
          rows={rows}
        />
      )}

      {isDraft ? (
        <Button
          onClick={() => {
            setFailure(null)
            setEditing({ kind: 'group', existing: null, draft: blankDesignGroupDraft() })
          }}
          variant="primary"
        >
          <FormattedMessage id="catalog.design.group.add" />
        </Button>
      ) : null}

      <h3>{intl.formatMessage({ id: 'catalog.design.rules.title' })}</h3>

      {rules.length === 0 ? (
        <EmptyState iconName="list" live="polite">
          {intl.formatMessage({ id: 'catalog.design.rules.empty' })}
        </EmptyState>
      ) : (
        <DataTable
          caption={intl.formatMessage(
            { id: 'catalog.design.rules.caption' },
            { name: category.name },
          )}
          columns={[
            {
              id: 'identifier',
              header: intl.formatMessage({ id: 'catalog.design.rule.identifier' }),
              primary: true,
              cell: (rule: CatalogDesignRule) => rule.identifier,
            },
            {
              id: 'type',
              header: intl.formatMessage({ id: 'catalog.design.rule.type' }),
              cell: (rule: CatalogDesignRule) =>
                intl.formatMessage({ id: `catalog.design.rule.type.${rule.type}` }),
            },
            {
              id: 'statement',
              header: intl.formatMessage({ id: 'catalog.design.rule.statement' }),
              cell: (rule: CatalogDesignRule) => rule.statement,
            },
            {
              id: 'findings',
              header: intl.formatMessage({ id: 'catalog.column.findings' }),
              cell: (rule: CatalogDesignRule) => findingsOnRule(rule).join(' '),
            },
          ]}
          rowActions={(rule: CatalogDesignRule) =>
            isDraft ? (
              <>
                <Button
                  onClick={() => {
                    setFailure(null)
                    setEditing({ kind: 'rule', existing: rule, draft: draftFromRule(rule) })
                  }}
                  variant="secondary"
                >
                  {intl.formatMessage(
                    { id: 'catalog.design.rule.edit' },
                    { identifier: rule.identifier },
                  )}
                </Button>
                <Button
                  onClick={() => {
                    setFailure(null)
                    setRemoving({ kind: 'rule', item: rule })
                  }}
                  variant="danger"
                >
                  {intl.formatMessage(
                    { id: 'catalog.design.rule.remove' },
                    { identifier: rule.identifier },
                  )}
                </Button>
              </>
            ) : null
          }
          rowKey={(rule) => rule.designRuleId}
          rowLabel={(rule) => rule.identifier}
          rows={rules}
        />
      )}

      {isDraft ? (
        <Button
          onClick={() => {
            setFailure(null)
            setEditing({ kind: 'rule', existing: null, draft: blankDesignRuleDraft() })
          }}
          variant="primary"
        >
          <FormattedMessage id="catalog.design.rule.add" />
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
            </Alert>
          )}
        </section>
      )}

      {editing?.kind === 'group' ? (
        <DesignGroupForm
          branches={(branches.value ?? []).map((branch) => ({
            branchId: branch.branchId,
            name: branch.name,
          }))}
          busy={busy}
          controlId={(name) => `catalog-design-${name}`}
          draft={editing.draft}
          existingCode={editing.existing?.code ?? null}
          onCancel={() => {
            setEditing(null)
          }}
          onChange={(draft) => {
            setEditing({ ...editing, draft })
          }}
          onSubmit={saveGroup}
        />
      ) : null}

      {editing?.kind === 'option' ? (
        <DesignOptionForm
          busy={busy}
          controlId={(name) => `catalog-design-${name}`}
          draft={editing.draft}
          existingCode={editing.existing?.code ?? null}
          onCancel={() => {
            setEditing(null)
          }}
          onChange={(draft) => {
            setEditing({ ...editing, draft })
          }}
          onSubmit={saveOption}
        />
      ) : null}

      {editing?.kind === 'rule' ? (
        <DesignRuleForm
          busy={busy}
          controlId={(name) => `catalog-design-${name}`}
          draft={editing.draft}
          groups={groups}
          onCancel={() => {
            setEditing(null)
          }}
          onChange={(draft) => {
            setEditing({ ...editing, draft })
          }}
          onSubmit={saveRule}
        />
      ) : null}

      {correcting?.kind === 'group' ? (
        <DesignGroupPresentationForm
          busy={busy}
          controlId={(name) => `catalog-design-${name}`}
          draft={correcting.draft}
          onCancel={() => {
            setCorrecting(null)
          }}
          onChange={(draft) => {
            setCorrecting({ ...correcting, draft })
          }}
          onSubmit={correct}
          subject={correcting.item.name}
        />
      ) : null}

      {correcting?.kind === 'option' ? (
        <DesignOptionPresentationForm
          busy={busy}
          controlId={(name) => `catalog-design-${name}`}
          draft={correcting.draft}
          illustrationKey={correcting.item.illustrationKey}
          onCancel={() => {
            setCorrecting(null)
          }}
          onChange={(draft) => {
            setCorrecting({ ...correcting, draft })
          }}
          onSubmit={correct}
          subject={correcting.item.name}
        />
      ) : null}

      {removing === null ? null : (
        <ConfirmDialog
          action={intl.formatMessage(
            { id: 'catalog.design.remove.action' },
            { name: removing.kind === 'rule' ? removing.item.identifier : removing.item.name },
          )}
          busy={busy}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          confirmLabel={intl.formatMessage(
            { id: 'catalog.design.remove.action' },
            { name: removing.kind === 'rule' ? removing.item.identifier : removing.item.name },
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
    </section>
  )
}
