# HyFib Tailor360 — progressive web application

The browser client for the tailoring shop management system. This is the scaffold delivered by issue
**#20**: a shell that boots, routes, translates, shows the training banner and builds. The design
system (#50), the service worker with its update prompt and offline queue (#51), the accessibility and
performance work (#52) and the generated API client (#53) land on top of it.

## Prerequisites

- Node 22 or newer.
- pnpm 10 (`corepack enable` picks up the `packageManager` field in `package.json`).
- For anything that talks to the API: the web host running on `http://localhost:8080`
  (`./scripts/dev run` from the repository root).

## Commands

Run them from `clients/pwa`.

| Command           | What it does                                                                               |
| ----------------- | ------------------------------------------------------------------------------------------ |
| `pnpm install`    | Installs dependencies from `pnpm-lock.yaml`.                                               |
| `pnpm dev`        | Vite dev server on <http://localhost:5173>, proxying `/api` and `/health` to the web host. |
| `pnpm lint`       | ESLint (flat config, type-aware rules).                                                    |
| `pnpm typecheck`  | `tsc -b --noEmit` across both TypeScript projects.                                         |
| `pnpm test`       | Vitest once, in jsdom.                                                                     |
| `pnpm test:watch` | Vitest in watch mode.                                                                      |
| `pnpm build`      | Type-checks, then produces `dist/`.                                                        |
| `pnpm preview`    | Serves the built `dist/` for a smoke check.                                                |
| `pnpm format`     | Rewrites files with Prettier (`pnpm format:check` only reports).                           |

`pnpm typecheck` uses `tsc -b` because the TypeScript configuration is solution-style: `tsconfig.json`
owns no files and references `tsconfig.app.json` (browser, JSX) and `tsconfig.node.json` (Vite and
Vitest configuration, Node types). A plain `tsc --noEmit` would silently check nothing.

## Talking to the API

There is no separate API origin. The dev server proxies `/api` and `/health` to
`http://localhost:8080`, so the browser sees a single origin exactly as it does in production behind
the reverse proxy — which is what makes session cookies, the anti-forgery header and the content
security policy behave the same in both places. If a request 502s, the web host is not running.

The shared transport recovers from `403 security.step-up-required` through the session provider's
identity dialog. It holds the original serialised body, `If-Match` and `Idempotency-Key`, refreshes the
anti-forgery token after proof, and retries once. Declining, aborting, or a second refusal never causes
an automatic replay. Concurrent challenges share one dialog. Authentication calls opt out so the
dialog cannot recursively challenge its own login or factor request. Template lifecycle commands use
this same recovery; they no longer maintain a separate retry loop.

`AdminStepUp.test.tsx` exercises branch trading, feature settings, staff suspension, role permissions
and outbox replay through their real screens. Template lifecycle recovery is covered by
`TemplateScreens.test.tsx`; transport tests cover bounded replay, unchanged request identity,
cancellation, concurrent requests and ordinary permission refusals. Server permissions and fresh
factor requirements remain authoritative.

## Layout

```
clients/pwa
├── index.html                  # document shell: viewport, theme colour, manifest link, <noscript>
├── public/
│   ├── manifest.webmanifest    # installability: name, scope, display, icons
│   └── icons/                  # placeholder brand icons (final artwork with #50)
├── src/
│   ├── main.tsx                # mounts the router inside StrictMode and the intl provider
│   ├── App.tsx                 # shell: skip link, training banner, header, nav, outlet, footer
│   ├── app/router.tsx          # data router: home and 404
│   ├── app/version.ts          # GET /api/version → { version, buildHash, environment }
│   ├── components/             # shell components and their tests
│   ├── i18n/                   # en-IN (complete) and ta-IN (awaiting native-speaker review)
│   └── styles/                 # design tokens and base layer
└── vite.config.ts              # react plugin, PWA plugin, dev proxy, chunking, Vitest
```

## Decisions worth knowing

- **The update strategy is `prompt`, never `autoUpdate`.** A shop-floor device must not reload in the
  middle of a measurement or a payment; the user is asked and chooses. #20 registers no service worker
  at all — the registration, the update prompt and the bounded offline queue arrive with #51.
- **The training banner states only what the server confirmed.** It renders when
  `GET /api/version` reports an environment other than `production`, and renders nothing while the
  request is in flight or after it fails, because a wrong banner in either direction is a safety
  problem.
- **`en-IN` is the only complete catalogue.** `ta-IN` carries Tamil where it is confident and the
  English string otherwise; it becomes a selectable UI language only after native-speaker review and
  the ≥ 95% coverage gate recorded in the accessibility and localisation policy.
- **Router imports come from `react-router` only, never `react-router/dom`.** The two entry points
  ship separate CommonJS bundles, so under Node's resolution (which Vitest uses) mixing them loads
  two copies of the router context and every router hook throws. One entry point everywhere.
- **Target sizes come from `docs/nfr/accessibility-localisation.md` section 5 (AL-03), not from the
  WCAG floor**: 56 x 56 CSS px for a primary shop-floor action with 12 px spacing, 44 x 44 for a
  standard control with 8 px, and 32 x 32 for a dense control **on desktop only** — the compact
  density collapses back to 44 px on a coarse pointer, so the dense size cannot reach a phone. The
  tokens are `--target-primary`, `--target-standard` and `--target-dense`; `--touch-target-min`
  remains 48 px as the shell's comfortable default. The scaffold's flat "at least 48px" rule and the
  plan's "44 px touch targets" line are both superseded by AL-03, which is the binding requirement.
- **Colour never carries meaning alone**: every status badge carries an icon and a word as well.
  Both rules are enforced by the design-system tokens, by the token-pair contrast test in
  `src/design-system/testing/tokenContrast.test.ts`, and by checklist items A11Y-68 and A11Y-70.
