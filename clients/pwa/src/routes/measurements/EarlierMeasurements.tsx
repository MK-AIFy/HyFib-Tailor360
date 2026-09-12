import { useState } from 'react'
import { useIntl } from 'react-intl'
import { Link } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { useCurrentUser } from '../../auth/useSession'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { Button } from '../../components/primitives/Button'
import { DataTable } from '../../components/primitives/DataTable'
import { LoadingState } from '../../components/states/LoadingState'
import { formattersForLocale } from '../../design-system/components/forms/formatting'
import { listCustomerMeasurements } from '../../measurements/measurementsApi'
import { MEASUREMENT_PERMISSIONS } from '../../measurements/measurementsPermissions'
import type { MeasurementSummary } from '../../measurements/types'

export interface EarlierMeasurementsProps {
  readonly customerId: string
  readonly templateId: string
  /** Starts a draft pre-filled from the version, as a reuse or as a correction of it. */
  readonly onStartFrom: (version: MeasurementSummary, correcting: boolean) => void
  readonly busy: boolean
}

/** What the person has committed to, and against which version. */
interface Pending {
  readonly kind: 'reuse' | 'correct'
  readonly version: MeasurementSummary
}

/**
 * Every earlier measurement of this customer for this garment, and what can be done with each
 * (#124): reuse it, correct it, compare it with the one before, open its sheet.
 *
 * ## Why reuse and correction each pass through a dialog that names the source
 *
 * "Reusing measurements requires visible confirmation of source date/version" is an acceptance
 * criterion of #28, and the reason is the failure it prevents: a pre-filled form that does not say
 * where the numbers came from is how a customer is cut to last year's chest. So neither act starts
 * from the row. Each opens a dialog that names the version, the date and who took it, and only the
 * dialog's own confirming control starts the draft — with `reuseFromVersionId` set, so the record
 * carries the provenance too.
 *
 * ## Why a correction says it is not an edit, before it starts
 *
 * A tailor's mental model is "fix the number". A confirmed measurement is never edited — a trigger
 * refuses the write, not a validator — because a job card renders through the version it was cut
 * to. The correction dialog says what will actually happen: a new version, with a reason, and the
 * old one still readable. Saying it after would be too late.
 *
 * ## Why the list is not a default
 *
 * Nothing here is pre-selected and nothing starts on its own. Offering only the newest version, or
 * starting from it silently, would make reuse mean "reuse the last one" — the silent reuse #28
 * forbids.
 */
export function EarlierMeasurements({
  customerId,
  templateId,
  onStartFrom,
  busy,
}: EarlierMeasurementsProps) {
  const intl = useIntl()
  const formatters = formattersForLocale(intl.locale)
  const { permissions } = useCurrentUser()
  const canReadSheet = permissions.includes(MEASUREMENT_PERMISSIONS.readSheet)

  const earlier = useAdminResource(`earlier-measurements:${customerId}:${templateId}`, (signal) =>
    listCustomerMeasurements(customerId, templateId, signal),
  )
  const [pending, setPending] = useState<Pending | null>(null)

  const rows = earlier.value ?? []

  const by = (version: MeasurementSummary): string =>
    version.takenByName ?? intl.formatMessage({ id: 'measurements.earlier.by.unknown' })

  /** The version taken just before this one, which is what a comparison is most often against. */
  const previousOf = (version: MeasurementSummary): MeasurementSummary | undefined => {
    const index = rows.findIndex(
      (candidate) => candidate.measurementVersionId === version.measurementVersionId,
    )
    return rows[index + 1]
  }

  const note = (version: MeasurementSummary): string => {
    if (version.correctsVersionId !== null) {
      return intl.formatMessage(
        { id: 'measurements.earlier.note.correction' },
        { reason: version.reason ?? '—' },
      )
    }
    if (version.reusedFromVersionId !== null) {
      return intl.formatMessage({ id: 'measurements.earlier.note.reused' })
    }
    return ''
  }

  return (
    <section aria-labelledby="capture-earlier" className="measurements__earlier">
      <h2 id="capture-earlier">{intl.formatMessage({ id: 'measurements.earlier.title' })}</h2>

      <AuthProblemAlert failure={earlier.failure} />

      {earlier.loading ? (
        <LoadingState
          headingLevel={3}
          what={intl.formatMessage({ id: 'measurements.earlier.loading' })}
        />
      ) : earlier.failure !== null ? null : rows.length === 0 ? (
        <p className="measurements__note">
          {intl.formatMessage({ id: 'measurements.earlier.none' })}
        </p>
      ) : (
        <DataTable
          caption={intl.formatMessage({ id: 'measurements.earlier.caption' })}
          columns={[
            {
              id: 'version',
              header: intl.formatMessage({ id: 'measurements.earlier.column.version' }),
              primary: true,
              cell: (row: MeasurementSummary) =>
                intl.formatMessage(
                  { id: 'measurements.earlier.version' },
                  { number: String(row.versionNumber) },
                ),
            },
            {
              id: 'taken',
              header: intl.formatMessage({ id: 'measurements.earlier.column.taken' }),
              cell: (row: MeasurementSummary) => formatters.formatDateTime(row.takenAt),
            },
            {
              id: 'by',
              header: intl.formatMessage({ id: 'measurements.earlier.column.by' }),
              cell: (row: MeasurementSummary) => by(row),
            },
            {
              id: 'note',
              header: intl.formatMessage({ id: 'measurements.earlier.column.note' }),
              hideWhenNarrow: true,
              cell: (row: MeasurementSummary) => note(row),
            },
          ]}
          rowKey={(row) => row.measurementVersionId}
          rowLabel={(row) =>
            intl.formatMessage(
              { id: 'measurements.earlier.version' },
              { number: String(row.versionNumber) },
            )
          }
          rows={rows}
          rowActions={(row) => {
            const previous = previousOf(row)
            return (
              <>
                <Button
                  aria-label={intl.formatMessage(
                    { id: 'measurements.earlier.reuse.label' },
                    { number: String(row.versionNumber) },
                  )}
                  onClick={() => {
                    setPending({ kind: 'reuse', version: row })
                  }}
                  unavailable={busy}
                  variant="secondary"
                >
                  {intl.formatMessage({ id: 'measurements.earlier.reuse' })}
                </Button>
                <Button
                  aria-label={intl.formatMessage(
                    { id: 'measurements.earlier.correct.label' },
                    { number: String(row.versionNumber) },
                  )}
                  onClick={() => {
                    setPending({ kind: 'correct', version: row })
                  }}
                  unavailable={busy}
                  variant="subtle"
                >
                  {intl.formatMessage({ id: 'measurements.earlier.correct' })}
                </Button>
                {previous === undefined ? null : (
                  <Link
                    aria-label={intl.formatMessage(
                      { id: 'measurements.earlier.compare.label' },
                      {
                        number: String(row.versionNumber),
                        previous: String(previous.versionNumber),
                      },
                    )}
                    to={`/measurements/compare/${previous.measurementVersionId}/${row.measurementVersionId}`}
                  >
                    {intl.formatMessage({ id: 'measurements.earlier.compare' })}
                  </Link>
                )}
                {canReadSheet ? (
                  <Link
                    aria-label={intl.formatMessage(
                      { id: 'measurements.earlier.sheet.label' },
                      { number: String(row.versionNumber) },
                    )}
                    to={`/measurements/${row.measurementVersionId}/sheet`}
                  >
                    {intl.formatMessage({ id: 'measurements.earlier.sheet' })}
                  </Link>
                ) : null}
              </>
            )
          }}
        />
      )}

      {pending === null ? null : (
        <ConfirmDialog
          open
          action={pending.kind}
          busy={busy}
          cancelLabel={intl.formatMessage({ id: 'dialogs.cancel' })}
          confirmLabel={intl.formatMessage(
            {
              id:
                pending.kind === 'reuse'
                  ? 'measurements.reuse.action'
                  : 'measurements.correct.action',
            },
            { number: String(pending.version.versionNumber) },
          )}
          onCancel={() => {
            setPending(null)
          }}
          onConfirm={() => {
            const chosen = pending
            setPending(null)
            onStartFrom(chosen.version, chosen.kind === 'correct')
          }}
          tier="confirm"
          title={intl.formatMessage(
            {
              id:
                pending.kind === 'reuse'
                  ? 'measurements.reuse.title'
                  : 'measurements.correct.title',
            },
            { number: String(pending.version.versionNumber) },
          )}
        >
          {intl.formatMessage(
            {
              id:
                pending.kind === 'reuse' ? 'measurements.reuse.body' : 'measurements.correct.body',
            },
            {
              number: String(pending.version.versionNumber),
              date: formatters.formatDateTime(pending.version.takenAt),
              by: by(pending.version),
            },
          )}
        </ConfirmDialog>
      )}
    </section>
  )
}
