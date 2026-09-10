import type { TemplateFinding } from './types'

/**
 * Where a publish-validation finding belongs on the screen.
 *
 * ## Why a finding is anchored rather than listed
 *
 * The server reports **every** finding rather than the first, deliberately: an administrator fixing
 * a template wants every problem at once, not one per round trip. That is exactly what makes a list
 * the wrong rendering — the list is long by design, and a list forces the person to map a target
 * path like `fields[sleeve_length].rule` onto a form by eye, which is the one piece of work the
 * `target` member exists to spare them.
 *
 * `TemplateFinding` names the thing it is about rather than describing it, in the same shape the
 * API's field-level problem details use. This module is what turns that name back into a control.
 */

/** The attribute of a field a finding can be about, and which control shows it. */
export const FINDING_ATTRIBUTES = [
  'key',
  'labelTamil',
  'precision',
  'bands',
  'options',
  'diagramKey',
  'rule',
] as const

export type FindingAttribute = (typeof FINDING_ATTRIBUTES)[number]

/** A parsed target: the field it is about, and which of its attributes. */
export interface FindingTarget {
  /** The field key, or null for a finding about the version as a whole. */
  readonly fieldKey: string | null
  /** The attribute, or null when the target names a field without naming one of its attributes. */
  readonly attribute: FindingAttribute | null
}

const FIELD_TARGET = /^fields\[([^\]]+)\](?:\.(.+))?$/

/**
 * Reads a target path.
 *
 * A path this build does not recognise resolves to a target with no field and no attribute, which
 * is what lets the screen render it in the summary rather than dropping it. **A finding that cannot
 * be anchored must still be shown**: it is a real refusal, and a screen that silently discarded it
 * would tell an administrator their version is ready when the server will refuse to publish it.
 */
export function parseTarget(target: string): FindingTarget {
  if (target === 'fields') {
    return { fieldKey: null, attribute: null }
  }

  const match = FIELD_TARGET.exec(target)

  if (match === null) {
    return { fieldKey: null, attribute: null }
  }

  const attribute = match[2]

  return {
    fieldKey: match[1] ?? null,
    attribute:
      attribute !== undefined && (FINDING_ATTRIBUTES as readonly string[]).includes(attribute)
        ? (attribute as FindingAttribute)
        : null,
  }
}

/** Whether a finding refuses publication. Warnings are worth saying and do not block. */
export function isError(finding: TemplateFinding): boolean {
  return finding.severity === 'Error'
}

/** The findings about one field, in the order the server reported them. */
export function findingsForField(
  findings: readonly TemplateFinding[],
  fieldKey: string,
): readonly TemplateFinding[] {
  return findings.filter((finding) => parseTarget(finding.target).fieldKey === fieldKey)
}

/**
 * The findings this screen cannot put beside a control, which the summary has to carry itself.
 *
 * Two kinds, and both matter. A finding about the version as a whole — `fields`, when a version has
 * none — has no control to sit beside. And a finding naming a field **this screen is not showing**
 * is the version having changed under the reader: the report was computed against a version that had
 * that field, and the one on screen does not. Reporting it in the summary is what stops the screen
 * quietly losing a refusal.
 */
export function unanchoredFindings(
  findings: readonly TemplateFinding[],
  knownKeys: readonly string[],
): readonly TemplateFinding[] {
  return findings.filter((finding) => {
    const { fieldKey } = parseTarget(finding.target)
    return fieldKey === null || !knownKeys.includes(fieldKey)
  })
}
