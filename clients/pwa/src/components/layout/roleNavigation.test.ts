import { describe, expect, it } from 'vitest'
import { JOURNEY_ROLES } from '../../design-system/foundations/types'
import { MAX_BOTTOM_NAV_ITEMS } from '../navigation/navigationItems'
import { ICON_NAMES } from '../primitives/icons'
import { DESTINATIONS, DESTINATION_IDS, PRIMARY_ACTIONS, ROLE_NAVIGATION } from './roleNavigation'
import { enIN } from '../../i18n/en-IN'

const catalogue = enIN as Record<string, string>

describe('the role navigation table', () => {
  it('covers every journey role', () => {
    // The eight of A11Y-RJ-01..08. A role with no table entry is a role whose reference journey
    // cannot be walked at all.
    for (const role of JOURNEY_ROLES) {
      expect(ROLE_NAVIGATION[role]).toBeDefined()
    }
  })

  it('starts every role at Home', () => {
    // 3.2.3 Consistent Navigation and checklist item A11Y-10: a first destination that moves per
    // role is the same failure repeated across a shift on a shared device.
    for (const role of JOURNEY_ROLES) {
      expect(ROLE_NAVIGATION[role].destinations[0]).toBe('home')
      expect(ROLE_NAVIGATION[role].bottomBar[0]).toBe('home')
    }
  })

  it('never puts more than five destinations in a phone bottom bar', () => {
    // At the 320 px reflow floor a sixth 44 px target with a legible label does not fit, and
    // shrinking the targets to make it fit is the trade section 5 refuses.
    for (const role of JOURNEY_ROLES) {
      expect(ROLE_NAVIGATION[role].bottomBar.length).toBeLessThanOrEqual(MAX_BOTTOM_NAV_ITEMS)
    }
  })

  it('only puts destinations in the bar that the role can reach', () => {
    for (const role of JOURNEY_ROLES) {
      const reachable = new Set(ROLE_NAVIGATION[role].destinations)
      for (const id of ROLE_NAVIGATION[role].bottomBar) {
        expect(reachable.has(id)).toBe(true)
      }
    }
  })

  it('gives every destination a real glyph and a translated name', () => {
    for (const id of DESTINATION_IDS) {
      const destination = DESTINATIONS[id]
      expect(ICON_NAMES).toContain(destination.icon)
      expect(catalogue[destination.messageId]).toBeDefined()
      expect(destination.href.startsWith('/')).toBe(true)
    }
  })

  it('marks only the root as an exact match', () => {
    // Without it every screen in the application would report itself as the current destination.
    expect(DESTINATIONS.home.end).toBe(true)
    for (const id of DESTINATION_IDS.filter((candidate) => candidate !== 'home')) {
      expect(DESTINATIONS[id].end).toBeUndefined()
    }
  })

  it('gives every primary action a glyph and a translated name', () => {
    for (const action of Object.values(PRIMARY_ACTIONS)) {
      expect(ICON_NAMES).toContain(action.icon)
      expect(catalogue[action.messageId]).toBeDefined()
    }
  })

  it('gives the four shop-floor roles Scan as their primary action', () => {
    // This is what the #50 blueprint means by a scanner-first phone layout: the roles that work with
    // a garment in one hand reach for the same control in the same corner.
    for (const role of ['tailor', 'tailor-master', 'inventory', 'delivery'] as const) {
      expect(ROLE_NAVIGATION[role].primaryAction).toBe('scan')
    }
  })

  it('gives the reading role no floating action at all', () => {
    // An Owner's phone work is dashboards. A floating button over a chart is a control in the way.
    expect(ROLE_NAVIGATION.owner.primaryAction).toBeNull()
  })

  it('never lists a destination twice for one role', () => {
    for (const role of JOURNEY_ROLES) {
      const { destinations } = ROLE_NAVIGATION[role]
      expect(new Set(destinations).size).toBe(destinations.length)
    }
  })
})
