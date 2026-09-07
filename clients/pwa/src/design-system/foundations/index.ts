/**
 * The foundations barrel.
 *
 * Foundations are types and pure functions — no JSX, no CSS, no React component. Everything in the
 * design system may import from here; nothing here imports from anywhere else in the design system.
 * That is what keeps the component families independent of one another, so they can be built and
 * reviewed in parallel.
 *
 * There is deliberately no barrel at the root of `src/design-system`: a single index that every
 * family had to be added to would be the one file every change touches.
 */
export { BREAKPOINTS, LAYOUT_PROOF_WIDTHS, shellKindForWidth } from './breakpoints'
export type { Breakpoint } from './breakpoints'
export { cx } from './cx'
export {
  DEFAULT_DISPLAY_PREFERENCES,
  DENSITY_ATTRIBUTE,
  TEXT_SIZE_ATTRIBUTE,
  THEME_ATTRIBUTE,
  applyDisplayPreferences,
  readDisplayPreferences,
} from './displayPreferences'
export type { DisplayPreferences } from './displayPreferences'
export {
  FIELD_INPUT_MODES,
  fieldControlAttributes,
  fieldDescribedBy,
  fieldElementIds,
  fieldErrorsFromProblemDetails,
  isFieldInvalid,
} from './FieldProps'
export type {
  FieldAriaSource,
  FieldControlAttributes,
  FieldElementIds,
  FieldErrorEntry,
  FieldInputMode,
  FieldProps,
  FieldUnit,
  ValidationProblemDetails,
} from './FieldProps'
export { useFieldIds } from './ids'
export { readCssVariable, setCssVariable } from './setCssVariable'
export type { CssVariableName } from './setCssVariable'
export {
  CONTROL_SIZES,
  DENSITIES,
  JOURNEY_ROLES,
  SHELL_KINDS,
  TEXT_SIZE_PREFERENCES,
  THEME_PREFERENCES,
  TONES,
} from './types'
export type {
  ControlSize,
  Density,
  JourneyRole,
  ShellKind,
  TextSizePreference,
  ThemePreference,
  Tone,
} from './types'
