import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// RouterProvider is imported from 'react-router', not from 'react-router/dom'. Both provide the
// same data router, but the two entry points ship separate CommonJS bundles, so under Node's
// resolution (Vitest) mixing them gives two copies of the router context and every hook throws
// "must be used within a data router". One entry point everywhere avoids that class of bug.
import { RouterProvider } from 'react-router'
import { DisplayPreferencesProvider } from './app/DisplayPreferencesProvider'
import { SessionProvider } from './auth/SessionProvider'
import { router } from './app/router'
import { AppIntlProvider } from './i18n/IntlProvider'
// Stylesheet order is the cascade-layer order: layers.css declares the layers, tokens.css and
// themes.css fill the tokens layer, global.css fills base, layout and utilities. Importing them
// out of order would leave a layer undeclared and let it win over the ones after it.
import './styles/layers.css'
import './styles/tokens.css'
import './styles/themes.css'
import './styles/global.css'

const container = document.getElementById('root')
if (!container) {
  // index.html is served by the .NET host; if the mount point is missing the deployment is broken and
  // failing loudly is better than a blank page nobody can diagnose.
  throw new Error('The root element is missing from index.html.')
}

createRoot(container).render(
  <StrictMode>
    <AppIntlProvider>
      {/* Outside the router, because the session outlives every navigation and because the
          re-authentication dialog it owns has to be able to open over any screen without that
          screen unmounting — which is the whole point of re-authenticating in place. */}
      <SessionProvider>
        {/* Inside the session and outside the router: the theme, text size, density and reduced
            motion belong to the person rather than to the screen, so they are applied once, to the
            document, and survive every navigation. DisplayPreferencesProvider reads the session
            itself (#374, e12-f01-2) — a signed-in account gets its own stored preferences, and an
            anonymous device, or the moment before the session resolves, gets the local device stub.
            See that provider's own remarks for why it reads the session directly rather than
            through useSession(), which would throw for the standalone tests and stories that render
            it with no SessionProvider above. */}
        <DisplayPreferencesProvider>
          <RouterProvider router={router} />
        </DisplayPreferencesProvider>
      </SessionProvider>
    </AppIntlProvider>
  </StrictMode>,
)
