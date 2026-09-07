import { useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { ShellStatusContext } from './shellStatus'
import type { ScanStatusMessage, ShellStatusValue, SyncStatusMessage } from './shellStatus'

export interface ShellStatusProviderProps {
  readonly children: ReactNode
  /** Starting values. Stories and tests use them to render a region without a screen behind it. */
  readonly initialScan?: ScanStatusMessage | null
  readonly initialSync?: SyncStatusMessage | null
  readonly initialAutosave?: string | null
}

/**
 * Holds the three status channels for one shell.
 *
 * Plain `useState`, no reducer and no queue. Each channel holds exactly one message because that is
 * what the requirement is: the last scan result, the current sync state, the current draft state. A
 * queue would produce a backlog to read through, and checklist item A11Y-43 fails a screen whose
 * live region chatters — the runner starts ignoring the voice, which costs more than the message was
 * worth.
 */
export function ShellStatusProvider({
  children,
  initialScan = null,
  initialSync = null,
  initialAutosave = null,
}: ShellStatusProviderProps) {
  const [scan, setScan] = useState<ScanStatusMessage | null>(initialScan)
  const [sync, setSync] = useState<SyncStatusMessage | null>(initialSync)
  const [autosave, setAutosave] = useState<string | null>(initialAutosave)

  const value = useMemo<ShellStatusValue>(
    () => ({
      scan,
      sync,
      autosave,
      announceScan: setScan,
      announceSync: setSync,
      announceAutosave: setAutosave,
    }),
    [scan, sync, autosave],
  )

  return <ShellStatusContext.Provider value={value}>{children}</ShellStatusContext.Provider>
}
