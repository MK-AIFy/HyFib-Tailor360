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
      {/* Outside the router, because the theme and the text size belong to the person rather than to
          the screen: they are applied once, to the document, and survive every navigation. The store
          behind them is a local stub until identity.user_preferences exists (#25). */}
      <DisplayPreferencesProvider>
        {/* Outside the router, because the session outlives every navigation and because the
            re-authentication dialog it owns has to be able to open over any screen without that
            screen unmounting — which is the whole point of re-authenticating in place. */}
        <SessionProvider>
          <RouterProvider router={router} />
        </SessionProvider>
      </DisplayPreferencesProvider>
    </AppIntlProvider>
  </StrictMode>,
)
