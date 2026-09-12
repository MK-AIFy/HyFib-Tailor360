import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useParams } from 'react-router'
import type { TemplateField } from '../../admin/types'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { Button } from '../../components/primitives/Button'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { formattersForLocale } from '../../design-system/components/forms/formatting'
import {
  captureGroups,
  captureKindOf,
  centimetreDecimalsOf,
  fractionStepOf,
} from '../../measurements/capture'
import { readMeasurementSheet } from '../../measurements/measurementsApi'
import type { MeasurementSheet, MeasurementValue } from '../../measurements/types'
import './measurements.css'

/**
 * A customer's measurements as a tailor reads them, on screen and on paper (#124).
 *
 * ## What is on the sheet, and what is not
 *
 * The measurements, the template and version they render through, when they were taken and by
 * whom, and an opaque customer reference. No name, no telephone number, no address, no other
 * order: a printed sheet is the widest audience any measurement gets, and
 * `docs/nfr/data-classification.md` decides what may appear. The server enforces it — the payload
 * carries no such field — and the screen renders nothing it was not sent, which is what the test
 * asserts field by field.
 *
 * ## Why each value is shown in the unit it was taken in
 *
 * A tailor who took a chest in inches reads it back in inches. The record carries the unit beside
 * the millimetres, so the sheet renders each value the way the tape read, and says so once at the
 * top rather than re-labelling every line.
 *
 * ## Why reading it is one request
 *
 * Reading a sheet is a sensitive read (INV-MSR-06) the server audits by name. The read happens
 * once, when the route mounts; nothing on this screen re-reads, and printing is a browser act that
 * makes no request at all.
 */
export function MeasurementSheetRoute() {
  const intl = useIntl()
  const { versionId } = useParams()
  const network = useNetworkState()

  const sheet = useAdminResource(`sheet:${versionId ?? ''}`, (signal) =>
    readMeasurementSheet(versionId ?? '', signal),
  )

  return (
    <section className="page measurements sheet">
      <h1>
        <FormattedMessage id="measurements.sheet.title" />
      </h1>

      {/* Offline, the blocked-action state below is the whole statement; a second alert saying the
          same request failed would say the same thing twice. */}
      {network.online ? <AuthProblemAlert failure={sheet.failure} /> : null}

      {!network.online && sheet.value === null ? (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'measurements.sheet.offlineAction' })}
          onRetry={sheet.reload}
        />
      ) : sheet.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'measurements.sheet.loading' })} />
      ) : sheet.value === null ? null : (
        <SheetBody sheet={sheet.value} />
      )}

      <p className="sheet__actions">
        <Link to="/measurements/new">
          {intl.formatMessage({ id: 'measurements.compare.back' })}
        </Link>
      </p>
    </section>
  )
}

function SheetBody({ sheet }: { readonly sheet: MeasurementSheet }) {
  const intl = useIntl()
  const formatters = formattersForLocale(intl.locale)
  const groups = captureGroups(sheet.templateVersion)
  const values = new Map(sheet.values.map((value) => [value.key, value] as const))

  const render = (field: TemplateField, value: MeasurementValue | undefined): string => {
    if (value === undefined) {
      return intl.formatMessage({ id: 'measurements.sheet.notAsked' })
    }
    if (value.choice !== null) {
      return field.options.find((option) => option.code === value.choice)?.label ?? value.choice
    }
    if (value.millimetres === null) {
      return intl.formatMessage({ id: 'measurements.sheet.notAsked' })
    }
    const millimetres = Number(value.millimetres)
    if (captureKindOf(field) === 'count') {
      return formatters.formatNumber(millimetres)
    }
    const unit = value.enteredUnit === 'Centimetre' ? 'cm' : 'in'
    return formatters.formatMeasurement(millimetres, {
      unit,
      step: fractionStepOf(field),
      decimals: centimetreDecimalsOf(field),
      unitLabel: intl.formatMessage({
        id: unit === 'cm' ? 'units.centimetre.symbol' : 'units.inch.symbol',
      }),
    })
  }

  return (
    <div className="sheet__body">
      <p className="measurements__lede">
        {intl.formatMessage(
          { id: 'measurements.sheet.lede' },
          {
            template: sheet.templateName,
            number: String(sheet.versionNumber),
            date: formatters.formatDateTime(sheet.takenAt),
            by: sheet.takenByName ?? intl.formatMessage({ id: 'measurements.earlier.by.unknown' }),
            templateVersion: String(sheet.templateVersion.versionNumber),
          },
        )}
      </p>
      <p className="measurements__note">
        {intl.formatMessage({ id: 'measurements.sheet.customer' }, { reference: sheet.customerId })}
      </p>
      {sheet.correctsVersionId === null ? null : (
        <p className="measurements__note">
          {intl.formatMessage(
            { id: 'measurements.sheet.correction' },
            { reason: sheet.reason ?? '—' },
          )}
        </p>
      )}
      {sheet.reusedFromVersionId === null || sheet.correctsVersionId !== null ? null : (
        <p className="measurements__note">
          {intl.formatMessage({ id: 'measurements.sheet.reused' })}
        </p>
      )}
      <p className="measurements__note">{intl.formatMessage({ id: 'measurements.sheet.units' })}</p>

      <p className="sheet__actions">
        <Button
          iconName="clipboard"
          onClick={() => {
            window.print()
          }}
          variant="primary"
        >
          {intl.formatMessage({ id: 'measurements.sheet.print' })}
        </Button>
      </p>

      {groups.map((group) => (
        <section className="sheet__group" key={group.name}>
          <h2>{group.name}</h2>
          <dl className="capture__summary">
            {group.fields.map((field) => (
              <div key={field.templateFieldId}>
                <dt>{field.label}</dt>
                <dd>{render(field, values.get(field.key))}</dd>
              </div>
            ))}
          </dl>
        </section>
      ))}

      <p className="measurements__note sheet__notice">
        {intl.formatMessage({ id: 'measurements.sheet.notice' })}
      </p>
    </div>
  )
}
