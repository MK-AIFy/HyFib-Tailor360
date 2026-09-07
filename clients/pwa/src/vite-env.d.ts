/// <reference types="vite/client" />
/// <reference types="vite-plugin-pwa/client" />

/**
 * This build's version, replaced at build time from package.json by the `define` in vite.config.ts.
 *
 * It is declared here rather than read from an import so that the version is a literal in the bundle:
 * importing package.json would pull the whole manifest — dependencies, scripts, private fields — into
 * a file the browser downloads.
 */
declare const __CLIENT_VERSION__: string
