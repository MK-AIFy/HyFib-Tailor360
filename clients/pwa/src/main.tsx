import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// RouterProvider is imported from 'react-router', not from 'react-router/dom'. Both provide the
// same data router, but the two entry points ship separate CommonJS bundles, so under Node's
// resolution (Vitest) mixing them gives two copies of the router context and every hook throws
// "must be used within a data router". One entry point everywhere avoids that class of bug.
import { RouterProvider } from 'react-router'
import { router } from './app/router'
import { AppIntlProvider } from './i18n/IntlProvider'
import './styles/tokens.css'
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
      <RouterProvider router={router} />
    </AppIntlProvider>
  </StrictMode>,
)
