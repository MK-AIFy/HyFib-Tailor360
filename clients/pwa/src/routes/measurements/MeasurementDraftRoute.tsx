import { useEffect, useRef, useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useNavigate, useParams } from 'react-router'
import type { TemplateField } from '../../admin/types'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ApiError } from '../../auth/apiClient'
import { useShellStatus } from '../../components/layout/useShellStatus'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { FormErrorSummary } from '../../design-system/components/forms/FormErrorSummary'
import { Select } from '../../design-system/components/forms/Select'
import { formattersForLocale } from '../../design-system/components/forms/formatting'
import type { MeasurementDisplayUnit } from '../../design-system/components/forms/measurement'
import type { FieldErrorEntry } from '../../design-system/foundations/FieldProps'
import type { FieldGroup } from '../../admin/templateFieldOrder'
import { CaptureField } from '../../measurements/CaptureField'
import {
  REVIEW_STEP_ID,
  captureGroups,
  captureKindOf,
  centimetreDecimalsOf,
  defaultDisplayUnitOf,
  findingErrorsOf,
  fractionStepOf,
  localErrorsOf,
  sectionRequestOf,
  stateOf,
  stepIdOf,
  visibilityOf,
} from '../../measurements/capture'
import type { CaptureState, CapturedFieldState } from '../../measurements/capture'
import {
  checkMeasurementDraft,
  confirmMeasurements,
  readMeasurementDraft,
  readMeasurementDraftTemplate,
  saveMeasurementSection,
} from '../../measurements/measurementsApi'
import {
  measurementProblemCode,
  measurementProblemMessage,
} from '../../measurements/measurementProblems'
import type {
  MeasurementCaptureTemplate,
  MeasurementDraft,
  MeasurementVersion,
} from '../../measurements/types'
import './measurements.css'

/**
 * The measurement capture wizard (#123): the screen the whole of #27 exists for.
 *
 * ## What is held where
 *
 * The route reads the draft and the template version it is pinned to, and remounts the wizard when
 * a person asks for a reload after a conflict — nothing else remounts it, because a save answers
 * with a fresh tag and a fresh draft and the wizard takes those in place. The wizard holds the
 * values in canonical millimetres, the step, the display unit, the tag the next save presents, and
 * the retry key of every act that has been committed to and not yet succeeded.
 *
 * ## When it saves
 *
 * On every step change, in either direction, and on "Save and leave" — never on a timer and never
 * on blur. A save is a `POST` with `If-Match`, and saving a half-typed fraction on every keystroke
 * would race the tag against the person's own typing. Nothing auto-advances: a wizard that jumps
 * when a value looks complete takes a wrong measurement from somebody holding a tape.
 *
 * ## What blocks a step, and what does not
 *
 * Moving forward is blocked by a required field left empty and by a value outside the hard bounds,
 * and the summary at the top names each one and moves focus to it. A value in the confirmation
 * band never blocks: it warns, offers the acknowledgement, and the server decides at confirmation
 * whether the acknowledgement was given. Moving *back* is blocked by nothing — the fields behind
 * the person are the ones already checked.
 *
 * ## Confirming
 *
 * The server is asked first (`/check`) so every finding arrives at once, mapped into the same
 * summary as the client's own. Then the confirmation, with a retry key minted when the person
 * committed and held across a refusal and across an in-place re-authentication, so an interrupted
 * confirmation retried cannot produce two versions. The key is forgotten only on success.
 */
export function MeasurementDraftRoute() {
  const intl = useIntl()
  const { draftId } = useParams()
  const [reloads, setReloads] = useState(0)

  const loaded = useAdminResource(
    `measurement-draft:${draftId ?? ''}:${String(reloads)}`,
    async (signal) => {
      const id = draftId ?? ''
      const [draft, template] = await Promise.all([
        readMeasurementDraft(id, signal),
        readMeasurementDraftTemplate(id, signal),
      ])
      return { draft: draft.value, version: draft.version, template }
    },
  )

  const notFound = measurementProblemCode(loaded.failure) === 'measurements.draft-not-found'
  const expired = measurementProblemCode(loaded.failure) === 'measurements.draft-expired'

  return (
    <section className="page measurements capture">
      <h1>
        <FormattedMessage id="measurements.title" />
      </h1>

      {notFound || expired ? (
        <EmptyState
          iconName="alert-circle"
          live="polite"
          title={intl.formatMessage({
            id: expired ? 'measurements.wizard.expired.title' : 'measurements.title',
          })}
          actions={
            <Link to="/measurements/new">
              {intl.formatMessage({ id: 'measurements.wizard.confirmed.another' })}
            </Link>
          }
        >
          {intl.formatMessage({
            id: expired ? 'measurements.wizard.expired.body' : 'measurements.wizard.notFound',
          })}
        </EmptyState>
      ) : (
        <AuthProblemAlert failure={loaded.failure} />
      )}

      {loaded.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'measurements.wizard.loading' })} />
      ) : loaded.value === null ? null : loaded.value.draft.consumedAt !== null ? (
        <EmptyState
          iconName="check"
          live="polite"
          title={intl.formatMessage({ id: 'measurements.wizard.consumed.title' })}
          actions={
            <Link to="/measurements/new">
              {intl.formatMessage({ id: 'measurements.wizard.confirmed.another' })}
            </Link>
          }
        >
          {intl.formatMessage({ id: 'measurements.wizard.consumed.body' })}
        </EmptyState>
      ) : (
        <CaptureWizard
          key={reloads}
          draft={loaded.value.draft}
          initialVersion={loaded.value.version}
          template={loaded.value.template}
          onReload={() => {
            setReloads((count) => count + 1)
          }}
        />
      )}
    </section>
  )
}

interface CaptureWizardProps {
  readonly draft: MeasurementDraft
  /** The tag the read carried. Undefined only if the server sent none, which it never does here. */
  readonly initialVersion: string | undefined
  readonly template: MeasurementCaptureTemplate
  readonly onReload: () => void
}

function CaptureWizard({ draft, initialVersion, template, onReload }: CaptureWizardProps) {
  const intl = useIntl()
  const navigate = useNavigate()
  const network = useNetworkState()
  const status = useShellStatus()
  const formatters = formattersForLocale(intl.locale)

  const version = template.version
  const groups = captureGroups(version)
  const steps = [
    ...groups.map((group) => ({ id: stepIdOf(group), label: group.name })),
    { id: REVIEW_STEP_ID, label: intl.formatMessage({ id: 'measurements.wizard.review' }) },
  ]

  const [state, setState] = useState<CaptureState>(() => stateOf(draft))
  const [unit, setUnit] = useState<MeasurementDisplayUnit>(() => defaultDisplayUnitOf(version))
  const [pendingUnit, setPendingUnit] = useState<MeasurementDisplayUnit | null>(null)
  const [stepId, setStepId] = useState<string>(steps[0]?.id ?? REVIEW_STEP_ID)
  const [tag, setTag] = useState<string | undefined>(initialVersion)
  /** The groups changed since their last save, by name. */
  const [dirty, setDirty] = useState<ReadonlySet<string>>(() => new Set())
  /** The retry key of every act committed to and not yet succeeded, by what it was for. */
  const [keys, setKeys] = useState<Readonly<Record<string, string>>>({})

  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)
  const [conflict, setConflict] = useState(false)
  const [errors, setErrors] = useState<readonly FieldErrorEntry[]>([])
  const [submission, setSubmission] = useState(0)
  const [confirming, setConfirming] = useState(false)
  const [confirmed, setConfirmed] = useState<MeasurementVersion | null>(null)

  const stepIndex = steps.findIndex((step) => step.id === stepId)
  const currentGroup = groups.find((group) => stepIdOf(group) === stepId)

  /**
   * Focus lands on the new step's heading after Back or Next, never on first paint. A step change
   * is a change of context the person asked for, and a heading is where a screen reader expects a
   * new page-like region to begin (2.4.3); the summary's own links move focus into the field they
   * name instead, so they do not set this.
   */
  const headingRef = useRef<HTMLHeadingElement>(null)
  const focusHeading = useRef(false)
  useEffect(() => {
    if (focusHeading.current) {
      focusHeading.current = false
      headingRef.current?.focus()
    }
  }, [stepId])

  const keyFor = (id: string): string => {
    const existing = keys[id]
    if (existing !== undefined) {
      return existing
    }
    const minted = crypto.randomUUID()
    setKeys((all) => ({ ...all, [id]: minted }))
    return minted
  }

  const forget = (id: string): void => {
    setKeys((all) => Object.fromEntries(Object.entries(all).filter(([spent]) => spent !== id)))
  }

  const unitName = (which: MeasurementDisplayUnit): string =>
    intl.formatMessage({
      id:
        which === 'cm' ? 'measurements.wizard.unit.centimetres' : 'measurements.wizard.unit.inches',
    })

  const formatBound = (field: TemplateField, millimetres: number): string =>
    formatters.formatMeasurement(millimetres, {
      unit,
      step: fractionStepOf(field),
      decimals: centimetreDecimalsOf(field),
      unitLabel: intl.formatMessage({
        id: unit === 'cm' ? 'units.centimetre.symbol' : 'units.inch.symbol',
      }),
    })

  const messages = {
    required: (label: string) =>
      intl.formatMessage({ id: 'measurements.wizard.required' }, { label }),
    outOfRange: (label: string, minimum: string, maximum: string) =>
      intl.formatMessage({ id: 'forms.measurement.outOfRange' }, { label, minimum, maximum }),
  }

  const change = (key: string, next: CapturedFieldState): void => {
    setState((all) => ({ ...all, [key]: next }))
    const owner = groups.find((group) => group.fields.some((field) => field.key === key))
    if (owner !== undefined) {
      setDirty((all) => new Set([...all, owner.name]))
    }
    // A corrected field leaves the summary at once; the rest of the summary stays until the next
    // attempt, so the person can work down the list.
    setErrors((all) => all.filter((entry) => entry.name !== key))
  }

  /** Saves one group. Resolves true when the draft holds it, false when it does not. */
  const save = async (group: FieldGroup): Promise<boolean> => {
    if (!dirty.has(group.name)) {
      return true
    }
    if (tag === undefined) {
      // Fail closed: the read always carries a tag, so a missing one is a state not worth saving
      // from. Ask for it again rather than send a precondition the server would have to guess at.
      setConflict(true)
      return false
    }

    const id = `save:${group.name}`
    setBusy(true)
    setFailure(null)
    status.announceAutosave(intl.formatMessage({ id: 'measurements.wizard.saving' }))

    try {
      const saved = await saveMeasurementSection({
        draftId: draft.measurementDraftId,
        body: sectionRequestOf(group, state, unit),
        version: tag,
        idempotencyKey: keyFor(id),
      })
      forget(id)
      setTag(saved.version)
      setDirty((all) => new Set([...all].filter((name) => name !== group.name)))
      status.announceAutosave(intl.formatMessage({ id: 'measurements.wizard.saved' }))
      return true
    } catch (cause: unknown) {
      if (cause instanceof ApiError && cause.code === 'measurements.draft-changed') {
        setConflict(true)
      } else {
        setFailure(cause)
      }
      status.announceAutosave(
        intl.formatMessage(
          { id: 'measurements.wizard.notSaved' },
          {
            reason: intl.formatMessage({
              id: measurementProblemMessage(cause) ?? 'states.problem.unknown',
            }),
          },
        ),
      )
      return false
    } finally {
      setBusy(false)
    }
  }

  const goTo = (index: number): void => {
    const step = steps[index]
    if (step !== undefined) {
      focusHeading.current = true
      setStepId(step.id)
      setErrors([])
    }
  }

  const next = async (): Promise<void> => {
    if (currentGroup === undefined) {
      return
    }
    const found = localErrorsOf([currentGroup], state, formatBound, messages)
    setSubmission((count) => count + 1)
    setErrors(found)
    if (found.length > 0) {
      return
    }
    if (await save(currentGroup)) {
      goTo(stepIndex + 1)
    }
  }

  const back = async (): Promise<void> => {
    if (currentGroup !== undefined && !(await save(currentGroup))) {
      return
    }
    goTo(stepIndex - 1)
  }

  const leave = async (): Promise<void> => {
    if (currentGroup !== undefined && !(await save(currentGroup))) {
      return
    }
    await navigate('/measurements')
  }

  const confirm = async (): Promise<void> => {
    setConfirming(false)
    if (tag === undefined) {
      setConflict(true)
      return
    }

    setBusy(true)
    setFailure(null)
    setSubmission((count) => count + 1)

    try {
      const check = await checkMeasurementDraft(draft.measurementDraftId)
      if (!check.confirmable) {
        const mapped = findingErrorsOf(check.findings, groups)
        // A refusal the mapping could place nowhere is still a refusal: say so at the review step
        // rather than leave the person pressing Confirm against silence.
        const found =
          mapped.length > 0
            ? mapped
            : [
                {
                  name: 'check',
                  message: intl.formatMessage({ id: 'measurements.wizard.check.failed' }),
                  controlId: 'capture-review',
                  stepId: REVIEW_STEP_ID,
                },
              ]
        setErrors(found)
        const first = found[0]
        if (first?.stepId !== undefined && first.stepId !== REVIEW_STEP_ID) {
          setStepId(first.stepId)
        }
        return
      }

      const record = await confirmMeasurements({
        draftId: draft.measurementDraftId,
        body: { reason: null, correctsVersionId: null },
        version: tag,
        idempotencyKey: keyFor('confirm'),
      })
      forget('confirm')
      setConfirmed(record)
    } catch (cause: unknown) {
      if (cause instanceof ApiError && cause.code === 'measurements.draft-changed') {
        setConflict(true)
      } else {
        setFailure(cause)
      }
    } finally {
      setBusy(false)
    }
  }

  const errorFor = (key: string): string | undefined =>
    errors.find((entry) => entry.name === key)?.message

  const reviewValue = (field: TemplateField): string => {
    const held = state[field.key]
    if (!visibilityOf(field, state).isShown) {
      return intl.formatMessage({ id: 'measurements.wizard.review.notAsked' })
    }
    const kind = captureKindOf(field)
    if (kind === 'choice') {
      const chosen = field.options.find((option) => option.code === held?.choice)
      return chosen?.label ?? intl.formatMessage({ id: 'measurements.wizard.review.empty' })
    }
    if (held?.millimetres === undefined) {
      return intl.formatMessage({ id: 'measurements.wizard.review.empty' })
    }
    return kind === 'count'
      ? formatters.formatNumber(held.millimetres)
      : formatBound(field, held.millimetres)
  }

  if (confirmed !== null) {
    return (
      <Alert
        live="polite"
        title={intl.formatMessage({ id: 'measurements.wizard.confirmed.title' })}
        tone="success"
        actions={
          <Link to="/measurements/new">
            {intl.formatMessage({ id: 'measurements.wizard.confirmed.another' })}
          </Link>
        }
      >
        {intl.formatMessage(
          { id: 'measurements.wizard.confirmed.body' },
          {
            version: String(confirmed.versionNumber),
            date: formatters.formatDateTime(confirmed.takenAt),
          },
        )}
      </Alert>
    )
  }

  return (
    <div className="capture__wizard">
      <p className="measurements__lede">
        {intl.formatMessage(
          { id: 'measurements.wizard.open' },
          {
            template: template.name,
            version: String(version.versionNumber),
            date:
              version.publishedAt === null ? '—' : formatters.formatShortDate(version.publishedAt),
          },
        )}
      </p>

      {draft.reusedFromVersionId === null ? null : (
        <Alert live="off" tone="info">
          {intl.formatMessage({ id: 'measurements.wizard.reused' })}
        </Alert>
      )}

      {/* The position in the wizard, in text. A progress bar alone tells a screen reader nothing. */}
      <nav aria-label={intl.formatMessage({ id: 'measurements.wizard.steps' })}>
        <p className="capture__progress">
          {intl.formatMessage(
            { id: 'measurements.wizard.progress' },
            { index: stepIndex + 1, count: steps.length },
          )}
        </p>
        <ol className="capture__steps">
          {steps.map((step, index) => (
            <li aria-current={step.id === stepId ? 'step' : undefined} key={step.id}>
              {intl.formatMessage(
                { id: 'measurements.wizard.step' },
                { index: index + 1, name: step.label },
              )}
            </li>
          ))}
        </ol>
      </nav>

      {conflict ? (
        <Alert
          live="assertive"
          title={intl.formatMessage({ id: 'measurements.wizard.conflict.title' })}
          tone="warning"
          actions={
            <Button iconName="refresh" onClick={onReload} variant="secondary">
              {intl.formatMessage({ id: 'measurements.wizard.conflict.reload' })}
            </Button>
          }
        >
          {intl.formatMessage({ id: 'measurements.wizard.conflict.body' })}
        </Alert>
      ) : null}

      <AuthProblemAlert failure={failure} />

      <FormErrorSummary
        currentStepId={stepId}
        errors={errors}
        onNavigateToStep={setStepId}
        steps={steps}
        submissionId={submission}
      />

      {stepId === REVIEW_STEP_ID ? (
        <section aria-labelledby="capture-review" className="capture__section">
          <h2 className="capture__heading" id="capture-review" ref={headingRef} tabIndex={-1}>
            {intl.formatMessage({ id: 'measurements.wizard.review.title' })}
          </h2>
          <p>{intl.formatMessage({ id: 'measurements.wizard.review.hint' })}</p>

          {groups.map((group) => (
            <div className="capture__reviewGroup" key={group.name}>
              <h3>{group.name}</h3>
              <dl className="capture__summary">
                {group.fields.map((field) => (
                  <div key={field.templateFieldId}>
                    <dt>{field.label}</dt>
                    <dd>{reviewValue(field)}</dd>
                  </div>
                ))}
              </dl>
            </div>
          ))}
        </section>
      ) : currentGroup === undefined ? null : (
        <section aria-labelledby="capture-group" className="capture__section">
          <h2 className="capture__heading" id="capture-group" ref={headingRef} tabIndex={-1}>
            {currentGroup.name}
          </h2>

          <Select
            description={intl.formatMessage({ id: 'measurements.wizard.unit.hint' })}
            emptyLabel={null}
            id="capture-unit"
            label={intl.formatMessage({ id: 'measurements.wizard.unit.label' })}
            name="displayUnit"
            onValueChange={(next) => {
              const wanted: MeasurementDisplayUnit = next === 'cm' ? 'cm' : 'in'
              if (wanted !== unit) {
                setPendingUnit(wanted)
              }
            }}
            options={[
              { value: 'in', label: unitName('in') },
              { value: 'cm', label: unitName('cm') },
            ]}
            value={unit}
          />

          <div className="capture__fields">
            {currentGroup.fields.map((field) => {
              const error = errorFor(field.key)
              return (
                <CaptureField
                  field={field}
                  key={field.templateFieldId}
                  onChange={change}
                  state={state}
                  unit={unit}
                  {...(error === undefined ? {} : { error })}
                />
              )
            })}
          </div>
        </section>
      )}

      {network.online ? (
        <div className="capture__bar">
          <Button
            busy={busy}
            iconName="chevron-left"
            onClick={() => {
              void back()
            }}
            unavailable={stepIndex === 0}
            variant="secondary"
          >
            {intl.formatMessage({ id: 'measurements.wizard.back' })}
          </Button>
          <Button
            busy={busy}
            iconName="clipboard"
            onClick={() => {
              void leave()
            }}
            variant="subtle"
          >
            {intl.formatMessage({ id: 'measurements.wizard.leave' })}
          </Button>
          {stepId === REVIEW_STEP_ID ? (
            <Button
              busy={busy}
              iconName="check"
              onClick={() => {
                setConfirming(true)
              }}
              size="primary"
              variant="primary"
            >
              {intl.formatMessage({
                id: busy ? 'measurements.wizard.confirming' : 'measurements.wizard.confirm',
              })}
            </Button>
          ) : (
            <Button
              busy={busy}
              iconName="chevron-right"
              iconPosition="trailing"
              onClick={() => {
                void next()
              }}
              size="primary"
              variant="primary"
            >
              {intl.formatMessage({
                id:
                  stepIndex === steps.length - 2
                    ? 'measurements.wizard.toReview'
                    : 'measurements.wizard.next',
              })}
            </Button>
          )}
        </div>
      ) : (
        <OfflineBlockedAction
          action={intl.formatMessage({
            id:
              stepId === REVIEW_STEP_ID
                ? 'measurements.wizard.offline.confirm'
                : 'measurements.wizard.offline.save',
          })}
        >
          {intl.formatMessage({ id: 'states.blocked.inputKept' })}
        </OfflineBlockedAction>
      )}

      {pendingUnit === null ? null : (
        <ConfirmDialog
          open
          action="displayUnit"
          cancelLabel={intl.formatMessage({ id: 'dialogs.cancel' })}
          confirmLabel={intl.formatMessage(
            { id: 'measurements.wizard.unit.confirm.action' },
            { unit: unitName(pendingUnit) },
          )}
          onCancel={() => {
            setPendingUnit(null)
          }}
          onConfirm={() => {
            setUnit(pendingUnit)
            setPendingUnit(null)
            status.announceAutosave(
              intl.formatMessage(
                { id: 'measurements.wizard.unit.changed' },
                { unit: unitName(pendingUnit) },
              ),
            )
          }}
          tier="confirm"
          title={intl.formatMessage(
            { id: 'measurements.wizard.unit.confirm.title' },
            { unit: unitName(pendingUnit) },
          )}
        >
          {intl.formatMessage({ id: 'measurements.wizard.unit.confirm.body' })}
        </ConfirmDialog>
      )}

      {confirming ? (
        <ConfirmDialog
          open
          action="confirm"
          busy={busy}
          cancelLabel={intl.formatMessage({ id: 'dialogs.cancel' })}
          confirmLabel={intl.formatMessage({ id: 'measurements.wizard.confirm' })}
          irreversible
          onCancel={() => {
            setConfirming(false)
          }}
          onConfirm={() => {
            void confirm()
          }}
          tier="confirm"
          title={intl.formatMessage({ id: 'measurements.wizard.confirm.title' })}
        >
          {intl.formatMessage({ id: 'measurements.wizard.confirm.body' })}
        </ConfirmDialog>
      ) : null}
    </div>
  )
}
