import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useSearchParams } from 'react-router'
import { listBranches } from '../../admin/adminApi'
import { useAdminResource } from '../../admin/useAdminResource'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { billingProblemCode, billingProblemField } from '../../billing/billingProblems'
import {
  listPriceLists,
  listPriceListVersions,
  readPriceListVersion,
} from '../../billing/priceListApi'
import { listTaxConfigurationVersions } from '../../billing/pricingConfigApi'
import { previewPricing } from '../../billing/pricingPreviewApi'
import type { PricingResult } from '../../billing/pricingPreviewTypes'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { NetworkStatusBanner } from '../../components/states/NetworkStatusBanner'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { DateField } from '../../design-system/components/forms/DateField'
import { Select } from '../../design-system/components/forms/Select'
import { TextField } from '../../design-system/components/forms/TextField'
import { getFormatters } from '../../i18n/formatters'
import { PRICING_CASE_SHAPES, applyPricingCaseShape } from './pricingCaseShapes'
import type { PricingCaseShapeId } from './pricingCaseShapes'
import {
  blankPricingPreviewFormDraft,
  blankPricingPreviewLineDraft,
  pricingPreviewRequestFrom,
} from './pricingPreviewDraft'
import type { PricingPreviewFormDraft, PricingPreviewLineDraft } from './pricingPreviewDraft'
import { PricingPreviewLineForm } from './PricingPreviewLineForm'
import { PricingPreviewResult } from './PricingPreviewResult'

const CONFIGURATION_MISSING_CODE = 'billing.configuration-missing'
const LINE_FIELD = /^lines\[(.+)]\.(discount\.value|override\.rate)$/

/** The line a server refusal named, and which part of it, read from the RFC 9457 `errors` map key. */
function lineFieldMatch(failure: unknown): { readonly lineKey: string } | null {
  const field = billingProblemField(failure)
  if (field === undefined) {
    return null
  }
  const match = LINE_FIELD.exec(field)
  return match?.[1] === undefined ? null : { lineKey: match[1] }
}

/**
 * The pricing preview and the accountant's test-case shapes before publish (E09-F01-8b).
 *
 * `Billing.Contracts.IPricingService` is the only calculator (`docs/IMPLEMENTATION_PLAN.md` Section
 * 6.2 note 1); this screen renders its answer and computes nothing — no total, tax or round-off is
 * ever added, divided or re-rounded here.
 *
 * ## Why the price list, its version and the version's detail are three separate reads
 *
 * `listPriceLists` and `listPriceListVersions` are the register's own two-step picker (E09-F01-6); once
 * a version is chosen, `readPriceListVersion` is what carries the items and discount rules a line and a
 * case shape are built from. A `?versionId=` arriving from the version editor's own link skips straight
 * to the third read; `priceListId` below is *derived* from it rather than synchronised into state by an
 * effect — the render simply prefers whichever price list the person has actually chosen, and falls
 * back to the one the read version belongs to only while nothing has been chosen yet — so the two
 * selects still agree, without a second round trip through the list the version editor already knows.
 *
 * ## Why the override permission is never checked here
 *
 * `billing.override_price` is not asked for by this screen and is not in `billingPermissions.ts`: the
 * engine consults it, a satisfied second factor and step-up freshness together
 * (`PricingService.cs` line 194), and this screen's job is to render the refusal when any of the three
 * is missing and the variance when it was exercised — never to decide the question itself.
 */
export function PricingPreviewRoute() {
  const intl = useIntl()
  const formatters = getFormatters()
  const network = useNetworkState()
  const [searchParams] = useSearchParams()

  const priceLists = useAdminResource('pricing-preview-price-lists', listPriceLists)
  const taxConfigVersions = useAdminResource(
    'pricing-preview-tax-config-versions',
    listTaxConfigurationVersions,
  )
  const branches = useAdminResource('pricing-preview-branches', listBranches)

  const [draft, setDraft] = useState<PricingPreviewFormDraft>(() => ({
    ...blankPricingPreviewFormDraft(),
    priceListVersionId: searchParams.get('versionId') ?? '',
  }))

  const versionDetail = useAdminResource(
    `pricing-preview-version-detail:${draft.priceListVersionId}`,
    (signal) =>
      draft.priceListVersionId === ''
        ? Promise.resolve(null)
        : readPriceListVersion(draft.priceListVersionId, signal).then((read) => read.value),
  )

  const detail = versionDetail.value

  // Derived, not synchronised: nothing has been chosen in the price-list select yet, so the price
  // list the `?versionId=` version belongs to is shown in its place, once that read has answered.
  const priceListId =
    draft.priceListId !== ''
      ? draft.priceListId
      : detail !== null && detail.version.priceListVersionId === draft.priceListVersionId
        ? detail.version.priceListId
        : ''

  const versions = useAdminResource(`pricing-preview-versions:${priceListId}`, (signal) =>
    priceListId === '' ? Promise.resolve([]) : listPriceListVersions(priceListId, signal),
  )

  const [editingLine, setEditingLine] = useState<{
    readonly lineKey: string | null
    readonly draft: PricingPreviewLineDraft
  } | null>(null)
  const [lastCaseId, setLastCaseId] = useState<PricingCaseShapeId | null>(null)
  const [running, setRunning] = useState(false)
  const [result, setResult] = useState<PricingResult | null>(null)
  const [failure, setFailure] = useState<unknown>(null)

  const controlId = (name: string): string => `pricing-preview-${name}`

  const setField = <TKey extends keyof PricingPreviewFormDraft>(
    key: TKey,
    value: PricingPreviewFormDraft[TKey],
  ): void => {
    setDraft((current) => ({ ...current, [key]: value }))
    setResult(null)
  }

  const setPriceList = (priceListId: string): void => {
    setDraft((current) => ({ ...current, priceListId, priceListVersionId: '', lines: [] }))
    setResult(null)
    setFailure(null)
    setLastCaseId(null)
  }

  const setVersion = (priceListVersionId: string): void => {
    setDraft((current) => ({ ...current, priceListVersionId, lines: [] }))
    setResult(null)
    setFailure(null)
    setLastCaseId(null)
  }

  const openAddLine = (): void => {
    if (detail === null) {
      return
    }
    const code =
      detail.items.find((item) => item.active && item.kind === 'Service')?.code ??
      detail.items.find((item) => item.active)?.code ??
      ''
    setFailure(null)
    setEditingLine({ lineKey: null, draft: blankPricingPreviewLineDraft(code) })
  }

  const openEditLine = (line: PricingPreviewLineDraft): void => {
    setFailure(null)
    setEditingLine({ lineKey: line.lineKey, draft: line })
  }

  const saveLine = (): void => {
    if (editingLine === null) {
      return
    }
    const { lineKey, draft: lineDraft } = editingLine
    setDraft((current) => ({
      ...current,
      lines:
        lineKey === null
          ? [...current.lines, lineDraft]
          : current.lines.map((line) => (line.lineKey === lineKey ? lineDraft : line)),
    }))
    setEditingLine(null)
    setResult(null)
  }

  const removeLine = (lineKey: string): void => {
    setDraft((current) => ({
      ...current,
      lines: current.lines.filter((line) => line.lineKey !== lineKey),
    }))
    setResult(null)
  }

  const runCase = (id: PricingCaseShapeId): void => {
    if (detail === null) {
      return
    }
    setDraft((current) =>
      applyPricingCaseShape(id, current, {
        items: detail.items,
        discountRules: detail.discountRules,
        overrideThresholdPercent: detail.version.overrideThresholdPercent,
        overrideReason: intl.formatMessage({ id: 'pricing.preview.case.override.reason' }),
      }),
    )
    setLastCaseId(id)
    setResult(null)
    setFailure(null)
  }

  const run = async (): Promise<void> => {
    setRunning(true)
    setFailure(null)
    try {
      setResult(await previewPricing(pricingPreviewRequestFrom(draft)))
    } catch (cause: unknown) {
      setFailure(cause)
      setResult(null)
    } finally {
      setRunning(false)
    }
  }

  const failedBaseRead = [priceLists, taxConfigVersions, branches].find(
    (resource) => resource.failure !== null,
  )

  if (priceLists.loading || taxConfigVersions.loading || branches.loading) {
    return (
      <section>
        <LoadingState what={intl.formatMessage({ id: 'pricing.preview.loading' })} />
      </section>
    )
  }

  if (failedBaseRead !== undefined) {
    return (
      <section>
        <BillingProblemAlert failure={failedBaseRead.failure} />
        <Button
          onClick={() => {
            failedBaseRead.reload()
          }}
          variant="secondary"
        >
          <FormattedMessage id="pricing.preview.retry" />
        </Button>
      </section>
    )
  }

  const priceListOptions = (priceLists.value ?? []).map((list) => ({
    value: list.priceListId,
    label: `${list.code} — ${list.name}`,
  }))

  const versionOptions = (versions.value ?? []).map((version) => ({
    value: version.priceListVersionId,
    label: intl.formatMessage(
      { id: 'pricing.preview.form.priceListVersionId.option' },
      { number: version.versionNumber, name: version.name, status: version.status },
    ),
  }))

  const taxConfigOptions = (taxConfigVersions.value ?? []).map((version) => ({
    value: version.taxConfigurationVersionId,
    label: intl.formatMessage(
      { id: 'pricing.preview.form.taxConfigurationVersionId.option' },
      { number: version.versionNumber, name: version.name, status: version.status },
    ),
  }))

  const branchOptions = (branches.value ?? []).map((branch) => ({
    value: branch.branchId,
    label: `${branch.name} (${branch.code})`,
  }))

  // Once a line is saved its edit form is closed, so a refusal naming its field is shown against the
  // line's own row in the table below — `matchedLineKey` — rather than re-opened into a form nobody
  // has open. `BillingProblemAlert` there already carries the server's own sentence: the one naming
  // who may authorise an override beyond the threshold, or that a discount exceeds its rule's maximum.
  const matchedLineKey = lineFieldMatch(failure)?.lineKey

  return (
    <section>
      <h2>{intl.formatMessage({ id: 'pricing.preview.heading' })}</h2>

      <NetworkStatusBanner />

      <section>
        <h3>{intl.formatMessage({ id: 'pricing.preview.form.title' })}</h3>

        <Select
          id={controlId('priceListId')}
          label={intl.formatMessage({ id: 'pricing.preview.form.priceListId' })}
          name="priceListId"
          onValueChange={setPriceList}
          options={priceListOptions}
          required
          value={priceListId}
        />

        <Select
          disabled={priceListId === ''}
          id={controlId('priceListVersionId')}
          label={intl.formatMessage({ id: 'pricing.preview.form.priceListVersionId' })}
          name="priceListVersionId"
          onValueChange={setVersion}
          options={versionOptions}
          required
          value={draft.priceListVersionId}
        />

        <Select
          description={intl.formatMessage({
            id: 'pricing.preview.form.taxConfigurationVersionId.hint',
          })}
          emptyLabel={intl.formatMessage({
            id: 'pricing.preview.form.taxConfigurationVersionId.publishedOption',
          })}
          id={controlId('taxConfigurationVersionId')}
          label={intl.formatMessage({ id: 'pricing.preview.form.taxConfigurationVersionId' })}
          name="taxConfigurationVersionId"
          onValueChange={(next) => {
            setField('taxConfigurationVersionId', next)
          }}
          options={taxConfigOptions}
          value={draft.taxConfigurationVersionId}
        />

        <Select
          id={controlId('branchId')}
          label={intl.formatMessage({ id: 'pricing.preview.form.branchId' })}
          name="branchId"
          onValueChange={(next) => {
            setField('branchId', next)
          }}
          options={branchOptions}
          required
          value={draft.branchId}
        />

        <DateField
          id={controlId('on')}
          label={intl.formatMessage({ id: 'pricing.preview.form.on' })}
          name="on"
          onValueChange={(next) => {
            setField('on', next)
          }}
          required
          value={draft.on}
        />

        <TextField
          description={intl.formatMessage({
            id: 'pricing.preview.form.placeOfSupplyStateCode.hint',
          })}
          id={controlId('placeOfSupplyStateCode')}
          label={intl.formatMessage({ id: 'pricing.preview.form.placeOfSupplyStateCode' })}
          name="placeOfSupplyStateCode"
          onValueChange={(next) => {
            setField('placeOfSupplyStateCode', next)
          }}
          required
          value={draft.placeOfSupplyStateCode}
        />
      </section>

      <section>
        <h3>{intl.formatMessage({ id: 'pricing.preview.lines.title' })}</h3>

        {versionDetail.loading ? (
          <LoadingState what={intl.formatMessage({ id: 'pricing.preview.lines.loading' })} />
        ) : draft.lines.length === 0 ? (
          <EmptyState
            iconName="list"
            live="polite"
            title={intl.formatMessage({ id: 'pricing.preview.lines.empty.title' })}
          >
            {intl.formatMessage({ id: 'pricing.preview.lines.empty' })}
          </EmptyState>
        ) : (
          <DataTable<PricingPreviewLineDraft>
            caption={intl.formatMessage({ id: 'pricing.preview.lines.caption' })}
            columns={[
              {
                id: 'itemCode',
                header: intl.formatMessage({ id: 'pricing.preview.line.column.itemCode' }),
                primary: true,
                cell: (row) => (
                  <>
                    {row.itemCode === ''
                      ? intl.formatMessage({ id: 'pricing.preview.result.none' })
                      : row.itemCode}
                    {matchedLineKey !== row.lineKey ? null : (
                      <BillingProblemAlert failure={failure} />
                    )}
                  </>
                ),
              },
              {
                id: 'quantity',
                header: intl.formatMessage({ id: 'pricing.preview.line.column.quantity' }),
                numeric: true,
                cell: (row) => row.quantity,
              },
              {
                id: 'surcharges',
                header: intl.formatMessage({ id: 'pricing.preview.line.column.surcharges' }),
                hideWhenNarrow: true,
                cell: (row) => String(row.surchargeItemCodes.length),
              },
              {
                id: 'discount',
                header: intl.formatMessage({ id: 'pricing.preview.line.column.discount' }),
                cell: (row) =>
                  row.discountRuleCode === ''
                    ? intl.formatMessage({ id: 'pricing.preview.result.none' })
                    : row.discountRuleCode,
              },
              {
                id: 'override',
                header: intl.formatMessage({ id: 'pricing.preview.line.column.override' }),
                cell: (row) =>
                  !row.override
                    ? intl.formatMessage({ id: 'pricing.preview.result.none' })
                    : formatters.formatMoney(row.overrideRate),
              },
            ]}
            rowActions={(row) =>
              !network.online ? null : (
                <>
                  <Button
                    onClick={() => {
                      openEditLine(row)
                    }}
                    variant="secondary"
                  >
                    {intl.formatMessage(
                      { id: 'pricing.preview.line.edit' },
                      { itemCode: row.itemCode },
                    )}
                  </Button>
                  <Button
                    onClick={() => {
                      removeLine(row.lineKey)
                    }}
                    variant="danger"
                  >
                    {intl.formatMessage(
                      { id: 'pricing.preview.line.remove' },
                      { itemCode: row.itemCode },
                    )}
                  </Button>
                </>
              )
            }
            rowKey={(row) => row.lineKey}
            rowLabel={(row) => row.itemCode}
            rows={draft.lines}
          />
        )}

        {detail === null ? null : (
          <Button onClick={openAddLine} variant="primary">
            <FormattedMessage id="pricing.preview.line.add" />
          </Button>
        )}
      </section>

      <section>
        <h3>{intl.formatMessage({ id: 'pricing.preview.cases.title' })}</h3>
        <p>{intl.formatMessage({ id: 'pricing.preview.cases.hint' })}</p>

        {PRICING_CASE_SHAPES.map((shape) => (
          <Button
            key={shape.id}
            onClick={() => {
              runCase(shape.id)
            }}
            unavailable={detail === null}
            variant="secondary"
          >
            {intl.formatMessage({ id: shape.labelId })}
          </Button>
        ))}
      </section>

      <section>
        {network.online ? (
          <Button
            busy={running}
            onClick={() => {
              void run()
            }}
            unavailable={draft.lines.length === 0}
            variant="primary"
          >
            <FormattedMessage id="pricing.preview.run" />
          </Button>
        ) : (
          <OfflineBlockedAction
            action={intl.formatMessage({ id: 'pricing.preview.run.offlineAction' })}
          />
        )}

        {failure === null || matchedLineKey !== undefined ? null : (
          <>
            <BillingProblemAlert failure={failure} />
            {billingProblemCode(failure) !== CONFIGURATION_MISSING_CODE ? null : (
              <ul>
                <li>
                  <Link to="/admin/gst-registrations">
                    {intl.formatMessage({ id: 'pricing.preview.configurationMissing.gstLink' })}
                  </Link>
                </li>
                {draft.priceListVersionId === '' ? null : (
                  <li>
                    <Link to={`/admin/price-lists/versions/${draft.priceListVersionId}`}>
                      {intl.formatMessage({
                        id: 'pricing.preview.configurationMissing.versionLink',
                      })}
                    </Link>
                  </li>
                )}
              </ul>
            )}
          </>
        )}

        {result === null ? null : (
          <PricingPreviewResult
            caseLabel={
              lastCaseId === null
                ? null
                : intl.formatMessage({
                    id:
                      PRICING_CASE_SHAPES.find((shape) => shape.id === lastCaseId)?.labelId ??
                      'pricing.preview.result.none',
                  })
            }
            result={result}
          />
        )}
      </section>

      {editingLine === null ? null : !network.online ? (
        <OfflineBlockedAction
          action={intl.formatMessage({
            id:
              editingLine.lineKey === null
                ? 'pricing.preview.line.add.offlineAction'
                : 'pricing.preview.line.edit.offlineAction',
          })}
        />
      ) : (
        <PricingPreviewLineForm
          busy={false}
          controlId={(name) => `pricing-preview-line-${name}`}
          discountRules={detail?.discountRules ?? []}
          draft={editingLine.draft}
          existing={editingLine.lineKey !== null}
          items={detail?.items ?? []}
          onCancel={() => {
            setEditingLine(null)
            setFailure(null)
          }}
          onChange={(next) => {
            setEditingLine({ ...editingLine, draft: next })
          }}
          onSubmit={saveLine}
        />
      )}
    </section>
  )
}
