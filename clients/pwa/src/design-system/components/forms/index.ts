/**
 * The forms family.
 *
 * Every control here implements the one `FieldProps` contract from
 * `src/design-system/foundations/FieldProps.ts` — imported, never redefined — so that a label, a
 * description, an error, a warning, a unit, `aria-describedby`, `aria-required` and `aria-invalid`
 * are wired the same way in all of them. `formContract.test.tsx` asserts exactly that, control by
 * control, which is what makes the contract a fact rather than a convention.
 *
 * There is no barrel at the root of `src/design-system` on purpose. A consumer imports from
 * `.../design-system/components/forms`, so a family can be added without every family's change
 * touching one shared index.
 */
export { Field, FieldGroup, FieldMessages } from './Field'
export type { FieldGroupShellProps, FieldMessagesProps, FieldShellProps } from './Field'

export { TextField } from './TextField'
export type { TextFieldProps } from './TextField'
export { TextArea } from './TextArea'
export type { TextAreaProps } from './TextArea'
export { Select } from './Select'
export type { SelectProps } from './Select'
export { Checkbox } from './Checkbox'
export type { CheckboxProps } from './Checkbox'
export { RadioGroup } from './RadioGroup'
export type { RadioGroupProps } from './RadioGroup'
export { Switch } from './Switch'
export type { SwitchProps } from './Switch'
export { DateField } from './DateField'
export type { DateFieldProps } from './DateField'

export { NumericStepper } from './NumericStepper'
export type { NumericStepperProps } from './NumericStepper'
export { FractionInput } from './FractionInput'
export type { FractionInputProps } from './FractionInput'
export { MeasurementField } from './MeasurementField'
export type { MeasurementFieldProps } from './MeasurementField'

export { FormErrorSummary } from './FormErrorSummary'
export type { FormErrorSummaryProps } from './FormErrorSummary'

export { AUTOCOMPLETE } from './autocomplete'
export type { AutocompleteToken } from './autocomplete'
export { ENTER_KEY_HINTS, TEXT_FIELD_TYPES } from './controlTypes'
export type { ChoiceOption, EnterKeyHint, FormStep, TextFieldType } from './controlTypes'
export {
  MEASUREMENT_BANDS,
  MEASUREMENT_DISPLAY_UNITS,
  MEASUREMENT_EXAMPLE_MILLIMETRES,
  clampToBounds,
  evaluateMeasurement,
  fractionOptions,
  fractionPartsToMillimetres,
  isConfirmationBand,
  isInchFractionStep,
  isMeasurementDisplayUnit,
  isRejectedBand,
  millimetresToFractionParts,
} from './measurement'
export type {
  FractionOption,
  FractionParts,
  MeasurementBand,
  MeasurementBounds,
  MeasurementDisplayUnit,
} from './measurement'
