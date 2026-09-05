import { describe, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { Filters } from './Filters'
import type { AppliedFilter } from './Filters'

const applied: readonly AppliedFilter[] = [
  { id: 'phase', label: 'Phase: Finishing' },
  { id: 'due', label: 'Due: this week' },
]

describe('Filters', () => {
  it('is a named region, so a screen reader can jump to it and back out', async () => {
    const { getByRole, container } = renderWithProviders(<Filters applied={applied} />)

    expect(getByRole('region', { name: 'Filters' })).toBeInTheDocument()
    await expectNoAccessibilityViolations(container)
  })

  it('states every applied filter in words', () => {
    const { getByRole } = renderWithProviders(<Filters applied={applied} />)
    const list = getByRole('list', { name: 'Applied filters' })

    // A filter that is on but invisible is why somebody reports that a job has vanished.
    expect(list.textContent).toContain('Phase: Finishing')
    expect(list.textContent).toContain('Due: this week')
  })

  it('says so when nothing is filtering the list', () => {
    const { getByText } = renderWithProviders(<Filters />)

    expect(getByText('No filters applied')).toBeInTheDocument()
  })

  it('gives each chip a remove control that names the filter it removes', async () => {
    const onRemove = vi.fn()
    const { getByRole } = renderWithProviders(<Filters applied={applied} onRemove={onRemove} />)

    await userEvent.click(getByRole('button', { name: 'Remove filter: Phase: Finishing' }))

    expect(onRemove).toHaveBeenCalledWith('phase')
  })

  it('offers one control that clears the lot', async () => {
    const onClearAll = vi.fn()
    const { getByRole } = renderWithProviders(<Filters applied={applied} onClearAll={onClearAll} />)

    await userEvent.click(getByRole('button', { name: 'Clear all filters' }))

    expect(onClearAll).toHaveBeenCalledTimes(1)
  })

  it('hides the clear control when there is nothing to clear', () => {
    const { queryByRole } = renderWithProviders(<Filters onClearAll={() => undefined} />)

    expect(queryByRole('button', { name: 'Clear all filters' })).toBeNull()
  })

  it('announces the result count politely, once, rather than per control', () => {
    const { getByRole } = renderWithProviders(<Filters applied={applied} resultCount={7} />)

    // 4.1.3. Polite, never assertive: an interrupting count would talk over the control that
    // changed it, and a person ticking three boxes would hear nine announcements.
    expect(getByRole('status').textContent).toContain('7 results')
  })

  it('uses the plural form the count needs', () => {
    const { getByRole } = renderWithProviders(<Filters resultCount={1} />)

    expect(getByRole('status').textContent).toContain('1 result')
  })

  it('holds the filter controls a screen gives it', () => {
    const { getByLabelText } = renderWithProviders(
      <Filters>
        <label>
          Phase
          <select>
            <option>Cutting</option>
          </select>
        </label>
      </Filters>,
    )

    expect(getByLabelText('Phase')).toBeInTheDocument()
  })

  it('translates its own words, which the pseudo-locale makes visible', () => {
    const { container } = renderWithProviders(<Filters applied={applied} resultCount={3} />, {
      locale: 'en-XA',
    })

    expect(container.textContent).toMatch(/⟦.+⟧/)
  })
})
