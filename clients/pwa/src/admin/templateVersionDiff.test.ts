import { describe, expect, it } from 'vitest'
import { aTemplateField, aTemplateVersion } from './testing/fixtures'
import { compareVersions, hasChanges } from './templateVersionDiff'
import type { TemplateField } from './types'

function version(fields: readonly TemplateField[]) {
  return aTemplateVersion({ fields: [...fields] })
}

const CHEST = aTemplateField()
const WAIST = aTemplateField({
  templateFieldId: 'id-waist',
  key: 'waist',
  label: 'Waist',
  displayOrder: 1,
})

describe('what changed between two versions', () => {
  it('reports nothing when nothing differs', () => {
    const comparison = compareVersions(version([CHEST]), version([CHEST]))

    expect(hasChanges(comparison)).toBe(false)
    expect(comparison.unchangedCount).toBe(1)
  })

  it('reports a field that was added', () => {
    const comparison = compareVersions(version([CHEST]), version([CHEST, WAIST]))

    expect(comparison.added.map((one) => one.key)).toEqual(['waist'])
    expect(comparison.removed).toEqual([])
    expect(comparison.unchangedCount).toBe(1)
  })

  it('reports a field that was removed', () => {
    const comparison = compareVersions(version([CHEST, WAIST]), version([CHEST]))

    expect(comparison.removed.map((one) => one.key)).toEqual(['waist'])
    expect(comparison.added).toEqual([])
  })

  it('reports which attribute of a changed field differs, and what it was', () => {
    const comparison = compareVersions(
      version([CHEST]),
      version([aTemplateField({ label: 'Chest' })]),
    )

    expect(comparison.changed).toHaveLength(1)
    expect(comparison.changed[0]?.differences).toEqual([
      { attribute: 'admin.field.label', before: 'Chest / bust', after: 'Chest' },
    ])
  })

  it.each([
    ['the bands', { minimumMillimetres: 200 }, 'admin.field.bands'],
    ['the precision', { inchFraction: 16 }, 'admin.field.inchFraction'],
    ['the step', { groupName: 'Neckline' }, 'admin.field.group'],
    ['whether it is required', { isRequired: false }, 'admin.field.required'],
    ['the help text', { helpText: 'Something else.' }, 'admin.field.help'],
    ['the diagram', { diagramKey: 'other_v1' }, 'admin.field.diagramKey'],
    ['its position', { displayOrder: 9 }, 'admin.template.column.position'],
  ])('notices a change to %s', (_named, overrides, attribute) => {
    const comparison = compareVersions(version([CHEST]), version([aTemplateField(overrides)]))

    expect(comparison.changed[0]?.differences.map((one) => one.attribute)).toContain(attribute)
  })

  it('compares the rule as a structure, never as the sentence the server renders', () => {
    // The sentence is written by the server and would report a difference every time its wording
    // changed, which is not a change to the template.
    const same = compareVersions(
      version([aTemplateField({ rule: 'Shown when the lining is chosen.' })]),
      version([aTemplateField({ rule: 'Asked for when the lining is chosen.' })]),
    )

    expect(hasChanges(same)).toBe(false)

    const different = compareVersions(
      version([CHEST]),
      version([
        aTemplateField({
          ruleDefinition: {
            effect: 'ShownWhen',
            anyOf: [{ scope: 'Field', name: 'has_lining', operator: 'IsAnyOf', values: ['YES'] }],
          },
        }),
      ]),
    )

    expect(different.changed[0]?.differences.map((one) => one.attribute)).toContain(
      'admin.field.rule',
    )
  })

  it('matches on the key, so a cloned version does not read as entirely new', () => {
    // A clone copies the fields with fresh identities and the same keys. Matching on identity would
    // report every field of a cloned version as added and every field of its source as removed —
    // which is the case a reviewer most needs this for.
    const cloned = aTemplateField({ templateFieldId: 'a-fresh-identity' })
    const comparison = compareVersions(version([CHEST]), version([cloned]))

    expect(hasChanges(comparison)).toBe(false)
    expect(comparison.unchangedCount).toBe(1)
  })

  it('reads a rename as one removed and one added, which is what a rename is here', () => {
    // The key cannot be changed, so the documented rename is a removal and an addition — and every
    // value already captured stays filed under the old key.
    const renamed = aTemplateField({ key: 'chest', label: 'Chest / bust' })
    const comparison = compareVersions(version([CHEST]), version([renamed]))

    expect(comparison.added.map((one) => one.key)).toEqual(['chest'])
    expect(comparison.removed.map((one) => one.key)).toEqual(['chest_bust'])
    expect(comparison.changed).toEqual([])
  })

  it('handles a version whose fields were not carried at all', () => {
    // The list read does not carry fields; a comparison against one has nothing to say rather than
    // reporting every field of the other as removed.
    const comparison = compareVersions(
      aTemplateVersion({ fields: null }),
      aTemplateVersion({ fields: null }),
    )

    expect(hasChanges(comparison)).toBe(false)
  })
})
