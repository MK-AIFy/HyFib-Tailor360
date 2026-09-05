import { useCallback, useEffect, useState } from 'react'
import { isInstalledDisplayMode } from './installPlatform'

/**
 * The `beforeinstallprompt` event, which is not in the DOM library because it is not in any
 * specification the library tracks. Declared here rather than in a global `.d.ts` so that the one
 * place in the application allowed to know about it is the one place that uses it.
 */
interface BeforeInstallPromptEvent extends Event {
  readonly platforms: readonly string[]
  readonly userChoice: Promise<{ readonly outcome: 'accepted' | 'dismissed' }>
  prompt: () => Promise<void>
}

function isBeforeInstallPromptEvent(event: Event): event is BeforeInstallPromptEvent {
  return typeof (event as { prompt?: unknown }).prompt === 'function'
}

/** What happened the last time the person was asked. `idle` means they have not been asked yet. */
export type InstallPromptOutcome = 'idle' | 'accepted' | 'dismissed'

export interface InstallPromptState {
  /** True once the browser has offered a prompt this page can show. */
  readonly available: boolean
  /** True when the page is already running as an installed application. */
  readonly installed: boolean
  /** The prompt is on screen and the person has not answered. */
  readonly busy: boolean
  readonly outcome: InstallPromptOutcome
  /** Shows the browser's own install prompt. A no-op when none is available. */
  readonly promptToInstall: () => void
}

/**
 * Captures the browser's install offer so the Install screen can hand it to a real button.
 *
 * ## Why the event has to be captured at all
 *
 * Chromium fires `beforeinstallprompt` once, early, wherever the person happens to be — usually not
 * on the Install screen. Left alone it shows a browser-drawn bar of the engine's choosing. Calling
 * `preventDefault()` and keeping the event is what lets the offer become a control this design
 * system owns: 44 px, labelled in the person's language, and reachable from the keyboard, none of
 * which is true of the browser's own bar.
 *
 * ## Why the screen must never depend on it
 *
 * The event does not fire on iOS at all, does not fire in a browser that has already installed the
 * application, and does not fire again once it has been used. So `available` being false is the
 * normal case rather than an error, and the Install screen shows the manual route through the
 * browser menu in every branch. The button is the shortcut, never the only path.
 *
 * ## Installed state
 *
 * Read once as the initial state and again on `appinstalled`. It is not polled: a person who installs from the
 * browser menu while this screen is open gets the `appinstalled` event, and a person who installs in
 * another tab sees the change the next time this screen is opened, which is soon enough for a screen
 * whose whole purpose is to be visited once.
 */
export function useInstallPrompt(): InstallPromptState {
  const [promptEvent, setPromptEvent] = useState<BeforeInstallPromptEvent | null>(null)
  // Read lazily at mount rather than set from the effect: the display mode is already known when
  // the hook first runs, so setting it in an effect would render once with a value known to be
  // wrong and then correct it. `isInstalledDisplayMode` guards its own feature detection.
  const [installed, setInstalled] = useState(isInstalledDisplayMode)
  const [busy, setBusy] = useState(false)
  const [outcome, setOutcome] = useState<InstallPromptOutcome>('idle')

  useEffect(() => {
    const onBeforeInstallPrompt = (event: Event) => {
      if (!isBeforeInstallPromptEvent(event)) {
        return
      }
      // Suppresses the browser's own bar so that the offer appears as this application's button.
      event.preventDefault()
      setPromptEvent(event)
    }

    const onAppInstalled = () => {
      setInstalled(true)
      setPromptEvent(null)
      setBusy(false)
    }

    window.addEventListener('beforeinstallprompt', onBeforeInstallPrompt)
    window.addEventListener('appinstalled', onAppInstalled)

    return () => {
      window.removeEventListener('beforeinstallprompt', onBeforeInstallPrompt)
      window.removeEventListener('appinstalled', onAppInstalled)
    }
  }, [])

  const promptToInstall = useCallback(() => {
    if (promptEvent === null) {
      return
    }

    setBusy(true)
    void promptEvent
      .prompt()
      .then(() => promptEvent.userChoice)
      .then((choice) => {
        setOutcome(choice.outcome)
        // The event is single-use whatever the answer: a second call throws.
        setPromptEvent(null)
      })
      .catch(() => {
        // A prompt the engine refused to show is indistinguishable from one nobody answered. The
        // manual instructions are on screen either way, so there is nothing to report here.
        setPromptEvent(null)
      })
      .finally(() => {
        setBusy(false)
      })
  }, [promptEvent])

  return {
    available: promptEvent !== null,
    installed,
    busy,
    outcome,
    promptToInstall,
  }
}
