import { waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it } from 'vitest'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import type { RenderWithProvidersOptions } from '../../design-system/testing/renderWithProviders'
import { PSEUDO_LOCALE } from '../../i18n/pseudo'
import { DisplayPreferencesProvider } from '../../app/DisplayPreferencesProvider'
import { createInMemoryDisplayPreferencesStore } from '../../app/preferences'
import type { DisplayPreferencesStore } from '../../app/preferences'
import { useDisplayPreferences } from '../../app/useDisplayPreferences'
import { DisplayPreferencesPanel } from './DisplayPreferencesPanel'

function renderPanel(
  store: DisplayPreferencesStore = createInMemoryDisplayPreferencesStore(),
  options?: RenderWithProvidersOptions,
) {
  return renderWithProviders(
    <DisplayPreferencesProvider store={store}>
      <DisplayPreferencesPanel />
    </DisplayPreferencesProvider>,
    options,
  )
}

afterEach(() => {
  document.documentElement.removeAttribute('data-theme')
  document.documentElement.removeAttribute('data-text-size')
  document.documentElement.removeAttribute('data-density')
})

describe('DisplayPreferencesPanel', () => {
  it('announces each setting as a named group before its first option', () => {
    // Checklist item A11Y-59: three ungrouped sets of radios is a list to guess at.
    const { getByRole } = renderPanel()

    expect(getByRole('group', { name: /Theme/ })).toBeInTheDocument()
    expect(getByRole('group', { name: /Text size/ })).toBeInTheDocument()
    expect(getByRole('group', { name: /Row spacing/ })).toBeInTheDocument()
  })

  it('offers the high-contrast theme by what it is for', () => {
    // A person choosing it is doing so in afternoon sunlight at the counter, which is not the moment
    // to work out what "contrast" means.
    const { getByRole } = renderPanel()

    expect(getByRole('radio', { name: 'High contrast — for sunlight' })).toBeInTheDocument()
  })

  it('applies a theme choice to the document at once', async () => {
    const user = userEvent.setup()
    const { getByRole } = renderPanel()

    await user.click(getByRole('radio', { name: 'Dark' }))

    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
  })

  it('hands the decision back to the device for the system theme', async () => {
    // `system` removes the attribute rather than setting one, which is what lets
    // prefers-color-scheme and prefers-contrast decide again.
    const user = userEvent.setup()
    const { getByRole } = renderPanel(
      createInMemoryDisplayPreferencesStore({
        theme: 'dark',
        textSize: '100',
        density: 'comfortable',
      }),
    )
    await waitFor(() => {
      expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
    })

    await user.click(getByRole('radio', { name: 'Follow the device' }))

    expect(document.documentElement).not.toHaveAttribute('data-theme')
  })

  it.each([
    ['125% — larger', '125'],
    ['150% — largest', '150'],
  ])('applies the %s text size', async (label, expected) => {
    // Checklist item A11Y-72: the product's own preference, separate from the browser zoom, and the
    // one a person with presbyopia actually finds.
    const user = userEvent.setup()
    const { getByRole } = renderPanel()

    await user.click(getByRole('radio', { name: label }))

    expect(document.documentElement).toHaveAttribute('data-text-size', expected)
  })

  it('persists the choice through the store', async () => {
    const user = userEvent.setup()
    const store = createInMemoryDisplayPreferencesStore()
    const { getByRole } = renderPanel(store)

    await user.click(getByRole('radio', { name: 'Compact — desktop only' }))

    expect(await store.read()).toEqual({ theme: 'system', textSize: '100', density: 'compact' })
  })

  it('shows the stored choice as the one already selected', async () => {
    const { getByRole } = renderPanel(
      createInMemoryDisplayPreferencesStore({
        theme: 'contrast',
        textSize: '150',
        density: 'comfortable',
      }),
    )

    await waitFor(() => {
      expect(getByRole('radio', { name: 'High contrast — for sunlight' })).toBeChecked()
    })
    expect(getByRole('radio', { name: '150% — largest' })).toBeChecked()
  })

  it('shows a sample of the text the setting is judged on', () => {
    // A job number, a date and an amount — exactly the shop-floor text A11Y-54 asks a person to read
    // in sunlight at arm's length.
    const { getByText } = renderPanel()

    expect(getByText(/J-CBE01-2627-000512-01/)).toBeInTheDocument()
  })

  it('says where the settings are kept', () => {
    // Honest about the stub: they are on this device until identity.user_preferences exists.
    const { getByText } = renderPanel()

    expect(getByText('These settings are stored on this device.')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = renderPanel()

    await expectNoAccessibilityViolations(container)
  })

  it('renders in the pseudo-locale', () => {
    const { container } = renderPanel(createInMemoryDisplayPreferencesStore(), {
      locale: PSEUDO_LOCALE,
    })

    // Every visible word came from the catalogue, so none of them survives untranslated.
    expect(container.textContent).not.toContain('Follow the device')
  })
})

describe('useDisplayPreferences', () => {
  it('fails loudly outside a provider', () => {
    // A settings control whose changes go nowhere looks like it is working, and a person adjusting
    // the text size because they cannot read the screen is the last person who should find out.
    function Orphan() {
      useDisplayPreferences()
      return null
    }

    expect(() => renderWithProviders(<Orphan />)).toThrow(/DisplayPreferencesProvider/)
  })
})
