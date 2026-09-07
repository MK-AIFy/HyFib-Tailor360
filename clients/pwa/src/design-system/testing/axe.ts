import axe from 'axe-core'
import type { AxeResults, ElementContext, RunOptions, Result } from 'axe-core'

/**
 * Automated component accessibility, in the ordinary test run.
 *
 * axe-core runs against jsdom rather than against a browser. That trade is deliberate: it catches
 * the whole class of structural defect a design system produces — a control with no accessible
 * name, a field with no label, a heading level skipped, an `aria-describedby` pointing at nothing,
 * a role with a missing required attribute — in a run that already exists and costs seconds, rather
 * than in a browser runner that would cost a Chromium download in every pipeline.
 *
 * What jsdom cannot decide is anything that needs layout or paint: colour contrast, target size,
 * whether a focused control is behind a bottom bar. Those are covered elsewhere and on purpose —
 * contrast by the token-pair test in tokenContrast.test.ts, overflow and obscured focus by
 * expectNoHorizontalOverflow, target size and everything a person has to judge by
 * docs/nfr/a11y-checklist.md. The rules that need paint are disabled below rather than left to
 * report a false pass.
 */

/** Rules jsdom cannot judge, each covered by a check that can. */
export const RULES_JSDOM_CANNOT_JUDGE = ['color-contrast', 'target-size'] as const

export interface AccessibilityCheckOptions {
  /** Extra axe options, merged over the defaults. */
  readonly axeOptions?: RunOptions
  /** Rule ids to disable for this check, with a comment in the test saying why. */
  readonly disabledRules?: readonly string[]
}

function describeViolation(violation: Result): string {
  const nodes = violation.nodes
    .slice(0, 5)
    .map((node) => `      ${node.html}\n        ${node.failureSummary ?? ''}`)
    .join('\n')
  return `  ${violation.id} (${violation.impact ?? 'unknown impact'}): ${violation.help}\n    ${violation.helpUrl}\n${nodes}`
}

/** Runs axe-core and returns the raw results, for a test that wants to inspect them. */
export async function runAccessibilityChecks(
  container: ElementContext,
  options: AccessibilityCheckOptions = {},
): Promise<AxeResults> {
  const disabled = [...RULES_JSDOM_CANNOT_JUDGE, ...(options.disabledRules ?? [])]
  const rules: RunOptions['rules'] = Object.fromEntries(
    disabled.map((id) => [id, { enabled: false }]),
  )

  return axe.run(container, {
    ...options.axeOptions,
    rules: { ...rules, ...options.axeOptions?.rules },
  })
}

/**
 * Fails with a readable report if the container has any accessibility violation.
 *
 * A plain assertion function rather than a Vitest matcher: it needs no type augmentation, no setup
 * file entry, and it reads the same in every test.
 */
export async function expectNoAccessibilityViolations(
  container: ElementContext,
  options: AccessibilityCheckOptions = {},
): Promise<void> {
  const results = await runAccessibilityChecks(container, options)
  if (results.violations.length === 0) {
    return
  }

  throw new Error(
    `${String(results.violations.length)} accessibility violation(s):\n${results.violations
      .map(describeViolation)
      .join('\n')}`,
  )
}
