import { describe, expect, it } from 'vitest'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { NavBadge } from './NavBadge'

describe('NavBadge', () => {
  it('says what the number means, so it is never a coloured dot', () => {
    const { container } = renderWithProviders(<NavBadge count={3} />)

    // A dot says "something here" only to somebody who can see it and already knows what it means,
    // which makes it a status conveyed by colour alone (section 4.1).
    expect(container.textContent).toContain('3 items need attention')
  })

  it('uses the singular when there is one', () => {
    const { container } = renderWithProviders(<NavBadge count={1} />)

    expect(container.textContent).toContain('1 item needs attention')
  })

  it('renders nothing at all for a zero', () => {
    const { container } = renderWithProviders(<NavBadge count={0} />)

    expect(container.querySelector('.nav-badge')).toBeNull()
  })

  it('caps the digits on screen but not the number that is announced', () => {
    const { container } = renderWithProviders(<NavBadge count={107} />)

    // A Tailor Master glancing at the bar needs to know it is a lot; a screen reader user needs to
    // know it is a hundred and seven.
    expect(container.querySelector('.nav-badge__count')?.textContent).toBe('99+')
    expect(container.textContent).toContain('107 items need attention')
  })
})
