import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useParams } from 'react-router'
import { listBranches } from '../../admin/adminApi'
import { useAdminResource } from '../../admin/useAdminResource'
import { ApiError } from '../../auth/apiClient'
import type { VersionedResponse } from '../../auth/apiClient'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import {
  billingProblemCode,
  billingProblemField,
  billingProblemMessage,
} from '../../billing/billingProblems'
import {
  addPriceListItem,
  describePriceListVersion,
  editPriceListItem,
  readPriceListVersion,
  removePriceListItem,
} from '../../billing/priceListApi'
import {
  listTaxConfigurationVersions,
  readTaxConfigurationVersion,
} from '../../billing/pricingConfigApi'
import type {
  DiscountRule,
  PriceListItem,
  PriceListVersionRequest,
} from '../../billing/priceListTypes'
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
import { getFormatters } from '../../i18n/formatters'
import { parseDecimalString } from '../../i18n/parseNumber'
import type { MessageKey } from '../../i18n/en-IN'
import { PriceListItemForm } from './PriceListItemForm'
import { PriceListVersionForm } from './PriceListVersionForm'
import {
  blankPriceListItemDraft,
  draftFromPriceListItem,
  priceListItemRequestFrom,
} from './priceListItemDraft'
import type { PriceListItemDraft } from './priceListItemDraft'
import { priceListVersionDraftForEditing } from './priceListVersionDraft'
import type { PriceListVersionDraft } from './priceListVersionDraft'

const VERSION_CHANGED_CODE = 'billing.version-changed'

const KIND_KEY: Readonly<Record<string, MessageKey>> = {
  Service: 'pricing.priceListItem.form.kind.Service',
  Surcharge: 'pricing.priceListItem.form.kind.Surcharge',
  Material: 'pricing.priceListItem.form.kind.Material',
}

const ROUND_OFF: Readonly<Record<string, MessageKey>> = {
  None: 'pricing.priceList.roundOff.none',
  NearestRupee: 'pricing.priceList.roundOff.nearestRupee',
}

/** The published tax configuration's active codes, read once for the item form's picker. */
interface PublishedTaxCodes {
  readonly available: boolean
  readonly codes: readonly string[]
}

/**
 * A field the client itself refuses to leave unanswered, phrased as the same `ApiError` shape a
 * server refusal would be — so it renders through the identical field-error path as one, the way
 * `TaxConfigurationEditorRoute.tsx` does for its own `kind` and `active` radio groups.
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

/**
 * One price-list version: its own conventions, its items, and its discount rules (E09-F01-7).
 *
 * ## Why the conventions form is reused rather than rebuilt inline
 *
 * `PriceListVersionForm.tsx` is what E09-F01-6 built to start a draft, and every convention it asks
 * for is exactly what `DescribePriceListVersion` demands whole-value. Rebuilding those seven fields a
 * second time here would be the surface this screen has to keep in step with itself; `mode: 'edit'`
 * is the one difference the two callers actually have.
 *
 * ## Why there is no validate control and no publish control here
 *
 * Checking a version and publishing it are E09-F01-7b's, which also owns the shared findings list
 * `BillingFindingsList.tsx`. A second rendering of severity here would be the duplication that whole
 * arrangement exists to avoid, so the screen says in one sentence that both arrive next.
 *
 * ## Why a published or retired version offers no item or conventions control, but still shows its
 * discount rules
 *
 * The server refuses every write against one (`billing.version-not-editable`): a published version is
 * what every invoice since was calculated on, and changing it would change what an order already
 * priced *meant*. The screen therefore offers no control that would end in that refusal — only the
 * sentence that says where the change is actually made, a draft cloned from it on the versions screen.
 * Its discount rules stay visible regardless, because a rate an administrator cannot see is worse than
 * one they cannot yet change; E09-F01-8 adds the editor for them.
 *
 * ## Why the tax code stays a typed field even when nothing is published
 *
 * An item's tax code is checked when the version is *published*
 * (`PriceListPublicationCheck`), not when the item is saved — so a shop that writes its price list
 * before it publishes a tax configuration must not be blocked here. See `PriceListItemForm.tsx`.
 */
export function PriceListVersionEditorRoute() {
  const intl = useIntl()
  const formatters = getFormatters()
  const { versionId } = useParams()
  const network = useNetworkState()

  const data = useAdminResource(`price-list-version:${versionId ?? ''}`, (signal) =>
    readPriceListVersion(versionId ?? '', signal),
  )
  const branches = useAdminResource('branches-for-price-list-item', (signal) =>
    listBranches(signal),
  )
  const publishedTaxCodes = useAdminResource<PublishedTaxCodes>(
    'published-tax-codes-for-price-list-item',
    async (signal) => {
      const versions = await listTaxConfigurationVersions(signal)
      const published = versions.find((version) => version.status === 'Published')
      if (published === undefined) {
        return { available: false, codes: [] }
      }
      const read = await readTaxConfigurationVersion(published.taxConfigurationVersionId, signal)
      return {
        available: true,
        codes: read.value.taxCodes.filter((code) => code.active).map((code) => code.code),
      }
    },
  )

  const [held, setHeld] = useState<string | undefined>(undefined)
  const [editingVersion, setEditingVersion] = useState<PriceListVersionDraft | null>(null)
  const [editingItem, setEditingItem] = useState<{
    readonly existing: PriceListItem | null
    readonly draft: PriceListItemDraft
  } | null>(null)
  const [removingItem, setRemovingItem] = useState<PriceListItem | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [keys, setKeys] = useState<Readonly<Record<string, string>>>({})

  const value = data.value?.value ?? null
  const precondition = held ?? data.value?.version

  const branchNamesAvailable = branches.failure === null
  const branchOptions = (branches.value ?? []).map((branch) => ({
    value: branch.branchId,
    label: `${branch.name} (${branch.code})`,
  }))
  const branchName = (branchId: string): string | undefined =>
    branches.value?.find((branch) => branch.branchId === branchId)?.name

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
      setFailure(new ApiError('The version must be read again.', { status: 409 }))
      return
    }

    setBusy(true)
    setFailure(null)
    setNotice(null)

    try {
      const result = await run({ version: precondition, idempotencyKey: keyFor(id) })
      forget(id)
      // The version's own tag, whichever child moved it — including a 204 item removal, whose body
      // carries nothing but whose tag is still what the next write must present.
      setHeld(result.version)
      setEditingVersion(null)
      setEditingItem(null)
      setRemovingItem(null)
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

  const openEditVersion = (): void => {
    if (value === null) {
      return
    }
    setFailure(null)
    setEditingItem(null)
    setRemovingItem(null)
    setEditingVersion(priceListVersionDraftForEditing(value.version))
  }

  const saveVersion = (): void => {
    if (versionId === undefined || editingVersion === null) {
      return
    }
    const draft = editingVersion

    const body: PriceListVersionRequest = {
      name: draft.name.trim() === '' ? null : draft.name.trim(),
      notes: draft.notes.trim() === '' ? null : draft.notes.trim(),
      effectiveFrom: draft.effectiveFrom === '' ? null : draft.effectiveFrom,
      taxInclusive: draft.taxInclusive === '' ? null : draft.taxInclusive === 'true',
      roundOff: draft.roundOff === '' ? null : draft.roundOff,
      overrideThresholdPercent: parseDecimalString(draft.overrideThresholdPercent),
      branchIds: draft.branchIdsTouched ? draft.branchIds : null,
      cloneFromVersionId: null,
      reason: draft.reason.trim() === '' ? null : draft.reason.trim(),
      saysTaxInclusive: draft.taxInclusive !== '',
      saysBranchIds: draft.branchIdsTouched,
    }

    void send(
      'describe-version',
      ({ version, idempotencyKey }) =>
        describePriceListVersion({ versionId, body, version, idempotencyKey }),
      intl.formatMessage({ id: 'pricing.priceList.editor.saved' }),
    )
  }

  const openAddItem = (): void => {
    setFailure(null)
    setEditingVersion(null)
    setRemovingItem(null)
    setEditingItem({ existing: null, draft: blankPriceListItemDraft() })
  }

  const openEditItem = (item: PriceListItem): void => {
    setFailure(null)
    setEditingVersion(null)
    setRemovingItem(null)
    setEditingItem({ existing: item, draft: draftFromPriceListItem(item) })
  }

  const saveItem = (): void => {
    if (versionId === undefined || editingItem === null) {
      return
    }
    const { draft, existing } = editingItem

    if (draft.kind === '') {
      setFailure(requiredFieldFailure('kind'))
      return
    }
    if (draft.active === '') {
      setFailure(requiredFieldFailure('active'))
      return
    }

    const body = priceListItemRequestFrom(draft)
    const id =
      existing === null ? `add-item:${body.code ?? ''}` : `edit-item:${existing.priceListItemId}`

    void send(
      id,
      ({ version, idempotencyKey }) =>
        existing === null
          ? addPriceListItem({ versionId, item: body, version, idempotencyKey })
          : editPriceListItem({
              versionId,
              itemId: existing.priceListItemId,
              item: body,
              version,
              idempotencyKey,
            }),
      intl.formatMessage({ id: 'pricing.priceListItem.saved' }, { code: body.code ?? '' }),
    )
  }

  const openRemoveItem = (item: PriceListItem): void => {
    setFailure(null)
    setEditingVersion(null)
    setEditingItem(null)
    setRemovingItem(item)
  }

  const removeItem = (outcome: ConfirmOutcome): void => {
    if (versionId === undefined || removingItem === null) {
      return
    }
    const item = removingItem
    const reason = outcome.reason?.trim() ?? ''

    void send(
      `remove-item:${item.priceListItemId}`,
      ({ version, idempotencyKey }) =>
        removePriceListItem({
          versionId,
          itemId: item.priceListItemId,
          reason: reason === '' ? null : reason,
          version,
          idempotencyKey,
        }),
      intl.formatMessage({ id: 'pricing.priceListItem.removed' }, { code: item.code }),
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

  const versionOpen = editingVersion !== null
  const nameError = fieldSentence(versionOpen, 'name')
  const notesError = fieldSentence(versionOpen, 'notes')
  const effectiveFromError = fieldSentence(versionOpen, 'effectiveFrom')
  const taxInclusiveError = fieldSentence(versionOpen, 'taxInclusive')
  const roundOffError = fieldSentence(versionOpen, 'roundOff')
  const overrideThresholdPercentError = fieldSentence(versionOpen, 'overrideThresholdPercent')
  const branchIdsError = fieldSentence(versionOpen, 'branchIds')
  const versionReasonError = fieldSentence(versionOpen, 'reason')
  const versionFieldMatched =
    nameError !== undefined ||
    notesError !== undefined ||
    effectiveFromError !== undefined ||
    taxInclusiveError !== undefined ||
    roundOffError !== undefined ||
    overrideThresholdPercentError !== undefined ||
    branchIdsError !== undefined ||
    versionReasonError !== undefined

  const itemOpen = editingItem !== null
  const codeError = fieldSentence(itemOpen, 'code')
  const descriptionError = fieldSentence(itemOpen, 'description')
  const kindError = fieldSentence(itemOpen, 'kind')
  const baseRateError = fieldSentence(itemOpen, 'baseRate')
  const unitError = fieldSentence(itemOpen, 'unit')
  const taxCodeError = fieldSentence(itemOpen, 'taxCode')
  const activeError = fieldSentence(itemOpen, 'active')
  const itemReasonError = fieldSentence(itemOpen, 'reason')
  const itemFieldMatched =
    codeError !== undefined ||
    descriptionError !== undefined ||
    kindError !== undefined ||
    baseRateError !== undefined ||
    unitError !== undefined ||
    taxCodeError !== undefined ||
    activeError !== undefined ||
    itemReasonError !== undefined

  if (data.loading) {
    return (
      <section>
        <LoadingState what={intl.formatMessage({ id: 'pricing.priceList.editor.loading' })} />
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
  const taxCodesResource = publishedTaxCodes.value
  const taxConfigurationAvailable = taxCodesResource?.available ?? false
  const taxCodeOptions = taxCodesResource?.codes ?? []

  return (
    <section>
      <p>
        <Link to={`/admin/price-lists/${value.version.priceListId}`}>
          <FormattedMessage id="admin.back" />
        </Link>
      </p>

      <h2>
        {intl.formatMessage(
          { id: 'pricing.priceList.editor.heading' },
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
          <FormattedMessage id="pricing.priceList.editor.readOnly" />
        </Alert>
      ) : value.version.status === 'Retired' ? (
        <Alert live="polite" tone="info">
          <FormattedMessage id="pricing.priceList.editor.retired" />
        </Alert>
      ) : null}

      <section>
        <h3>{intl.formatMessage({ id: 'pricing.priceList.editor.conventionsTitle' })}</h3>
        <dl>
          <dt>{intl.formatMessage({ id: 'pricing.priceList.version.form.name' })}</dt>
          <dd>{value.version.name}</dd>
          <dt>{intl.formatMessage({ id: 'pricing.priceList.version.form.notes' })}</dt>
          <dd>{value.version.notes ?? ''}</dd>
          <dt>{intl.formatMessage({ id: 'pricing.priceList.version.form.effectiveFrom' })}</dt>
          <dd>{formatters.formatShortDate(value.version.effectiveFrom)}</dd>
          <dt>{intl.formatMessage({ id: 'pricing.priceList.version.form.tax' })}</dt>
          <dd>
            {intl.formatMessage({
              id: value.version.taxInclusive
                ? 'pricing.priceList.version.tax.inclusive'
                : 'pricing.priceList.version.tax.exclusive',
            })}
          </dd>
          <dt>{intl.formatMessage({ id: 'pricing.priceList.version.form.roundOff' })}</dt>
          <dd>
            {intl.formatMessage({
              id: ROUND_OFF[value.version.roundOff] ?? 'pricing.priceList.roundOff.none',
            })}
          </dd>
          <dt>{intl.formatMessage({ id: 'pricing.priceList.version.form.threshold' })}</dt>
          <dd>{formatters.formatPercent(value.version.overrideThresholdPercent)}</dd>
          <dt>{intl.formatMessage({ id: 'pricing.priceList.version.form.branches' })}</dt>
          <dd>
            {value.version.branchIds.length === 0
              ? intl.formatMessage({ id: 'pricing.priceList.version.branches.none' })
              : value.version.branchIds
                  .map((branchId) => branchName(branchId) ?? branchId)
                  .join(', ')}
          </dd>
        </dl>

        {!isDraft ? null : editingVersion === null ? (
          network.online ? (
            <Button onClick={openEditVersion} variant="secondary">
              {intl.formatMessage({ id: 'pricing.priceList.editor.editConventions' })}
            </Button>
          ) : (
            <OfflineBlockedAction
              action={intl.formatMessage({
                id: 'pricing.priceList.editor.conventions.offlineAction',
              })}
            />
          )
        ) : !network.online ? (
          <OfflineBlockedAction
            action={intl.formatMessage({
              id: 'pricing.priceList.editor.conventions.offlineAction',
            })}
          />
        ) : (
          <>
            {versionFieldMatched ? null : <BillingProblemAlert failure={failure} />}
            {isConcurrencyFailure ? (
              <Button onClick={reread} variant="secondary">
                {intl.formatMessage({ id: 'pricing.priceList.editor.reread' })}
              </Button>
            ) : null}
            <PriceListVersionForm
              branchNamesAvailable={branchNamesAvailable}
              branchOptions={branchOptions}
              busy={busy}
              cloneFromVersionNumber={null}
              controlId={(name) => `price-list-editor-version-${name}`}
              draft={editingVersion}
              mode="edit"
              onCancel={() => {
                setEditingVersion(null)
                setFailure(null)
              }}
              onChange={(draft) => {
                setEditingVersion(draft)
              }}
              onSubmit={saveVersion}
              versionNumber={value.version.versionNumber}
              {...(branchIdsError === undefined ? {} : { branchIdsError })}
              {...(effectiveFromError === undefined ? {} : { effectiveFromError })}
              {...(overrideThresholdPercentError === undefined
                ? {}
                : { overrideThresholdPercentError })}
              {...(roundOffError === undefined ? {} : { roundOffError })}
              {...(taxInclusiveError === undefined ? {} : { taxInclusiveError })}
            />
          </>
        )}
      </section>

      <section>
        <h3>{intl.formatMessage({ id: 'pricing.priceList.editor.itemsTitle' })}</h3>

        {value.items.length === 0 ? (
          <EmptyState iconName="list" live="polite">
            {intl.formatMessage({ id: 'pricing.priceList.editor.items.empty' })}
          </EmptyState>
        ) : (
          <DataTable
            caption={intl.formatMessage({ id: 'pricing.priceList.editor.items.caption' })}
            columns={[
              {
                id: 'code',
                header: intl.formatMessage({ id: 'pricing.priceListItem.column.code' }),
                primary: true,
                cell: (row: PriceListItem) => row.code,
              },
              {
                id: 'description',
                header: intl.formatMessage({ id: 'pricing.priceListItem.column.description' }),
                cell: (row: PriceListItem) => row.description,
              },
              {
                id: 'kind',
                header: intl.formatMessage({ id: 'pricing.priceListItem.column.kind' }),
                cell: (row: PriceListItem) =>
                  intl.formatMessage({
                    id: KIND_KEY[row.kind] ?? 'pricing.priceListItem.form.kind.Service',
                  }),
              },
              {
                id: 'baseRate',
                header: intl.formatMessage({ id: 'pricing.priceListItem.column.baseRate' }),
                numeric: true,
                cell: (row: PriceListItem) => formatters.formatMoney(row.baseRate),
              },
              {
                id: 'unit',
                header: intl.formatMessage({ id: 'pricing.priceListItem.column.unit' }),
                hideWhenNarrow: true,
                cell: (row: PriceListItem) => row.unit,
              },
              {
                id: 'taxCode',
                header: intl.formatMessage({ id: 'pricing.priceListItem.column.taxCode' }),
                hideWhenNarrow: true,
                cell: (row: PriceListItem) => row.taxCode,
              },
              {
                id: 'active',
                header: intl.formatMessage({ id: 'pricing.priceListItem.column.active' }),
                cell: (row: PriceListItem) => (
                  <span className="pricing-price-list-item__status">
                    <Icon name={row.active ? 'check-circle' : 'x-circle'} />
                    {intl.formatMessage({
                      id: row.active
                        ? 'pricing.priceListItem.active.yes'
                        : 'pricing.priceListItem.active.no',
                    })}
                  </span>
                ),
              },
            ]}
            rowActions={(row: PriceListItem) =>
              isDraft ? (
                <>
                  <Button
                    onClick={() => {
                      openEditItem(row)
                    }}
                    variant="secondary"
                  >
                    {intl.formatMessage({ id: 'pricing.priceListItem.edit' }, { code: row.code })}
                  </Button>
                  <Button
                    onClick={() => {
                      openRemoveItem(row)
                    }}
                    variant="danger"
                  >
                    {intl.formatMessage({ id: 'pricing.priceListItem.remove' }, { code: row.code })}
                  </Button>
                </>
              ) : null
            }
            rowKey={(row) => row.priceListItemId}
            rowLabel={(row) => row.code}
            rows={value.items}
          />
        )}

        {!isDraft ? null : network.online ? (
          <Button onClick={openAddItem} variant="primary">
            <FormattedMessage id="pricing.priceListItem.add" />
          </Button>
        ) : (
          <OfflineBlockedAction
            action={intl.formatMessage({ id: 'pricing.priceListItem.add.offlineAction' })}
          />
        )}
      </section>

      <section>
        <h3>{intl.formatMessage({ id: 'pricing.priceList.editor.discountRulesTitle' })}</h3>
        <Alert live="off" tone="info">
          <FormattedMessage id="pricing.priceList.editor.discountRulesComingSoon" />
        </Alert>

        {value.discountRules.length === 0 ? (
          <EmptyState iconName="list" live="polite">
            {intl.formatMessage({ id: 'pricing.priceList.editor.discountRules.empty' })}
          </EmptyState>
        ) : (
          <DataTable
            caption={intl.formatMessage({ id: 'pricing.priceList.editor.discountRules.caption' })}
            columns={[
              {
                id: 'code',
                header: intl.formatMessage({ id: 'pricing.discountRule.column.code' }),
                primary: true,
                cell: (row: DiscountRule) => row.code,
              },
              {
                id: 'description',
                header: intl.formatMessage({ id: 'pricing.discountRule.column.description' }),
                cell: (row: DiscountRule) => row.description,
              },
              {
                id: 'kind',
                header: intl.formatMessage({ id: 'pricing.discountRule.column.kind' }),
                cell: (row: DiscountRule) =>
                  intl.formatMessage({
                    id:
                      row.kind === 'Percentage'
                        ? 'pricing.discountRule.kind.Percentage'
                        : 'pricing.discountRule.kind.Amount',
                  }),
              },
              {
                id: 'maximumWithoutApproval',
                header: intl.formatMessage({
                  id: 'pricing.discountRule.column.maximumWithoutApproval',
                }),
                numeric: true,
                cell: (row: DiscountRule) =>
                  row.kind === 'Percentage'
                    ? formatters.formatPercent(row.maximumWithoutApproval)
                    : formatters.formatMoney(row.maximumWithoutApproval),
              },
              {
                id: 'maximum',
                header: intl.formatMessage({ id: 'pricing.discountRule.column.maximum' }),
                numeric: true,
                cell: (row: DiscountRule) =>
                  row.kind === 'Percentage'
                    ? formatters.formatPercent(row.maximum)
                    : formatters.formatMoney(row.maximum),
              },
              {
                id: 'active',
                header: intl.formatMessage({ id: 'pricing.discountRule.column.active' }),
                cell: (row: DiscountRule) => (
                  <span className="pricing-discount-rule__status">
                    <Icon name={row.active ? 'check-circle' : 'x-circle'} />
                    {intl.formatMessage({
                      id: row.active
                        ? 'pricing.discountRule.active.yes'
                        : 'pricing.discountRule.active.no',
                    })}
                  </span>
                ),
              },
            ]}
            rowKey={(row) => row.discountRuleId}
            rowLabel={(row) => row.code}
            rows={value.discountRules}
          />
        )}
      </section>

      {editingItem === null ? null : !network.online ? (
        <OfflineBlockedAction
          action={intl.formatMessage({
            id:
              editingItem.existing === null
                ? 'pricing.priceListItem.add.offlineAction'
                : 'pricing.priceListItem.edit.offlineAction',
          })}
        />
      ) : (
        <>
          {itemFieldMatched ? null : <BillingProblemAlert failure={failure} />}
          {isConcurrencyFailure ? (
            <Button onClick={reread} variant="secondary">
              {intl.formatMessage({ id: 'pricing.priceList.editor.reread' })}
            </Button>
          ) : null}
          <PriceListItemForm
            busy={busy}
            controlId={(name) => `price-list-item-${name}`}
            draft={editingItem.draft}
            existing={editingItem.existing}
            onCancel={() => {
              setEditingItem(null)
              setFailure(null)
            }}
            onChange={(draft) => {
              setEditingItem({ ...editingItem, draft })
            }}
            onSubmit={saveItem}
            taxCodeOptions={taxCodeOptions}
            taxConfigurationAvailable={taxConfigurationAvailable}
            {...(codeError === undefined ? {} : { codeError })}
            {...(descriptionError === undefined ? {} : { descriptionError })}
            {...(kindError === undefined ? {} : { kindError })}
            {...(baseRateError === undefined ? {} : { baseRateError })}
            {...(unitError === undefined ? {} : { unitError })}
            {...(taxCodeError === undefined ? {} : { taxCodeError })}
            {...(activeError === undefined ? {} : { activeError })}
            {...(itemReasonError === undefined ? {} : { reasonError: itemReasonError })}
          />
        </>
      )}

      {removingItem === null ? null : !network.online ? (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'pricing.priceListItem.remove.offlineAction' })}
        />
      ) : (
        <ConfirmDialog
          action={intl.formatMessage(
            { id: 'pricing.priceListItem.remove' },
            { code: removingItem.code },
          )}
          busy={busy}
          cancelLabel={intl.formatMessage({ id: 'admin.cancel' })}
          confirmLabel={intl.formatMessage(
            { id: 'pricing.priceListItem.remove' },
            { code: removingItem.code },
          )}
          onCancel={() => {
            setRemovingItem(null)
            setFailure(null)
          }}
          onConfirm={removeItem}
          open
          problem={<BillingProblemAlert failure={failure} />}
          tier="reason"
          title={intl.formatMessage({ id: 'pricing.priceListItem.remove.title' })}
        >
          {intl.formatMessage({ id: 'pricing.priceListItem.remove.body' })}
        </ConfirmDialog>
      )}
    </section>
  )
}
