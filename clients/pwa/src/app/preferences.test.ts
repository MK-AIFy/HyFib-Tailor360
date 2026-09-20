import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { forgetAntiforgeryToken } from '../auth/antiforgery'
import { setSessionChallengeHandler } from '../auth/apiClient'
import { jsonResponse, stubFetch } from '../auth/testing/fixtures'
import type { AccountPreferences } from '../auth/types'
import { DEFAULT_DISPLAY_PREFERENCES } from '../design-system/foundations/displayPreferences'
import {
  DISPLAY_PREFERENCES_STORAGE_KEY,
  createInMemoryDisplayPreferencesStore,
  createLocalDisplayPreferencesStore,
  createServerDisplayPreferencesStore,
  parseDisplayPreferences,
} from './preferences'

afterEach(() => {
  window.localStorage.clear()
  vi.restoreAllMocks()
})

describe('parseDisplayPreferences', () => {
  it('reads a complete stored value', () => {
    expect(
      parseDisplayPreferences({
        theme: 'contrast',
        textSize: '150',
        density: 'compact',
        reducedMotion: true,
      }),
    ).toEqual({ theme: 'contrast', textSize: '150', density: 'compact', reducedMotion: true })
  })

  it('falls back for anything it does not recognise', () => {
    // A stored value from a future version of the application must give a person the system theme,
    // not a blank screen.
    expect(
      parseDisplayPreferences({
        theme: 'sepia',
        textSize: '400',
        density: 'airy',
        reducedMotion: 'yes',
      }),
    ).toEqual(DEFAULT_DISPLAY_PREFERENCES)
  })

  it.each([null, undefined, 'contrast', 42])('falls back for the non-object %s', (payload) => {
    expect(parseDisplayPreferences(payload)).toEqual(DEFAULT_DISPLAY_PREFERENCES)
  })

  it('keeps the settings it can read and defaults only the rest', () => {
    expect(parseDisplayPreferences({ theme: 'dark' })).toEqual({
      theme: 'dark',
      textSize: '100',
      density: 'comfortable',
      reducedMotion: false,
    })
  })

  it('reads reduced motion when it is a boolean', () => {
    expect(parseDisplayPreferences({ reducedMotion: true })).toEqual({
      ...DEFAULT_DISPLAY_PREFERENCES,
      reducedMotion: true,
    })
  })
})

describe('the local display-preferences store', () => {
  it('round-trips a choice', async () => {
    const store = createLocalDisplayPreferencesStore()

    await store.write({
      theme: 'contrast',
      textSize: '125',
      density: 'comfortable',
      reducedMotion: true,
    })

    expect(await store.read()).toEqual({
      theme: 'contrast',
      textSize: '125',
      density: 'comfortable',
      reducedMotion: true,
    })
  })

  it('starts from the defaults when nothing is stored', async () => {
    expect(await createLocalDisplayPreferencesStore().read()).toEqual(DEFAULT_DISPLAY_PREFERENCES)
  })

  it('survives a corrupt stored value', async () => {
    window.localStorage.setItem(DISPLAY_PREFERENCES_STORAGE_KEY, 'not json at all')

    expect(await createLocalDisplayPreferencesStore().read()).toEqual(DEFAULT_DISPLAY_PREFERENCES)
  })

  it('survives storage that throws', async () => {
    // Kiosk profiles and private-browsing windows both do. A theme preference is never worth
    // breaking a shell over.
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied')
    })
    const store = createLocalDisplayPreferencesStore()

    await expect(
      store.write({ theme: 'dark', textSize: '100', density: 'comfortable', reducedMotion: false }),
    ).resolves.toBeUndefined()
    expect(await store.read()).toEqual(DEFAULT_DISPLAY_PREFERENCES)
  })
})

describe('the in-memory display-preferences store', () => {
  it('remembers within one session and nothing beyond it', async () => {
    const store = createInMemoryDisplayPreferencesStore()

    await store.write({ theme: 'dark', textSize: '150', density: 'compact', reducedMotion: true })

    expect(await store.read()).toEqual({
      theme: 'dark',
      textSize: '150',
      density: 'compact',
      reducedMotion: true,
    })
    expect(window.localStorage.getItem(DISPLAY_PREFERENCES_STORAGE_KEY)).toBeNull()
  })
})

describe('the server-backed display-preferences store', () => {
  function anAccount(overrides: Partial<AccountPreferences> = {}): AccountPreferences {
    return {
      locale: 'ta-IN',
      timeZoneId: 'Asia/Kolkata',
      theme: 'HighContrast',
      textSize: 'Larger',
      density: 'Compact',
      reducedMotion: true,
      landingRoute: '/orders/workboard',
      ...overrides,
    }
  }

  beforeEach(() => {
    forgetAntiforgeryToken()
    setSessionChallengeHandler(null)
  })

  it('maps the server vocabulary onto the document shape without a second GET', async () => {
    const transport = stubFetch()
    const store = createServerDisplayPreferencesStore(anAccount())

    expect(await store.read()).toEqual({
      theme: 'contrast',
      textSize: '150',
      density: 'compact',
      reducedMotion: true,
    })
    expect(transport.callsTo('GET /api/v1/me')).toHaveLength(0)
  })

  it('falls back to the defaults for a server value this build does not recognise', async () => {
    stubFetch()
    const store = createServerDisplayPreferencesStore(
      anAccount({ theme: 'Sepia', textSize: '175', density: 'Airy' }),
    )

    expect(await store.read()).toEqual({
      theme: DEFAULT_DISPLAY_PREFERENCES.theme,
      textSize: DEFAULT_DISPLAY_PREFERENCES.textSize,
      density: DEFAULT_DISPLAY_PREFERENCES.density,
      reducedMotion: true,
    })
  })

  it('writes a full replacement, carrying locale, timezone and landing route back unchanged', async () => {
    const transport = stubFetch()
    transport.route('PUT /api/v1/me/preferences', () => jsonResponse({}))
    const account = anAccount()
    const store = createServerDisplayPreferencesStore(account)

    await store.write({
      theme: 'dark',
      textSize: '100',
      density: 'comfortable',
      reducedMotion: false,
    })

    const [call] = transport.callsTo('PUT /api/v1/me/preferences')
    expect(call?.body).toEqual({
      locale: account.locale,
      timeZoneId: account.timeZoneId,
      theme: 'Dark',
      textSize: 'Standard',
      density: 'Comfortable',
      reducedMotion: false,
      landingRoute: account.landingRoute,
    })
  })

  it('rejects when the write fails, so the provider can report it', async () => {
    const transport = stubFetch()
    transport.route('PUT /api/v1/me/preferences', () =>
      Promise.resolve(new Response(null, { status: 500 })),
    )
    const store = createServerDisplayPreferencesStore(anAccount())

    await expect(
      store.write({
        theme: 'system',
        textSize: '100',
        density: 'comfortable',
        reducedMotion: false,
      }),
    ).rejects.toThrow()
  })
})
