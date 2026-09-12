import { useEffect, useRef, useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import type { TemplateField } from '../../admin/types'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { MeasurementProblemAlert } from '../../measurements/MeasurementProblemAlert'
import { ApiError } from '../../auth/apiClient'
import { useShellStatus } from '../../components/layout/useShellStatus'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import type { ConfirmOutcome } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
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
  effectiveUnitOf,
  findingErrorsOf,
  groupsWithVisibilityChanged,
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
  readMeasurement,
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
  const [params] = useSearchParams()
  // A correction is a draft pre-filled from the version it replaces, and the address says so: a
  // draft is shared within the branch, and the address is what a colleague picks up (#124).
  const corrects = params.get('corrects')
  const [reloads, setReloads] = useState(0)

  const loaded = useAdminResource(
    `measurement-draft:${draftId ?? ''}:${corrects ?? ''}:${String(reloads)}`,
    async (signal) => {
      const id = draftId ?? ''
      const draft = await readMeasurementDraft(id, signal)
      // The version this draft came from: the one the address names, else the one the draft was
      // pre-filled from. Either can be offered as the version a correction replaces; the draft is
      // read first because it is what says whether there is a second candidate at all.
      const sourceId = corrects ?? draft.value.reusedFromVersionId
      const [template, source] = await Promise.all([
        readMeasurementDraftTemplate(id, signal),
        sourceId === null ? Promise.resolve(null) : readSourceOrNull(sourceId, signal),
      ])
      return { draft: draft.value, version: draft.version, template, source }
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
          // Keyed by the tag the read carried, not by the reload count: the resource keeps the old
          // draft on screen while a re-read is in flight, so a key that changed on the press would
          // remount the wizard with the stale draft and the stale tag — and the next save would
          // meet the same conflict. A tag that changed is a draft that changed.
          key={loaded.value.version ?? 'untagged'}
          correctsRequested={corrects !== null}
          draft={loaded.value.draft}
          source={loaded.value.source}
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

/**
 * The source version, or null when it cannot be read. A reuse whose source has since become
 * unreadable is still a draft worth finishing, so a refusal here is not a refusal of the wizard;
 * what it costs is the offer to record the result as a correction, and the screen says so.
 */
async function readSourceOrNull(
  versionId: string,
  signal: AbortSignal,
): Promise<MeasurementVersion | null> {
  try {
    return await readMeasurement(versionId, signal)
  } catch (cause: unknown) {
    if (cause instanceof ApiError) {
      return null
    }
    throw cause
  }
}

interface CaptureWizardProps {
  readonly draft: MeasurementDraft
  /** Whether the address named a version to correct, as distinct from the draft having a source. */
  readonly correctsRequested: boolean
  /** The version the draft came from, when it has one and it could be read. */
  readonly source: MeasurementVersion | null
  /** The tag the read carried. Undefined only if the server sent none, which it never does here. */
  readonly initialVersion: string | undefined
  readonly template: MeasurementCaptureTemplate
  readonly onReload: () => void
}

function CaptureWizard({
  draft,
  correctsRequested,
  source,
  initialVersion,
  template,
  onReload,
}: CaptureWizardProps) {
  const intl = useIntl()
  const navigate = useNavigate()
  const network = useNetworkState()
  const status = useShellStatus()
  const formatters = formattersForLocale(intl.locale)

  /**
   * The version a correction may name: the source, if it is one of this customer's measurements
   * for this garment. The server refuses a correction of anything else, so the offer is withheld
   * here rather than made and then refused — and an address naming a version that does not belong
   * is said to be wrong, not silently treated as a plain reuse.
   */
  const correctable =
    source !== null &&
    source.customerId === draft.customerId &&
    source.measurementTemplateId === draft.measurementTemplateId
      ? source
      : null
  const sourceMismatch = correctsRequested && correctable === null
  /**
   * Whether the confirmation records a correction. Chosen on the review step, pre-set when the
   * address asked for one: a draft reused from an earlier version is by default a new
   * measurement, and turning it into a correction is a decision the person makes, with a reason.
   */
  const [correcting, setCorrecting] = useState(correctsRequested && correctable !== null)
  const corrects = correcting ? correctable : null

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
  /**
   * The fields changed since the last attempt. The summary keeps every entry until the next attempt
   * — `FormErrorSummary` takes focus whenever its count changes, so shrinking it as a person types
   * would pull focus out of the field under their hands — but the message *on* a field a person is
   * correcting comes off at the first keystroke, so they are not told to fix what they are fixing.
   */
  const [touched, setTouched] = useState<ReadonlySet<string>>(() => new Set())
  const [submission, setSubmission] = useState(0)
  const [confirming, setConfirming] = useState(false)
  const [confirmFailure, setConfirmFailure] = useState<unknown>(null)
  const [confirmed, setConfirmed] = useState<MeasurementVersion | null>(null)
  /** The draft is finished with, one way or the other, as a save or a confirmation found out. */
  const [closed, setClosed] = useState<'expired' | 'confirmed' | null>(null)

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

  const formatBound = (field: TemplateField, millimetres: number): string => {
    const fieldUnit = effectiveUnitOf(field, unit)
    return formatters.formatMeasurement(millimetres, {
      unit: fieldUnit,
      step: fractionStepOf(field),
      decimals: centimetreDecimalsOf(field),
      unitLabel: intl.formatMessage({
        id: fieldUnit === 'cm' ? 'units.centimetre.symbol' : 'units.inch.symbol',
      }),
    })
  }

  const messages = {
    required: (label: string) =>
      intl.formatMessage({ id: 'measurements.wizard.required' }, { label }),
    outOfRange: (label: string, minimum: string, maximum: string) =>
      intl.formatMessage({ id: 'forms.measurement.outOfRange' }, { label, minimum, maximum }),
  }

  const change = (key: string, next: CapturedFieldState): void => {
    const after = { ...state, [key]: next }
    setState(after)
    const owner = groups.find((group) => group.fields.some((field) => field.key === key))
    // The field's own group, and any group holding a field this answer just showed or hid: a
    // hidden field is dropped from its group's save, and a group that is not saved keeps it.
    const affected = [
      ...(owner === undefined ? [] : [owner.name]),
      ...groupsWithVisibilityChanged(groups, state, after),
    ]
    setDirty((all) => new Set([...all, ...affected]))
    setTouched((all) => new Set([...all, key]))
  }

  /** What a refusal means for the draft as a whole, when it means anything. */
  const noteRefusal = (cause: unknown): void => {
    if (!(cause instanceof ApiError)) {
      return
    }
    if (cause.code === 'measurements.draft-changed') {
      setConflict(true)
    } else if (cause.code === 'measurements.draft-expired') {
      setClosed('expired')
    } else if (cause.code === 'measurements.draft-already-confirmed') {
      setClosed('confirmed')
    }
  }

  /**
   * Saves one group against the tag given, and resolves to the tag the server answered with, or
   * null when the save did not land.
   *
   * The tag travels as an argument and a result rather than through state, because two groups
   * saved in one go — every dirty group before a confirmation — must each present the tag the
   * *previous* save answered with, and state set in the first would not be visible to the second.
   */
  const saveGroup = async (group: FieldGroup, against: string): Promise<string | null> => {
    const id = `save:${group.name}`
    status.announceAutosave(intl.formatMessage({ id: 'measurements.wizard.saving' }))

    try {
      const saved = await saveMeasurementSection({
        draftId: draft.measurementDraftId,
        body: sectionRequestOf(group, state, unit),
        version: against,
        idempotencyKey: keyFor(id),
      })
      forget(id)
      setDirty((all) => new Set([...all].filter((name) => name !== group.name)))
      status.announceAutosave(intl.formatMessage({ id: 'measurements.wizard.saved' }))
      // The server always answers a save with a tag; a missing one is a state not worth acting
      // on, and is treated as the conflict it would become.
      return saved.version ?? null
    } catch (cause: unknown) {
      noteRefusal(cause)
      if (!(cause instanceof ApiError && cause.code === 'measurements.draft-changed')) {
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
      return null
    }
  }

  /**
   * Saves every dirty group among those given, in template order, each against the tag the one
   * before answered with. Resolves to the tag the draft now carries, or null when a save did not
   * land — returned rather than read back from state, because the caller's closure still holds
   * the tag it rendered with.
   */
  const flush = async (candidates: readonly FieldGroup[]): Promise<string | null> => {
    if (tag === undefined) {
      // Fail closed. The read behind this screen always carries a tag, so a missing one means the
      // screen is not showing a state worth saving from — ask for it again rather than send a
      // precondition the server would have to guess at.
      setConflict(true)
      return null
    }

    const pending = candidates.filter((group) => dirty.has(group.name))
    if (pending.length === 0) {
      return tag
    }

    setBusy(true)
    setFailure(null)
    try {
      let against = tag
      for (const group of pending) {
        const answered = await saveGroup(group, against)
        if (answered === null) {
          return null
        }
        against = answered
        setTag(answered)
      }
      return against
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
      setTouched(new Set())
    }
  }

  const next = async (): Promise<void> => {
    if (currentGroup === undefined) {
      return
    }
    const found = localErrorsOf([currentGroup], state, formatBound, messages)
    setSubmission((count) => count + 1)
    setErrors(found)
    setTouched(new Set())
    if (found.length > 0) {
      return
    }
    if ((await flush([currentGroup])) !== null) {
      goTo(stepIndex + 1)
    }
  }

  const back = async (): Promise<void> => {
    if (currentGroup !== undefined && (await flush([currentGroup])) === null) {
      return
    }
    goTo(stepIndex - 1)
  }

  /**
   * Every dirty group, not only the one on screen: a summary link opens another step without
   * saving the one it left, and a value the person changed there and can see in the review must
   * not be the one thing the record does not hold.
   */
  const leave = async (): Promise<void> => {
    if ((await flush(groups)) === null) {
      return
    }
    await navigate('/measurements')
  }

  const confirm = async (outcome: ConfirmOutcome): Promise<void> => {
    setConfirmFailure(null)
    setSubmission((count) => count + 1)

    // Nothing unsaved reaches the record: every dirty group first, each against the tag the one
    // before answered with. A refusal here closes the dialog and is said on the screen behind it.
    const against = await flush(groups)
    if (against === null) {
      setConfirming(false)
      return
    }

    setBusy(true)
    setFailure(null)

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
        setTouched(new Set())
        setConfirming(false)
        const first = found[0]
        if (first?.stepId !== undefined && first.stepId !== REVIEW_STEP_ID) {
          setStepId(first.stepId)
        }
        return
      }

      const record = await confirmMeasurements({
        draftId: draft.measurementDraftId,
        // A correction carries the reason the dialog demanded and names the version it replaces;
        // the server refuses a correction without a reason, so the dialog collects it first.
        body: {
          reason: corrects === null ? null : (outcome.reason?.trim() ?? null),
          correctsVersionId: corrects?.measurementVersionId ?? null,
        },
        version: against,
        idempotencyKey: keyFor('confirm'),
      })
      forget('confirm')
      setConfirming(false)
      setConfirmed(record)
    } catch (cause: unknown) {
      // The dialog stays open on a refusal, with the refusal inside it: it is modal, so an alert
      // behind it would sit under the backdrop and outside the focus trap. Staying open is also
      // what keeps the retry key the next attempt must reuse.
      noteRefusal(cause)
      if (cause instanceof ApiError && cause.code === 'measurements.draft-changed') {
        setConfirming(false)
      } else {
        setConfirmFailure(cause)
      }
    } finally {
      setBusy(false)
    }
  }

  const errorFor = (key: string): string | undefined =>
    touched.has(key) ? undefined : errors.find((entry) => entry.name === key)?.message

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

  if (closed !== null) {
    return (
      <EmptyState
        iconName={closed === 'confirmed' ? 'check' : 'alert-circle'}
        live="assertive"
        title={intl.formatMessage({
          id:
            closed === 'confirmed'
              ? 'measurements.wizard.consumed.title'
              : 'measurements.wizard.expired.title',
        })}
        actions={
          <Link to="/measurements/new">
            {intl.formatMessage({ id: 'measurements.wizard.confirmed.another' })}
          </Link>
        }
      >
        {intl.formatMessage({
          id:
            closed === 'confirmed'
              ? 'measurements.wizard.consumed.body'
              : 'measurements.wizard.expired.body',
        })}
      </EmptyState>
    )
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
        {corrects === null
          ? null
          : ' ' +
            intl.formatMessage(
              { id: 'measurements.wizard.confirmed.corrects' },
              { number: String(corrects.versionNumber) },
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

      {corrects === null ? null : (
        <Alert live="off" tone="warning">
          {intl.formatMessage(
            { id: 'measurements.wizard.correcting' },
            {
              number: String(corrects.versionNumber),
              date: formatters.formatDateTime(corrects.takenAt),
            },
          )}
        </Alert>
      )}

      {sourceMismatch ? (
        <Alert live="off" tone="warning">
          {intl.formatMessage({ id: 'measurements.wizard.correction.mismatch' })}
        </Alert>
      ) : null}

      {draft.reusedFromVersionId === null || corrects !== null ? null : (
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

      <MeasurementProblemAlert failure={failure} />

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

          {correctable === null ? null : (
            <Checkbox
              description={intl.formatMessage({ id: 'measurements.wizard.correction.toggle.hint' })}
              id="capture-correcting"
              label={intl.formatMessage(
                { id: 'measurements.wizard.correction.toggle' },
                { number: String(correctable.versionNumber) },
              )}
              name="correcting"
              onValueChange={setCorrecting}
              value={correcting}
            />
          )}

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
          action={intl.formatMessage({ id: 'measurements.wizard.confirm' })}
          busy={busy}
          cancelLabel={intl.formatMessage({ id: 'dialogs.cancel' })}
          confirmLabel={intl.formatMessage({
            id:
              corrects === null
                ? 'measurements.wizard.confirm'
                : 'measurements.wizard.confirm.correction.action',
          })}
          irreversible
          problem={<MeasurementProblemAlert failure={confirmFailure} />}
          onCancel={() => {
            setConfirming(false)
            setConfirmFailure(null)
          }}
          onConfirm={(outcome) => {
            void confirm(outcome)
          }}
          // A correction demands a reason, as everything that changes a confirmed record does; the
          // server refuses one without, so the dialog collects it rather than discovering the refusal.
          tier={corrects === null ? 'confirm' : 'reason'}
          title={intl.formatMessage({
            id:
              corrects === null
                ? 'measurements.wizard.confirm.title'
                : 'measurements.wizard.confirm.correction.title',
          })}
        >
          {corrects === null
            ? intl.formatMessage({ id: 'measurements.wizard.confirm.body' })
            : intl.formatMessage(
                { id: 'measurements.wizard.confirm.correction.body' },
                { number: String(corrects.versionNumber) },
              )}
        </ConfirmDialog>
      ) : null}
    </div>
  )
}
