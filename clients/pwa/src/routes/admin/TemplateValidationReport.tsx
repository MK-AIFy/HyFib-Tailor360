import { useIntl } from 'react-intl'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { isError, unanchoredFindings } from '../../admin/templateFindings'
import type { TemplateFinding, TemplateValidation } from '../../admin/types'

/**
 * What the publish checks found, said where it can be acted on.
 *
 * ## Why the summary is small and the findings live beside their controls
 *
 * An administrator fixing a template is holding one question: which control is wrong. A list of
 * findings makes them map a target path onto a form by eye — and the whole point of the server
 * reporting *every* finding rather than the first is that the list is long. So this component is
 * deliberately a summary and a set of links, and the messages themselves are rendered by the field
 * rows, beside the thing that caused them.
 *
 * ## What must never be dropped
 *
 * A finding whose target names something not on this screen. Two ways that happens: a finding about
 * the version as a whole, which has no control to sit beside, and a finding about a field the report
 * knew and the screen does not — the version having changed under the reader. Both are carried here
 * in full, because a screen that quietly discarded one would tell an administrator their version is
 * ready when the server will refuse to publish it.
 */
export interface TemplateValidationReportProps {
  readonly validation: TemplateValidation
  /** Every field key on screen, so a finding naming another can be reported rather than lost. */
  readonly knownKeys: readonly string[]
  /** True when the report was computed against a state this screen has since moved past. */
  readonly stale: boolean
  /** Moves to the control a finding is about. */
  readonly onGoTo: (finding: TemplateFinding) => void
  /** The label of the field a finding is about, for the link's accessible name. */
  readonly labelFor: (finding: TemplateFinding) => string | null
}

export function TemplateValidationReport(props: TemplateValidationReportProps) {
  const { validation, knownKeys, stale, onGoTo, labelFor } = props
  const intl = useIntl()

  const errors = validation.findings.filter(isError)
  const warnings = validation.findings.filter((finding) => !isError(finding))
  const orphans = unanchoredFindings(validation.findings, knownKeys)

  if (validation.findings.length === 0) {
    return (
      <Alert live="polite" tone="success">
        {intl.formatMessage({ id: 'admin.version.ready' })}
      </Alert>
    )
  }

  return (
    <section>
      {stale ? (
        <Alert live="polite" tone="info">
          {intl.formatMessage({ id: 'admin.version.stale' })}
        </Alert>
      ) : null}

      <Alert live="polite" tone={errors.length > 0 ? 'warning' : 'info'}>
        <p>
          {intl.formatMessage(
            { id: 'admin.version.findings' },
            { errors: errors.length, warnings: warnings.length },
          )}
        </p>
        <p>
          {intl.formatMessage({
            id: errors.length > 0 ? 'admin.version.blocked' : 'admin.version.warningsOnly',
          })}
        </p>

        <ul>
          {validation.findings.map((finding) => {
            const label = labelFor(finding)

            return (
              <li key={`${finding.code}-${finding.target}`}>
                {intl.formatMessage({
                  id: isError(finding)
                    ? 'admin.version.finding.error'
                    : 'admin.version.finding.warning',
                })}
                {': '}
                {finding.message}
                {label === null ? null : (
                  <Button
                    onClick={() => {
                      onGoTo(finding)
                    }}
                    type="button"
                    variant="secondary"
                  >
                    {intl.formatMessage({ id: 'admin.version.finding.goto' }, { label })}
                  </Button>
                )}
              </li>
            )
          })}
        </ul>

        {orphans.length > 0 ? (
          <p>{intl.formatMessage({ id: 'admin.version.unanchored' })}</p>
        ) : null}
      </Alert>
    </section>
  )
}
