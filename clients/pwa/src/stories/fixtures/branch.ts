/**
 * The branch, the calendar and the staff the eight reference journeys are walked in.
 *
 * ## Synthetic data, and why these particular strings
 *
 * Every name, number and payload in this directory is synthetic, and all of it is lifted from
 * docs/prd/walkthroughs.md, whose section 1.2 declares the whole set synthetic and states the
 * conventions the identifiers follow. Reusing that set rather than inventing a second one has two
 * benefits: a reviewer walking `A11Y-RJ-04` against the workboard sees the same job numbers the
 * walkthrough describes, and there is exactly one place where the repository's "no real customer
 * data" rule has to hold.
 *
 * ## Why the clock is frozen
 *
 * `NOW` is a fixed instant rather than `Date.now()`. A due cue that reads "in 2 days" on Monday and
 * "overdue" on Thursday would make the visual-regression baselines of #52 flap, and a journey whose
 * screenshots change by themselves cannot be evidence of anything.
 */

export const BRANCH = {
  code: 'CBE01',
  /** The launch branch list is owner decision OD-06; this one is illustrative. */
  name: 'Coimbatore',
  timeZone: 'Asia/Kolkata',
  financialYear: '2026-27',
} as const

/** The instant every journey is rendered at. Thursday 7 May 2026, mid-morning at the counter. */
export const NOW = '2026-05-07T11:05:00+05:30'

/** Working days ahead of `NOW`, as ISO instants. Monday to Saturday working, per OD-06. */
export const DATES = {
  yesterday: '2026-05-06T11:05:00+05:30',
  today: '2026-05-07T11:05:00+05:30',
  tomorrow: '2026-05-08T11:05:00+05:30',
  nextWeek: '2026-05-13T11:05:00+05:30',
  lastMonth: '2026-04-09T11:05:00+05:30',
  /** Deliberately in the past, so at least one row on every queue is overdue. */
  overdue: '2026-05-04T11:05:00+05:30',
} as const

/**
 * The staff of docs/prd/walkthroughs.md section 1.3, by the journey role they hold rather than by
 * their RACI column. Measurement staff is a permission bundle (`measurements.capture`, OD-13) that
 * Reception holds in the default grant, which is why one name appears twice.
 */
export const STAFF = {
  reception: 'Kalaiselvi R.',
  measurementStaff: 'Kalaiselvi R.',
  tailorMaster: 'Murugesan P.',
  tailor: 'Shanthi K.',
  inventory: 'Vijaya S.',
  cashier: 'Deepa N.',
  delivery: 'Arun T.',
  manager: 'Saravanan M.',
} as const

/** The tailors a job can be assigned to, with the categories each is qualified for. */
export const TAILORS = [
  { id: 'shanthi', name: 'Shanthi K.', skills: 'Blouse, choli' },
  { id: 'ramesh', name: 'Ramesh V.', skills: 'Salwar, gown' },
  { id: 'latha', name: 'Latha M.', skills: "Kids' wear" },
  { id: 'anbu', name: 'Anbu Aari Works', skills: 'Aari — external unit' },
] as const

/** The workflow phases a garment job moves through, in order. */
export const PHASES = ['Cutting', 'Stitching', 'Finishing', 'QC'] as const

export type PhaseName = (typeof PHASES)[number]
