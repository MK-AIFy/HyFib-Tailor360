import type { StatusKind } from '../../components/primitives/statuses'
import { DATES } from './branch'
import type { PhaseName } from './branch'

/**
 * Garment jobs, as every queue and workboard in the journeys sees them. Synthetic — see `branch.ts`.
 *
 * `barcode` is the opaque payload of plan D9: a `G-` namespace, eleven Crockford base32 characters
 * and a check character. The payloads here are illustrative and their final character is not
 * computed with the production checksum, exactly as docs/prd/walkthroughs.md section 1.2 says of its
 * own. Nothing in a journey decodes one; the scan screen resolves it by lookup, which is what the
 * real one does too.
 */
export interface JourneyJob {
  readonly id: string
  readonly order: string
  readonly customer: string
  readonly garment: string
  readonly phase: PhaseName
  /** Null means unassigned, which is what the workboard's first filter is for. */
  readonly assignee: string | null
  readonly due: string
  readonly status: StatusKind
  readonly barcode: string
  /** Why the job is held, when it is. Never a code: checklist item A11Y-53 wants the reason. */
  readonly holdReason?: string
}

export const JOBS: readonly JourneyJob[] = [
  {
    id: 'J-CBE01-2627-000512-01',
    order: 'O-CBE01-2627-000512',
    customer: 'Kavitha Raman',
    garment: 'Blouse — pattern',
    phase: 'Stitching',
    assignee: 'Shanthi K.',
    due: DATES.today,
    status: 'in-progress',
    barcode: 'G-6MTB4XZ9DKQ2',
  },
  {
    id: 'J-CBE01-2627-000689-01',
    order: 'O-CBE01-2627-000689',
    customer: 'Revathi Murugan',
    garment: 'Blouse — Aari',
    phase: 'Finishing',
    assignee: 'Anbu Aari Works',
    due: DATES.nextWeek,
    status: 'held',
    holdReason: 'Stones and beads short by one kit — waiting on the supplier.',
    barcode: 'G-4Q7NBX2K9WMT',
  },
  {
    id: 'J-CBE01-2627-000934-01',
    order: 'O-CBE01-2627-000934',
    customer: 'Anitha Selvam',
    garment: 'Salwar — churidar',
    phase: 'Cutting',
    assignee: null,
    due: DATES.tomorrow,
    status: 'queued',
    barcode: 'G-2H8FKQ3NRW5Y',
  },
  {
    id: 'J-CBE01-2627-000934-02',
    order: 'O-CBE01-2627-000934',
    customer: 'Anitha Selvam',
    garment: 'Salwar — palazzo',
    phase: 'Finishing',
    assignee: 'Ramesh V.',
    due: DATES.overdue,
    status: 'overdue',
    barcode: 'G-5PN3WYQ7KB8M',
  },
  {
    id: 'J-CBE01-2627-001007-01',
    order: 'O-CBE01-2627-001007',
    customer: 'Bhuvaneswari Karthik',
    garment: 'Lehenga — choli',
    phase: 'QC',
    assignee: 'Murugesan P.',
    due: DATES.nextWeek,
    status: 'qc-passed',
    barcode: 'G-3RVK8QT2NXH6',
  },
  {
    id: 'J-CBE01-2627-001007-02',
    order: 'O-CBE01-2627-001007',
    customer: 'Bhuvaneswari Karthik',
    garment: 'Lehenga — skirt',
    phase: 'Cutting',
    assignee: null,
    due: DATES.nextWeek,
    status: 'queued',
    barcode: 'G-8DWQ2KMY5TB3',
  },
]

/** The jobs one tailor has in hand, which is a different list from the branch workboard. */
export const MY_JOBS: readonly JourneyJob[] = JOBS.filter(
  (job) => job.assignee === 'Shanthi K.' || job.assignee === null,
)

/**
 * Resolves a scanned payload to a job.
 *
 * Returns the job, `superseded` for a payload that was replaced by a reprint (EX-07 in walkthrough
 * 1), or `unknown`. The three outcomes exist because step 8 of `A11Y-RJ-03` is "attempt a scan that
 * must be rejected, and hear which rule failed" — and "which rule" is only answerable if the
 * rejections are told apart.
 */
export type ScanResolution =
  | { readonly outcome: 'accepted'; readonly job: JourneyJob }
  | { readonly outcome: 'superseded'; readonly replacedBy: string }
  | { readonly outcome: 'unknown' }

/** The label that was soaked at the wash basin on 5 May and reprinted (walkthrough 1, step 13). */
export const SUPERSEDED_BARCODE = 'G-7K3M9QW2XZ4B'

export function resolveScan(payload: string): ScanResolution {
  const trimmed = payload.trim().toUpperCase()

  if (trimmed === SUPERSEDED_BARCODE) {
    return { outcome: 'superseded', replacedBy: 'G-6MTB4XZ9DKQ2' }
  }

  const job = JOBS.find((candidate) => candidate.barcode === trimmed || candidate.id === trimmed)
  return job === undefined ? { outcome: 'unknown' } : { outcome: 'accepted', job }
}
