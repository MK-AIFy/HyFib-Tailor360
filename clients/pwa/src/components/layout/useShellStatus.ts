import { useContext } from 'react'
import { ShellStatusContext } from './shellStatus'
import type { ShellStatusValue } from './shellStatus'

/**
 * Reads the shell's status channels.
 *
 * Throws outside a `ShellStatusProvider`. That is deliberate: the alternative is a screen that
 * publishes a rejected scan into nothing and looks like it worked, which is a defect a shop would
 * find and a test would not. `AppShell` provides the context; a story or a focused test wraps the
 * component in `ShellStatusProvider` directly.
 */
export function useShellStatus(): ShellStatusValue {
  const value = useContext(ShellStatusContext)
  if (value === undefined) {
    throw new Error(
      'useShellStatus was called outside a ShellStatusProvider. Wrap the tree in AppShell, or in ShellStatusProvider for an isolated story or test.',
    )
  }
  return value
}
