# Migration recovery and final review

Branch: `deploy/five-role-final`. Verified 2026-09-29. No commit or push.

## Database recovery

A custom-format pg_dump backup was created outside the repository in the local
OS temporary directory, under `BuildWise-recovery-backups`. The retained file is
`before-legacy-recovery-20260929-113737.dump` (106,907 bytes).
Its SHA-256 is `3519c00434d1b618c6386e503c60c3245046548c74457b419610f6a8e2d0bbf8`.
pg_restore successfully decoded the archive without restoring it into a database.

Read-only checks immediately before rollback confirmed zero rows in approvals,
Rfqs, RfqSuppliers and ProcurementRecommendations. Their only incoming references
were between the empty RFQ tables. The migration Down contained only four table drops.

EF rolled back only `20260928172743_AddMaterialRequestApprovalsTable`, leaving the
previous four history entries. EF then removed that migration from source and
restored the previous model. No snapshot was edited manually.

After legacy model removal, EF generated
`20260929061152_AddMaterialRequestApprovalsTable`. Its Up creates only approvals,
with Id, MaterialRequestId, ApprovedByUserId, Decision, Comment, DecisionDate,
CreatedAt and UpdatedAt. Decision is a required string with maximum length 50.
MaterialRequestId references material_requests with cascade deletion;
ApprovedByUserId references users with restricted deletion. Both FK columns have
indexes. Down drops only approvals. The final snapshot diff adds only Approval
and its relationships. No old committed migration was changed.

The clean migration was applied to the preserved development database. approvals
exists; the three legacy tables do not. The clean migration is recorded in history.
EF reports no pending model changes.

| Table | Before | After rollback | After clean migration and testing |
|---|---:|---:|---:|
| users | 5 | 5 | 5 |
| material_requests | 4 | 4 | 4 |
| quotations | 5 | 5 | 5 |
| purchase_orders | 2 | 2 | 2 |
| deliveries | 2 | 2 | 2 |
| inspections | 2 | 2 | 2 |
| agent_workflows | 9 | 9 | 9 |

Fresh-database verification applied all five migrations from zero, verified the
expected schema without legacy tables, started the API, and fetched Swagger.
DbSeeder produced 5 users, 7 retained role rows, 1 project, 3 suppliers, 1 material
request and 1 request item. Quotations are intentionally not seeded. The initial
verification harness incorrectly expected quotations; its assertion was corrected
and the complete check rerun successfully. Both disposable databases were deleted.
The preserved development database was never recreated.

## Implementation and review

- Removed legacy RFQ/recommendation entities, DbSets, enums, exclusive DTOs,
  controller, evaluation service and DI registration. Current DTOs are retained
  under the separate API DTOs namespace.
- Removed the unreachable React service and two legacy pages. Current routed
  procurement components remain.
- Flutter Administrator Orders uses `/api/purchase-orders`, parses the paginated
  items envelope and loads subsequent pages. It displays the material request ID
  available in the current DTO. Mobile role visibility remains unchanged.
- Rewrote legacy security/validation tests against workflow decisions. Duplicate
  tests cover repeated decisions, repeated PO creation and separate workflows for
  the same request. PostgreSQL concurrency tests cover same/different workflows.
- Fixed pre-existing working-tree regressions found by execution: restored material
  request authentication, JWT actors, ownership/state validation and status/options
  endpoints; persisted approval history and request status in one SaveChanges.
- Approval reviews permit ProcurementManager and Administrator. Approved, Rejected
  and RevisionRequested append UTC history; invalid decisions do not write history.
  A spoofed body user ID is ignored. Previous history is preserved.
- Restored provider-appropriate inspection access. PostgreSQL keeps row locks.
  No prior inspection permits starting; active or accepted inspections block it;
  completed partial/rejected inspections permit another. Pending queries match.
  Received-quantity validation and one-to-many inspection relationships remain.
- Agent 1 no longer reuses an arbitrary procurement workflow or overwrites step 1.
  Each invocation creates its own completed workflow and step atomically. A test
  verifies that procurement recommendations and earlier planning history survive.
- Role entity/configuration changes are comments only; retained legacy role rows
  are not deleted. ERD changes only clarify the five application roles.

## Remaining legacy references

- ProcurementPlanningAgentService.cs: RFQ strategy prose, not entity access.
- ProcurementPlanningPanel.jsx: RFQ strategy description, not an API dependency.
- MaterialRequestsPage.jsx: RfqInProgress status/filter wording retained; no RFQ calls.
- This report: historical explanation of the removed schema.

There is no active dependency on the removed entities, service or endpoints.

## Repository checks

`git diff --check` passes. Status, name-status and diff statistics were reviewed.
No real credentials, JWTs, API keys, database dumps, local database files, temporary
smoke scripts, build outputs or IDE files were added. Existing demo/test password
literals are fixtures, not newly introduced private credentials. Machine-specific
paths in the edited handoff were replaced with repository-relative commands.
.pytest_cache and TestResults are now explicitly ignored; no generated/cache files
are tracked. The backup remains outside the repository. No staged changes.

## Executed validation

| Suite/check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| BuildWise.Api.Tests, local PostgreSQL + quotation service | 318 | 0 | 1 |
| BuildWise.Tests | 3 | 0 | 0 |
| BuildWise.QualityAgent.Tests executable checks | 17 | 0 | 0 |
| React | 159 | 0 | 0 |
| Flutter | 90 | 0 | 1 |
| Quotation Python agent | 11 | 0 | 0 |
| Quality Python agent | 26 | 0 | 0 |

The quality Python run also passed 17 subtests. These are not added to the 26 tests.
The default API run without external configuration passed 296, failed 0 and skipped
11. The PostgreSQL-enabled run above supersedes it as the final backend evidence;
the two runs are not added together. The only backend skip is the opt-in live-client
host. Flutter skips its opt-in live integration test. External AI provider calls
were disabled; the real local Python HTTP service used its deterministic mode.

Within the final API run: approval history 5 passed; re-inspection 6 passed;
PostgreSQL inspection checks 6 passed; security authorization 118 passed; demo-role
seeding 2 passed; planning audit preservation 1 passed. All had zero failures.

Release API build succeeded (5 existing nullable warnings). React lint succeeded
with 10 warnings and its production build succeeded. Flutter analyze reported
zero issues. No browser/live-client verification is claimed for this recovery.

FINAL COMMIT READINESS: GO. No commit or push performed. Restart any previously
running development API before manually exercising the rebuilt application.

## File classification

- `.gitignore`: required documentation/repository hygiene.
- `backend/BuildWise.Api.Tests/DeliveryDiscrepancyAgentTests.cs`: required test.
- `backend/BuildWise.Api.Tests/DeliveryReconciliationTests.cs`: required test.
- `backend/BuildWise.Api.Tests/DemoRoleSeedingTests.cs`: required test.
- `backend/BuildWise.Api.Tests/InspectionDuplicatePostgresTests.cs`: required test.
- `backend/BuildWise.Api.Tests/MaterialRequestApprovalHistoryTests.cs`: required test.
- `backend/BuildWise.Api.Tests/ProcurementIntegrationTests.cs`: required test.
- `backend/BuildWise.Api.Tests/ProcurementPlanningHistoryTests.cs`: required test.
- `backend/BuildWise.Api.Tests/QualityAuthorizationTests.cs`: required test.
- `backend/BuildWise.Api.Tests/QualityReinspectionTests.cs`: required test.
- `backend/BuildWise.Api.Tests/SecurityAuthorizationTests.cs`: required test.
- `backend/BuildWise.Api/Controllers/DeliveriesController.cs`: required backend change.
- `backend/BuildWise.Api/Controllers/MaterialRequestsController.cs`: required backend change.
- `backend/BuildWise.Api/Controllers/ProcurementController.cs`: required backend legacy cleanup.
- `backend/BuildWise.Api/Controllers/PurchaseOrdersController.cs`: required backend change.
- `backend/BuildWise.Api/Data/ApplicationDbContext.cs`: required backend legacy cleanup.
- `backend/BuildWise.Api/Data/Configurations/ApprovalConfiguration.cs`: required migration.
- `backend/BuildWise.Api/Data/Configurations/RoleConfiguration.cs`: required backend change.
- `backend/BuildWise.Api/Data/DbSeeder.cs`: required backend change.
- `backend/BuildWise.Api/Data/Migrations/20260929061152_AddMaterialRequestApprovalsTable.Designer.cs`: required migration.
- `backend/BuildWise.Api/Data/Migrations/20260929061152_AddMaterialRequestApprovalsTable.cs`: required migration.
- `backend/BuildWise.Api/Data/Migrations/ApplicationDbContextModelSnapshot.cs`: required migration.
- `backend/BuildWise.Api/Models/Dtos/ProcurementDtos.cs`: required backend legacy cleanup.
- `backend/BuildWise.Api/Models/Entities/ProcurementRecommendation.cs`: required backend legacy cleanup.
- `backend/BuildWise.Api/Models/Entities/Rfq.cs`: required backend legacy cleanup.
- `backend/BuildWise.Api/Models/Entities/RfqSupplier.cs`: required backend legacy cleanup.
- `backend/BuildWise.Api/Models/Entities/Role.cs`: required backend change.
- `backend/BuildWise.Api/Models/Enums/RecommendationStatus.cs`: required backend legacy cleanup.
- `backend/BuildWise.Api/Models/Enums/RfqStatus.cs`: required backend legacy cleanup.
- `backend/BuildWise.Api/Program.cs`: required backend legacy cleanup.
- `backend/BuildWise.Api/Services/ProcurementPlanningAgentService.cs`: required backend change.
- `backend/BuildWise.Api/Services/QualityInspectionService.cs`: required backend change.
- `backend/BuildWise.Api/Services/SupplierEvaluationAgentService.cs`: required backend legacy cleanup.
- `docs/BuildWise_ERD.dbml`: required documentation/repository hygiene.
- `docs/component2_spec.md`: required documentation/repository hygiene.
- `docs/handoff/component2_to_component3_handoff.md`: required documentation/repository hygiene.
- `docs/handoff/component3-handoff.md`: required documentation/repository hygiene.
- `docs/reports/five-role-rbac-refactor.md`: required documentation/repository hygiene.
- `docs/reports/migration-recovery.md`: required documentation/repository hygiene.
- `mobile/buildwise_mobile/lib/features/deliveries/screens/delivery_list_screen.dart`: required Flutter RBAC/endpoint migration.
- `mobile/buildwise_mobile/lib/features/deliveries/screens/receive_delivery_screen.dart`: required Flutter RBAC/endpoint migration.
- `mobile/buildwise_mobile/lib/features/material_requests/screens/material_request_list_screen.dart`: required Flutter RBAC/endpoint migration.
- `mobile/buildwise_mobile/lib/features/procurement/screens/po_list_screen.dart`: required Flutter RBAC/endpoint migration.
- `mobile/buildwise_mobile/lib/features/procurement/services/procurement_service.dart`: required Flutter RBAC/endpoint migration.
- `mobile/buildwise_mobile/lib/main.dart`: required Flutter RBAC/endpoint migration.
- `mobile/buildwise_mobile/test/procurement_service_test.dart`: required test.
- `mobile/buildwise_mobile/test/role_navigation_test.dart`: required test.
- `web/buildwise-web/ROUTING-UX.md`: required documentation.
- `web/buildwise-web/e2e/procurement.live.js`: required test.
- `web/buildwise-web/e2e/routing.pw.js`: required test.
- `web/buildwise-web/src/App.test.jsx`: required test.
- `web/buildwise-web/src/Features/MaterialRequests/pages/MaterialRequestsPage.jsx`: required React RBAC/legacy cleanup.
- `web/buildwise-web/src/Features/MaterialRequests/pages/MaterialRequestsPage.test.jsx`: required test.
- `web/buildwise-web/src/Features/deliveries/pages/DeliveryDashboard.jsx`: required React RBAC/legacy cleanup.
- `web/buildwise-web/src/Features/deliveries/pages/DeliveryDashboard.test.jsx`: required test.
- `web/buildwise-web/src/Features/deliveries/pages/RecordDeliveryForm.jsx`: required React RBAC/legacy cleanup.
- `web/buildwise-web/src/Features/procurement/pages/ProcurementApp.jsx`: required React RBAC/legacy cleanup.
- `web/buildwise-web/src/Features/procurement/pages/PurchaseOrderNavigation.test.jsx`: required test.
- `web/buildwise-web/src/Features/procurement/pages/QuotationComparisonPage.jsx`: required React RBAC/legacy cleanup.
- `web/buildwise-web/src/Features/procurement/pages/SupplierManagementPage.jsx`: required React RBAC/legacy cleanup.
- `web/buildwise-web/src/Features/procurement/services/procurementService.js`: required React RBAC/legacy cleanup.
- `web/buildwise-web/src/routes/routeConfig.js`: required React RBAC/legacy cleanup.

Accidental/unrelated files remaining: NONE.

## Seven proposed commit groups

Commands are provided for review only; none were executed. Run each group separately
when committing is authorized. Deleted tracked paths are intentionally included.

### 1. Remove legacy backend procurement and migrate its tests

```powershell
git add -- "backend/BuildWise.Api.Tests/ProcurementIntegrationTests.cs" "backend/BuildWise.Api.Tests/SecurityAuthorizationTests.cs" "backend/BuildWise.Api/Controllers/ProcurementController.cs" "backend/BuildWise.Api/Data/ApplicationDbContext.cs" "backend/BuildWise.Api/Models/Dtos/ProcurementDtos.cs" "backend/BuildWise.Api/Models/Entities/ProcurementRecommendation.cs" "backend/BuildWise.Api/Models/Entities/Rfq.cs" "backend/BuildWise.Api/Models/Entities/RfqSupplier.cs" "backend/BuildWise.Api/Models/Enums/RecommendationStatus.cs" "backend/BuildWise.Api/Models/Enums/RfqStatus.cs" "backend/BuildWise.Api/Program.cs" "backend/BuildWise.Api/Services/SupplierEvaluationAgentService.cs"
```

### 2. Persist material-request approval history with the clean migration

```powershell
git add -- "backend/BuildWise.Api.Tests/MaterialRequestApprovalHistoryTests.cs" "backend/BuildWise.Api/Controllers/MaterialRequestsController.cs" "backend/BuildWise.Api/Data/Configurations/ApprovalConfiguration.cs" "backend/BuildWise.Api/Data/Migrations/20260929061152_AddMaterialRequestApprovalsTable.Designer.cs" "backend/BuildWise.Api/Data/Migrations/20260929061152_AddMaterialRequestApprovalsTable.cs" "backend/BuildWise.Api/Data/Migrations/ApplicationDbContextModelSnapshot.cs"
```

### 3. Allow controlled quality re-inspection

```powershell
git add -- "backend/BuildWise.Api.Tests/InspectionDuplicatePostgresTests.cs" "backend/BuildWise.Api.Tests/QualityReinspectionTests.cs" "backend/BuildWise.Api/Services/QualityInspectionService.cs"
```

### 4. Align backend roles, delivery context, seeding and planning audit

```powershell
git add -- "backend/BuildWise.Api.Tests/DeliveryDiscrepancyAgentTests.cs" "backend/BuildWise.Api.Tests/DeliveryReconciliationTests.cs" "backend/BuildWise.Api.Tests/DemoRoleSeedingTests.cs" "backend/BuildWise.Api.Tests/ProcurementPlanningHistoryTests.cs" "backend/BuildWise.Api.Tests/QualityAuthorizationTests.cs" "backend/BuildWise.Api/Controllers/DeliveriesController.cs" "backend/BuildWise.Api/Controllers/PurchaseOrdersController.cs" "backend/BuildWise.Api/Data/Configurations/RoleConfiguration.cs" "backend/BuildWise.Api/Data/DbSeeder.cs" "backend/BuildWise.Api/Models/Entities/Role.cs" "backend/BuildWise.Api/Services/ProcurementPlanningAgentService.cs"
```

### 5. Align React RBAC and remove unreachable procurement UI

```powershell
git add -- "web/buildwise-web/ROUTING-UX.md" "web/buildwise-web/e2e/procurement.live.js" "web/buildwise-web/e2e/routing.pw.js" "web/buildwise-web/src/App.test.jsx" "web/buildwise-web/src/Features/MaterialRequests/pages/MaterialRequestsPage.jsx" "web/buildwise-web/src/Features/MaterialRequests/pages/MaterialRequestsPage.test.jsx" "web/buildwise-web/src/Features/deliveries/pages/DeliveryDashboard.jsx" "web/buildwise-web/src/Features/deliveries/pages/DeliveryDashboard.test.jsx" "web/buildwise-web/src/Features/deliveries/pages/RecordDeliveryForm.jsx" "web/buildwise-web/src/Features/procurement/pages/ProcurementApp.jsx" "web/buildwise-web/src/Features/procurement/pages/PurchaseOrderNavigation.test.jsx" "web/buildwise-web/src/Features/procurement/pages/QuotationComparisonPage.jsx" "web/buildwise-web/src/Features/procurement/pages/SupplierManagementPage.jsx" "web/buildwise-web/src/Features/procurement/services/procurementService.js" "web/buildwise-web/src/routes/routeConfig.js"
```

### 6. Align Flutter role scope and migrate Administrator PO listing

```powershell
git add -- "mobile/buildwise_mobile/lib/features/deliveries/screens/delivery_list_screen.dart" "mobile/buildwise_mobile/lib/features/deliveries/screens/receive_delivery_screen.dart" "mobile/buildwise_mobile/lib/features/material_requests/screens/material_request_list_screen.dart" "mobile/buildwise_mobile/lib/features/procurement/screens/po_list_screen.dart" "mobile/buildwise_mobile/lib/features/procurement/services/procurement_service.dart" "mobile/buildwise_mobile/lib/main.dart" "mobile/buildwise_mobile/test/procurement_service_test.dart" "mobile/buildwise_mobile/test/role_navigation_test.dart"
```

### 7. Document recovery and exclude generated artifacts

```powershell
git add -- ".gitignore" "docs/BuildWise_ERD.dbml" "docs/component2_spec.md" "docs/handoff/component2_to_component3_handoff.md" "docs/handoff/component3-handoff.md" "docs/reports/five-role-rbac-refactor.md" "docs/reports/migration-recovery.md"
```
