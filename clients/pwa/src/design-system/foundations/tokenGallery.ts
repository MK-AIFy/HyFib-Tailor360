/**
 * The token names the gallery story shows.
 *
 * A plain list in its own module rather than in the story file, so the story stays a story and this
 * stays reviewable: adding a semantic token means adding one line here, and the gallery then shows
 * it in every theme, at every text size, next to everything it has to sit beside.
 */

export interface TokenGroup {
  readonly title: string
  readonly note: string
  readonly tokens: readonly string[]
}

export const COLOUR_GROUPS: readonly TokenGroup[] = [
  {
    title: 'Surfaces',
    note: 'What a component sits on. Every ink token below is proved against each of these.',
    tokens: [
      '--colour-surface',
      '--colour-surface-sunken',
      '--colour-surface-raised',
      '--colour-surface-selected',
      '--colour-surface-hover',
      '--colour-surface-pressed',
      '--colour-surface-disabled',
    ],
  },
  {
    title: 'Ink',
    note: '--colour-ink reaches 7:1 on every surface: it is what a Tailor reads in sunlight.',
    tokens: [
      '--colour-ink',
      '--colour-ink-muted',
      '--colour-ink-disabled',
      '--colour-ink-on-brand',
    ],
  },
  {
    title: 'Lines',
    note: '--colour-border-subtle draws dividers and is exempt from 1.4.11; the other two are not.',
    tokens: ['--colour-border-subtle', '--colour-border', '--colour-border-strong'],
  },
  {
    title: 'Brand',
    note: 'The header and the primary action.',
    tokens: [
      '--colour-brand',
      '--colour-brand-strong',
      '--colour-brand-contrast',
      '--colour-accent',
      '--colour-accent-ink',
      '--colour-link',
    ],
  },
  {
    title: 'Status',
    note: 'Never used alone: every status carries an icon and a word as well (1.4.1).',
    tokens: [
      '--colour-danger-ink',
      '--colour-danger-surface',
      '--colour-danger-border',
      '--colour-success-ink',
      '--colour-success-surface',
      '--colour-success-border',
      '--colour-warning-ink',
      '--colour-warning-surface',
      '--colour-warning-border',
      '--colour-info-ink',
      '--colour-info-surface',
      '--colour-info-border',
    ],
  },
  {
    title: 'Focus',
    note: 'Two rings, so the indicator is visible on a white field and on the dark header alike.',
    tokens: ['--colour-focus-ring', '--colour-focus-ring-contrast'],
  },
]

export const SPACING_TOKENS = [
  '--space-1',
  '--space-2',
  '--space-3',
  '--space-4',
  '--space-5',
  '--space-6',
  '--space-7',
  '--space-8',
] as const

export const TYPE_TOKENS = [
  '--font-size-050',
  '--font-size-100',
  '--font-size-200',
  '--font-size-300',
  '--font-size-400',
  '--font-size-500',
  '--font-size-600',
] as const

export const TARGET_TOKENS = [
  '--target-primary',
  '--target-standard',
  '--target-dense',
  '--target-floor',
] as const

export const ELEVATION_TOKENS = ['--elevation-1', '--elevation-2', '--elevation-3'] as const
