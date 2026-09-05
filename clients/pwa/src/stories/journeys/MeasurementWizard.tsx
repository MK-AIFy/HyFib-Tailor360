import { useState } from 'react'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { useShellStatus } from '../../components/layout/useShellStatus'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ButtonGroup } from '../../components/primitives/ButtonGroup'
import { Card } from '../../components/primitives/Card'
import { useDemoText } from '../../components/primitives/demoText'
import { FormErrorSummary } from '../../design-system/components/forms/FormErrorSummary'
import { MeasurementField } from '../../design-system/components/forms/MeasurementField'
import { Select } from '../../design-system/components/forms/Select'
import {
  evaluateMeasurement,
  isRejectedBand,
} from '../../design-system/components/forms/measurement'
import type { MeasurementDisplayUnit } from '../../design-system/components/forms/measurement'
import type { FieldErrorEntry } from '../../design-system/foundations/FieldProps'
import { getFormatters } from '../../i18n/formatters'
import { STAFF } from '../fixtures/branch'
import { CUSTOMERS } from '../fixtures/customers'
import {
  MEASUREMENT_FIELDS,
  MEASUREMENT_STEPS,
  MEASUREMENT_TEMPLATE,
} from '../fixtures/measurements'

type StepId = (typeof MEASUREMENT_STEPS)[number]['id']

const CONTROL_ID_PREFIX = 'measurement-'

/**
 * `A11Y-RJ-02` — Measurement Staff: the measurement wizard.
 *
 * The journey with its own record in docs/nfr/a11y-checklist.md section 6.8, because family 5.1
 * covers the screens and nothing covered the journey. All ten of its steps are reachable here.
 *
 * ## The three things this screen exists to prove
 *
 * **A fraction is one value, not two controls.** `MeasurementField` renders whole inches and a
 * fraction, and reads back as "36 1/2 in" — step 3 of the record. Switching the unit re-renders the
 * same canonical millimetres as centimetres (step 4); nothing is converted in storage, ever, and
 * nothing is rounded on the way through.
 *
 * **A warning is not an error.** A value inside the confirmation band is unusual but allowed: it
 * shows a warning, offers an acknowledgement, and never blocks the step (steps 6 and 7). A value
 * outside the hard bounds is an error, is announced with the range *in the unit on screen*, and does
 * block. Two different things, two different treatments, and the difference is what a tailor needs
 * at the tape.
 *
 * **The error summary is step-aware.** A failed confirm on the review step lists errors from the
 * bodice step, and its link opens that step before it moves focus — checklist item A11Y-36, and the
 * reason `FormErrorSummary` takes `steps`, `currentStepId` and `onNavigateToStep` at all.
 *
 * The measurement values are canonical millimetres throughout, which is the rule the whole product
 * is built on: the tape is imperial, the database is metric, and the conversion happens once, at the
 * edge, in a component that has been tested.
 */
export function MeasurementWizardScreen() {
  const t = useDemoText()
  const formatters = getFormatters()
  const status = useShellStatus()

  const [stepId, setStepId] = useState<StepId>('bodice')
  const [unit, setUnit] = useState<MeasurementDisplayUnit>('in')
  const [values, setValues] = useState<Readonly<Record<string, number>>>({})
  const [acknowledged, setAcknowledged] = useState<Readonly<Record<string, boolean>>>({})
  const [submission, setSubmission] = useState(0)
  const [visibleErrors, setVisibleErrors] = useState<readonly FieldErrorEntry[]>([])
  const [confirming, setConfirming] = useState(false)
  const [confirmedVersion, setConfirmedVersion] = useState<number | null>(null)

  const customer = CUSTOMERS[0]

  /** Every field that is missing or outside its hard bounds, across every step. */
  const errors: readonly FieldErrorEntry[] = MEASUREMENT_FIELDS.flatMap((field) => {
    const value = values[field.name]
    const controlId = `${CONTROL_ID_PREFIX}${field.name}`

    if (value === undefined) {
      return [
        {
          name: field.name,
          message: `${field.label} is required.`,
          controlId,
          stepId: field.stepId,
        },
      ]
    }

    if (!isRejectedBand(evaluateMeasurement(value, field.bounds))) {
      return []
    }

    const { minimumMillimetres: min, maximumMillimetres: max } = field.bounds
    const bound = (millimetres: number) =>
      formatters.formatMeasurement(millimetres, { unit, step: field.fractionStep })

    return [
      {
        name: field.name,
        message:
          min !== undefined && max !== undefined
            ? `${field.label} must be between ${bound(min)} and ${bound(max)}.`
            : `${field.label} is outside the allowed range.`,
        controlId,
        stepId: field.stepId,
      },
    ]
  })

  /** Narrows the summary's `stepId` — a plain string — back to one of this wizard's own steps. */
  function openStep(target: string) {
    const step = MEASUREMENT_STEPS.find((candidate) => candidate.id === target)
    if (step !== undefined) {
      setStepId(step.id)
    }
  }

  const visibleFields = MEASUREMENT_FIELDS.filter((field) => field.stepId === stepId)
  const stepIndex = MEASUREMENT_STEPS.findIndex((step) => step.id === stepId)

  /**
   * Validates and, if there is nothing to report, moves on.
   *
   * `scope` is the whole point. Moving from the bodice step to the sleeve step must not be blocked
   * by the sleeve fields being empty — nobody has reached them yet. Confirming the version must be
   * blocked by any field on any step, and the summary's link then has to open the step it belongs
   * to before it moves focus, which is checklist item A11Y-36.
   */
  function attempt(scope: 'step' | 'all', next: () => void) {
    const found = scope === 'all' ? errors : errors.filter((entry) => entry.stepId === stepId)
    setSubmission((previous) => previous + 1)
    setVisibleErrors(found)
    if (found.length === 0) {
      next()
    }
  }

  return (
    <section className="page journey-screen">
      <h1>{t('Measurements')}</h1>
      <p>
        {customer?.name} · {MEASUREMENT_TEMPLATE.code} version {MEASUREMENT_TEMPLATE.version},
        published {formatters.formatShortDate(MEASUREMENT_TEMPLATE.publishedOn)}. Taken by{' '}
        {STAFF.measurementStaff}.
      </p>

      {/* The position in the wizard, in text. A progress bar alone tells a screen reader nothing. */}
      <ol className="journey-steps">
        {MEASUREMENT_STEPS.map((step, index) => (
          <li aria-current={step.id === stepId ? 'step' : undefined} key={step.id}>
            {index + 1}. {t(step.label)}
          </li>
        ))}
      </ol>

      {/* Renders nothing at all while there is nothing to report, so it is safe to leave mounted. */}
      <FormErrorSummary
        currentStepId={stepId}
        errors={visibleErrors}
        onNavigateToStep={openStep}
        steps={MEASUREMENT_STEPS.map((step) => ({ id: step.id, label: t(step.label) }))}
        submissionId={submission}
      />

      {confirmedVersion === null ? null : (
        <Alert live="polite" title={t('Measurement version confirmed')} tone="success">
          Version {confirmedVersion} is now immutable. A change from here is a new version, taken by
          the person who takes it and recorded against a reason.
        </Alert>
      )}

      {stepId === 'review' ? (
        <section aria-labelledby="wizard-review" className="journey-section">
          <h2 id="wizard-review">{t('Review and confirm')}</h2>
          <Card headingLevel={3} title={t('Every value, in the unit on screen')}>
            <dl className="journey-summary">
              {MEASUREMENT_FIELDS.map((field) => {
                const value = values[field.name]
                return (
                  <div key={field.name}>
                    <dt>{t(field.label)}</dt>
                    <dd className="journey-amount">
                      {value === undefined
                        ? '—'
                        : formatters.formatMeasurement(value, {
                            unit,
                            step: field.fractionStep,
                          })}
                    </dd>
                  </div>
                )
              })}
            </dl>
          </Card>
        </section>
      ) : (
        <section aria-labelledby="wizard-fields" className="journey-section">
          <h2 id="wizard-fields">{t(MEASUREMENT_STEPS[stepIndex]?.label ?? '')}</h2>

          <Select
            description={t('Millimetres are stored whichever unit is shown.')}
            emptyLabel={null}
            label={t('Show measurements in')}
            name="displayUnit"
            onValueChange={(next) => {
              setUnit(next === 'cm' ? 'cm' : 'in')
              status.announceAutosave(
                next === 'cm' ? t('Showing centimetres.') : t('Showing inches.'),
              )
            }}
            options={[
              { value: 'in', label: t('Inches') },
              { value: 'cm', label: t('Centimetres') },
            ]}
            value={unit}
          />

          <ButtonGroup>
            <Button
              iconName="refresh"
              onClick={() => {
                setValues(
                  Object.fromEntries(
                    MEASUREMENT_FIELDS.map((field) => [field.name, field.previousMillimetres]),
                  ),
                )
                status.announceAutosave(
                  `Values from version ${String(MEASUREMENT_TEMPLATE.previousVersion)} copied in. Change any that have moved.`,
                )
              }}
            >
              {t(`Use version ${String(MEASUREMENT_TEMPLATE.previousVersion)} values`)}
            </Button>
          </ButtonGroup>

          <div className="journey-fields" data-columns="2">
            {visibleFields.map((field) => (
              <MeasurementField
                acknowledged={acknowledged[field.name] ?? false}
                bounds={field.bounds}
                description={t(field.description)}
                displayUnit={unit}
                fractionStep={field.fractionStep}
                id={`${CONTROL_ID_PREFIX}${field.name}`}
                key={field.name}
                label={t(field.label)}
                name={field.name}
                onAcknowledgedChange={(next) => {
                  setAcknowledged((previous) => ({ ...previous, [field.name]: next }))
                }}
                onValueChange={(next) => {
                  setValues((previous) => ({ ...previous, [field.name]: next }))
                }}
                required
                {...(values[field.name] === undefined ? {} : { value: values[field.name] })}
              />
            ))}
          </div>
        </section>
      )}

      <ButtonGroup size="primary">
        {stepIndex > 0 ? (
          <Button
            iconName="chevron-left"
            onClick={() => {
              openStep(MEASUREMENT_STEPS[stepIndex - 1]?.id ?? 'bodice')
            }}
          >
            {t('Back')}
          </Button>
        ) : null}

        {stepId === 'review' ? (
          <Button
            iconName="check"
            onClick={() => {
              attempt('all', () => {
                setConfirming(true)
              })
            }}
            size="primary"
            variant="primary"
          >
            {t('Confirm this version')}
          </Button>
        ) : (
          <Button
            iconName="chevron-right"
            iconPosition="trailing"
            onClick={() => {
              attempt('step', () => {
                openStep(MEASUREMENT_STEPS[stepIndex + 1]?.id ?? 'review')
              })
            }}
            size="primary"
            variant="primary"
          >
            {t('Next')}
          </Button>
        )}
      </ButtonGroup>

      <ConfirmDialog
        action="confirming this measurement version"
        confirmLabel={t('Confirm version')}
        irreversible
        onCancel={() => {
          setConfirming(false)
        }}
        onConfirm={() => {
          setConfirming(false)
          setConfirmedVersion(MEASUREMENT_TEMPLATE.previousVersion + 1)
        }}
        open={confirming}
        tier="reason"
        title={t('Confirm this measurement version?')}
      >
        The draft becomes version {MEASUREMENT_TEMPLATE.previousVersion + 1} and can never be
        edited. Later changes are a new version, not a correction of this one.
      </ConfirmDialog>
    </section>
  )
}
