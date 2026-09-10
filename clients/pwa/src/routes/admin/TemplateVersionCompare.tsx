import { useIntl } from 'react-intl'
import { DataTable } from '../../components/primitives/DataTable'
import { Select } from '../../design-system/components/forms/Select'
import { compareVersions, hasChanges } from '../../admin/templateVersionDiff'
import type { FieldComparison } from '../../admin/templateVersionDiff'
import type { TemplateVersion } from '../../admin/types'

/**
 * What changed between the version under review and another one.
 *
 * A reviewer approving version 4 is being asked one question — is this change right — and a screen
 * that hands them every field of both versions makes them answer a different one. A template with
 * two changed bands and thirty-eight untouched fields reads as thirty-eight fields of noise, and the
 * two that matter are the ones a tired reviewer skips.
 *
 * The count of unchanged fields is stated rather than the fields themselves, so the screen still
 * says how much of the template this comparison is *not* about.
 */
export interface TemplateVersionCompareProps {
  readonly version: TemplateVersion
  /** Every other version of the template, newest first, which is what may be compared against. */
  readonly others: readonly TemplateVersion[]
  readonly againstId: string | null
  readonly onAgainstChange: (versionId: string) => void
  readonly controlId: (name: string) => string
}

export function TemplateVersionCompare(props: TemplateVersionCompareProps) {
  const { version, others, againstId, onAgainstChange, controlId } = props
  const intl = useIntl()

  if (others.length === 0) {
    return null
  }

  const against = others.find((one) => one.templateVersionId === againstId) ?? others[0]

  if (against === undefined) {
    return null
  }

  const comparison = compareVersions(against, version)
  const rows = [...comparison.added, ...comparison.removed, ...comparison.changed]

  const describe = (row: FieldComparison): string =>
    row.differences
      .map((difference) => {
        const attribute = intl.formatMessage({ id: difference.attribute })

        if (difference.before === '') {
          return intl.formatMessage(
            { id: 'admin.version.compare.wasEmpty' },
            { attribute, after: difference.after },
          )
        }
        if (difference.after === '') {
          return intl.formatMessage(
            { id: 'admin.version.compare.nowEmpty' },
            { attribute, before: difference.before },
          )
        }
        return intl.formatMessage(
          { id: 'admin.version.compare.was' },
          { attribute, before: difference.before, after: difference.after },
        )
      })
      .join('; ')

  return (
    <section>
      <h3>{intl.formatMessage({ id: 'admin.version.compare' })}</h3>

      <Select
        emptyLabel={null}
        id={controlId('compareAgainst')}
        label={intl.formatMessage({ id: 'admin.version.compare.against' })}
        name="compareAgainst"
        onValueChange={onAgainstChange}
        options={others.map((one) => ({
          value: one.templateVersionId,
          label: intl.formatMessage(
            { id: 'admin.templates.version' },
            { number: one.versionNumber },
          ),
        }))}
        value={against.templateVersionId}
      />

      {hasChanges(comparison) ? (
        <>
          <p>
            {[
              intl.formatMessage(
                { id: 'admin.version.compare.added' },
                { count: comparison.added.length },
              ),
              intl.formatMessage(
                { id: 'admin.version.compare.removed' },
                { count: comparison.removed.length },
              ),
              intl.formatMessage(
                { id: 'admin.version.compare.changed' },
                { count: comparison.changed.length },
              ),
              intl.formatMessage(
                { id: 'admin.version.compare.unchanged' },
                { count: comparison.unchangedCount },
              ),
            ].join(' · ')}
          </p>

          <p>{intl.formatMessage({ id: 'admin.version.compare.renameNote' })}</p>

          <DataTable
            caption={intl.formatMessage({ id: 'admin.version.compare.caption' })}
            rows={rows}
            rowKey={(row) => `${row.change}-${row.key}`}
            rowLabel={(row) => row.label}
            columns={[
              {
                id: 'field',
                header: intl.formatMessage({ id: 'admin.version.compare.column.field' }),
                primary: true,
                cell: (row: FieldComparison) => `${row.label} (${row.key})`,
              },
              {
                id: 'change',
                header: intl.formatMessage({ id: 'admin.version.compare.column.change' }),
                cell: (row: FieldComparison) =>
                  intl.formatMessage({
                    id:
                      row.change === 'added'
                        ? 'admin.version.compare.change.added'
                        : row.change === 'removed'
                          ? 'admin.version.compare.change.removed'
                          : 'admin.version.compare.change.changed',
                  }),
              },
              {
                id: 'detail',
                header: intl.formatMessage({ id: 'admin.version.compare.column.detail' }),
                cell: (row: FieldComparison) => describe(row),
              },
            ]}
          />
        </>
      ) : (
        <p>{intl.formatMessage({ id: 'admin.version.compare.nothing' })}</p>
      )}
    </section>
  )
}
