# React routing and role-aware UX

Frontend only, on `integration/final-mvp`. No backend, database, Flutter, agent,
authentication policy, commit or push changes. Existing worktree changes preserved.

Dependency: `react-router-dom ^7.18.4` (package and lockfile updated).
`AuthProvider` stays above `BrowserRouter`; navigation does not recreate the session.
The route list, menu and route guards share `src/routes/routeConfig.js`.

## Route map

| URL | Existing screen / behavior |
| --- | --- |
| `/` | Permission-based landing redirect |
| `/login` | Shared sign-in/register screen; restores requested internal URL through role guard |
| `/dashboard`, `/procurement` | Procurement dashboard |
| `/material-requests` | Material requests and approval management |
| `/suppliers`, `/suppliers/:id` | Supplier list / detail |
| `/quotations`, `/quotations/:id` | Approved requests / request quotation and recommendation workspace |
| `/purchase-orders`, `/purchase-orders/:id` | Purchase-order list / detail |
| `/deliveries` | Existing delivery dashboard and scheduling modal |
| `/quality-inspections`, `/quality-inspections/:id` | Inspection list / detail |
| `/quality-inspections/new/:deliveryId` | Start inspection; reloads pending delivery by ID |
| `/quality-inspections/:id/complete` | Complete inspection; reloads inspection by ID |
| `/quality-inspections/:id/non-conformances/new/:itemId` | Create NCR; reloads inspection and item |
| `/non-conformances`, `/non-conformances/:id` | NCR list / detail |
| `/ai-workflows`, `/reports`, `/users` | Reserved existing placeholder URLs; Access Denied, hidden from navigation |
| `/access-denied` | Clean denial page with a permitted workspace link when available |
| Other paths | Page not found |

The three placeholder screens have no implemented page/API permission contract.
Their allowedRoles are empty rather than inventing permissions. Existing AI tools
remain available within their authorized material, procurement, delivery and
quality workflows. Dialogs and in-page controls remain component state.

## Navigation and landing matrix

| Role | Visible navigation | Landing |
| --- | --- | --- |
| Administrator | Dashboard, Material Requests, Suppliers, Quotations, Procurement, Purchase Orders, Deliveries, Quality Inspections, Non-Conformances | Dashboard |
| ProcurementOfficer / ProcurementManager | Dashboard, Material Requests, Suppliers, Quotations, Procurement, Purchase Orders, Deliveries | Procurement |
| SiteEngineer | Material Requests, Deliveries | Material Requests |
| QualityInspector | Deliveries, Quality Inspections, Non-Conformances | Quality Inspections |
| Unknown / no roles | None | Access Denied |

For multiple roles, visibility is the union. Landing precedence is Administrator,
SiteEngineer, QualityInspector, procurement roles, then the first
permitted entry. A requested deep link takes precedence and still passes the guard.

Sources in `backend/BuildWise.Api/Controllers` (read only):

- MaterialRequestsController: GET list/detail role attributes.
- SuppliersController, QuotationsController and
  ProcurementWorkflowController: procurement roles on controllers.
- PurchaseOrdersController: procurement roles and Administrator only.
  SiteEngineer uses limited Delivery API context without pricing or quotations.
- DeliveriesController: expected/history GET roles; schedule/risk roles and
  receiving roles control matching buttons.
- InspectionsController, NonConformancesController, QualityRiskAgentController:
  QualityInspector and Administrator.

Frontend guards are UX only. ASP.NET remains the authorization authority.

## UX and responsive behavior

- Native navigation links, active state, browser history, page titles, skip link,
  and focus moved to the main content on route changes.
- Subtle page/modal/status entry animations and consistent legacy button feedback.
- Reduced-motion preference disables animations/transitions and button movement.
- Navy sidebar and orange accents retained. Narrow screens use scrollable navigation.
- Tables scroll locally, with readable minimum widths; delivery grids collapse.
- Backend validation and existing feature mutations are reused.

## Exact files changed for this task

Paths relative to `web/buildwise-web`:

- `package.json`
- `package-lock.json`
- `src/App.jsx`
- `src/App.test.jsx` (new)
- `src/main.jsx`
- `src/routes/routeConfig.js` (new)
- `src/layouts/AppLayout.jsx`
- `src/layouts/AppLayout.css`
- `src/styles/global.css`
- `src/test/setup.js`
- `src/Features/deliveries/pages/DeliveryDashboard.jsx`
- `src/Features/deliveries/pages/DeliveryDashboard.test.jsx` (already untracked before this task)
- `src/Features/procurement/pages/ProcurementApp.jsx`
- `src/Features/procurement/pages/PurchaseOrderDetail.jsx`
- `src/Features/procurement/pages/PurchaseOrderNavigation.test.jsx` (new)
- `src/Features/quality/QualityApp.jsx`
- `src/Features/quality/QualityNavigation.test.jsx`
- `src/Features/quality/QualityRoutes.test.jsx` (new)
- `e2e/quality-responsive.pw.js`
- `e2e/routing.pw.js` (new)
- `e2e/procurement.live.js` (navigation locator updated; live integration not run)
- `ROUTING-UX.md` (new)

Other files reported by git status were already modified/untracked before this task.

## Verification and release checklist

Automated React tests cover unauthenticated redirects, five application role menus, legacy-role denial and
landings, multi-role union, unauthorized deep links, route IDs, actual AuthProvider
session persistence through navigation/remount, sign-out, start/complete/NCR URL
loading, and read-only purchase-order behavior. Existing workflow tests retained.

Browser tests use fixture APIs, never a live backend or database. Screenshots are
under `../../logs/quality-responsive/` for phone 390px, tablet 768px, desktop 1440px.
The installed Edge browser is selected with `$env:PLAYWRIGHT_CHANNEL = 'msedge'`.

Manual release checklist (live API / deployed host still to verify):

- Sign in using each real role; confirm the menu and landing match the matrix.
- Open a permitted detail URL, refresh it, then use Back and Forward.
- Enter an unauthorized URL; verify Access Denied without feature data loading.
- Schedule/cancel/receive a delivery using the corresponding authorized account.
- Open a pending inspection, start, refresh completion, save, and create an NCR.
- Follow supplier, quotation workspace and generated purchase-order links.
- At phone/tablet widths, scroll to the last sidebar entry and final table columns.
- Enable reduced motion; verify page/modal/button motion is removed.
- Sign out, then press Back; the previous protected page must stay inaccessible.

Production hosting must serve `index.html` for unknown non-asset frontend paths
while preserving API routing. Vite dev and preview supply SPA fallback; production
host configuration is outside this frontend-only change. Verify a direct request
and refresh on `/purchase-orders/42` before release.
See https://reactrouter.com/how-to/spa for SPA hosting requirements.

## Historical results (before final RBAC/recovery changes)

- `npx.cmd vitest run`: 144 tests passed across 18 files.
- `npm.cmd run build`: passed.
- `npm.cmd run lint`: passed (exit 0), 10 existing warnings; no errors.
- `git diff --check`: passed.
- `PLAYWRIGHT_CHANNEL=msedge npx.cmd playwright test`: 18 passed across three viewport sizes.
- Screenshots visually reviewed: desktop administrator dashboard, phone deliveries,
  administrator navigation and schedule modal. Browser checks also cover quality
  table/form geometry, local scrolling, reduced motion, direct refresh and history.
- No commit or push performed.

PowerShell's script policy blocks npm/npx `.ps1` shims; the equivalent `.cmd`
launchers were used. The first browser run needed the installed Edge channel
because Playwright's bundled Chromium was absent. The final run used approved
execution outside the sandbox to allow browser/server process cleanup and report
writing. All browser traffic to the API was intercepted with fixtures.
