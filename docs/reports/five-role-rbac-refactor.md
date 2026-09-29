# BuildWise five-role RBAC refactor

Branch: `deploy/five-role-final`. No commit or push was made.

The verification and database-audit sections below record the earlier RBAC work.
See [migration recovery](migration-recovery.md) for the current 2026-09-29 changes and checks.
Authority: approved BuildWise Scenario and the user's explicit decision that older
handoffs describe the previous implementation. Existing Administrator permissions
are preserved. Database role deletion remains unapproved and was not performed.

## Permissions before and after

| Operation | Before | After |
|---|---|---|
| Material request options | SiteEngineer, ProjectManager, Administrator | SiteEngineer, Administrator |
| Request list/detail | SiteEngineer, ProjectManager, procurement roles, Administrator | SiteEngineer, procurement roles, Administrator |
| Request approval | ProjectManager, Administrator | ProcurementManager, Administrator |
| Request Planning Agent | SiteEngineer, ProjectManager, procurement roles, Administrator | SiteEngineer, ProcurementManager, Administrator |
| Request creation/submission and own procurement status | SiteEngineer, Administrator | Unchanged |
| Delivery reads/context/history | ReceivingOfficer, ProjectManager, procurement roles, QualityInspector, Administrator | SiteEngineer, procurement roles, QualityInspector, Administrator |
| Receive/evidence/report issue | ReceivingOfficer, Administrator | SiteEngineer, Administrator |
| Delivery scheduling and pre-delivery risk | ReceivingOfficer, procurement roles, Administrator | Procurement roles, Administrator |
| Post-receipt discrepancy agent | ReceivingOfficer, ProjectManager, procurement roles, QualityInspector, Administrator | SiteEngineer, procurement roles, QualityInspector, Administrator |
| Full PO reads | ReceivingOfficer, procurement roles, Administrator | Procurement roles, Administrator |
| PO creation | ProcurementManager, Administrator | Unchanged; approved workflow required |
| PO status actions | Procurement roles, Administrator | Unchanged; deterministic transitions required |
| Quality/NCR | QualityInspector, Administrator | Unchanged |

Here, procurement roles means ProcurementOfficer and ProcurementManager.

React routes follow this matrix. Agent 1, request approval and receipt buttons now
match backend access. `/ai-workflows`, `/reports`, `/users` remain locked.
Flutter uses the stored ASP.NET authentication response and the same JWT/API:

- SiteEngineer: Requests, Deliveries.
- QualityInspector: Deliveries, Quality.
- ProcurementOfficer/ProcurementManager: Web Portal guidance; no mobile procurement workspace.
- Administrator: Requests, Orders, Deliveries, Quality.
- Multiple roles: union of permitted modules. Legacy/unknown roles alone: no workspace.

## Important changes, code, testing and viva notes

### Material requests

FILE: `backend/BuildWise.Api/Controllers/MaterialRequestsController.cs`

CHANGE/WHY: Transfer manager review to ProcurementManager; remove legacy access;
exclude ProcurementOfficer from Agent 1. No state or ownership validation changed.

IMPORTANT CODE: `[Authorize(Roles = "ProcurementManager,Administrator")]`

HOW TO TEST: `SecurityAuthorizationTests` covers pending-only approval, a site user's
own draft submission, foreign-draft denial, and denied officer approval/Agent 1 calls.

COMMON FAILURE: a hidden approval button cannot stop a direct HTTP request; MVC
must validate role claims before executing the action.

### Deliveries and limited procurement context

FILE: `backend/BuildWise.Api/Controllers/DeliveriesController.cs`

CHANGE/WHY: SiteEngineer receives, attaches evidence and reports issues. Procurement
retains scheduling and pre-delivery risk. All five roles may read deliveries and run
advisory post-receipt discrepancy analysis, preserving the existing viewer responsibilities.

IMPORTANT CODE: `[Authorize(Roles = "SiteEngineer,Administrator")]`

Expected/detail responses add `previouslyReceivedUsableQuantity` and
`outstandingQuantity`. Usable quantity is received minus damaged quantities on
other finalized deliveries, matching the existing receipt validation. Schedules
and issues use limited projections instead of serializing full EF navigation graphs.

HOW TO TEST: `Delivery_context_is_available_without_sensitive_navigation_properties`,
`Repeat_receiving_context_uses_remaining_usable_quantity`,
`Engineer_can_submit_own_draft_and_record_damage_and_evidence`, and
`Site_receives_procurement_order_using_only_delivery_context`.

COMMON FAILURE: returning an EF entity can serialize loaded pricing/user properties.
Client quantity defaults can also become stale; the backend recalculates and rejects
over-receipt inside its existing transaction.

### Purchase orders

FILE: `backend/BuildWise.Api/Controllers/PurchaseOrdersController.cs`

CHANGE/WHY: Remove legacy PO-reader authorization and its now-unreachable conditional
filters. SiteEngineer reads only the Delivery API and own-request status endpoint.

IMPORTANT CODE: `[Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator")]`

HOW TO TEST: direct site-user PO list/detail/PATCH calls return 403. The PostgreSQL
workflow test proves receiving still works without PO API access.

COMMON FAILURE: mapping the old receiving role to SiteEngineer on every endpoint
would expose pricing and quotation data unnecessarily.

### React

FILES: `web/buildwise-web/src/routes/routeConfig.js` and the MaterialRequestsPage,
DeliveryDashboard, RecordDeliveryForm and ProcurementApp files listed below.

CHANGE/WHY: Five-role routes and buttons; no legacy procurement exception. Receipt
forms default to outstanding quantity and no longer send a simulated actor ID.

IMPORTANT CODE: `export const purchaseOrderRoles = procurementRoles`

HOW TO TEST: route/deep-link and action tests, plus `e2e/routing.pw.js` on three viewport sizes.

COMMON FAILURE: route guards alone miss action-level permissions. Officer access to
material requests does not imply permission to run Agent 1 or approve the request.

### Flutter

FILES: `mobile/buildwise_mobile/lib/main.dart` and the three changed feature screens.

CHANGE/WHY: Restore roles through existing `AuthService.currentUser()`, build only
permitted tabs and mount only the selected screen. Delivery viewers cannot open a
receipt form; procurement request viewers do not call the owner-only status API or
see request creation controls. No second authentication system was introduced.

IMPORTANT CODE: `MainAppShell(roles: roles, onSignOut: _handleSignedOut)`

HOW TO TEST: `test/role_navigation_test.dart` checks stored-session restoration for all
five roles, legacy/unknown denial, multiple-role union, action flags and sign-out.

COMMON FAILURE: an IndexedStack can mount hidden modules and make unauthorized API
calls. Stored role data is for UX; the signed JWT remains the server's authority.

### Seeding and tests

FILES: `DbSeeder.cs`, `RoleConfiguration.cs`, and authorization/workflow test files below.

CHANGE/WHY: Only five demo assignments are created on a fresh database. Legacy seed
rows remain for compatibility. Denial tests intentionally retain legacy role names.
Integration fixtures use the new manager/site accounts. Test database cleanup retries
without forcing PostgreSQL to terminate another process.

HOW TO TEST: `DemoRoleSeedingTests` proves five assignments, retained legacy rows and
idempotence. PostgreSQL tests exercise approval, rejection, revision, concurrency,
quotation validation and receiving through the new contract.

COMMON FAILURE: removing seed rows generates deletion operations; a cascading FK can
remove assignments before users are remapped. Seeding does not migrate existing users.

## Historical verification (2026-09-28)

- Release backend build succeeded. Initial Debug test/build failed because the running
  API executable was locked; it was left running. Earlier clean compilation reported
  existing nullable warnings; the final incremental build does not repeat them.
- Final full backend suite with local PostgreSQL and quotation agent: **308 passed, 1 skipped** (opt-in live-client host).
- `npm.cmd ci`: succeeded after stopping locked Vite processes; no audit vulnerabilities.
  npm warned that installed Node 24.13.1 is below jsdom's supported 24.15.0 range.
- `npm.cmd run lint`: exit 0, 10 warnings (hooks dependencies, fast-refresh export, unused state).
- `npm.cmd test -- --run`: 159 passed across 18 files.
- `npm.cmd run build`: succeeded.
- Edge Playwright routing: 9 passed across 390/768/1440 widths. Initial default browser
  launch failed; the installed Edge retry succeeded.
- `flutter pub get`: succeeded.
- `flutter analyze`: no issues.
- `flutter test`: 84 passed, 1 opt-in live test skipped.
- `git diff --check`: passed.

Failures corrected: quotation security test used a nonexistent route; a success test
expected 201 instead of the existing 200 quotation response; Flutter combined-role
fixture lacked a secure-storage stub; opt-in integration fixtures used obsolete accounts;
PostgreSQL temporary-database cleanup attempted prohibited force-termination.
No failing test was deleted.

The external AI provider was disabled for the temporary local quotation-agent test
server (stopped after verification). Real HTTP/JWT/PostgreSQL and the existing Python deterministic fallback were
exercised. A simultaneous live React/Flutter/API session was not exercised; its opt-in
host and Flutter live test remain skipped. No agent decision-making logic was changed.

## Remaining legacy references: every location classified

Both names remain in:

- `backend/BuildWise.Api/Data/Configurations/RoleConfiguration.cs`: intentionally retained compatibility data.
- `backend/BuildWise.Api/Data/Migrations/20260914170347_AddAuthTables.cs`: historical migration; keep.
- `backend/BuildWise.Api/Data/Migrations/20260914170347_AddAuthTables.Designer.cs`: generated historical model; keep.
- `backend/BuildWise.Api/Data/Migrations/20260922061206_MergeComponent2AndDelivery.Designer.cs`: generated historical model; keep.
- `backend/BuildWise.Api/Data/Migrations/20260923091156_AddQualityInspectionAndNonConformance.Designer.cs`: generated historical model; keep.
- `backend/BuildWise.Api/Data/Migrations/ApplicationDbContextModelSnapshot.cs`: generated current model; leave until approved migration.
- `backend/BuildWise.Api.Tests/SecurityAuthorizationTests.cs`: negative authorization/registration fixtures; keep.
- `backend/BuildWise.Api.Tests/DemoRoleSeedingTests.cs`: compatibility-row assertions; keep.
- `web/buildwise-web/src/App.test.jsx`: legacy-role denial; keep.
- `web/buildwise-web/src/Features/MaterialRequests/pages/MaterialRequestsPage.test.jsx`: denied approval/Agent 1; keep.
- `web/buildwise-web/src/Features/procurement/pages/PurchaseOrderNavigation.test.jsx`: denied PO access; keep.
- `web/buildwise-web/src/Features/quality/QualityApp.test.jsx`: denied quality access; keep.
- `mobile/buildwise_mobile/test/role_navigation_test.dart`: no-workspace tests; keep.
- This report: documentation of the migration and cleanup plan; keep.

ReceivingOfficer additionally remains in `backend/BuildWise.Api.Tests/QualityAuthorizationTests.cs`
as a denied-role fixture. No active controller, React route/action, Flutter navigation,
demo assignment, or live test login depends on either legacy role.
Audit covers tracked repository text and new files. Binary scenario/ERD PDFs and Git
history were not rewritten. Cache directories with access errors contain no tracked source.

## Database audit and proposed cleanup (not executed)

Read-only inspection of the local application database on 2026-09-28 found:

| Role | ID | Assignments |
|---|---|---|
| Administrator | 1 | 1 |
| SiteEngineer | 2 | 1 |
| ProjectManager | 3 | 0 |
| ProcurementOfficer | 4 | 1 |
| ProcurementManager | 5 | 1 |
| ReceivingOfficer | 6 | 0 |
| QualityInspector | 7 | 0 |

The sole FK to roles is `user_roles.RoleId -> roles.Id ON DELETE CASCADE`.
The code also defines composite key `(UserId, RoleId)`. Current local counts do not
prove that another developer/deployment database has no legacy assignments.

Migration needed now: **No**, the five-role application works with retained rows.
Migration needed for permanent row removal: **Yes**, after separate approval.

Recommended data-preserving EF migration, once approved:

1. Back up and recheck FK dependencies and role assignments in the target database.
2. Within the migration transaction, find roles by name and ensure target roles exist.
3. Insert missing ProjectManager user assignments into ProcurementManager and missing
   ReceivingOfficer assignments into SiteEngineer, avoiding composite-key duplicates.
4. Verify every old assignment has a target assignment before removing old links.
5. Remove legacy links and role rows; update HasData and regenerate the model snapshot
   through EF. Do not rewrite historical migrations.
6. Require affected users to sign in again; already issued JWTs retain their old claims.
7. Document that automatic rollback cannot reconstruct original role membership without
   an explicit backup/mapping record.

For this local database, the remap would currently affect zero users. Still, use a
reviewed migration so seed metadata and deployed databases remain consistent.
Recreating a disposable development database would be simpler only if its owner confirms
all data can be discarded; disposability was not established. No application database data was deleted; integration tests created and dropped isolated temporary databases. An earlier cleanup failure may have left its temporary test database behind.

Current development seeding adds missing demo accounts and role assignments without
replacing existing password hashes. This includes QualityInspector; idempotence and
backfill are covered by DemoRoleSeedingTests. Legacy role rows remain unchanged.

## Historical limits and next step (2026-09-28)

No regression remains in the executed suites. The running Debug API must be restarted
with the rebuilt code before manually testing the updated permissions. Both Vite servers
were restarted after dependency installation. No deployment or destructive migration was
performed. Confirm existing-account provisioning for the demo, then review the optional
cleanup migration. The approved source conflict is resolved; Figma was not changed.

## Files changed

- `backend/BuildWise.Api.Tests/DeliveryDiscrepancyAgentTests.cs`
- `backend/BuildWise.Api.Tests/DeliveryReconciliationTests.cs`
- `backend/BuildWise.Api.Tests/DemoRoleSeedingTests.cs`
- `backend/BuildWise.Api.Tests/ProcurementIntegrationTests.cs`
- `backend/BuildWise.Api.Tests/QualityAuthorizationTests.cs`
- `backend/BuildWise.Api.Tests/SecurityAuthorizationTests.cs`
- `backend/BuildWise.Api/Controllers/DeliveriesController.cs`
- `backend/BuildWise.Api/Controllers/MaterialRequestsController.cs`
- `backend/BuildWise.Api/Controllers/PurchaseOrdersController.cs`
- `backend/BuildWise.Api/Data/Configurations/RoleConfiguration.cs`
- `backend/BuildWise.Api/Data/DbSeeder.cs`
- `backend/BuildWise.Api/Models/Entities/Role.cs`
- `docs/BuildWise_ERD.dbml`
- `docs/component2_spec.md`
- `docs/handoff/component2_to_component3_handoff.md`
- `docs/handoff/component3-handoff.md`
- `docs/reports/five-role-rbac-refactor.md`
- `mobile/buildwise_mobile/lib/features/deliveries/screens/delivery_list_screen.dart`
- `mobile/buildwise_mobile/lib/features/deliveries/screens/receive_delivery_screen.dart`
- `mobile/buildwise_mobile/lib/features/material_requests/screens/material_request_list_screen.dart`
- `mobile/buildwise_mobile/lib/main.dart`
- `mobile/buildwise_mobile/test/role_navigation_test.dart`
- `web/buildwise-web/ROUTING-UX.md`
- `web/buildwise-web/e2e/procurement.live.js`
- `web/buildwise-web/e2e/routing.pw.js`
- `web/buildwise-web/src/App.test.jsx`
- `web/buildwise-web/src/Features/MaterialRequests/pages/MaterialRequestsPage.jsx`
- `web/buildwise-web/src/Features/MaterialRequests/pages/MaterialRequestsPage.test.jsx`
- `web/buildwise-web/src/Features/deliveries/pages/DeliveryDashboard.jsx`
- `web/buildwise-web/src/Features/deliveries/pages/DeliveryDashboard.test.jsx`
- `web/buildwise-web/src/Features/deliveries/pages/RecordDeliveryForm.jsx`
- `web/buildwise-web/src/Features/procurement/pages/ProcurementApp.jsx`
- `web/buildwise-web/src/Features/procurement/pages/PurchaseOrderNavigation.test.jsx`
- `web/buildwise-web/src/routes/routeConfig.js`
