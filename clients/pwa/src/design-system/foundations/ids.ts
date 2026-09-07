import { useId } from 'react'
import { fieldElementIds } from './FieldProps'
import type { FieldElementIds } from './FieldProps'

/**
 * The ids for one field's parts.
 *
 * Pass the field's explicit `id` when it has one — the step-aware `FormErrorSummary` needs a stable
 * id to link to — and otherwise let React generate one. `useId` is stable across a re-render and
 * unique across a server render and its hydration, which is what a hand-rolled counter is not.
 */
export function useFieldIds(explicitId?: string): FieldElementIds {
  const generated = useId()
  return fieldElementIds(explicitId ?? generated)
}
