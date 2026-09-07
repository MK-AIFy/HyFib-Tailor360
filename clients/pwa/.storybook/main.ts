import type { StorybookConfig } from '@storybook/react-vite'
import type { PluginOption } from 'vite'

/**
 * Storybook is where the design system is reviewed, and where the evidence #50 owes is produced:
 * a story per component state, a pseudo-locale story for every component (the 40% text-growth rule),
 * a story in each theme including the high-contrast sunlight one, and the eight role reference
 * journeys against synthetic data.
 *
 * Two files only. Anything with JSX in it — the decorators — lives in preview.tsx, and both files
 * are named in a tsconfig (`main.ts` in tsconfig.node.json, `preview.tsx` in tsconfig.app.json)
 * because ESLint resolves types through the project service and a file in no project is a parse
 * error rather than a lint warning.
 */

/** True for the vite-plugin-pwa plugin objects, whatever shape the plugin array is nested in. */
function isProgressiveWebAppPlugin(plugin: unknown): boolean {
  if (typeof plugin !== 'object' || plugin === null || !('name' in plugin)) {
    return false
  }
  const { name } = plugin
  return typeof name === 'string' && name.includes('vite-plugin-pwa')
}

/**
 * Storybook reuses the application's Vite configuration, which carries vite-plugin-pwa. Storybook
 * has no service worker and must not gain one — that is #51's decision to make, once, for the
 * application — so the plugin is removed here rather than being configured twice.
 */
function withoutProgressiveWebAppPlugin(plugins: readonly PluginOption[]): PluginOption[] {
  return plugins
    .map((plugin): PluginOption =>
      Array.isArray(plugin) ? withoutProgressiveWebAppPlugin(plugin) : plugin,
    )
    .filter((plugin) => !isProgressiveWebAppPlugin(plugin))
}

const config: StorybookConfig = {
  stories: ['../src/**/*.mdx', '../src/**/*.stories.@(ts|tsx)'],
  addons: [
    // axe-core in the preview pane, so an accessibility violation is visible while a component is
    // being built rather than at the pull request.
    '@storybook/addon-a11y',
    '@storybook/addon-docs',
  ],
  framework: {
    name: '@storybook/react-vite',
    options: {},
  },
  core: {
    // Storybook reports anonymous usage telemetry by default. Nothing about this repository's data
    // handling makes that acceptable without a decision, and no decision has been taken, so it is
    // off. Turning it on would be a change to docs/nfr/data-classification.md first.
    disableTelemetry: true,
  },
  // The manifest, the icons and the screenshots, so an install story shows the real artefacts.
  staticDirs: ['../public'],
  viteFinal(viteConfig) {
    return {
      ...viteConfig,
      plugins: withoutProgressiveWebAppPlugin(viteConfig.plugins ?? []),
    }
  },
}

export default config
