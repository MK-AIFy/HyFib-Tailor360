import { useCallback, useId, useRef, useState } from 'react'
import type { KeyboardEvent, ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Icon } from '../primitives/Icon'
import type { IconName } from '../primitives/icons'
import { NavBadge } from './NavBadge'
import './Tabs.css'

export interface TabItem {
  readonly id: string
  /** The tab's name, already translated. Always text: there are no icon-only tabs. */
  readonly label: string
  readonly icon?: IconName
  /** The panel's content. Only the selected panel is mounted. */
  readonly panel: ReactNode
  readonly badgeCount?: number
}

export interface TabsProps {
  readonly items: readonly TabItem[]
  /** Controlled selection. Omit both this and `defaultTabId` and the first tab is selected. */
  readonly selectedTabId?: string
  readonly defaultTabId?: string
  readonly onTabChange?: (tabId: string) => void
  /** Names the tab list. Defaults to "Sections of this screen". */
  readonly label?: string
  readonly className?: string
}

/**
 * A tab set: one strip of tabs, one panel.
 *
 * The keyboard behaviour is the ARIA authoring practice, and each part of it earns its place:
 *
 *  - **One tab stop for the whole strip.** A roving `tabindex` means `Tab` moves past the tabs to
 *    the panel rather than through six of them, which is what a keyboard-driven counter desktop
 *    needs when the tabs are the same on every screen.
 *  - **Arrow keys move between tabs**, `Home` and `End` jump to the ends, and selection follows
 *    focus. Automatic activation is right here because every panel is content that is already
 *    loaded — nothing is fetched by arrowing across, so nothing is wasted.
 *  - **The panel is focusable.** A panel whose content has no focusable element would otherwise be
 *    unreachable from the keyboard, and 2.1.1 does not have an exception for "it is only text".
 *
 * A tab set is not navigation between screens. It divides one screen, so the tabs are buttons and
 * the address does not change; moving between screens is `BottomNav` and `SideNav`, which are links
 * because they go somewhere.
 */
export function Tabs({
  items,
  selectedTabId,
  defaultTabId,
  onTabChange,
  label,
  className,
}: TabsProps) {
  const intl = useIntl()
  const baseId = useId()
  const tabRefs = useRef(new Map<string, HTMLButtonElement>())
  const [uncontrolledId, setUncontrolledId] = useState(defaultTabId ?? items[0]?.id ?? '')

  const selected = selectedTabId ?? uncontrolledId

  const select = useCallback(
    (tabId: string) => {
      if (selectedTabId === undefined) {
        setUncontrolledId(tabId)
      }
      onTabChange?.(tabId)
    },
    [selectedTabId, onTabChange],
  )

  const moveTo = useCallback(
    (index: number) => {
      const target = items[index]
      if (target === undefined) {
        return
      }
      select(target.id)
      tabRefs.current.get(target.id)?.focus()
    },
    [items, select],
  )

  const onKeyDown = useCallback(
    (event: KeyboardEvent<HTMLDivElement>) => {
      const current = items.findIndex((item) => item.id === selected)
      if (current < 0) {
        return
      }

      switch (event.key) {
        case 'ArrowRight':
          moveTo((current + 1) % items.length)
          break
        case 'ArrowLeft':
          moveTo((current - 1 + items.length) % items.length)
          break
        case 'Home':
          moveTo(0)
          break
        case 'End':
          moveTo(items.length - 1)
          break
        default:
          return
      }

      // Only reached when a key above was handled: stops Home and End scrolling the page out from
      // under somebody who was only changing tab.
      event.preventDefault()
    },
    [items, selected, moveTo],
  )

  const activeItem = items.find((item) => item.id === selected)

  return (
    <div className={cx('tabs', className)}>
      <div
        className="tabs__list"
        role="tablist"
        aria-label={label ?? intl.formatMessage({ id: 'navigation.tabs' })}
        onKeyDown={onKeyDown}
      >
        {items.map((item) => {
          const isSelected = item.id === selected
          return (
            <button
              key={item.id}
              ref={(element) => {
                if (element === null) {
                  tabRefs.current.delete(item.id)
                } else {
                  tabRefs.current.set(item.id, element)
                }
              }}
              type="button"
              role="tab"
              id={`${baseId}-tab-${item.id}`}
              className="tabs__tab"
              aria-selected={isSelected}
              aria-controls={`${baseId}-panel-${item.id}`}
              tabIndex={isSelected ? 0 : -1}
              onClick={() => {
                select(item.id)
              }}
            >
              {item.icon === undefined ? null : <Icon name={item.icon} />}
              <span className="tabs__tab-label">{item.label}</span>
              {item.badgeCount === undefined ? null : <NavBadge count={item.badgeCount} />}
            </button>
          )
        })}
      </div>

      {activeItem === undefined ? null : (
        <div
          className="tabs__panel"
          role="tabpanel"
          id={`${baseId}-panel-${activeItem.id}`}
          aria-labelledby={`${baseId}-tab-${activeItem.id}`}
          tabIndex={0}
        >
          {activeItem.panel}
        </div>
      )}
    </div>
  )
}
