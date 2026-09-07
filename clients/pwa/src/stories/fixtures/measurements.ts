import type { MeasurementBounds } from '../../design-system/components/forms/measurement'
import type { InchFractionStep } from '../../i18n/units'

/**
 * A measurement template, as the wizard of `A11Y-RJ-02` opens it. Synthetic — see `branch.ts`.
 *
 * Values are canonical millimetres throughout, and are the ones docs/prd/walkthroughs.md walkthrough
 * 1 step 3 records for `MT_BLOUSE_PATTERN` version 1. The display unit is a preference; the storage
 * unit never is.
 *
 * `fractionStep` follows docs/prd/measurement-templates.md: eighths for a length, sixteenths for
 * shaping and neckline, because that is the precision a tape and a cutting table actually carry.
 */
export interface MeasurementFieldFixture {
  readonly name: string
  readonly label: string
  /** Where on the body it is taken. Shown as the field's persistent description. */
  readonly description: string
  readonly stepId: 'bodice' | 'sleeve'
  /** The value on the customer's previous confirmed version, offered for reuse. */
  readonly previousMillimetres: number
  readonly bounds: MeasurementBounds
  readonly fractionStep: InchFractionStep
}

export const MEASUREMENT_TEMPLATE = {
  code: 'MT_BLOUSE_PATTERN',
  version: 1,
  /** The version the offered values came from, which step 9 of the journey has to announce. */
  previousVersion: 3,
  publishedOn: '2026-04-01T00:00:00+05:30',
} as const

export const MEASUREMENT_FIELDS: readonly MeasurementFieldFixture[] = [
  {
    name: 'blouseLength',
    label: 'Blouse length',
    description: 'Shoulder seam to the finished hem, at the centre back.',
    stepId: 'bodice',
    previousMillimetres: 380,
    bounds: {
      minimumMillimetres: 250,
      maximumMillimetres: 520,
      warnBelowMillimetres: 300,
      warnAboveMillimetres: 450,
    },
    fractionStep: 8,
  },
  {
    name: 'shoulder',
    label: 'Shoulder',
    description: 'Point to point across the back, at the shoulder seams.',
    stepId: 'bodice',
    previousMillimetres: 355,
    bounds: {
      minimumMillimetres: 250,
      maximumMillimetres: 480,
      warnBelowMillimetres: 300,
      warnAboveMillimetres: 420,
    },
    fractionStep: 16,
  },
  {
    name: 'chest',
    label: 'Chest',
    description: 'Around the fullest part, tape level and not pulled tight.',
    stepId: 'bodice',
    previousMillimetres: 890,
    bounds: {
      minimumMillimetres: 600,
      maximumMillimetres: 1400,
      warnBelowMillimetres: 700,
      warnAboveMillimetres: 1150,
    },
    fractionStep: 8,
  },
  {
    name: 'waist',
    label: 'Waist',
    description: 'Around the natural waist, where the body bends to the side.',
    stepId: 'bodice',
    previousMillimetres: 760,
    bounds: {
      minimumMillimetres: 500,
      maximumMillimetres: 1300,
      warnBelowMillimetres: 600,
      warnAboveMillimetres: 1100,
    },
    fractionStep: 8,
  },
  {
    name: 'armhole',
    label: 'Armhole',
    description: 'Around the arm at the shoulder joint, over the shoulder point.',
    stepId: 'sleeve',
    previousMillimetres: 430,
    bounds: {
      minimumMillimetres: 300,
      maximumMillimetres: 600,
      warnBelowMillimetres: 340,
      warnAboveMillimetres: 540,
    },
    fractionStep: 16,
  },
  {
    name: 'sleeveLength',
    label: 'Sleeve length',
    description: 'Shoulder point to the finished sleeve hem, arm relaxed.',
    stepId: 'sleeve',
    previousMillimetres: 230,
    bounds: {
      minimumMillimetres: 100,
      maximumMillimetres: 700,
      warnBelowMillimetres: 130,
      warnAboveMillimetres: 620,
    },
    fractionStep: 8,
  },
  {
    name: 'sleeveRound',
    label: 'Sleeve round',
    description: 'Around the arm at the finished sleeve hem.',
    stepId: 'sleeve',
    previousMillimetres: 280,
    bounds: {
      minimumMillimetres: 180,
      maximumMillimetres: 500,
      warnBelowMillimetres: 210,
      warnAboveMillimetres: 440,
    },
    fractionStep: 16,
  },
]

/** The wizard's steps, in the order the tape follows rather than alphabetically. */
export const MEASUREMENT_STEPS = [
  { id: 'bodice', label: 'Bodice' },
  { id: 'sleeve', label: 'Sleeve' },
  { id: 'review', label: 'Review and confirm' },
] as const
