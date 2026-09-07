import type { Meta, StoryObj } from '@storybook/react-vite'
import { useState } from 'react'
import { PSEUDO_LOCALE } from '../../../i18n/pseudo'
import { AUTOCOMPLETE } from './autocomplete'
import { Checkbox } from './Checkbox'
import { DateField } from './DateField'
import { RadioGroup } from './RadioGroup'
import { Select } from './Select'
import { Switch } from './Switch'
import { TextArea } from './TextArea'
import { TextField } from './TextField'
import './forms.css'

/**
 * The controls of the forms family, in the states a screen has to be able to show.
 *
 * Every story is worth switching the toolbar on:
 *
 *   Pseudo-locale  proves the 40% text-growth tolerance the Tamil catalogue depends on, and reveals
 *                  any string a component hard-coded — hard-coded text is the only unaccented text
 *                  on the screen.
 *   High contrast  the sunlight theme. A control that only looks right in the default theme has a
 *                  colour written into it somewhere.
 *   150% text      the product's own text-size preference (checklist item A11Y-72). Nothing may
 *                  clip, overlap or become unreachable.
 *   320 px         the reflow floor. The page must not scroll sideways (1.4.10).
 */

const WAIST_FINISHES = [
  { value: 'ELASTIC', label: 'Elastic' },
  { value: 'DRAWSTRING', label: 'Drawstring' },
  { value: 'BOTH', label: 'Both' },
]

interface GalleryProps {
  /** Renders every control in its invalid state, with a sentence a person can act on. */
  readonly invalid?: boolean
  readonly required?: boolean
  readonly disabled?: boolean
  readonly readOnly?: boolean
}

function ControlGallery({ invalid, required, disabled, readOnly }: GalleryProps) {
  const [text, setText] = useState('Meena R')
  const [note, setNote] = useState('')
  const [finish, setFinish] = useState('')
  const [style, setStyle] = useState('ELASTIC')
  const [reuse, setReuse] = useState(false)
  const [diagrams, setDiagrams] = useState(true)
  const [due, setDue] = useState('2026-09-18')

  const state = {
    ...(required === true ? { required: true } : {}),
    ...(disabled === true ? { disabled: true } : {}),
    ...(readOnly === true ? { readOnly: true } : {}),
  }

  return (
    <div className="field-stack">
      <TextField
        {...state}
        autoComplete={AUTOCOMPLETE.name}
        description="As the customer gives it. Nothing here is transliterated or corrected."
        label="Customer name"
        name="customer_name"
        onValueChange={setText}
        value={text}
        {...(invalid === true ? { error: 'Enter the customer’s name.' } : {})}
      />
      <TextArea
        {...state}
        description="Quoted on the audit record exactly as typed."
        label="Reason for the correction"
        name="reason"
        onValueChange={setNote}
        value={note}
        {...(invalid === true ? { error: 'A reason is needed before this can be recorded.' } : {})}
      />
      <Select
        {...state}
        label="Waist finish"
        name="waist_finish"
        onValueChange={setFinish}
        options={WAIST_FINISHES}
        value={finish}
        {...(invalid === true ? { error: 'Choose a waist finish.' } : {})}
      />
      <RadioGroup
        {...state}
        description="Shown as a list because the three are compared, not searched."
        label="Bottom style"
        name="bottom_style"
        onValueChange={setStyle}
        options={WAIST_FINISHES}
        value={style}
        {...(invalid === true ? { error: 'Choose a bottom style.' } : {})}
      />
      <Checkbox
        {...state}
        description="Version 3, taken on 12-08-2026 by Reception."
        label="Reuse the previous measurements"
        name="reuse_measurements"
        onValueChange={setReuse}
        value={reuse}
        {...(invalid === true
          ? { error: 'Choose whether to reuse or take new measurements.' }
          : {})}
      />
      <Switch
        {...state}
        description="Takes effect immediately, on this device only."
        label="Show diagrams beside the fields"
        name="show_diagrams"
        onValueChange={setDiagrams}
        value={diagrams}
      />
      <DateField
        {...state}
        label="Promised delivery date"
        name="promised_date"
        onValueChange={setDue}
        value={due}
        {...(invalid === true ? { error: 'The promised date cannot be in the past.' } : {})}
      />
    </div>
  )
}

/**
 * A customer intake row, which is the reason WCAG 1.3.5 is in the blueprint at all: at a busy
 * counter the alternative to a filled field is somebody typing a phone number one-handed while
 * holding a garment, and a mistyped number is a customer who never gets the ready message.
 */
function CustomerFields() {
  const [name, setName] = useState('')
  const [phone, setPhone] = useState('')
  const [address, setAddress] = useState('')
  const [postcode, setPostcode] = useState('')

  return (
    <div className="field-stack">
      <TextField
        autoComplete={AUTOCOMPLETE.name}
        enterKeyHint="next"
        label="Customer name"
        name="customer_name"
        onValueChange={setName}
        required
        value={name}
      />
      <TextField
        autoComplete={AUTOCOMPLETE.tel}
        description="Ten digits. Used for the ready-for-delivery message."
        enterKeyHint="next"
        inputMode="tel"
        label="Mobile number"
        name="mobile"
        onValueChange={setPhone}
        required
        type="tel"
        value={phone}
      />
      <TextArea
        autoComplete={AUTOCOMPLETE.streetAddress}
        label="Address"
        name="address"
        onValueChange={setAddress}
        value={address}
      />
      <TextField
        autoComplete={AUTOCOMPLETE.postalCode}
        enterKeyHint="done"
        inputMode="numeric"
        label="PIN code"
        name="postal_code"
        onValueChange={setPostcode}
        value={postcode}
      />
    </div>
  )
}

const meta = {
  title: 'Forms/Controls',
  component: ControlGallery,
  parameters: { layout: 'padded' },
} satisfies Meta<typeof ControlGallery>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const Required: Story = { args: { required: true } }

/** Every control invalid at once: the error is a sentence, never a code (3.3.1). */
export const Invalid: Story = { args: { invalid: true, required: true } }

/** Not editable and not focusable. Never used to convey a status. */
export const Disabled: Story = { args: { disabled: true } }

/** Not editable but still focusable and readable — the right state for a value under review. */
export const ReadOnly: Story = { args: { readOnly: true } }

/** Required for every component: the layout has to survive 40% text growth. */
export const PseudoLocale: Story = {
  args: { required: true, invalid: true },
  globals: { locale: PSEUDO_LOCALE },
}

export const Tamil: Story = { args: { required: true }, globals: { locale: 'ta-IN' } }

export const HighContrastSunlight: Story = {
  args: { invalid: true, required: true },
  globals: { theme: 'contrast' },
}

export const Dark: Story = { args: { invalid: true }, globals: { theme: 'dark' } }

export const LargestTextSize: Story = {
  args: { invalid: true, required: true },
  globals: { textSize: '150' },
}

/** The reflow floor. Nothing may scroll the page sideways here (1.4.10). */
export const ReflowFloor: Story = {
  args: { invalid: true, required: true },
  globals: { viewport: { value: 'reflowFloor' } },
}

export const CustomerDetails: Story = {
  render: () => <CustomerFields />,
}
