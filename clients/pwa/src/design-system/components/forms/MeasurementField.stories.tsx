import type { Meta, StoryObj } from '@storybook/react-vite'
import { useState } from 'react'
import { PSEUDO_LOCALE } from '../../../i18n/pseudo'
import { FractionInput } from './FractionInput'
import { MeasurementField } from './MeasurementField'
import { NumericStepper } from './NumericStepper'
import type { MeasurementDisplayUnit } from './measurement'
import './forms.css'

/**
 * The measurement controls, against the real seeded template.
 *
 * Every field below is a row of `MT_BLOUSE_PATTERN` in docs/prd/measurement-templates.md section
 * 9.1 — the same keys, labels, fraction steps, hard bounds and confirmation bands a Reception
 * member of staff will meet. Synthetic values, real rules: a story built on invented ranges proves
 * nothing about the screen that ships.
 *
 * The states worth stepping through: an ordinary value, a value in the confirmation band with its
 * acknowledgement, a rejected value, and both display units — because the bounds exist to catch a
 * centimetre value typed into an inch field, and the message has to be in the unit on screen.
 */

/** `MT_BLOUSE_PATTERN` rows, verbatim. Bounds and bands are canonical millimetres. */
const BLOUSE_FIELDS = [
  {
    key: 'blouse_full_length',
    label: 'Blouse length',
    help: 'Shoulder seam at the neck to the intended garment hem. A finished measurement.',
    step: 8,
    bounds: {
      minimumMillimetres: 250,
      maximumMillimetres: 900,
      warnBelowMillimetres: 330,
      warnAboveMillimetres: 520,
    },
    value: 390.53,
  },
  {
    key: 'shoulder',
    label: 'Shoulder',
    help: 'Shoulder point to shoulder point across the back.',
    step: 8,
    bounds: {
      minimumMillimetres: 250,
      maximumMillimetres: 560,
      warnBelowMillimetres: 320,
      warnAboveMillimetres: 480,
    },
    value: 368.3,
  },
  {
    key: 'chest_bust',
    label: 'Chest (bust)',
    help: 'Round the fullest part of the bust, tape level at the back.',
    step: 8,
    bounds: {
      minimumMillimetres: 550,
      maximumMillimetres: 1500,
      warnBelowMillimetres: 710,
      warnAboveMillimetres: 1270,
    },
    value: 914.4,
  },
  {
    key: 'front_neck_depth',
    label: 'Front neck depth',
    help: 'From the shoulder seam beside the neck, straight down the front to the neckline point.',
    step: 16,
    bounds: {
      minimumMillimetres: 30,
      maximumMillimetres: 450,
      warnBelowMillimetres: 50,
      warnAboveMillimetres: 300,
    },
    // 6 3/16 in — a sixteenths value, so the finer strip has something to show.
    value: 157.16,
  },
] as const

interface SheetProps {
  readonly displayUnit: MeasurementDisplayUnit
  /** Overrides every field's value, to put the whole sheet in one band at once. */
  readonly overrideMillimetres?: number
}

function MeasurementSheet({ displayUnit, overrideMillimetres }: SheetProps) {
  const [values, setValues] = useState<Record<string, number>>(() =>
    Object.fromEntries(BLOUSE_FIELDS.map((field) => [field.key, field.value])),
  )
  const [acknowledged, setAcknowledged] = useState<Record<string, boolean>>({})

  return (
    <div className="field-stack">
      {BLOUSE_FIELDS.map((field) => (
        <MeasurementField
          acknowledged={acknowledged[field.key] ?? false}
          bounds={field.bounds}
          description={field.help}
          displayUnit={displayUnit}
          fractionStep={field.step}
          key={field.key}
          label={field.label}
          name={field.key}
          onAcknowledgedChange={(next) => {
            setAcknowledged((current) => ({ ...current, [field.key]: next }))
          }}
          onValueChange={(next) => {
            setValues((current) => ({ ...current, [field.key]: next }))
          }}
          required
          value={overrideMillimetres ?? values[field.key] ?? 0}
        />
      ))}
    </div>
  )
}

const meta = {
  title: 'Forms/Measurement entry',
  component: MeasurementSheet,
  parameters: { layout: 'padded' },
  args: { displayUnit: 'in' },
} satisfies Meta<typeof MeasurementSheet>

export default meta

type Story = StoryObj<typeof meta>

/** Inches with the fraction strip, which is how most of the shop works. */
export const Inches: Story = {}

/** Centimetres with the stepper at one decimal place. */
export const Centimetres: Story = { args: { displayUnit: 'cm' } }

/**
 * Every value inside the confirmation band. The warning never blocks a save, so it has to be read —
 * and acknowledged from the keyboard (checklist item A11Y-ME-07).
 */
export const ConfirmationBand: Story = { args: { overrideMillimetres: 320 } }

/** Every value outside the hard bounds. The capture cannot be confirmed until they are inside. */
export const Rejected: Story = { args: { overrideMillimetres: 20 } }

/** The same rejection in inches: the range has to be quoted in the unit on screen (A11Y-ME-06). */
export const RejectedInCentimetres: Story = {
  args: { displayUnit: 'cm', overrideMillimetres: 20 },
}

export const PseudoLocale: Story = {
  args: { overrideMillimetres: 320 },
  globals: { locale: PSEUDO_LOCALE },
}

export const Tamil: Story = { globals: { locale: 'ta-IN' } }

export const HighContrastSunlight: Story = {
  args: { overrideMillimetres: 20 },
  globals: { theme: 'contrast' },
}

export const LargestTextSize: Story = {
  args: { overrideMillimetres: 320 },
  globals: { textSize: '150' },
}

/** 320 CSS px: a sixteenths strip is sixteen 44 px targets, and it must wrap rather than scroll. */
export const ReflowFloor: Story = {
  globals: { viewport: { value: 'reflowFloor' } },
}

/** The two entry controls on their own, at the primary shop-floor size. */
export const EntryControls: Story = {
  render: () => <ControlPair />,
}

function ControlPair() {
  const [length, setLength] = useState(390.53)
  const [neckDepth, setNeckDepth] = useState(157.16)
  const [count, setCount] = useState(12)

  return (
    <div className="field-stack">
      <FractionInput
        description="Eighths for a length; the template chooses."
        label="Blouse length"
        name="blouse_full_length"
        onValueChange={setLength}
        size="primary"
        value={length}
      />
      <FractionInput
        description="Sixteenths, where a quarter inch changes the fit."
        label="Front neck depth"
        name="front_neck_depth"
        onValueChange={setNeckDepth}
        step={16}
        value={neckDepth}
      />
      <NumericStepper
        description="Repetitive counting, one-handed, all morning."
        label="Received quantity"
        max={999}
        min={0}
        name="received_quantity"
        onValueChange={setCount}
        size="primary"
        unit={{ symbol: 'pc', label: 'pieces' }}
        value={count}
      />
    </div>
  )
}
