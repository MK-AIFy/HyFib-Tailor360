import { useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useParams } from 'react-router'
import type { TemplateField } from '../../admin/types'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { DataTable } from '../../components/primitives/DataTable'
import { Icon } from '../../components/primitives/Icon'
import type { IconName } from '../../components/primitives/icons'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { Select } from '../../design-system/components/forms/Select'
import { formattersForLocale } from '../../design-system/components/forms/formatting'
import type { MeasurementDisplayUnit } from '../../design-system/components/forms/measurement'
import type { MessageKey } from '../../i18n/en-IN'
import {
  captureGroups,
  captureKindOf,
  centimetreDecimalsOf,
  defaultDisplayUnitOf,
  effectiveUnitOf,
  fractionStepOf,
} from '../../measurements/capture'
import {
  compareMeasurements,
  readMeasurementVersionTemplate,
} from '../../measurements/measurementsApi'
import type { MeasurementDifference, MeasurementValue } from '../../measurements/types'
import './measurements.css'

/** The four changes the server reports, and how each is shown: a word and a glyph, never a colour. */
const CHANGES: Readonly<Record<string, { readonly message: MessageKey; readonly icon: IconName }>> =
  {
    Unchanged: { message: 'measurements.compare.change.Unchanged', icon: 'dot' },
    Changed: { message: 'measurements.compare.change.Changed', icon: 'edit' },
    Added: { message: 'measurements.compare.change.Added', icon: 'plus' },
    Dropped: { message: 'measurements.compare.change.Dropped', icon: 'close' },
  }

/**
 * Two of a customer's measurements, field by field, before reuse or after a correction (#124).
 *
 * ## Why each value renders through its own template version
 *
 * The server matches on the field key, so a field the newer template version no longer has comes
 * back as `Dropped` — and that is said in words rather than left as a gap, because a dropped field
 * on a comparison screen is exactly the thing a person is here to notice. The order and the change
 * come from the version the newer measurement was captured under; each value's precision and each
 * choice's label come from the version *that* measurement was captured under, because a value on
 * this screen is an immutable record and a template change since must not restate it. A key
 * neither version knows is shown by its key, which is all that is left of it.
 *
 * ## Why differences are marked with a word and a glyph
 *
 * Colour never carries meaning alone on this product. The change column carries the server's own
 * word and an icon, and the row order is the template's, so a person reads down the tape rather
 * than hunting for red.
 */
export function MeasurementCompareRoute() {
  const intl = useIntl()
  const { beforeId, afterId } = useParams()
  const network = useNetworkState()

  const loaded = useAdminResource(`compare:${beforeId ?? ''}:${afterId ?? ''}`, async (signal) => {
    const [comparison, template, beforeTemplate] = await Promise.all([
      compareMeasurements(beforeId ?? '', afterId ?? '', signal),
      readMeasurementVersionTemplate(afterId ?? '', signal),
      readMeasurementVersionTemplate(beforeId ?? '', signal),
    ])
    return { comparison, template, beforeTemplate }
  })

  const [unit, setUnit] = useState<MeasurementDisplayUnit | null>(null)

  return (
    <section className="page measurements">
      <h1>
        <FormattedMessage id="measurements.compare.title" />
      </h1>

      {network.online ? <AuthProblemAlert failure={loaded.failure} /> : null}

      {loaded.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'measurements.compare.loading' })} />
      ) : loaded.value !== null ? (
        <CompareBody
          value={loaded.value}
          unit={unit ?? defaultDisplayUnitOf(loaded.value.template.version)}
          onUnitChange={setUnit}
        />
      ) : network.online ? null : (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'measurements.compare.offlineAction' })}
          onRetry={loaded.reload}
        />
      )}

      <p>
        <Link to="/measurements/new">
          {intl.formatMessage({ id: 'measurements.compare.back' })}
        </Link>
      </p>
    </section>
  )
}

interface CompareBodyProps {
  readonly value: {
    readonly comparison: Awaited<ReturnType<typeof compareMeasurements>>
    readonly template: Awaited<ReturnType<typeof readMeasurementVersionTemplate>>
    readonly beforeTemplate: Awaited<ReturnType<typeof readMeasurementVersionTemplate>>
  }
  readonly unit: MeasurementDisplayUnit
  readonly onUnitChange: (unit: MeasurementDisplayUnit) => void
}

function CompareBody({ value, unit, onUnitChange }: CompareBodyProps) {
  const intl = useIntl()
  const formatters = formattersForLocale(intl.locale)
  const { comparison, template, beforeTemplate } = value

  const unitName = (which: MeasurementDisplayUnit): string =>
    intl.formatMessage({
      id:
        which === 'cm' ? 'measurements.wizard.unit.centimetres' : 'measurements.wizard.unit.inches',
    })

  const unitLabel = (which: MeasurementDisplayUnit): string =>
    intl.formatMessage({ id: which === 'cm' ? 'units.centimetre.symbol' : 'units.inch.symbol' })

  /**
   * One value as text. A field its own template version knows is shown in the unit chosen on
   * screen, to that field's step; a key neither version knows is shown the way it was entered,
   * because the only unit anything knows for it is the one it was taken in.
   */
  const render = (field: TemplateField | undefined, value: MeasurementValue | null): string => {
    if (value === null || (value.choice === null && value.millimetres === null)) {
      return intl.formatMessage({ id: 'measurements.compare.empty' })
    }
    if (value.choice !== null) {
      return field?.options.find((option) => option.code === value.choice)?.label ?? value.choice
    }
    const millimetres = Number(value.millimetres)
    if (field === undefined) {
      if (value.enteredUnit === 'Count') {
        return formatters.formatNumber(millimetres)
      }
      const enteredIn: MeasurementDisplayUnit = value.enteredUnit === 'Centimetre' ? 'cm' : 'in'
      return formatters.formatMeasurement(millimetres, {
        unit: enteredIn,
        unitLabel: unitLabel(enteredIn),
      })
    }
    if (captureKindOf(field) === 'count') {
      return formatters.formatNumber(millimetres)
    }
    const fieldUnit = effectiveUnitOf(field, unit)
    return formatters.formatMeasurement(millimetres, {
      unit: fieldUnit,
      step: fractionStepOf(field),
      decimals: centimetreDecimalsOf(field),
      unitLabel: unitLabel(fieldUnit),
    })
  }

  const fields = new Map(
    captureGroups(template.version).flatMap((group) =>
      group.fields.map((field) => [field.key, field] as const),
    ),
  )
  // The older value renders through the version it was taken under, never through the newer one:
  // a precision coarsened or a choice relabelled since would otherwise misstate an immutable record.
  const beforeFields = new Map(
    captureGroups(beforeTemplate.version).flatMap((group) =>
      group.fields.map((field) => [field.key, field] as const),
    ),
  )
  const order = [...fields.keys()]

  // Template order first; a dropped key, which the newer version does not know, comes after.
  const rows = [...comparison.differences].sort((left, right) => {
    const a = order.indexOf(left.key)
    const b = order.indexOf(right.key)
    return (a === -1 ? order.length : a) - (b === -1 ? order.length : b)
  })

  const changeOf = (row: MeasurementDifference) => CHANGES[row.change]

  return (
    <>
      <p className="measurements__lede">
        {intl.formatMessage(
          { id: 'measurements.compare.lede' },
          {
            before: String(comparison.before.versionNumber),
            beforeDate: formatters.formatDateTime(comparison.before.takenAt),
            after: String(comparison.after.versionNumber),
            afterDate: formatters.formatDateTime(comparison.after.takenAt),
            count: Number(comparison.changedCount),
          },
        )}
      </p>

      <Select
        emptyLabel={null}
        id="compare-unit"
        label={intl.formatMessage({ id: 'measurements.compare.unit.label' })}
        name="displayUnit"
        onValueChange={(next) => {
          onUnitChange(next === 'cm' ? 'cm' : 'in')
        }}
        options={[
          { value: 'in', label: unitName('in') },
          { value: 'cm', label: unitName('cm') },
        ]}
        value={unit}
      />

      <DataTable
        caption={intl.formatMessage({ id: 'measurements.compare.caption' })}
        columns={[
          {
            id: 'field',
            header: intl.formatMessage({ id: 'measurements.compare.column.field' }),
            primary: true,
            cell: (row: MeasurementDifference) =>
              fields.get(row.key)?.label ?? beforeFields.get(row.key)?.label ?? row.key,
          },
          {
            id: 'before',
            header: intl.formatMessage(
              { id: 'measurements.compare.column.version' },
              { number: String(comparison.before.versionNumber) },
            ),
            numeric: true,
            cell: (row: MeasurementDifference) => render(beforeFields.get(row.key), row.before),
          },
          {
            id: 'after',
            header: intl.formatMessage(
              { id: 'measurements.compare.column.version' },
              { number: String(comparison.after.versionNumber) },
            ),
            numeric: true,
            cell: (row: MeasurementDifference) => render(fields.get(row.key), row.after),
          },
          {
            id: 'change',
            header: intl.formatMessage({ id: 'measurements.compare.column.change' }),
            cell: (row: MeasurementDifference) => {
              const change = changeOf(row)
              return (
                <span className="compare__change" data-change={row.change}>
                  <Icon name={change?.icon ?? 'help'} />
                  {intl.formatMessage({
                    id: change?.message ?? 'measurements.compare.change.unknown',
                  })}
                </span>
              )
            },
          },
        ]}
        rowKey={(row) => row.key}
        rowLabel={(row) =>
          fields.get(row.key)?.label ?? beforeFields.get(row.key)?.label ?? row.key
        }
        rows={rows}
      />
    </>
  )
}
