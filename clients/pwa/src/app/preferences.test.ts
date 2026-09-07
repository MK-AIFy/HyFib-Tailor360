import { afterEach, describe, expect, it, vi } from 'vitest'
import { DEFAULT_DISPLAY_PREFERENCES } from '../design-system/foundations/displayPreferences'
import {
  DISPLAY_PREFERENCES_STORAGE_KEY,
  createInMemoryDisplayPreferencesStore,
  createLocalDisplayPreferencesStore,
  parseDisplayPreferences,
} from './preferences'

afterEach(() => {
  window.localStorage.clear()
  vi.restoreAllMocks()
})

describe('parseDisplayPreferences', () => {
  it('reads a complete stored value', () => {
    expect(
      parseDisplayPreferences({ theme: 'contrast', textSize: '150', density: 'compact' }),
    ).toEqual({ theme: 'contrast', textSize: '150', density: 'compact' })
  })

  it('falls back for anything it does not recognise', () => {
    // A stored value from a future version of the application must give a person the system theme,
    // not a blank screen.
    expect(parseDisplayPreferences({ theme: 'sepia', textSize: '400', density: 'airy' })).toEqual(
      DEFAULT_DISPLAY_PREFERENCES,
    )
  })

  it.each([null, undefined, 'contrast', 42])('falls back for the non-object %s', (payload) => {
    expect(parseDisplayPreferences(payload)).toEqual(DEFAULT_DISPLAY_PREFERENCES)
  })

  it('keeps the settings it can read and defaults only the rest', () => {
    expect(parseDisplayPreferences({ theme: 'dark' })).toEqual({
      theme: 'dark',
      textSize: '100',
      density: 'comfortable',
    })
  })
})

describe('the local display-preferences store', () => {
  it('round-trips a choice', async () => {
    const store = createLocalDisplayPreferencesStore()

    await store.write({ theme: 'contrast', textSize: '125', density: 'comfortable' })

    expect(await store.read()).toEqual({
      theme: 'contrast',
      textSize: '125',
      density: 'comfortable',
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
      store.write({ theme: 'dark', textSize: '100', density: 'comfortable' }),
    ).resolves.toBeUndefined()
    expect(await store.read()).toEqual(DEFAULT_DISPLAY_PREFERENCES)
  })
})

describe('the in-memory display-preferences store', () => {
  it('remembers within one session and nothing beyond it', async () => {
    const store = createInMemoryDisplayPreferencesStore()

    await store.write({ theme: 'dark', textSize: '150', density: 'compact' })

    expect(await store.read()).toEqual({ theme: 'dark', textSize: '150', density: 'compact' })
    expect(window.localStorage.getItem(DISPLAY_PREFERENCES_STORAGE_KEY)).toBeNull()
  })
})
