# `clients/pwa` — client rules

Rules for working in the progressive web application. Read [`../../CLAUDE.md`](../../CLAUDE.md) first; this file
carries only what is specific to this tree. [`README.md`](README.md) describes the layout and the decisions behind
it; the authorities behind the rules are
[`../../docs/nfr/accessibility-localisation.md`](../../docs/nfr/accessibility-localisation.md),
[`../../docs/nfr/a11y-checklist.md`](../../docs/nfr/a11y-checklist.md) and plan Section 4.6 in
[`../../docs/IMPLEMENTATION_PLAN.md`](../../docs/IMPLEMENTATION_PLAN.md).

The shop floor is the design constraint. Staff work standing, one-handed, often holding a garment, sometimes in
sunlight and always in a hurry. Every rule below comes from that, not from taste.

---

## 1. Commands

Node 22 or newer; pnpm 10 (`corepack enable` picks up the `packageManager` field). Run these from `clients/pwa`,
or from the repository root with `pnpm --dir clients/pwa <script>`.

| Command                   | What it does                                                                              |
| ------------------------- | ----------------------------------------------------------------------------------------- |
| `pnpm install`            | Installs from `pnpm-lock.yaml` (`--frozen-lockfile` under `CI=true`)                      |
| `pnpm dev`                | Vite dev server on <http://localhost:5173>, proxying `/api` and `/health` to the web host |
| `pnpm lint`               | ESLint, flat config, type-aware rules                                                     |
| `pnpm typecheck`          | `tsc -b --noEmit` across both TypeScript projects                                         |
| `pnpm test`               | Vitest once, in jsdom (`pnpm test:watch` to iterate)                                      |
| `pnpm build`              | `tsc -b && vite build` — type-checks, then produces `dist/`                               |
| `pnpm format:check`       | Prettier in report mode (`pnpm format` rewrites)                                          |
| `pnpm generate:api`       | Regenerates `src/api/schema.d.ts` from `docs/api/openapi.v1.json`                         |
| `pnpm generate:api:check` | Regenerates and fails when the committed types have drifted (CI runs it)                  |

`pnpm typecheck` uses `tsc -b` because the configuration is solution-style: `tsconfig.json` owns no files and
references `tsconfig.app.json` (browser, JSX) and `tsconfig.node.json` (Vite and Vitest config, Node types). A
plain `tsc --noEmit` would silently check nothing.

There is no separate API origin. The dev server proxies `/api` and `/health` to `http://localhost:8080`, so the
browser sees one origin exactly as it does behind the reverse proxy — which is what makes session cookies, the
anti-forgery header and the content security policy behave the same in both places. A 502 means the web host is
not running (`./scripts/dev run` from the repository root).

## 2. TypeScript and style

- **Strict TypeScript, no `any`.** The type-aware ESLint rules are on because floating promises and unsafe `any`
  are the bug class that matters most in a client that talks to an API.
- **Prettier owns formatting** — 100 columns, single quotes, no semicolons, trailing commas. Do not hand-format
  around it, and do not add formatting rules to ESLint.
- **Unused arguments are prefixed with `_`**; nothing else is exempt.
- **Import the router from `react-router` only, never `react-router/dom`.** The two entry points ship separate
  CommonJS bundles, so under Node's resolution — which Vitest uses — mixing them loads two copies of the router
  context and every router hook throws.
- **The contract is generated; the transport is not.** `src/api/schema.d.ts` is generated from
  `docs/api/openapi.v1.json` by `pnpm generate:api`, and `src/api/contract.ts` pins every hand-written payload type
  against it — so an endpoint whose response changes fails `pnpm typecheck`. Regenerate in the same commit as the
  API change; `pnpm generate:api:check` fails on drift and CI runs it. Never edit `schema.d.ts`.
- **One transport, `src/auth/apiClient.ts`.** It adds `X-Correlation-Id`, `X-Client-Version` and the anti-forgery
  header itself, sends the `Idempotency-Key` the caller holds, refetches a refused token once, re-authenticates in
  place on a 401, and renews a stale proof of identity in place on a `403 security.step-up-required`. Both replay
  the identical request, so the retry key, the precondition and whatever the person typed survive by construction
  rather than by a screen remembering to preserve them. Do not hand-write a second `fetch` wrapper, and do not
  answer either refusal at a call site.

## 3. Components

- **Every screen has five states**: loading, empty, error, offline and forbidden. A screen with only a happy path
  is not finished, and from #50 each state has a Storybook story.
- **Design tokens, never literals.** Colours, spacing, radii, type sizes and focus come from
  `src/styles/tokens.css`. A hard-coded hex or pixel value is a defect: the high-contrast sunlight theme replaces
  token values, not component code.
- **Colour never carries meaning alone.** Every status badge carries an icon _and_ a word — overdue, held, ready,
  unpaid, rework.
- **No inline scripts or styles.** The content security policy forbids them; a component that needs a dynamic
  value sets a custom property.
- **Toasts are never used for scan results, sync state or actionable errors.** Those go to a live region or an
  in-place state. A toast that disappears is not a way to tell someone a payment failed.
- **Confirmation has three tiers** — confirm; confirm with a reason; typed confirmation, which is for desktop and
  tablet administration only and never appears on a phone layout.
- **Every drag has a button alternative**, including reordering, cropping and signing.
- Layouts are chosen by **container queries, not user agent**: phone (bottom navigation, scanner-first), tablet
  (master-detail), desktop (side navigation, dense tables).

## 4. Accessibility

The commitment is **WCAG 2.2 Level AA** on every screen and every rendered customer-facing document, verified by
axe at the phone, tablet and desktop profiles, at 200% zoom and at 320 px. These are the ones that fail most often
here:

| Rule                                                                                      | The number                                                                              |
| ----------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| Touch target — primary shop-floor action (scan, capture, confirm, take payment, dispatch) | 56 × 56 CSS px, 12 px spacing (AL-03)                                                   |
| Touch target — standard control                                                           | 44 × 44 CSS px, 8 px spacing (AL-03)                                                    |
| Touch target — dense desktop control, mouse only                                          | 32 × 32 CSS px, 8 px spacing (AL-03)                                                    |
| Text contrast                                                                             | 4.5:1 body, 3:1 large; 3:1 for borders, focus rings and meaningful icons                |
| Reflow                                                                                    | No horizontal page scroll at 320 CSS px; wide tables scroll inside their own container  |
| Zoom                                                                                      | Everything usable at 200%, including bottom navigation, dialogs and the scanner overlay |
| Text growth                                                                               | Layouts tolerate 40% growth, which is what makes the Tamil catalogue safe to switch on  |

The three sizes marked **AL-03** are **proposed, to be confirmed** in
[`../../docs/nfr/accessibility-localisation.md`](../../docs/nfr/accessibility-localisation.md) section 5: they
exceed the WCAG floor deliberately, and a staff trial confirms them before #50 closes. Build to them; expect the
numbers, not the rule, to be the thing that could move. The WCAG floor underneath them — 24 × 24 CSS px, or 24 px
of clear spacing around a smaller target — is settled and is never the target on a phone or tablet layout. Every
other row in the table is settled in the same document.

- **The target is the hit area, not the ink.** A 24 px icon inside a 44 px padded button is compliant.
- **Focus is never obscured** by the bottom navigation, a sticky action bar, a toast or the virtual keyboard. This
  is the single most common phone-form failure, which is why a helper asserts it at 320/360/768/1024/1280 px.
- **Nothing traps the keyboard** — not dialogs, not bottom sheets, not the camera overlay, and not the wedge
  scanner, which listens without capturing focus. A wedge scanner is a keyboard.
- **Every field has a persistent visible label.** A placeholder is never the only label; measurement fields also
  carry their unit and expected range.
- **Errors are text**, associated with their field and summarised at the top of the step. Problem details are
  rendered in plain language, never as a code.
- **Status is announced through a polite live region** — scan results, save confirmations, queue and sync state.
- **`prefers-reduced-motion: reduce` removes motion**, and motion is never the only signal that something happened.
- **Nothing auto-advances or re-orders under the user's hands.** A queue that updates offers a "new items" control.
- Destructive actions are not placed beside frequent ones, and nothing sits in the bottom 8 px of a phone viewport
  where the system gesture bar takes the touch.

## 5. Localisation

- **Every user-facing string comes from a message catalogue** through `react-intl`. No literal in a component, no
  sentence built by concatenation — word order differs in Tamil.
- `src/i18n/en-IN.ts` is the source of truth. Its `MessageKey` type makes a missing key in another catalogue a
  compile error, so add the key there first.
- **ICU MessageFormat for anything variable** — plurals, gender, numbers, dates. Placeholders, never string
  addition.
- `ta-IN` exists and is filled in progressively. It becomes a selectable language only when the catalogue is at
  least 95% translated and the review gate passes; until then it stays behind a flag, off by default. A
  half-translated interface is worse than an English one.
- `<html lang>` follows the active locale, which `AppIntlProvider` maintains. Screen readers and hyphenation both
  depend on it.
- **All formatting goes through the shared `formatters` module** — INR with lakh/crore grouping, `dd-MM-yyyy`,
  12-hour time. Never `toLocaleString` at a call site. The module itself arrives with the design system (#50); the
  rule holds from the first component that needs it, so add it there rather than formatting inline.
- **User-generated content is never machine-translated.** It is shown exactly as entered, tagged with its language
  where known.

## 6. State and the network

- **Server state is server state.** From #50 it is TanStack Query with `Idempotency-Key`, `X-Correlation-Id`,
  `X-Client-Version` and the anti-forgery header injected centrally; today the shell has only the small
  `useVersion` hook in `src/app/version.ts`. Do not build a second data-fetching path.
- **Retry reuses the same `Idempotency-Key` and never discards typed input.** A retry that creates a second
  payment is worse than an error message.
- **Billing, payment and inventory reconciliation are online-only**, with an explicit blocked-action state
  ("Needs connection — this will not be queued"). The bounded offline queue (#51) covers approved idempotent
  operations only, chiefly scan submissions.
- **The service-worker update strategy is `prompt`, never `autoUpdate`.** A shop-floor device must not reload in
  the middle of a measurement or a payment. Registration, the update prompt and the offline queue arrive with #51;
  nothing registers a worker today.
- **The client never asserts what the server has not confirmed.** The training banner renders only when
  `GET /api/version` reports a non-production environment, and renders nothing while the request is in flight or
  after it fails — a wrong banner in either direction is a safety problem.
- **No personal data in client telemetry, ever** — no measurements, no names, no phone numbers, no image bytes.
  Route names, component names, stack hashes and timings only.
- Nothing sensitive goes in `localStorage`. The locale preference lives there only until the identity module gives
  it a durable home on the user record; a shop device is shared.

## 7. Tests

Vitest in jsdom, with Testing Library. `globals` is off, so every test imports what it uses — which keeps the
editor honest. Tests live beside their subject as `*.test.ts(x)` under `src/`.

- **Query the way a user does** — by role, label and text, not by class name or test id.
- **Assert the accessible name**, not the DOM shape. A test that passes when the label is gone is not a test.
- Cover the states that matter: loading, empty, error, offline and forbidden, plus the case where a request fails
  and the user retries.
- Synthetic data only — never a real name, phone number or measurement in a fixture.

## 8. Before opening a pull request

```bash
pnpm --dir clients/pwa lint
pnpm --dir clients/pwa typecheck
pnpm --dir clients/pwa format:check
pnpm --dir clients/pwa test
pnpm --dir clients/pwa build
```

A user-interface change also needs the evidence Definition of Done item 7 asks for: the axe pass, screenshots at
phone, tablet and desktop widths, the overflow and obscured-focus helper, the pseudo-locale story, and the
screen-reader items of [`../../docs/nfr/a11y-checklist.md`](../../docs/nfr/a11y-checklist.md) for a new journey.
