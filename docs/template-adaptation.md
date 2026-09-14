# Template Adaptation

## Source

- Upstream repository: https://github.com/creativetimofficial/material-dashboard-react-laravel
- Commit used: `084da007dfa2ae4b3b2588d95bdc5bb91b3cf5d8` (`master`, 2024-05-27, "Update CHANGELOG.md")
- Only `react-material-laravel-app/` (the React frontend) was used. `laravel-json-api/`
  (the Laravel/JSON:API backend) was inspected for reference and **not** reused —
  the entire backend is a fresh ASP.NET Core implementation (`MPSellerTools.PlatformHost`
  / `MPSellerTools.TenantHost`).
- License: MIT, Copyright (c) 2019 Creative Tim. Preserved at
  `frontend/packages/ui/LICENSE-creative-tim.md`; see also `THIRD-PARTY-NOTICES.md`.

## What was imported into `frontend/packages/ui/`

From `react-material-laravel-app/src/`:

| Upstream path | Notes |
| --- | --- |
| `components/MD*` (MDAlert, MDAvatar, MDBadge, MDBox, MDButton, MDInput, MDPagination, MDProgress, MDSnackbar, MDTypography) | Copied as-is. |
| `examples/*` (Breadcrumbs, Cards, Charts, Configurator, Footer, Items, LayoutContainers, Lists, Navbars/DashboardNavbar, ProtectedRoute, Sidenav, Tables, Timeline) | Copied, then adapted — see below. |
| `assets/theme`, `assets/theme-dark` | Copied (light/dark MUI theme, base tokens, functions). RTL variants dropped (see below). |
| `assets/images` | Copied (icons, illustrations, logos used by layout chrome). |
| `context/index.jsx` | Copied, then stripped of app-specific auth state (see below). |

All `.js` files that contain JSX were renamed to `.jsx` (50 files) so Vite's
esbuild transform picks them up without extra loader configuration; files
with no JSX kept the `.js` extension. No import statements needed to change —
Vite resolves `.jsx` by default the same way CRA's webpack config did.

`components/`, `examples/`, `context/`, and `assets/` are consumed by both
`frontend/apps/platform` and `frontend/apps/workspace` via the same bare
specifiers the upstream code uses internally (`"components/MDBox"`,
`"examples/Sidenav"`, `"context"`, `"assets/theme"`), resolved through a Vite
`resolve.alias` in each app's `vite.config.ts` pointing at
`frontend/packages/ui/src/{components,examples,context,assets}`. This mirrors
the upstream CRA `jsconfig.json` `baseUrl: "src"` setup without requiring a
rewrite of every import statement in the ported files. These files are
intentionally **not** resolved through TypeScript's `paths` — see "TypeScript
and the ported JS" below.

## What was changed

- **`context/index.jsx`**: removed `AuthContext`/`AuthContextProvider`, which
  stored a bearer token in `localStorage` and drove login/logout via
  `navigate()`. The brief requires server-managed cookie sessions and
  forbids storing tokens in `localStorage` (§7); real auth state is being
  built per-app on top of ASP.NET Core Identity cookies (Increment 2), not as
  shared UI state. `MaterialUIControllerProvider`/`useMaterialUIController`
  (sidenav/theme/layout state — legitimate shared UI state) were kept as-is.
- **`examples/Navbars/DashboardNavbar`**: removed its direct dependency on the
  deleted `AuthContext` and the Laravel `services/auth-service`. It now takes
  an `onLogout` callback prop and a `profileHref` prop (defaulting to
  `/profile`) instead of hardcoding `AuthService.logout()` and a Laravel
  demo route.
- **`examples/Sidenav`**: removed the "Examples" nav section
  (`renderExampleRoutes`, a Creative Tim docs-links block unrelated to this
  product) and the "Upgrade to Pro" CTA button linking to Creative Tim's paid
  product page — the brief requires the free edition and no upsell content.
- **`examples/Configurator`**: removed the "view documentation" link, GitHub
  star button (`react-github-btn` dependency dropped entirely), and
  Twitter/Facebook share buttons — all Creative Tim marketing content. The
  actual display-setting controls (sidenav color/type, fixed navbar,
  light/dark toggle) were kept; the panel title was reworded from "Material
  UI Configurator" to "Display Settings".
- **`examples/Footer`**: replaced the default "made with ❤ by Creative Tim &
  UPDIVISION" copy and its promotional link list (Creative Tim, UPDIVISION,
  blog, license, etc.) with a plain "© {year} MPSellerTools" default.

## What was removed entirely

- **`examples/Navbars/DefaultNavbar`** (+ `DefaultNavbarMobile`,
  `DefaultNavbarLink`): the public marketing-site navbar with sign-in/sign-up
  links. Nothing else referenced it. MPSellerTools has no public landing
  page — every host goes straight to its own `/login` (brief §6) — so this
  was dead weight rather than something worth adapting.
- **`assets/theme/theme-rtl.js`, `assets/theme-dark/theme-rtl.js`**: RTL
  theme variants. The brief requires an English-only interface (§8); RTL
  support, the `stylis`/`stylis-plugin-rtl` dependency, and the
  `@emotion/cache`-based RTL cache setup in the upstream `App.js` were not
  carried over.
- `services/auth-service.js`, `services/htttp.service.js`,
  `services/interceptor.js` (Laravel/JSON:API HTTP client + token-refresh
  interceptor) were **not copied**. A cookie-based fetch client is being
  built fresh per-app in Increment 2 against the ASP.NET Core API instead of
  adapting a bearer-token client.
- `layouts/*`, `auth/*` (login/register/forgot-password/reset-password
  pages), `routes.js`, `App.js` were **not copied wholesale**. Their
  structure (each page composes `DashboardLayout` + `DashboardNavbar` +
  `Footer`; `routes.js` drives both the Sidenav and the router; `App.js`
  wires `ThemeProvider` + `Sidenav` + `Configurator` + `Routes`) was used as
  the pattern for new, purpose-built `frontend/apps/platform/src/{App.tsx,
  routes.tsx,pages/}` and `frontend/apps/workspace/src/{App.tsx,routes.tsx,
  pages/}`, written for MPSellerTools's own route set (brief §8) rather than
  the demo pages (billing, tables, rtl, notifications, user-profile,
  user-management demo). React registration/self-signup pages were dropped
  outright — the brief disables public tenant self-registration (§7).

## TypeScript and the ported JS

New application code (`App.tsx`, `routes.tsx`, `pages/*.tsx`, etc.) is
TypeScript, per the brief. The ported Creative Tim components stay as
`.js`/`.jsx` — rewriting ~30 components and their prop-types to strict
TypeScript would be a large, low-value effort for a UI foundation whose
maintainers will keep tracking upstream Material Dashboard React patterns.

Each app declares ambient `any`-typed modules for `components/*`,
`examples/*`, `context`, and `assets/*` in `src/types/mpsellertools-ui.d.ts`
so TypeScript accepts the same bare-specifier imports the JSX runtime alias
resolves. TypeScript's own `paths` option is deliberately **not** used for
these specifiers: pointing `paths` at the real `.jsx` files makes `tsc`
infer each component's actual (loose, `PropTypes`-only) prop shape instead of
falling back to the ambient `any` declarations, which then rejects the
custom style props (`shadow`, `bgColor`, `variant`, etc.) these components
actually accept. Vite's bundler-time `resolve.alias` still points at the real
files, so runtime behavior is unaffected — only `tsc`'s module resolution for
these specifiers is intentionally left unresolved so the ambient fallback
applies.

## Versions

The upstream `react-material-laravel-app/package.json` pinned React 18.2,
MUI 5.5.2, react-router-dom 6.16.0 (2023/2024-era versions), and used Create
React App (`react-scripts`). This project instead pins the latest patch
releases on those same major/minor-compatible lines that are actually
available and mutually compatible today (verified via `npm view <pkg>
version` against the npm registry, not guessed):

| Package | Upstream pin | Used here | Why |
| --- | --- | --- | --- |
| react / react-dom | 18.2.0 | 18.3.1 | Latest 18.x patch; MUI v5 and `react-table` v7 are not verified against React 19. |
| react-router-dom | 6.16.0 | 6.30.6 | Latest 6.x; v7 changes the routing API (data routers), which the ported `Sidenav`/`App.tsx` pattern does not use. |
| @mui/material / @mui/icons-material | 5.5.2 / 5.5.1 | 5.18.0 | Latest v5.x. MUI is at major v9 upstream; jumping to it would require auditing breaking changes across v6–v9 for every ported component, which was judged not worth it for a UI foundation — see the brief's "do not force-upgrade every dependency to conceal conflicts" instruction. |
| @emotion/react / @emotion/styled | 11.8.x | 11.14.0 / 11.14.1 | Latest 11.x (MUI v5's required emotion major). |
| axios | 1.5.1 | 1.20.0 | Latest 1.x. |
| chart.js / react-chartjs-2 | 4.4.0 / 5.2.0 | 4.5.1 / 5.3.1 | Latest patches. |
| react-table | 7.8.0 | 7.8.0 | Unchanged — no newer release exists (project appears unmaintained upstream); `@types/react-table` `7.7.20` added for TS. |
| chroma-js | 2.4.2 (exact) | 2.6.0 | Latest 2.x; upstream v3 was not adopted without auditing its API changes. |
| prop-types | 15.8.1 | 15.8.1 | Unchanged, already latest. |
| Build tool | Create React App (`react-scripts` 5.0.1) | Vite 8.3.0 + `@vitejs/plugin-react` | Brief §2 requires Vite; CRA is deprecated upstream. |
| TypeScript | none (plain JS) | 6.0.3 | New app code only; ported components stay JS (see above). |
| Linting | ESLint + `eslint-config-airbnb` | `oxlint` | Vite's current scaffold default; a fast Rust-based linter. Not a brief requirement either way — chosen for the fresh app code since there was no existing ESLint setup to preserve for it. |

`package-lock.json` at `frontend/` pins the resolved dependency tree.
