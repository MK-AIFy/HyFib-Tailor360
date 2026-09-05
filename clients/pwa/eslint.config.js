import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'

export default tseslint.config(
  {
    // Build output and generated service-worker artefacts are never linted.
    ignores: ['dist', 'dev-dist', 'coverage', 'node_modules'],
  },
  {
    // The configuration files themselves run in Node and are plain JavaScript.
    files: ['*.js'],
    extends: [js.configs.recommended],
    languageOptions: {
      ecmaVersion: 2023,
      sourceType: 'module',
      globals: globals.node,
    },
  },
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      // Type-aware rules: they catch floating promises and unsafe any, which is the class of bug that
      // matters most in a client that talks to an API.
      ...tseslint.configs.recommendedTypeChecked,
    ],
    languageOptions: {
      ecmaVersion: 2022,
      globals: globals.browser,
      parserOptions: {
        // projectService picks the right tsconfig (app or node) per file, so no file list is duplicated.
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': [
        'warn',
        {
          allowConstantExport: true,
          // Two module-level values live next to the components that define their meaning: the data
          // router object and the list of supported locales. Splitting either into its own file would
          // buy nothing but an extra import, and neither is edited during a hot-reload session.
          allowExportNames: ['router', 'SUPPORTED_LOCALES'],
        },
      ],
      // Unused arguments are allowed when prefixed with an underscore, which keeps handler signatures
      // readable.
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
    },
  },
)
