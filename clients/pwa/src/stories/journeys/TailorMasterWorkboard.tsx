import { useState } from 'react'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { useShellStatus } from '../../components/layout/useShellStatus'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ButtonGroup } from '../../components/primitives/ButtonGroup'
import { Card } from '../../components/primitives/Card'
import { DataTable } from '../../components/primitives/DataTable'
import { Filters } from '../../components/primitives/Filters'
import { StatusBadge } from '../../components/primitives/StatusBadge'
import { Timeline } from '../../components/primitives/Timeline'
import { useDemoText } from '../../components/primitives/demoText'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { RadioGroup } from '../../design-system/components/forms/RadioGroup'
import { Select } from '../../design-system/components/forms/Select'
import { TextField } from '../../design-system/components/forms/TextField'
import { getFormatters } from '../../i18n/formatters'
import { NOW, STAFF, TAILORS } from '../fixtures/branch'
import { JOBS } from '../fixtures/jobs'
import type { JourneyJob } from '../fixtures/jobs'
import {
  DEFECT_CODES,
  JOB_HISTORY,
  PRODUCTION_STARTED_AT,
  QC_CHECKLIST,
  REWORK_PHASE,
  WORKFLOW,
  assignmentRefusal,
} from '../fixtures/production'
import type { ProductionEvent, QualityVerdict } from '../fixtures/production'

/** The job the QC and rework steps are walked on: the one whose history already holds a QC pass. */
const QC_JOB_ID = 'J-CBE01-2627-001007-01'

/**
 * `A11Y-RJ-04` — Tailor Master: the workboard, assignment, starting production and QC.
 *
 * The journey with no record anywhere else in docs/nfr/a11y-checklist.md — the workboard,
 * assignment, start production and QC had none — so all nine steps of section 6.8's record are here.
 *
 * ## Every row control is a button, and there is no drag handle to fall back from
 *
 * 2.5.7 Dragging Movements requires a button alternative to every drag, and the #50 blueprint makes
 * that alternative the *primary* implementation. So a job is assigned from its own row's Assign
 * control, and this file contains no drag target at all. Nothing here is a fallback: there is
 * nothing to fall back from.
 *
 * ## A refusal names the rule, not the outcome
 *
 * Assigning a salwar to a tailor qualified for blouse work is refused with the qualification in the
 * sentence, and a held job is refused with the hold's own reason. "Not allowed" would pass a colour
 * contrast check and fail the person holding the garment (3.3.1, 3.3.3, checklist item A11Y-53).
 *
 * ## Three versions travel with the work
 *
 * Starting production pins the workflow version, and revising the route after that is refused rather
 * than quietly applied. A QC result is recorded against the checklist version, and the version is on
 * screen beside the criteria rather than printed once at the top. A failed criterion becomes a
 * rework whose entry sits **beside** the earlier pass on the timeline, never in place of it — an
 * amended record and a rewritten record are different things, and this product keeps both.
 *
 * There is no backend: the state is `useState` over the fixtures.
 */
export function TailorMasterWorkboardScreen() {
  const t = useDemoText()
  const formatters = getFormatters()
  const status = useShellStatus()

  const [unassignedOnly, setUnassignedOnly] = useState(false)
  const [overdueOnly, setOverdueOnly] = useState(false)
  const [assignments, setAssignments] = useState<Readonly<Record<string, string>>>({})
  const [assigning, setAssigning] = useState<JourneyJob | null>(null)
  const [chosenTailor, setChosenTailor] = useState(TAILORS[0]?.id ?? '')
  const [refusal, setRefusal] = useState<string | null>(null)
  const [productionPinned, setProductionPinned] = useState(false)
  const [revisionRefused, setRevisionRefused] = useState(false)
  const [verdicts, setVerdicts] = useState<Readonly<Record<string, QualityVerdict>>>({})
  const [defectCode, setDefectCode] = useState<string>(DEFECT_CODES[1]?.value ?? '')
  const [evidenceCaption, setEvidenceCaption] = useState('')
  const [evidenceAttached, setEvidenceAttached] = useState(false)
  const [confirmingRework, setConfirmingRework] = useState(false)
  const [rework, setRework] = useState<ProductionEvent | null>(null)

  const assigneeOf = (job: JourneyJob) => assignments[job.id] ?? job.assignee

  const rows = JOBS.filter((job) => {
    if (unassignedOnly && assigneeOf(job) !== null) {
      return false
    }
    return !(overdueOnly && job.status !== 'overdue')
  })

  const applied = [
    ...(unassignedOnly ? [{ id: 'unassigned', label: t('Assignment: unassigned') }] : []),
    ...(overdueOnly ? [{ id: 'overdue', label: t('Due: overdue') }] : []),
  ]

  const qcJob = JOBS.find((job) => job.id === QC_JOB_ID)
  const failed = QC_CHECKLIST.criteria.filter((criterion) => verdicts[criterion.id] === 'fail')
  const returnsTo = failed.length === 0 ? null : (REWORK_PHASE[failed[0]?.id ?? ''] ?? 'Stitching')

  /** The history, oldest first, with the rework appended beside the earlier pass rather than over it. */
  const history: readonly ProductionEvent[] =
    rework === null ? JOB_HISTORY : [...JOB_HISTORY, rework]

  return (
    <section className="page journey-screen">
      <h1>{t('Workboard')}</h1>
      <p>
        {t(STAFF.tailorMaster)} · {formatters.formatDateTime(NOW)}
      </p>

      <section aria-labelledby="board-list" className="journey-section">
        <h2 id="board-list">{t('1. The branch workboard')}</h2>

        {/* The count is announced once, by the filter region, rather than by each control. */}
        <Filters
          applied={applied}
          label={t('Narrow the workboard')}
          onClearAll={() => {
            setUnassignedOnly(false)
            setOverdueOnly(false)
          }}
          onRemove={(id) => {
            if (id === 'unassigned') {
              setUnassignedOnly(false)
            }
            if (id === 'overdue') {
              setOverdueOnly(false)
            }
          }}
          resultCount={rows.length}
        >
          <Checkbox
            label={t('Unassigned only')}
            name="filterUnassigned"
            onValueChange={setUnassignedOnly}
            value={unassignedOnly}
          />
          <Checkbox
            label={t('Overdue only')}
            name="filterOverdue"
            onValueChange={setOverdueOnly}
            value={overdueOnly}
          />
        </Filters>

        <DataTable
          caption={t('Garment jobs in this branch')}
          columns={[
            { id: 'job', header: t('Job'), primary: true, cell: (job) => job.id },
            { id: 'garment', header: t('Garment'), cell: (job) => t(job.garment) },
            {
              id: 'customer',
              header: t('Customer'),
              hideWhenNarrow: true,
              cell: (job) => t(job.customer),
            },
            { id: 'phase', header: t('Phase'), cell: (job) => t(job.phase) },
            {
              id: 'assignee',
              header: t('Assigned to'),
              cell: (job) => t(assigneeOf(job) ?? 'Unassigned'),
            },
            {
              id: 'due',
              header: t('Due'),
              // The due cue is text, always: "in 2 days" beside the date, never a colour alone.
              cell: (job) => (
                <>
                  {formatters.formatShortDate(job.due)}{' '}
                  <span className="journey-row__meta">
                    {formatters.formatRelativeTime(job.due, NOW).relative}
                  </span>
                </>
              ),
            },
            {
              id: 'status',
              header: t('State'),
              cell: (job) => <StatusBadge status={job.status} />,
            },
          ]}
          rowActions={(job) => (
            <ButtonGroup>
              <Button
                onClick={() => {
                  setAssigning(job)
                  setRefusal(null)
                }}
                size="dense"
              >
                {t('Assign')}
                {/*
                  Checklist item A11Y-60: a row action has to announce which row it belongs to, and
                  six identical "Assign" buttons on a workboard is a garment assigned to the wrong
                  tailor. The job number belongs in the accessible name and not on the screen — the
                  column beside it already says which row this is, and putting the number in the
                  visible label squeezed the actions column until the word broke one letter to a
                  line, which is what the first screenshot of this journey showed.
                */}{' '}
                <span className="visually-hidden">{job.id}</span>
              </Button>
            </ButtonGroup>
          )}
          rowKey={(job) => job.id}
          rowLabel={(job) => `${t('job')} ${job.id}`}
          rows={rows}
        />
      </section>

      {assigning === null ? null : (
        <section aria-labelledby="board-assign" className="journey-section">
          <h2 id="board-assign">{t('3. Assign this job')}</h2>
          <Card headingLevel={3} title={`${assigning.id} — ${t(assigning.garment)}`}>
            <Select
              description={t('Every tailor on the roster, with what each is qualified for.')}
              emptyLabel={null}
              label={t('Assign to')}
              name="assignTo"
              onValueChange={setChosenTailor}
              options={TAILORS.map((tailor) => ({
                value: tailor.id,
                label: t(`${tailor.name} — ${tailor.skills}`),
              }))}
              value={chosenTailor}
            />
            <ButtonGroup>
              <Button
                iconName="check"
                onClick={() => {
                  const reason = assignmentRefusal(assigning, chosenTailor)
                  if (reason !== null) {
                    setRefusal(reason)
                    return
                  }
                  const name = TAILORS.find((tailor) => tailor.id === chosenTailor)?.name ?? ''
                  setAssignments((previous) => ({ ...previous, [assigning.id]: name }))
                  setRefusal(null)
                  setAssigning(null)
                  status.announceAutosave(t(`${assigning.id} assigned to ${name}.`))
                }}
                variant="primary"
              >
                {t('Assign')}
              </Button>
              <Button
                onClick={() => {
                  setAssigning(null)
                  setRefusal(null)
                }}
              >
                {t('Cancel')}
              </Button>
            </ButtonGroup>
          </Card>

          {refusal === null ? null : (
            // Assertive: the assignment did not happen, and the person is already reaching for the
            // next row. This is one of the two things on this product that earns an interruption.
            <Alert live="assertive" title={t('That assignment was refused')} tone="danger">
              {t(refusal)}
            </Alert>
          )}
        </section>
      )}

      <section aria-labelledby="board-production" className="journey-section">
        <h2 id="board-production">{t('5. Start production')}</h2>
        <Card
          headingLevel={3}
          title={t(`Workflow ${WORKFLOW.code}, version ${String(WORKFLOW.version)}`)}
        >
          <p>
            {t('Published')} {formatters.formatShortDate(WORKFLOW.publishedOn)}.{' '}
            {t(
              'Starting production pins this version to the job. A workflow edited afterwards does not re-route a garment that is already cut.',
            )}
          </p>
          <ButtonGroup>
            <Button
              iconName="play"
              onClick={() => {
                setProductionPinned(true)
                status.announceAutosave(
                  t(
                    `Production started. ${QC_JOB_ID} is pinned to workflow ${WORKFLOW.code} version ${String(WORKFLOW.version)} as at ${formatters.formatDateTime(PRODUCTION_STARTED_AT)}.`,
                  ),
                )
              }}
              variant="primary"
            >
              {t('Start production')}
            </Button>
            <Button
              onClick={() => {
                setRevisionRefused(true)
              }}
            >
              {t('Revise the route')}
            </Button>
          </ButtonGroup>
        </Card>

        {!productionPinned ? null : (
          <Alert live="polite" title={t('Workflow version pinned')} tone="success">
            {t(
              `${QC_JOB_ID} runs on ${WORKFLOW.code} version ${String(WORKFLOW.version)} from here to delivery, whatever the workflow becomes.`,
            )}
          </Alert>
        )}

        {!revisionRefused ? null : (
          <Alert
            actions={
              <Button
                onClick={() => {
                  setRevisionRefused(false)
                }}
              >
                {t('Understood')}
              </Button>
            }
            live="assertive"
            title={t('The route cannot be revised')}
            tone="danger"
          >
            {t(
              productionPinned
                ? `Production has started, so the route is fixed at ${WORKFLOW.code} version ${String(WORKFLOW.version)}. Raise a rework instead, which is recorded as its own entry.`
                : 'Start production first. Until then there is no pinned version to revise.',
            )}
          </Alert>
        )}
      </section>

      <section aria-labelledby="board-qc" className="journey-section">
        <h2 id="board-qc">{t('6. Quality check')}</h2>
        <p>
          {qcJob === undefined ? '' : `${qcJob.id} — ${t(qcJob.garment)}`}.{' '}
          {t(`Checklist ${QC_CHECKLIST.code}, version ${String(QC_CHECKLIST.version)}`)},{' '}
          {t('published')} {formatters.formatShortDate(QC_CHECKLIST.publishedOn)}.
        </p>

        {/* Each criterion is its own named group, which is what a screen reader announces on entry. */}
        <div className="journey-fields">
          {QC_CHECKLIST.criteria.map((criterion) => (
            <RadioGroup
              description={t(criterion.description)}
              key={criterion.id}
              label={t(criterion.label)}
              name={`qc-${criterion.id}`}
              onValueChange={(next) => {
                setVerdicts((previous) => ({
                  ...previous,
                  [criterion.id]: next === 'fail' ? 'fail' : 'pass',
                }))
              }}
              options={[
                { value: 'pass', label: t('Passed') },
                { value: 'fail', label: t('Failed') },
              ]}
              value={verdicts[criterion.id] ?? ''}
            />
          ))}
        </div>

        {failed.length === 0 ? null : (
          <section aria-labelledby="board-defect" className="journey-section">
            <h3 id="board-defect">{t('7. Code the defect and attach the evidence')}</h3>
            <div className="journey-fields" data-columns="2">
              <Select
                emptyLabel={null}
                label={t('Defect code')}
                name="defectCode"
                onValueChange={setDefectCode}
                options={DEFECT_CODES.map((code) => ({
                  value: code.value,
                  label: t(code.label),
                }))}
                value={defectCode}
              />
              <TextField
                description={t(
                  'What the photograph shows, in words. This is what somebody hears who cannot see it, and what the rework is judged against.',
                )}
                label={t('Describe the photograph')}
                name="evidenceCaption"
                onValueChange={setEvidenceCaption}
                required
                value={evidenceCaption}
              />
            </div>
            <ButtonGroup>
              <Button
                iconName="package"
                onClick={() => {
                  setEvidenceAttached(true)
                  status.announceAutosave(t('Evidence photograph attached to the quality result.'))
                }}
              >
                {t('Attach the photograph')}
              </Button>
              <Button
                iconName="refresh"
                onClick={() => {
                  setConfirmingRework(true)
                }}
                unavailable={evidenceCaption.trim() === '' || !evidenceAttached}
                variant="primary"
              >
                {t('Raise the rework')}
              </Button>
            </ButtonGroup>
            <p className="journey-row__meta">
              {t(
                'The rework cannot be raised until the photograph is attached and described — a defect nobody can see is a defect nobody can fix.',
              )}
            </p>
          </section>
        )}
      </section>

      {rework === null ? null : (
        <Alert live="polite" title={t('Rework raised')} tone="warning">
          {t(`${QC_JOB_ID} returns to ${returnsTo ?? ''} against defect ${defectCode}.`)}
        </Alert>
      )}

      <section aria-labelledby="board-history" className="journey-section">
        <h2 id="board-history">{t('9. This job’s history')}</h2>
        <Timeline
          entries={history.map((event) => ({
            id: event.id,
            title: t(event.title),
            absoluteTime: formatters.formatDateTime(event.at),
            dateTime: event.at,
            relativeTime: formatters.formatRelativeTime(event.at, NOW).relative,
            actor: t(event.actor),
            ...(event.status === undefined ? {} : { status: event.status }),
            ...(event.detail === undefined ? {} : { detail: t(event.detail) }),
          }))}
          label={t(`History of ${QC_JOB_ID}`)}
        />
      </section>

      <ConfirmDialog
        action="raising this rework"
        confirmLabel={t('Raise the rework')}
        onCancel={() => {
          setConfirmingRework(false)
        }}
        onConfirm={() => {
          setConfirmingRework(false)
          setRework({
            id: 'ev-rework',
            title: `QC failed on re-inspection — returned to ${returnsTo ?? ''}`,
            at: NOW,
            actor: STAFF.tailorMaster,
            detail: `Checklist ${QC_CHECKLIST.code} version ${String(QC_CHECKLIST.version)}. ${failed
              .map((criterion) => criterion.label)
              .join(
                ', ',
              )} failed against ${defectCode}. Evidence: ${evidenceCaption}. The earlier pass stays on this trail.`,
            status: 'rework',
          })
        }}
        open={confirmingRework}
        tier="confirm"
        title={t('Raise this rework?')}
      >
        {t(
          `${QC_JOB_ID} returns to ${returnsTo ?? ''} and the promised date is recalculated. The earlier QC pass is not removed: this is recorded beside it.`,
        )}
      </ConfirmDialog>
    </section>
  )
}
