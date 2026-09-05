import { useEffect, useRef } from 'react'
import { useIntl } from 'react-intl'
import type { FieldErrorEntry } from '../../foundations/FieldProps'
import { useFieldIds } from '../../foundations/ids'
import type { FormStep } from './controlTypes'
import './forms.css'

/**
 * The step-aware error summary.
 *
 * WCAG 3.3.1 and checklist item A11Y-36 between them ask for four things, and all four are here:
 * focus moves to the summary on a failed submit, the summary is announced, every entry moves focus
 * to its field, and an entry whose field is on another wizard step changes the step first.
 *
 * ## Why there is no `role="alert"`
 *
 * The obvious implementation is `role="alert"` plus a focus move, and it is the one this component
 * deliberately does not use. Moving focus into a live region makes several screen readers announce
 * the same content twice — the alert fires, then the newly focused container is read — which is
 * precisely the doubling checklist item A11Y-43 asks a runner to fail a screen for. The GOV.UK
 * Design System reached the same conclusion and removed the role from its own error summary.
 *
 * So the focus move is the announcement, and it is the reliable one: a live region that mounts
 * already containing its text is announced inconsistently across readers, while focus landing on a
 * labelled container is announced by all of them.
 *
 * The polite region below is therefore used in exactly one case: when the caller has passed
 * `autoFocus={false}` and there is no focus move to do the announcing. One channel or the other,
 * never both.
 *
 * ## Server errors
 *
 * `fieldErrorsFromProblemDetails` in the foundations turns an RFC 9457 validation problem into the
 * same `FieldErrorEntry` list a client-side check produces, so by the time an error reaches this
 * component there is no difference between the two. What is never rendered is the problem's own
 * `detail` or `title`: those can carry a stack or an identifier, and
 * docs/nfr/accessibility-localisation.md section 8.2 requires plain language plus the correlation
 * identifier for support, which is what `reference` is.
 */
export interface FormErrorSummaryProps {
  /** The errors, in the order they should be listed — usually the order of the fields on screen. */
  readonly errors: readonly FieldErrorEntry[]
  /** The wizard's steps, so an entry can name the step its field is on. */
  readonly steps?: readonly FormStep[]
  /** The step currently on screen. An entry on any other step navigates before it moves focus. */
  readonly currentStepId?: string
  /** Called with the step to open before focus moves. Required for cross-step entries to work. */
  readonly onNavigateToStep?: (stepId: string) => void
  /** The correlation identifier from a server problem detail, quoted for support. Never a stack. */
  readonly reference?: string
  /**
   * Changes on each submit attempt. The summary takes focus whenever it changes, so a second failed
   * submit brings the person back to the summary rather than leaving them where they were.
   */
  readonly submissionId?: string | number
  /**
   * Whether the summary takes focus. Leave it on for a form validated at submit. Turn it off for a
   * form that revalidates as the person types, where moving focus would take it out of the field
   * they are still working in — the polite region announces the change instead.
   */
  readonly autoFocus?: boolean
}

export function FormErrorSummary(props: FormErrorSummaryProps) {
  const {
    errors,
    steps = [],
    currentStepId,
    onNavigateToStep,
    reference,
    submissionId,
    autoFocus = true,
  } = props
  const intl = useIntl()
  const ids = useFieldIds()
  const containerRef = useRef<HTMLDivElement>(null)
  /** The control to focus once a step change has rendered. A ref, so no render is spent on it. */
  const pendingFocusRef = useRef<string | null>(null)

  const count = errors.length
  const title = intl.formatMessage({ id: 'forms.errorSummary.title' }, { count })
  /*
   * Derived, not stored. When focus moves, the live region stays empty and the focus move is the
   * announcement; when the caller has turned focus off, the region carries the title and is the
   * only channel. It is never both, which is what checklist item A11Y-43 is asking about.
   */
  const announcement = autoFocus ? '' : title

  useEffect(() => {
    if (count > 0 && autoFocus) {
      containerRef.current?.focus()
    }
  }, [autoFocus, count, submissionId])

  /*
   * Focus after a step change. The click handler asks the caller to open the step and records what
   * to focus; the step change re-renders this component, and by the time this effect runs the new
   * step is in the DOM. One attempt only: a control still absent means the caller's step change is
   * asynchronous, and retrying on a timer would move focus under a person's hands later.
   */
  useEffect(() => {
    const pending = pendingFocusRef.current
    if (pending === null) {
      return
    }
    pendingFocusRef.current = null
    document.getElementById(pending)?.focus()
  })

  if (count === 0) {
    return null
  }

  const stepLabel = (stepId: string | undefined): string | undefined => {
    if (stepId === undefined || stepId === currentStepId) {
      return undefined
    }
    return steps.find((step) => step.id === stepId)?.label
  }

  return (
    <div
      aria-labelledby={ids.label}
      className="form-error-summary"
      ref={containerRef}
      role="group"
      tabIndex={-1}
    >
      <h2 className="form-error-summary__title" id={ids.label}>
        {title}
      </h2>
      <p className="form-error-summary__instruction">
        {intl.formatMessage({ id: 'forms.errorSummary.instruction' })}
      </p>
      <ul className="form-error-summary__list">
        {errors.map((entry) => {
          const step = stepLabel(entry.stepId)
          const text =
            step === undefined
              ? entry.message
              : intl.formatMessage(
                  { id: 'forms.errorSummary.entryOnStep' },
                  { message: entry.message, step },
                )
          return (
            <li key={`${entry.name}:${entry.message}`}>
              <button
                className="form-error-summary__link"
                onClick={() => {
                  const target = document.getElementById(entry.controlId)
                  if (target !== null) {
                    target.focus()
                    return
                  }
                  // Not on screen: open the step it lives on, then focus it once that has rendered.
                  if (entry.stepId !== undefined && entry.stepId !== currentStepId) {
                    onNavigateToStep?.(entry.stepId)
                  }
                  pendingFocusRef.current = entry.controlId
                }}
                type="button"
              >
                {text}
              </button>
            </li>
          )
        })}
      </ul>
      {reference === undefined ? null : (
        <p className="form-error-summary__reference">
          {intl.formatMessage({ id: 'forms.errorSummary.reference' }, { reference })}
        </p>
      )}
      <span className="visually-hidden" role="status">
        {announcement}
      </span>
    </div>
  )
}
