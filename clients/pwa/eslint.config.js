import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'

export default tseslint.config(
  {
    // Build output and generated service-worker artefacts are never linted.
    // src/api/schema.d.ts is generated from the published OpenAPI document; it is the contract,
    // not code anybody edits, and lint findings in it can only be fixed by changing the API.
    ignores: [
      'dist',
      'dev-dist',
      'coverage',
      'node_modules',
      'storybook-static',
      'src/api/schema.d.ts',
    ],
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
          // One module-level value lives next to the component that defines its meaning: the data
          // router object. Splitting it into its own file would buy nothing but an extra import, and
          // it is not edited during a hot-reload session.
          allowExportNames: ['router'],
        },
      ],
      // Unused arguments are allowed when prefixed with an underscore, which keeps handler signatures
      // readable.
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
    },
  },
  {
    // A story file exports a meta object and a set of story objects, none of which are components.
    // Fast refresh does not apply to them at all: Storybook has its own hot-reload path, and the
    // same is true of the preview configuration.
    files: ['**/*.stories.{ts,tsx}', '.storybook/**/*.{ts,tsx}'],
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
)
