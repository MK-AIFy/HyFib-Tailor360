import { act, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import type { RenderWithProvidersOptions } from '../../design-system/testing/renderWithProviders'
import { PSEUDO_LOCALE } from '../../i18n/pseudo'
import { DisplayPreferencesProvider } from '../../app/DisplayPreferencesProvider'
import { createInMemoryDisplayPreferencesStore } from '../../app/preferences'
import type { DisplayPreferencesStore } from '../../app/preferences'
import { useDisplayPreferences } from '../../app/useDisplayPreferences'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { SessionProvider } from '../../auth/SessionProvider'
import { useSession } from '../../auth/useSession'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import type { SignInStep } from '../../auth/types'
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
  document.documentElement.removeAttribute('data-reduced-motion')
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

    await waitFor(() => {
      expect(getByRole('radio', { name: 'Dark' })).toBeEnabled()
    })
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
        reducedMotion: false,
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

    await waitFor(() => {
      expect(getByRole('radio', { name: label })).toBeEnabled()
    })
    await user.click(getByRole('radio', { name: label }))

    expect(document.documentElement).toHaveAttribute('data-text-size', expected)
  })

  it('persists the choice through the store', async () => {
    const user = userEvent.setup()
    const store = createInMemoryDisplayPreferencesStore()
    const { getByRole } = renderPanel(store)

    await waitFor(() => {
      expect(getByRole('radio', { name: 'Compact — desktop only' })).toBeEnabled()
    })
    await user.click(getByRole('radio', { name: 'Compact — desktop only' }))

    expect(await store.read()).toEqual({
      theme: 'system',
      textSize: '100',
      density: 'compact',
      reducedMotion: false,
    })
  })

  it('shows the stored choice as the one already selected', async () => {
    const { getByRole } = renderPanel(
      createInMemoryDisplayPreferencesStore({
        theme: 'contrast',
        textSize: '150',
        density: 'comfortable',
        reducedMotion: false,
      }),
    )

    await waitFor(() => {
      expect(getByRole('radio', { name: 'High contrast — for sunlight' })).toBeChecked()
    })
    expect(getByRole('radio', { name: '150% — largest' })).toBeChecked()
  })

  it('toggles reduced motion and persists it through the store', async () => {
    const user = userEvent.setup()
    const store = createInMemoryDisplayPreferencesStore()
    const { getByRole } = renderPanel(store)

    await waitFor(() => {
      expect(getByRole('switch', { name: 'Reduce motion' })).toBeEnabled()
    })
    await user.click(getByRole('switch', { name: 'Reduce motion' }))

    expect(document.documentElement).toHaveAttribute('data-reduced-motion', 'true')
    expect(await store.read()).toMatchObject({ reducedMotion: true })
  })

  it('disables every control until the store has answered', () => {
    // The acceptance criterion is "while status is loading the defaults are applied and the panel's
    // controls are disabled" — a store whose read() never resolves is the loading state held still.
    const neverReads: DisplayPreferencesStore = {
      read: () => new Promise(() => undefined),
      write: () => Promise.resolve(),
    }
    const { getByRole } = renderPanel(neverReads)

    expect(getByRole('radio', { name: 'Dark' })).toBeDisabled()
    expect(getByRole('switch', { name: 'Reduce motion' })).toBeDisabled()
  })

  it('shows a sample of the text the setting is judged on', () => {
    // A job number, a date and an amount — exactly the shop-floor text A11Y-54 asks a person to read
    // in sunlight at arm's length.
    const { getByText } = renderPanel()

    expect(getByText(/J-CBE01-2627-000512-01/)).toBeInTheDocument()
  })

  it('says where the settings are kept, on this device, when there is no account behind them', () => {
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

/* Account-backed preferences ------------------------------------------------------------------- */

/** Drives sign-in and sign-out from inside the tree, the same shape as RequireSession.test.tsx's. */
function SessionActions({ step }: { readonly step: SignInStep }) {
  const { recordAuthentication, signOut } = useSession()
  return (
    <>
      <button onClick={() => void recordAuthentication(step)} type="button">
        Sign in
      </button>
      <button onClick={() => void signOut()} type="button">
        Sign out
      </button>
    </>
  )
}

function renderAccountBackedPanel() {
  return renderWithProviders(
    <SessionProvider>
      <DisplayPreferencesProvider>
        <SessionActions step="complete" />
        <DisplayPreferencesPanel />
      </DisplayPreferencesProvider>
    </SessionProvider>,
  )
}

describe('account-backed preferences', () => {
  // Routes must be registered before rendering: SessionProvider's mount effect fires its
  // GET /api/v1/me request synchronously during render, so a route added afterwards is too late
  // and the request falls through to stubFetch's default response.
  let transport: FetchStub

  beforeEach(() => {
    forgetAntiforgeryToken()
    setSessionChallengeHandler(null)
    transport = stubFetch()
  })

  afterEach(() => {
    setSessionChallengeHandler(null)
    forgetAntiforgeryToken()
    vi.unstubAllGlobals()
  })

  it('applies the signed-in account before anything behind RequireSession would paint', async () => {
    transport.route('GET /api/v1/me', () =>
      jsonResponse(
        aCurrentUser({
          preferences: {
            locale: 'en-IN',
            timeZoneId: 'Asia/Kolkata',
            theme: 'HighContrast',
            textSize: 'Larger',
            density: 'Compact',
            reducedMotion: true,
            landingRoute: null,
          },
        }),
      ),
    )
    const { getByRole, getByText } = renderAccountBackedPanel()

    await waitFor(() => {
      expect(document.documentElement).toHaveAttribute('data-theme', 'contrast')
    })
    expect(document.documentElement).toHaveAttribute('data-text-size', '150')
    expect(document.documentElement).toHaveAttribute('data-density', 'compact')
    expect(document.documentElement).toHaveAttribute('data-reduced-motion', 'true')
    expect(getByRole('radio', { name: 'High contrast — for sunlight' })).toBeChecked()
    expect(
      getByText('These settings are saved to your account and applied wherever you sign in.'),
    ).toBeInTheDocument()
  })

  it('saves a change, sends all seven members, and announces it', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    transport.route('PUT /api/v1/me/preferences', () => jsonResponse({}))
    const { getByRole } = renderAccountBackedPanel()

    await waitFor(() => {
      expect(getByRole('radio', { name: 'Dark' })).toBeEnabled()
    })
    await user.click(getByRole('radio', { name: 'Dark' }))

    await waitFor(() => {
      expect(getByRole('status')).toHaveTextContent('Saved')
    })

    const [call] = transport.callsTo('PUT /api/v1/me/preferences')
    expect(call?.body).toEqual({
      locale: 'en-IN',
      timeZoneId: 'Asia/Kolkata',
      theme: 'Dark',
      textSize: 'Standard',
      density: 'Comfortable',
      reducedMotion: false,
      landingRoute: null,
    })
  })

  it('shows a retryable error when the save fails, keeps the choice applied, and resends on retry', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    let attempts = 0
    transport.route('PUT /api/v1/me/preferences', () => {
      attempts += 1
      return attempts === 1
        ? Promise.resolve(new Response(null, { status: 500 }))
        : jsonResponse({})
    })
    const { getByRole } = renderAccountBackedPanel()

    await waitFor(() => {
      expect(getByRole('radio', { name: 'Dark' })).toBeEnabled()
    })
    await user.click(getByRole('radio', { name: 'Dark' }))

    await waitFor(() => {
      expect(getByRole('alert')).toBeInTheDocument()
    })
    // The choice stays applied even though the save failed — this session is correct regardless.
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')

    await user.click(getByRole('button', { name: /Try again/ }))

    await waitFor(() => {
      expect(getByRole('status')).toHaveTextContent('Saved')
    })
    expect(attempts).toBe(2)
  })

  it('blocks the save while offline, without attempting it, and retries once the connection returns', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    transport.route('PUT /api/v1/me/preferences', () => jsonResponse({}))
    const { getByRole, getByText } = renderAccountBackedPanel()

    await waitFor(() => {
      expect(getByRole('radio', { name: 'Dark' })).toBeEnabled()
    })

    Object.defineProperty(window.navigator, 'onLine', { value: false, configurable: true })
    act(() => {
      window.dispatchEvent(new Event('offline'))
    })

    await user.click(getByRole('radio', { name: 'Dark' }))

    await waitFor(() => {
      expect(getByText('Needs connection — this will not be queued')).toBeInTheDocument()
    })
    expect(transport.callsTo('PUT /api/v1/me/preferences')).toHaveLength(0)

    Object.defineProperty(window.navigator, 'onLine', { value: true, configurable: true })
    act(() => {
      window.dispatchEvent(new Event('online'))
    })

    await user.click(getByRole('button', { name: /Try again/ }))

    await waitFor(() => {
      expect(transport.callsTo('PUT /api/v1/me/preferences')).toHaveLength(1)
    })
  })

  it('renders the defaults for a server value this build does not recognise, rather than throwing', async () => {
    transport.route('GET /api/v1/me', () =>
      jsonResponse(
        aCurrentUser({
          preferences: {
            locale: 'en-IN',
            timeZoneId: 'Asia/Kolkata',
            theme: 'Sepia',
            textSize: '175',
            density: 'Airy',
            reducedMotion: false,
            landingRoute: null,
          },
        }),
      ),
    )
    const { getByRole } = renderAccountBackedPanel()

    await waitFor(() => {
      expect(getByRole('radio', { name: 'Follow the device' })).toBeChecked()
    })
    expect(getByRole('radio', { name: '100% — standard' })).toBeChecked()
    expect(getByRole('radio', { name: 'Comfortable' })).toBeChecked()
  })

  it('never lets one account leak into the next on the same device', async () => {
    // The criterion the whole slice exists for: signing out reverts to the defaults, and a second
    // account signing in afterwards gets its own preferences, never the first account's.
    transport.route('GET /api/v1/me', () =>
      jsonResponse(
        aCurrentUser({
          userId: 'account-a',
          preferences: {
            locale: 'en-IN',
            timeZoneId: 'Asia/Kolkata',
            theme: 'HighContrast',
            textSize: 'Larger',
            density: 'Compact',
            reducedMotion: true,
            landingRoute: null,
          },
        }),
      ),
    )
    transport.route('POST /api/v1/auth/logout', () => new Response(null, { status: 204 }))
    const { getByRole } = renderAccountBackedPanel()

    await waitFor(() => {
      expect(document.documentElement).toHaveAttribute('data-theme', 'contrast')
    })

    await userEvent.setup().click(getByRole('button', { name: 'Sign out' }))

    await waitFor(() => {
      expect(document.documentElement).not.toHaveAttribute('data-theme')
    })
    expect(document.documentElement).not.toHaveAttribute('data-reduced-motion')

    transport.route('GET /api/v1/me', () =>
      jsonResponse(
        aCurrentUser({
          userId: 'account-b',
          preferences: {
            locale: 'en-IN',
            timeZoneId: 'Asia/Kolkata',
            theme: 'Dark',
            textSize: 'Standard',
            density: 'Comfortable',
            reducedMotion: false,
            landingRoute: null,
          },
        }),
      ),
    )

    await userEvent.setup().click(getByRole('button', { name: 'Sign in' }))

    await waitFor(() => {
      expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
    })
    expect(getByRole('radio', { name: 'Dark' })).toBeChecked()
    expect(getByRole('radio', { name: '100% — standard' })).toBeChecked()
  })

  it('keeps an anonymous device choice from surviving into a session that stores the default', async () => {
    // The opposite direction: a device-local choice, made before anyone signed in, must not outlive
    // the sign-in of an account that stores something else. The reading is deliberate and asserted,
    // because the alternative is defensible and must not arrive here by accident.
    const user = userEvent.setup()
    transport.route('GET /api/v1/me', () => problemResponse(401, 'identity.session-required'))
    const { getByRole } = renderAccountBackedPanel()

    await waitFor(() => {
      expect(getByRole('radio', { name: 'Dark' })).toBeEnabled()
    })
    await user.click(getByRole('radio', { name: 'High contrast — for sunlight' }))
    expect(document.documentElement).toHaveAttribute('data-theme', 'contrast')

    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser()))
    await user.click(getByRole('button', { name: 'Sign in' }))

    await waitFor(() => {
      expect(document.documentElement).not.toHaveAttribute('data-theme')
    })
    expect(getByRole('radio', { name: 'Follow the device' })).toBeChecked()
  })
})
