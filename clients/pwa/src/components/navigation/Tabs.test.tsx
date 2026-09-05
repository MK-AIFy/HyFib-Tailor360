import { describe, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { Tabs } from './Tabs'
import type { TabItem } from './Tabs'

const items: readonly TabItem[] = [
  { id: 'details', label: 'Details', panel: <p>Order details</p> },
  { id: 'measurements', label: 'Measurements', icon: 'ruler', panel: <p>Measurement sheet</p> },
  { id: 'history', label: 'History', panel: <p>Custody history</p>, badgeCount: 2 },
]

describe('Tabs', () => {
  it('is a named tab list with one panel', () => {
    const { getByRole } = renderWithProviders(<Tabs items={items} />)

    expect(getByRole('tablist', { name: 'Sections of this screen' })).toBeInTheDocument()
    expect(getByRole('tabpanel', { name: 'Details' })).toBeInTheDocument()
  })

  it('selects the first tab when nothing says otherwise', () => {
    const { getByRole } = renderWithProviders(<Tabs items={items} />)

    expect(getByRole('tab', { name: 'Details' })).toHaveAttribute('aria-selected', 'true')
  })

  it('honours a starting tab', () => {
    const { getByRole } = renderWithProviders(<Tabs items={items} defaultTabId="history" />)

    expect(getByRole('tab', { name: /History/ })).toHaveAttribute('aria-selected', 'true')
  })

  it('is one tab stop for the whole strip, not one per tab', async () => {
    const { getByRole } = renderWithProviders(<Tabs items={items} />)

    await userEvent.tab()

    // A roving tabindex: Tab reaches the tabs once and then moves on to the panel, which is what a
    // keyboard-driven counter desktop needs when the tabs are the same on every screen.
    expect(getByRole('tab', { name: 'Details' })).toHaveFocus()
    expect(getByRole('tab', { name: 'Measurements' })).toHaveAttribute('tabindex', '-1')
  })

  it('moves between tabs with the arrow keys, selection following focus', async () => {
    const { getByRole } = renderWithProviders(<Tabs items={items} />)

    await userEvent.tab()
    await userEvent.keyboard('{ArrowRight}')

    expect(getByRole('tab', { name: /Measurements/ })).toHaveFocus()
    expect(getByRole('tab', { name: /Measurements/ })).toHaveAttribute('aria-selected', 'true')
    expect(getByRole('tabpanel', { name: /Measurements/ })).toBeInTheDocument()
  })

  it('wraps around at both ends', async () => {
    const { getByRole } = renderWithProviders(<Tabs items={items} />)

    await userEvent.tab()
    await userEvent.keyboard('{ArrowLeft}')

    expect(getByRole('tab', { name: /History/ })).toHaveFocus()
  })

  it('jumps to the ends with Home and End', async () => {
    const { getByRole } = renderWithProviders(<Tabs items={items} defaultTabId="measurements" />)

    await userEvent.tab()
    await userEvent.keyboard('{End}')
    expect(getByRole('tab', { name: /History/ })).toHaveFocus()

    await userEvent.keyboard('{Home}')
    expect(getByRole('tab', { name: 'Details' })).toHaveFocus()
  })

  it('selects on a click as well', async () => {
    const onTabChange = vi.fn()
    const { getByRole } = renderWithProviders(<Tabs items={items} onTabChange={onTabChange} />)

    await userEvent.click(getByRole('tab', { name: /History/ }))

    expect(onTabChange).toHaveBeenCalledWith('history')
    expect(getByRole('tabpanel', { name: /History/ })).toBeInTheDocument()
  })

  it('obeys a controlled selection rather than its own', async () => {
    const onTabChange = vi.fn()
    const { getByRole } = renderWithProviders(
      <Tabs items={items} selectedTabId="details" onTabChange={onTabChange} />,
    )

    await userEvent.click(getByRole('tab', { name: /History/ }))

    expect(onTabChange).toHaveBeenCalledWith('history')
    // The parent owns the value; the component does not move on its own.
    expect(getByRole('tab', { name: 'Details' })).toHaveAttribute('aria-selected', 'true')
  })

  it('makes the panel reachable from the keyboard even when it holds no controls', () => {
    const { getByRole } = renderWithProviders(<Tabs items={items} />)

    // 2.1.1 has no exception for "it is only text".
    expect(getByRole('tabpanel')).toHaveAttribute('tabindex', '0')
  })

  it('points each tab at its panel and each panel back at its tab', () => {
    const { getByRole } = renderWithProviders(<Tabs items={items} />)
    const tab = getByRole('tab', { name: 'Details' })
    const panel = getByRole('tabpanel')

    expect(tab.getAttribute('aria-controls')).toBe(panel.getAttribute('id'))
    expect(panel.getAttribute('aria-labelledby')).toBe(tab.getAttribute('id'))
  })

  it('carries an attention count in words as well as a number', () => {
    const { getByRole } = renderWithProviders(<Tabs items={items} />)

    expect(getByRole('tab', { name: /History/ })).toHaveAccessibleName(/2 items need attention/)
  })

  it('translates its own words, which the pseudo-locale makes visible', () => {
    const { getByRole } = renderWithProviders(<Tabs items={items} />, { locale: 'en-XA' })

    expect(getByRole('tablist').getAttribute('aria-label')).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(<Tabs items={items} />)

    await expectNoAccessibilityViolations(container)
  })
})
