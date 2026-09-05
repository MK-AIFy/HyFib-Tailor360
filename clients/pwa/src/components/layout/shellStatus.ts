import { createContext } from 'react'
import type { AlertTone } from '../primitives/variants'

/**
 * The shell's persistent status channels.
 *
 * WCAG 4.1.3 Status Messages says a change of state must reach assistive technology without moving
 * focus. docs/nfr/accessibility-localisation.md section 6 says more than that, and it is the harder
 * requirement: on this product a scan result, the sync state and an actionable error are **never**
 * delivered by a toast. A toast disappears before somebody holding a garment has read it, and a
 * screen-reader user may never hear it at all. Checklist item A11Y-42 is the question a person asks
 * on a real device — is this message still on the screen?
 *
 * So the shell owns three channels, they live in a reserved slot at the top of the main landmark,
 * and they stay until they are dismissed or superseded:
 *
 *   scan       the result of the last scan. Acceptance is announced politely; a rejection is
 *              announced assertively and names which rule failed, because the garment has already
 *              moved (section 6, checklist items A11Y-40 and A11Y-41)
 *   sync       queued, sending, sent, or blocked because the action needs a connection. The offline
 *              queue itself is #51's; this is the region it will publish into
 *   autosave   "Saving…", "Saved", "Not saved — retrying". A draft that saves silently is
 *              indistinguishable from one that does not save at all (checklist item A11Y-44)
 *
 * Screens publish into the channels; the shell renders them. That split is what keeps the three
 * regions in the same place on every screen, which is what 3.2.3 asks for and what a person picking
 * up a shared device in the middle of a job depends on.
 */

/** Whether the scan was taken or refused. Two outcomes, two politeness levels, two glyph shapes. */
export type ScanOutcome = 'accepted' | 'rejected'

export interface ScanStatusMessage {
  readonly outcome: ScanOutcome
  /**
   * What happened, in words a person can act on: the job number and the next expected action, or
   * **which** rule the scan broke. Never a code and never "invalid" (3.3.1, 3.3.3).
   */
  readonly message: string
}

export interface SyncStatusMessage {
  readonly tone: AlertTone
  readonly message: string
}

export interface ShellStatusValue {
  readonly scan: ScanStatusMessage | null
  readonly sync: SyncStatusMessage | null
  readonly autosave: string | null
  /** Publishes a scan result, or clears it with null. */
  readonly announceScan: (status: ScanStatusMessage | null) => void
  /** Publishes the sync state, or clears it with null. */
  readonly announceSync: (status: SyncStatusMessage | null) => void
  /** Publishes the draft state, or clears it with null. */
  readonly announceAutosave: (message: string | null) => void
}

/**
 * Undefined rather than a no-op default, so `useShellStatus` can throw.
 *
 * A silent no-op would mean a scan result published from a screen that is not inside a shell simply
 * vanishes — the exact failure these regions exist to prevent, and one that would show up in a shop
 * rather than in a test.
 */
export const ShellStatusContext = createContext<ShellStatusValue | undefined>(undefined)
