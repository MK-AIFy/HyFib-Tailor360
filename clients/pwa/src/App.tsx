import { Outlet } from 'react-router'
import { AppShell } from './components/layout/AppShell'

/**
 * The application shell: it is rendered once and stays mounted while routes change.
 *
 * Everything it used to hold inline — the skip link, the training banner, the header, the single
 * navigation list, the footer — now belongs to `AppShell` and the three role-optimised layouts in
 * `src/components/layout`, because the arrangement of those parts is what differs between a phone,
 * a counter tablet and a back-office desktop, and only a shell that measures itself can choose.
 *
 * What is left here is the join: the router renders `App`, `App` renders the shell, and the shell
 * renders the current screen into its main landmark.
 *
 * The role is not passed yet. `AppShell` falls back to `DEFAULT_JOURNEY_ROLE` until the session
 * exists — authentication is #23 and the permission model #24 — and this issue deliberately does not
 * invent one.
 */
export function App() {
  return (
    <AppShell>
      <Outlet />
    </AppShell>
  )
}
