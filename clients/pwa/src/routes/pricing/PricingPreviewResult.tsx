import { useIntl } from 'react-intl'
import { DataTable } from '../../components/primitives/DataTable'
import { Icon } from '../../components/primitives/Icon'
import type { IconName } from '../../components/primitives/icons'
import { getFormatters } from '../../i18n/formatters'
import type { MessageKey } from '../../i18n/en-IN'
import type { PricedLine, PricingResult } from '../../billing/pricingPreviewTypes'

export interface PricingPreviewResultProps {
  readonly result: PricingResult
  /** The case shape last pressed before this result was run, or null. */
  readonly caseLabel: string | null
}

const SCHEME_PRESENTATION: Readonly<
  Record<string, { readonly icon: IconName; readonly labelId: MessageKey }>
> = {
  IntraState: { icon: 'home', labelId: 'pricing.preview.result.scheme.IntraState' },
  InterState: { icon: 'truck', labelId: 'pricing.preview.result.scheme.InterState' },
}

/** An icon and a word together — never colour alone (NFR-AC-05). */
function BooleanBadge({
  value,
  yesId,
  noId,
}: {
  readonly value: boolean
  readonly yesId: MessageKey
  readonly noId: MessageKey
}) {
  const intl = useIntl()
  return (
    <span className="pricing-preview__badge">
      <Icon name={value ? 'check-circle' : 'x-circle'} />
      {intl.formatMessage({ id: value ? yesId : noId })}
    </span>
  )
}

/**
 * The engine's answer, shown in full: every line's base, surcharges, discount, gross, taxable value,
 * tax components, line total, variance and `approvalExercised`, and the document totals with
 * `roundOff` on its own line. Every figure here is exactly what `result` carries — nothing is added,
 * divided or re-rounded (`ComputesNoTotalInTheClient`).
 */
export function PricingPreviewResult({ result, caseLabel }: PricingPreviewResultProps) {
  const intl = useIntl()
  const formatters = getFormatters()
  const scheme = SCHEME_PRESENTATION[result.scheme]

  return (
    <section
      aria-label={intl.formatMessage({ id: 'pricing.preview.result.heading' })}
      aria-live="polite"
    >
      <h3>{intl.formatMessage({ id: 'pricing.preview.result.heading' })}</h3>

      {caseLabel === null ? null : (
        <p>{intl.formatMessage({ id: 'pricing.preview.result.case' }, { case: caseLabel })}</p>
      )}

      <dl>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.scheme' })}</dt>
        <dd>
          <span className="pricing-preview__badge">
            {scheme === undefined ? null : <Icon name={scheme.icon} />}
            {scheme === undefined ? result.scheme : intl.formatMessage({ id: scheme.labelId })}
          </span>
        </dd>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.taxInclusive' })}</dt>
        <dd>
          {intl.formatMessage({
            id: result.taxInclusive
              ? 'pricing.priceList.version.tax.inclusive'
              : 'pricing.priceList.version.tax.exclusive',
          })}
        </dd>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.priceListVersionId' })}</dt>
        <dd>{result.priceListVersionId}</dd>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.taxConfigurationVersionId' })}</dt>
        <dd>{result.taxConfigurationVersionId}</dd>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.gstRegistrationId' })}</dt>
        <dd>{result.gstRegistrationId}</dd>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.calculatedAt' })}</dt>
        <dd>{formatters.formatDateTime(result.calculatedAt)}</dd>
      </dl>

      <DataTable<PricedLine>
        caption={intl.formatMessage({ id: 'pricing.preview.result.lines.caption' })}
        columns={[
          {
            id: 'itemCode',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.itemCode' }),
            primary: true,
            cell: (row) => (
              <>
                {row.itemCode}
                <br />
                {row.description}
              </>
            ),
          },
          {
            id: 'quantity',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.quantity' }),
            numeric: true,
            cell: (row) => formatters.formatNumber(row.quantity),
          },
          {
            id: 'catalogueRate',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.catalogueRate' }),
            numeric: true,
            hideWhenNarrow: true,
            cell: (row) => formatters.formatMoney(row.catalogueRate),
          },
          {
            id: 'appliedRate',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.appliedRate' }),
            numeric: true,
            hideWhenNarrow: true,
            cell: (row) => formatters.formatMoney(row.appliedRate),
          },
          {
            id: 'base',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.base' }),
            numeric: true,
            cell: (row) => formatters.formatMoney(row.base),
          },
          {
            id: 'surcharges',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.surcharges' }),
            hideWhenNarrow: true,
            cell: (row) =>
              row.surcharges.length === 0 ? (
                <span>{intl.formatMessage({ id: 'pricing.preview.result.none' })}</span>
              ) : (
                <ul>
                  {row.surcharges.map((surcharge) => (
                    <li key={surcharge.itemCode}>
                      {surcharge.itemCode} — {formatters.formatMoney(surcharge.amount)}
                    </li>
                  ))}
                </ul>
              ),
          },
          {
            id: 'discount',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.discount' }),
            cell: (row) =>
              row.discount === null ? (
                <span>{intl.formatMessage({ id: 'pricing.preview.result.none' })}</span>
              ) : (
                <>
                  {row.discount.ruleCode} — {formatters.formatMoney(row.discount.amount)}
                  <br />
                  <BooleanBadge
                    value={row.discount.approvalExercised}
                    yesId="pricing.preview.result.approvalExercised.yes"
                    noId="pricing.preview.result.approvalExercised.no"
                  />
                </>
              ),
          },
          {
            id: 'gross',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.gross' }),
            numeric: true,
            cell: (row) => formatters.formatMoney(row.gross),
          },
          {
            id: 'taxableValue',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.taxableValue' }),
            numeric: true,
            hideWhenNarrow: true,
            cell: (row) => formatters.formatMoney(row.taxableValue),
          },
          {
            id: 'taxCode',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.taxCode' }),
            hideWhenNarrow: true,
            cell: (row) => `${row.taxCode} (${row.classification})`,
          },
          {
            id: 'taxes',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.taxes' }),
            cell: (row) =>
              row.taxes.length === 0 ? (
                <span>{intl.formatMessage({ id: 'pricing.preview.result.none' })}</span>
              ) : (
                <ul>
                  {row.taxes.map((tax) => (
                    <li key={tax.kind}>
                      {tax.kind} {formatters.formatPercent(tax.ratePercent)} —{' '}
                      {formatters.formatMoney(tax.amount)}
                    </li>
                  ))}
                </ul>
              ),
          },
          {
            id: 'taxTotal',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.taxTotal' }),
            numeric: true,
            cell: (row) => formatters.formatMoney(row.taxTotal),
          },
          {
            id: 'lineTotal',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.lineTotal' }),
            numeric: true,
            cell: (row) => formatters.formatMoney(row.lineTotal),
          },
          {
            id: 'variance',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.variance' }),
            numeric: true,
            hideWhenNarrow: true,
            cell: (row) =>
              `${formatters.formatMoney(row.variance)} (${formatters.formatPercent(row.variancePercent)})`,
          },
          {
            id: 'approvalExercised',
            header: intl.formatMessage({ id: 'pricing.preview.result.column.approvalExercised' }),
            cell: (row) => (
              <BooleanBadge
                value={row.approvalExercised}
                yesId="pricing.preview.result.approvalExercised.yes"
                noId="pricing.preview.result.approvalExercised.no"
              />
            ),
          },
        ]}
        rowKey={(row) => row.lineKey}
        rowLabel={(row) => row.itemCode}
        rows={result.lines}
      />

      <h4>{intl.formatMessage({ id: 'pricing.preview.result.totalsTitle' })}</h4>
      <dl>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.totals.subtotal' })}</dt>
        <dd>{formatters.formatMoney(result.totals.subtotal)}</dd>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.totals.discountTotal' })}</dt>
        <dd>{formatters.formatMoney(result.totals.discountTotal)}</dd>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.totals.taxableValue' })}</dt>
        <dd>{formatters.formatMoney(result.totals.taxableValue)}</dd>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.totals.centralTax' })}</dt>
        <dd>{formatters.formatMoney(result.totals.centralTax)}</dd>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.totals.stateTax' })}</dt>
        <dd>{formatters.formatMoney(result.totals.stateTax)}</dd>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.totals.integratedTax' })}</dt>
        <dd>{formatters.formatMoney(result.totals.integratedTax)}</dd>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.totals.cess' })}</dt>
        <dd>{formatters.formatMoney(result.totals.cess)}</dd>
        {/* Its own line, never folded into the grand total — the parent's acceptance criterion 4. */}
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.totals.roundOff' })}</dt>
        <dd>{formatters.formatMoney(result.totals.roundOff)}</dd>
        <dt>{intl.formatMessage({ id: 'pricing.preview.result.totals.grandTotal' })}</dt>
        <dd>
          <strong>{formatters.formatMoney(result.totals.grandTotal)}</strong>
        </dd>
      </dl>
    </section>
  )
}
