import type { MessageKey } from '../i18n/en-IN'
import type { TemplateField, TemplateVersion } from './types'

/**
 * What changed between two versions of a template.
 *
 * ## Why a reviewer needs this rather than the two versions side by side
 *
 * A reviewer approving version 4 is being asked one question — is this change right — and the screen
 * that hands them every field of both versions makes them answer a different one: which of these
 * forty fields is not the same as before. A template with two changed bands and thirty-eight
 * untouched fields reads as thirty-eight fields of noise, and the two that matter are the ones a
 * tired reviewer skips.
 *
 * ## Why fields are matched on their key
 *
 * Because that is what a value is filed under. A field's identifier changes when a version is cloned
 * — the clone copies the fields with fresh identities and the same keys — so matching on identity
 * would report every field of a cloned version as added and every field of its source as removed,
 * which is the case a reviewer most needs this for.
 *
 * It also means a rename reads as one field removed and another added, which is exactly what a
 * rename *is* here: the key cannot be changed, so the documented rename is a removal and an
 * addition, and every value filed under the old key stays with the old key.
 */

/** Which of a field's attributes differs, named so a screen can say what changed. */
export interface FieldDifference {
  readonly attribute: MessageKey
  readonly before: string
  readonly after: string
}

/** One field's place in the comparison. */
export interface FieldComparison {
  readonly key: string
  readonly label: string
  readonly change: 'added' | 'removed' | 'changed' | 'unchanged'
  readonly differences: readonly FieldDifference[]
}

/** The whole comparison, in a stable order a reviewer can read down. */
export interface VersionComparison {
  readonly added: readonly FieldComparison[]
  readonly removed: readonly FieldComparison[]
  readonly changed: readonly FieldComparison[]
  readonly unchangedCount: number
}

/**
 * The attributes compared, and the sentence naming each.
 *
 * Every attribute a person can set through the editor, and nothing else: `displayOrder` is compared
 * because moving a field is a change a reviewer should see, and `templateFieldId` is not, because it
 * changes on every clone and means nothing to a reader.
 */
const ATTRIBUTES: readonly {
  readonly name: MessageKey
  readonly read: (field: TemplateField) => string
}[] = [
  { name: 'admin.field.label', read: (field) => field.label },
  { name: 'admin.field.labelTamil', read: (field) => field.labelTamil ?? '' },
  { name: 'admin.field.group', read: (field) => field.groupName },
  { name: 'admin.field.help', read: (field) => field.helpText },
  { name: 'admin.field.unit', read: (field) => field.canonicalUnit },
  {
    name: 'admin.field.inchFraction',
    read: (field) => `${String(field.inchFraction)}/${String(field.centimetreDecimals)}`,
  },
  { name: 'admin.field.required', read: (field) => (field.isRequired ? 'yes' : 'no') },
  {
    name: 'admin.field.bands',
    read: (field) =>
      [
        field.minimumMillimetres,
        field.maximumMillimetres,
        field.warnBelowMillimetres,
        field.warnAboveMillimetres,
      ]
        .map((value) => (value === null ? '—' : String(value)))
        .join('/'),
  },
  { name: 'admin.field.diagramKey', read: (field) => field.diagramKey ?? '' },
  {
    name: 'admin.field.options',
    read: (field) => field.options.map((option) => option.code).join(', '),
  },
  {
    // The *definition*, not the rendered sentence: the sentence is written by the server and would
    // report a difference every time its wording changed, which is not a change to the template.
    name: 'admin.field.rule',
    read: (field) =>
      field.ruleDefinition === null
        ? ''
        : `${field.ruleDefinition.effect}:${field.ruleDefinition.anyOf
            .map(
              (clause) =>
                `${clause.scope}.${clause.name} ${clause.operator} ${clause.values.join('|')}`,
            )
            .join(' or ')}`,
  },
  {
    name: 'admin.template.column.position',
    read: (field) => String(field.displayOrder),
  },
]

/**
 * Compares two versions, field by field.
 *
 * @param before The version being compared against — usually the published one.
 * @param after The version under review.
 */
export function compareVersions(
  before: TemplateVersion,
  after: TemplateVersion,
): VersionComparison {
  const was = new Map((before.fields ?? []).map((field) => [field.key, field]))
  const is = new Map((after.fields ?? []).map((field) => [field.key, field]))

  const added: FieldComparison[] = []
  const changed: FieldComparison[] = []
  let unchangedCount = 0

  for (const [key, field] of is) {
    const previous = was.get(key)

    if (previous === undefined) {
      added.push({ key, label: field.label, change: 'added', differences: [] })
      continue
    }

    const differences = ATTRIBUTES.flatMap((attribute) => {
      const beforeValue = attribute.read(previous)
      const afterValue = attribute.read(field)

      return beforeValue === afterValue
        ? []
        : [{ attribute: attribute.name, before: beforeValue, after: afterValue }]
    })

    if (differences.length === 0) {
      unchangedCount += 1
      continue
    }

    changed.push({ key, label: field.label, change: 'changed', differences })
  }

  const removed = [...was.entries()]
    .filter(([key]) => !is.has(key))
    .map(([key, field]) => ({
      key,
      label: field.label,
      change: 'removed' as const,
      differences: [],
    }))

  return { added, removed, changed, unchangedCount }
}

/** Whether anything at all differs, so a screen can say "nothing changed" rather than show nothing. */
export function hasChanges(comparison: VersionComparison): boolean {
  return (
    comparison.added.length > 0 || comparison.removed.length > 0 || comparison.changed.length > 0
  )
}
