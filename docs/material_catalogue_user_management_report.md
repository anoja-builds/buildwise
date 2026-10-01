# Material catalogue and Administrator user management

Implemented on `ui/final-figma-flutter`. No commit or push was performed.

## Files changed or created by this task

Modified:
- `backend/BuildWise.Api/Data/DbSeeder.cs`
- `backend/BuildWise.Api/Controllers/MaterialRequestsController.cs`
- `backend/BuildWise.Api.Tests/SecurityAuthorizationTests.cs` (partial class declaration to reuse the JWT/MVC test fixture)
- `web/buildwise-web/src/App.jsx`
- `web/buildwise-web/src/App.test.jsx`
- `web/buildwise-web/src/routes/routeConfig.js`

Created:
- `backend/BuildWise.Api/Controllers/UsersController.cs`
- `backend/BuildWise.Api/Models/Dtos/UserManagementDtos.cs`
- `backend/BuildWise.Api.Tests/UserManagementTests.cs`
- `web/buildwise-web/src/Features/users/UsersPage.jsx`
- `web/buildwise-web/src/Features/users/usersApi.js`
- `web/buildwise-web/src/Features/users/UsersPage.test.jsx`
- `web/buildwise-web/src/Features/users/usersApi.test.js`
- `docs/material_catalogue_user_management_report.md`

No schema changes or migrations were needed. Existing users, roles, user-role relations and materials are reused. Public registration and workflow logic are unchanged. No files under `mobile/buildwise_mobile/**` or `ai/buildwise-agent-service/**` were modified by this task. The unrelated `backend/BuildWise.sln` was left untouched.

## Material catalogue

| Material | Unit | Category |
| --- | --- | --- |
| Cement (50kg bag) | bag | Structural Materials |
| Sand | m3 | Aggregate |
| Crushed Aggregate | m3 | Aggregate |
| Reinforcement Steel Bar | kg | Structural Materials |
| Concrete Block | unit | Masonry |
| Clay Brick | unit | Masonry |
| Timber | m | Carpentry |
| Plywood Sheet | sheet | Carpentry |
| PVC Pipe | m | Plumbing |
| Electrical Cable | m | Electrical |
| Interior Paint | litre | Finishes |
| Ceramic Tile | m2 | Finishes |

`SeedMaterialsAsync` runs before, and independently of, procurement scenario seeding. It matches trimmed names case-insensitively, inserts only missing entries as active, and preserves all existing records and their active status, units and categories. The scenario reuses the catalogue cement record instead of inserting another one.

Seeding was exercised in isolated test databases, including a database with existing suppliers. The running development PostgreSQL database was not changed during verification. The existing development startup seeder applies the catalogue when the API is next started.

## API and authorization

- `GET /api/users`: lists only `id`, `fullName`, `email`, `isActive`, `roles`, `createdAt`.
- `POST /api/users`: validates required fields, database field lengths, email format, normalized duplicate email, password length of at least eight characters, and a supported role present in the database. Creates an active account with `PasswordHasher<User>` and a `UserRole` relation. Returns safe user fields only.
- `PATCH /api/users/{id}/status`: requires an explicit boolean, rejects nonpositive IDs and missing users, blocks self-deactivation, and persists `UpdatedAt`. Returns safe user fields only.
- `GET /api/material-requests/options`: added as an alias for the existing `/api/MaterialRequests/options` action; returns active database materials.

The whole Users controller requires `[Authorize(Roles = "Administrator")]`. Anonymous requests receive 401; authenticated non-administrators receive 403. Material options retain SiteEngineer/Administrator authorization. Public `/api/auth/register` remains restricted to SiteEngineer, including the existing service-level restriction.

Supported creation roles: SiteEngineer, ProcurementOfficer, ProcurementManager, QualityInspector, Administrator. Existing legacy roles cannot be selected through this endpoint.

## React

`/users` now renders Users & Roles for Administrators. The shared navigation and direct route guard use the same role restriction, with the API remaining authoritative. The page reuses shared BuildWise headers, cards, form controls, buttons, table and status badges. It provides a user list, Add User form, exact backend role mappings, masked temporary password input, API validation errors, and Activate/Deactivate actions. Successful creation removes the password form. Own-account status changes are disabled in the UI and self-deactivation is blocked by the API.

## Tests

Added 17 backend test cases covering anonymous/non-admin access to all three user endpoints, creation and login for all five supported roles, safe list/create/status responses, normalized duplicate email rejection, invalid role/name/email/password input, activation/deactivation and subsequent login behavior, update timestamps, self-deactivation, invalid/missing IDs, missing status, and idempotent catalogue seeding with existing suppliers and preserved inactive/custom material data. The options test requests the hyphenated URL and verifies multiple active materials and exclusion of inactive material.

Added nine React test cases covering Administrator navigation/page access, direct-route denial for all four non-admin roles, selected-role creation, password-form removal, API error display, status toggling, authenticated API requests and validation error parsing. Updated the existing Administrator navigation expectation.

## Verification results

| Command | Result |
| --- | --- |
| `dotnet build backend/BuildWise.Api/BuildWise.Api.csproj` | Passed: 0 errors, 5 existing CS8602 warnings in PurchaseOrdersController.cs |
| `dotnet test backend/BuildWise.Api.Tests/BuildWise.Api.Tests.csproj` | Passed: 313 passed, 11 skipped, 0 failed, 324 total; existing CS8602 warning in QualityInspectionWorkflowTests.cs |
| `npm.cmd test -- --run` in web/buildwise-web | Final run passed: 22 test files, 178 tests, 0 failures |
| `npm.cmd run lint` in web/buildwise-web | Passed: 7 existing warnings in procurement components/pages and AuthContext.jsx |
| `npm.cmd run build` in web/buildwise-web | Passed: Vite production bundle generated |
| `git diff --check` | Passed; Git emitted existing LF/CRLF conversion notices |

The requested npm commands initially failed because PowerShell blocks `npm.ps1`; the standard `npm.cmd` launcher worked without changing execution policy. `--run` makes the test command finish rather than remain in watch mode. The first React run had one failure in the old Administrator navigation expectation; after adding Users & Roles to that expectation, all tests passed.

The 11 existing PostgreSQL/agent/live-client tests were skipped by their opt-in configuration. New HTTP tests use real JWT bearer validation and MVC authorization with isolated EF InMemory databases. No live PostgreSQL or browser visual verification is claimed.

Deactivation blocks new logins; already-issued JWTs remain valid until their existing expiry. This task does not add session revocation or a password-reset flow. The UI uses the existing design components; no external Figma file was supplied or inspected.

## Final git status --short

The following includes the pre-existing Flutter, Python, documentation and solution-file changes, alongside this task's files.

```text
 M ai/buildwise-agent-service/app/quality_agent.py
 M ai/buildwise-agent-service/app/tools.py
 M ai/buildwise-agent-service/tests/test_quality_agent.py
 M backend/BuildWise.Api.Tests/SecurityAuthorizationTests.cs
 M backend/BuildWise.Api/Controllers/MaterialRequestsController.cs
 M backend/BuildWise.Api/Data/DbSeeder.cs
 M mobile/buildwise_mobile/lib/core/api/api_client.dart
 M mobile/buildwise_mobile/lib/core/theme/app_colors.dart
 M mobile/buildwise_mobile/lib/core/theme/app_theme.dart
 M mobile/buildwise_mobile/lib/core/widgets/status_chip.dart
 M mobile/buildwise_mobile/lib/core/widgets/widgets.dart
 M mobile/buildwise_mobile/lib/features/auth/screens/login_screen.dart
 M mobile/buildwise_mobile/lib/features/deliveries/screens/delivery_list_screen.dart
 M mobile/buildwise_mobile/lib/features/deliveries/screens/receive_delivery_screen.dart
 M mobile/buildwise_mobile/lib/features/deliveries/services/delivery_service.dart
 M mobile/buildwise_mobile/lib/features/material_requests/screens/create_material_request_screen.dart
 M mobile/buildwise_mobile/lib/features/material_requests/screens/material_request_list_screen.dart
 M mobile/buildwise_mobile/lib/features/material_requests/services/material_request_service.dart
 M mobile/buildwise_mobile/lib/features/procurement/screens/po_list_screen.dart
 M mobile/buildwise_mobile/lib/features/procurement/screens/procurement_home_screen.dart
 M mobile/buildwise_mobile/lib/features/procurement/services/procurement_service.dart
 M mobile/buildwise_mobile/lib/features/quality/screens/inspection_record_screen.dart
 M mobile/buildwise_mobile/lib/features/quality/screens/pending_inspections_screen.dart
 M mobile/buildwise_mobile/lib/features/quality/screens/start_inspection_screen.dart
 M mobile/buildwise_mobile/lib/features/quality/services/quality_api_service.dart
 M mobile/buildwise_mobile/lib/main.dart
 M mobile/buildwise_mobile/pubspec.lock
 M mobile/buildwise_mobile/pubspec.yaml
 M mobile/buildwise_mobile/test/features/deliveries/delivery_receiving_workflow_test.dart
 M mobile/buildwise_mobile/test/features/quality/inspection_record_screen_test.dart
 M mobile/buildwise_mobile/test/material_request_workflow_test.dart
 M mobile/buildwise_mobile/test/role_navigation_test.dart
 M mobile/buildwise_mobile/test/widget_test.dart
 M web/buildwise-web/src/App.jsx
 M web/buildwise-web/src/App.test.jsx
 M web/buildwise-web/src/routes/routeConfig.js
?? backend/BuildWise.Api.Tests/UserManagementTests.cs
?? backend/BuildWise.Api/Controllers/UsersController.cs
?? backend/BuildWise.Api/Models/Dtos/UserManagementDtos.cs
?? backend/BuildWise.sln
?? docs/flutter_figma_implementation_report.md
?? docs/material_catalogue_user_management_report.md
?? mobile/buildwise_mobile/assets/
?? mobile/buildwise_mobile/lib/common/screens/operational_home_screen.dart
?? mobile/buildwise_mobile/lib/core/api/api_exception.dart
?? mobile/buildwise_mobile/lib/core/widgets/workspace_widgets.dart
?? mobile/buildwise_mobile/lib/features/deliveries/screens/delivery_detail_screen.dart
?? mobile/buildwise_mobile/lib/features/material_requests/screens/material_request_detail_screen.dart
?? mobile/buildwise_mobile/lib/features/procurement/models/
?? mobile/buildwise_mobile/lib/features/procurement/screens/procurement_queue_screen.dart
?? mobile/buildwise_mobile/lib/features/procurement/screens/procurement_workflow_screen.dart
?? mobile/buildwise_mobile/lib/features/procurement/screens/purchase_order_detail_screen.dart
?? mobile/buildwise_mobile/lib/features/procurement/screens/quotation_comparison_screen.dart
?? mobile/buildwise_mobile/lib/features/procurement/screens/supplier_screens.dart
?? mobile/buildwise_mobile/lib/features/quality/screens/quality_risk_screen.dart
?? mobile/buildwise_mobile/test/features/quality/quality_risk_screen_test.dart
?? mobile/buildwise_mobile/test/mobile_layout_test.dart
?? mobile/buildwise_mobile/test/procurement_workspace_test.dart
?? web/buildwise-web/src/Features/users/
```
