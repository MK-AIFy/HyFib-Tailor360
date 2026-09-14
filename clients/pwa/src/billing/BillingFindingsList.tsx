import { useIntl } from 'react-intl'
import { Alert } from '../components/primitives/Alert'
import { Icon } from '../components/primitives/Icon'
import type { BillingFinding } from './pricingAdminTypes'
import './BillingFindingsList.css'

export interface BillingFindingsListProps {
  readonly findings: readonly BillingFinding[]
}

/**
 * What a publish validation found, or what a publish itself still warned about — errors separated
 * from warnings, each finding carrying its own severity, its target and the server's own sentence.
 *
 * Shared, unchanged, by the tax configuration editor (E09-F01-5b) and the price-list editor
 * (E09-F01-7b): severity is read from each finding's own `severity` field, never from a client-side
 * list of codes, which is what lets the price list's own warnings (real, unlike the tax
 * configuration's today) render correctly here without this component changing at all (OD-19).
 *
 * A warning never refuses a publish and an error always does; the two are shown as separate groups so
 * an administrator can tell which is which without reading every row. This is a live region
 * (`live="polite"`) so a validation run announces its own result — 4.1.3 Status Messages — and
 * severity is carried by an icon *and* a word, never colour alone (NFR-AC-05).
 */
export function BillingFindingsList({ findings }: BillingFindingsListProps) {
  const intl = useIntl()

  if (findings.length === 0) {
    return (
      <Alert live="polite" tone="success">
        {intl.formatMessage({ id: 'billing.findings.empty' })}
      </Alert>
    )
  }

  const errors = findings.filter((finding) => finding.severity === 'Error')
  const warnings = findings.filter((finding) => finding.severity !== 'Error')

  return (
    <Alert live="polite" tone={errors.length > 0 ? 'danger' : 'warning'}>
      <div className="billing-findings-list__scroll" tabIndex={0}>
        {errors.length === 0 ? null : (
          <section>
            <h4>
              {intl.formatMessage(
                { id: 'billing.findings.errors.title' },
                { count: errors.length },
              )}
            </h4>
            <ul className="billing-findings-list__list">
              {errors.map((finding, index) => (
                <BillingFindingRow finding={finding} key={`${finding.code}-${index}`} />
              ))}
            </ul>
          </section>
        )}

        {warnings.length === 0 ? null : (
          <section>
            <h4>
              {intl.formatMessage(
                { id: 'billing.findings.warnings.title' },
                { count: warnings.length },
              )}
            </h4>
            <ul className="billing-findings-list__list">
              {warnings.map((finding, index) => (
                <BillingFindingRow finding={finding} key={`${finding.code}-${index}`} />
              ))}
            </ul>
          </section>
        )}
      </div>
    </Alert>
  )
}

function BillingFindingRow({ finding }: { readonly finding: BillingFinding }) {
  const intl = useIntl()
  const isError = finding.severity === 'Error'

  return (
    <li className="billing-findings-list__row">
      <Icon
        className="billing-findings-list__icon"
        name={isError ? 'alert-circle' : 'alert-triangle'}
      />
      {/* The severity in words, not colour alone (NFR-AC-05) — visible, not screen-reader-only,
          because a finding is read on its own once the list scrolls past its group heading. */}
      <span className="billing-findings-list__severity">
        {intl.formatMessage({
          id: isError ? 'billing.findings.severity.error' : 'billing.findings.severity.warning',
        })}
      </span>
      {finding.target === null ? null : (
        <span className="billing-findings-list__target">{finding.target}</span>
      )}
      <span className="billing-findings-list__message">{finding.message}</span>
    </li>
  )
}
