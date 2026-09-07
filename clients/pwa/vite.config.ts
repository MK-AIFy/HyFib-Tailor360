import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'
import { VitePWA } from 'vite-plugin-pwa'

/** The web host (src/Hosts/Tailor360.Web) listens on 8080 in every environment, including containers. */
const WEB_HOST_ORIGIN = 'http://localhost:8080'

/**
 * This build's version, taken from package.json and compiled into the bundle.
 *
 * It is what every request declares in `X-Client-Version`, and what the server compares against the
 * minimum it supports before answering. Read here rather than imported into application code, so that
 * the version travels as a literal string and nothing in the bundle can reach the rest of the manifest.
 */
const clientVersion: string = (
  JSON.parse(readFileSync(fileURLToPath(new URL('./package.json', import.meta.url)), 'utf8')) as {
    version: string
  }
).version

export default defineConfig({
  define: {
    __CLIENT_VERSION__: JSON.stringify(clientVersion),
  },
  plugins: [
    react(),
    VitePWA({
      // 'prompt', never 'autoUpdate': a shop-floor device must not reload in the middle of a
      // measurement or a payment, so the user is asked and decides when to take the new version
      // (implementation plan #51).
      registerType: 'prompt',
      // The manifest is a hand-written file in public/, so the plugin must not generate a second one.
      manifest: false,
      // #20 ships no service-worker registration: the update prompt, the offline queue and the
      // encrypted drafts belong to #51. Fixing the strategy here keeps the decision recorded, while
      // injectRegister: null means nothing registers a worker yet.
      injectRegister: null,
      devOptions: { enabled: false },
      workbox: {
        globPatterns: ['**/*.{js,css,html,svg,png,webmanifest}'],
        // A client-side router serves every unknown path from the shell.
        navigateFallback: 'index.html',
      },
    }),
  ],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      // The browser must see one origin: the API is a same-origin BFF (plan section 4.6), so session
      // cookies, the anti-forgery header and the content security policy all behave in development as
      // they do behind the reverse proxy in production. changeOrigin stays false so the host header
      // reaches the .NET host unchanged.
      '/api': { target: WEB_HOST_ORIGIN, changeOrigin: false },
      '/health': { target: WEB_HOST_ORIGIN, changeOrigin: false },
    },
  },
  build: {
    // Production source maps are required to symbolicate the client error reports of #52.
    sourcemap: true,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (!id.includes('node_modules')) {
            return undefined
          }
          // Split the rarely changing vendor code so that an application release does not invalidate
          // it in the precache. Order matters: react-intl and react-router both contain 'react'.
          if (
            id.includes('react-intl') ||
            id.includes('@formatjs') ||
            id.includes('intl-messageformat')
          ) {
            return 'vendor-intl'
          }
          if (id.includes('react-router')) {
            return 'vendor-router'
          }
          return 'vendor'
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    // Test globals stay off: every test imports what it uses, which keeps the editor honest.
    globals: false,
    setupFiles: ['./src/setupTests.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
})
