/**
 * The five things a republish can leave standing against a pinned draft
 * (`DesignSelectionMigration.DesignMigrationChange`, #142).
 *
 * Every kind already carries its own sentence in `change.message`, composed server-side in the
 * shop's words — there is nothing for the client to template. What the client adds is which of them
 * mean a choice was actually lost, which is what decides whether the prompt reads as a plain notice
 * or as a warning that something the customer chose will not survive migrating.
 */
export const DESIGN_MIGRATION_KINDS = [
  'design.group-no-longer-offered',
  'design.group-newly-required',
  'design.option-retired',
  'design.service-type-no-longer-offered',
  'design.rule-added',
] as const

export type DesignMigrationKind = (typeof DESIGN_MIGRATION_KINDS)[number]

/** Whether this kind of change means something already chosen would be dropped by migrating. */
export function dropsAChoice(kind: string): boolean {
  return kind === 'design.group-no-longer-offered' || kind === 'design.option-retired'
}
