import { describe, expect, it } from 'vitest'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { Timeline } from './Timeline'
import type { TimelineEntry } from './Timeline'

const entries: readonly TimelineEntry[] = [
  {
    id: '1',
    title: 'Scanned in at Cutting',
    status: 'in-progress',
    absoluteTime: '04-09-2026 09:12 AM',
    dateTime: '2026-09-04T03:42:00Z',
    relativeTime: '3 hours ago',
    actor: 'Kavitha R',
  },
  {
    id: '2',
    title: 'QC failed — seam finish',
    status: 'qc-failed',
    absoluteTime: '04-09-2026 02:40 PM',
    dateTime: '2026-09-04T09:10:00Z',
    actor: 'Tailor Master',
    detail: 'Rework opened; the garment stays in production.',
  },
]

describe('Timeline', () => {
  it('is an ordered list, because the sequence is the meaning', () => {
    const { getByRole, getAllByRole } = renderWithProviders(<Timeline entries={entries} />)

    // "Item 3 of 11" is what a person walking a custody chain needs while looking for the handover
    // that went wrong.
    expect(getByRole('list', { name: 'History' })).toBeInTheDocument()
    expect(getAllByRole('listitem')).toHaveLength(entries.length)
  })

  it('always shows the absolute time, even when a relative cue is given', () => {
    const { container } = renderWithProviders(<Timeline entries={entries} />)

    // Section 12: a relative time never replaces the absolute one it stands for.
    expect(container.textContent).toContain('04-09-2026 09:12 AM')
    expect(container.textContent).toContain('3 hours ago')
  })

  it('marks the instant up for a machine as well as for a person', () => {
    const { container } = renderWithProviders(<Timeline entries={entries} />)
    const time = container.querySelector('time')

    expect(time).toHaveAttribute('datetime', '2026-09-04T03:42:00Z')
  })

  it('names the actor through the catalogue rather than by concatenation', () => {
    const { container } = renderWithProviders(<Timeline entries={entries} />)

    // Word order differs in Tamil, so "by {actor}" is an ICU message, not two strings joined.
    expect(container.textContent).toContain('by Kavitha R')
  })

  it('takes its marker glyph from the status, so the rail reads in greyscale', () => {
    const { container } = renderWithProviders(<Timeline entries={entries} />)
    const markers = container.querySelectorAll('.timeline__entry')

    expect(markers[0]).toHaveAttribute('data-tone', 'info')
    expect(markers[1]).toHaveAttribute('data-tone', 'danger')
  })

  it('falls back to a neutral marker for an entry that is not a status change', () => {
    const { container } = renderWithProviders(
      <Timeline
        entries={[{ id: '1', title: 'Note added', absoluteTime: '04-09-2026 09:12 AM' }]}
      />,
    )

    expect(container.querySelector('.timeline__entry')).toHaveAttribute('data-tone', 'neutral')
  })

  it('never announces itself: an audit trail that moved under a reader would fail 2.2.2', () => {
    const { container } = renderWithProviders(<Timeline entries={entries} />)

    expect(container.querySelector('.timeline')).not.toHaveAttribute('aria-live')
  })

  it('translates its own words, which the pseudo-locale makes visible', () => {
    const { container } = renderWithProviders(<Timeline entries={entries} />, { locale: 'en-XA' })

    expect(container.textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(<Timeline entries={entries} />)

    await expectNoAccessibilityViolations(container)
  })
})
