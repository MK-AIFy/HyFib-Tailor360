import { DATES } from './branch'
import { TAILORS } from './branch'
import type { JourneyJob } from './jobs'

/**
 * Production: the workflow a job is pinned to, the quality checklist it is judged against, and the
 * rules the workboard refuses an assignment by. Synthetic — see the note at the top of `branch.ts`.
 *
 * Three of the nine steps of `A11Y-RJ-04` are about a *version*: the workflow version pinned at the
 * start of production (step 5), the checklist version a QC result was recorded against (step 6), and
 * the rework that comes out of a failed criterion (step 8). None of them can be demonstrated by a
 * screen that shows a list of tick boxes, which is why the versions are in the fixture rather than
 * in the layout.
 */

/**
 * The workflow the branch runs, and the version a job is pinned to when production starts.
 *
 * Pinning is the point. Once production starts the job is executed against the version that was
 * current at that instant, so a workflow edited this afternoon does not silently re-route a garment
 * that was cut this morning — and revising the job's route is refused rather than quietly applied.
 */
export const WORKFLOW = {
  code: 'WF-STITCH-STANDARD',
  version: 4,
  publishedOn: '2026-03-02T09:00:00+05:30',
} as const

/** One thing a finished garment is checked for. */
export interface QualityCriterion {
  readonly id: string
  readonly label: string
  /** What "pass" means for this criterion, in words a person at the table can apply. */
  readonly description: string
}

/**
 * The quality checklist, at the version this job's result is recorded against.
 *
 * Step 6 of `A11Y-RJ-04` asks to hear "the checklist version and each criterion as a named group",
 * so the version travels with the criteria rather than being printed once at the top of a screen and
 * lost the moment somebody scrolls.
 */
export const QC_CHECKLIST = {
  code: 'QC-BLOUSE',
  version: 2,
  publishedOn: '2026-02-11T09:00:00+05:30',
  criteria: [
    {
      id: 'measurements',
      label: 'Finished measurements match the version',
      description: 'Every finished measurement is within tolerance of the confirmed version.',
    },
    {
      id: 'seams',
      label: 'Seams and finishing',
      description: 'No puckering, no raw edge, and every seam locked at both ends.',
    },
    {
      id: 'fastenings',
      label: 'Hooks, piping and fastenings',
      description: 'Fastenings sit square, hold closed, and match the design selection.',
    },
    {
      id: 'pressing',
      label: 'Pressing and presentation',
      description: 'Pressed, lint-free and folded to the branch standard.',
    },
  ] as readonly QualityCriterion[],
} as const

export type QualityVerdict = 'pass' | 'fail'

/**
 * The defect vocabulary a failed criterion is coded against.
 *
 * A code and a word together, never a code alone: checklist item A11Y-53 asks for the reason, and
 * "DEF-04" read aloud is not a reason. The final list is a product decision (the defect taxonomy of
 * plan E07); these four are illustrative and cover the failure the journey walks.
 */
export const DEFECT_CODES = [
  { value: 'DEF-01', label: 'DEF-01 — Measurement outside tolerance' },
  { value: 'DEF-02', label: 'DEF-02 — Seam or finishing fault' },
  { value: 'DEF-03', label: 'DEF-03 — Fastening or trim fault' },
  { value: 'DEF-04', label: 'DEF-04 — Soiling, pressing or presentation' },
] as const

/** Which phase a job returns to when a given criterion fails. Step 8 of `A11Y-RJ-04`. */
export const REWORK_PHASE: Readonly<Record<string, string>> = {
  measurements: 'Cutting',
  seams: 'Stitching',
  fastenings: 'Stitching',
  pressing: 'Finishing',
}

/** The garment category a job belongs to — the first word of its garment name, lower-cased. */
export function categoryOf(job: JourneyJob): string {
  return (job.garment.split('—')[0] ?? job.garment).trim().toLowerCase()
}

/**
 * Why this tailor may not take this job, or null when the assignment is allowed.
 *
 * Two rules, and both of them state which rule failed rather than saying "not allowed": a tailor
 * takes only the categories they are qualified for, and a held job is not assigned to anybody until
 * the hold is cleared. Step 4 of `A11Y-RJ-04` is precisely "attempt an assignment that must be
 * refused, and hear the reason".
 */
export function assignmentRefusal(job: JourneyJob, tailorId: string): string | null {
  const tailor = TAILORS.find((candidate) => candidate.id === tailorId)
  if (tailor === undefined) {
    return 'That person is not on the branch roster.'
  }

  if (job.status === 'held') {
    return `${job.id} is on hold — ${job.holdReason ?? 'the hold has no recorded reason.'} Clear the hold before assigning it.`
  }

  const category = categoryOf(job)
  if (!tailor.qualifiedFor.includes(category)) {
    return `${tailor.name} is qualified for ${tailor.skills.toLowerCase()}, not ${category} work.`
  }

  return null
}

/** One entry of a job's history, before it is formatted for the `Timeline` component. */
export interface ProductionEvent {
  readonly id: string
  readonly title: string
  readonly at: string
  readonly actor: string
  readonly detail?: string
  /** The status the entry put the job into, where it changed one. */
  readonly status?: JourneyJob['status']
}

/**
 * The history of the lehenga choli that reaches QC, oldest first.
 *
 * Step 9 of `A11Y-RJ-04` is "read the job's timeline and hear the rework as a new entry beside the
 * old one" — so the QC pass that came before the failure has to still be here. An audit trail that
 * replaced the earlier result with the later one would make the step unwalkable and the history a
 * lie: a corrected record and an amended record are different things, and this product keeps both.
 */
export const JOB_HISTORY: readonly ProductionEvent[] = [
  {
    id: 'ev-1',
    title: 'Order confirmed',
    at: '2026-04-28T10:20:00+05:30',
    actor: 'Kalaiselvi R.',
    detail: 'Price, design and measurement snapshots frozen.',
    status: 'draft',
  },
  {
    id: 'ev-2',
    title: 'Cutting complete',
    at: '2026-05-04T15:40:00+05:30',
    actor: 'Murugesan P.',
    status: 'in-progress',
  },
  {
    id: 'ev-3',
    title: 'Stitching complete',
    at: '2026-05-06T17:05:00+05:30',
    actor: 'Shanthi K.',
    status: 'in-progress',
  },
  {
    id: 'ev-4',
    title: 'QC passed on the first inspection',
    at: '2026-05-07T09:15:00+05:30',
    actor: 'Murugesan P.',
    detail: `Checklist ${QC_CHECKLIST.code} version ${String(QC_CHECKLIST.version)}, all four criteria passed.`,
    status: 'qc-passed',
  },
]

/** When production started on that job, which is the instant the workflow version was pinned. */
export const PRODUCTION_STARTED_AT = DATES.yesterday
