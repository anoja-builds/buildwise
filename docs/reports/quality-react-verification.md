# React Quality Management verification

Verified 24 September 2026 on `feature/quality-inspection`. No commit, push, branch switch, or merge was performed.

Follow-up: [live browser smoke-test results](quality-browser-smoke.md) now provide real browser/API/PostgreSQL evidence: 21 checks passed, one narrow-layout defect found, and real agent execution remains unverified. The browser limitation below describes the earlier implementation run.

## Implemented

- Existing Quality Inspections and Non-Conformances sidebar entries open the new screens inside the current application layout and authentication provider.
- Inspection history and detail show API records, delivery references, inspector names (with user-ID fallback), dates, status, decisions, conditions, received/accepted/rejected quantities, and remarks.
- NCR creation starts from a completed inspection item with positive rejected quantity. The reviewer explicitly supplies and submits the issue, severity, and optional corrective action.
- NCR list/detail, corrective-action updates, resolution, and closure use the existing endpoints and permitted transitions. Resolved/closed records cannot be edited through the UI. Unsaved corrective-action changes block the resolution button until saved.
- Loading, empty, authorization, validation, network, conflict, and session-expiration states use existing shared components. Pending actions block repeated clicks; obsolete reads and NCR-create responses cannot navigate back after leaving a view.
- Quality Risk Agent analysis is an explicit human action available on completed inspections. Saved workflows can be retrieved by workflow ID through the existing GET endpoint. The panel shows risk flags, structured item recommendations, corrective-action suggestions, evidence references, execution steps, validation, trace data, and failures.
- HTTP 502 responses containing persisted failed workflows retain their audit details. Unvalidated structured output is labelled audit evidence and is not presented as a successful recommendation. The panel never creates an NCR, records corrective action, approves a workflow, or changes an inspection decision.

## Backend changes and procurement discrepancy

Added `GET /api/inspections`, projecting EF Core records to `InspectionHistoryDto` without tracking or writes. Results are ordered by inspection date and ID descending. Authorization remains `QualityInspector,Administrator`. Added nullable `inspectorName` to the existing detail response; existing Flutter fields are preserved. No migration is required.

The approved ERD and current procurement creation code establish:

- Supplier: delivery -> purchase order -> quotation -> supplier.
- Material: delivery item -> purchase-order item -> quotation item -> material-request item -> material.

The original quality evidence service read only direct purchase-order supplier/material fields. Current procurement creates quotation-item links without populating the legacy direct material field, so material evidence could be missing. This discrepancy was reported before editing the quality service. Its evidence queries now prefer quotation provenance, including supplier history, NCR history, discrepancy deliveries, and delivery issues. Direct fields remain a compatibility path only for records without the respective quotation link. Conflicting direct fields cannot override quotation provenance. Procurement code, entity mappings, and the approved ERD are unchanged.

## Actual verification results

| Check | Result |
| --- | --- |
| Phase A `npm.cmd test -- --run` | 39 passed before Phase B began |
| Phase A `npm.cmd run build` | Passed |
| Final `npm.cmd test -- --run` | **54 passed**, 8 test files |
| Final `npm.cmd run build` | Passed, 58 modules transformed |
| `npm.cmd run lint` | Exit 0; 7 existing warnings in procurement/shared authentication, none in quality code |
| `dotnet test backend/BuildWise.Api.Tests --no-restore` with isolated PostgreSQL | **112 passed, 0 failed, 0 skipped** |
| `dotnet run --project backend/BuildWise.QualityAgent.Tests --no-restore` | **17 offline validator checks passed** |
| `git diff --check` | Passed |

The normal PowerShell `npm` wrapper was blocked by execution policy; the equivalent `npm.cmd` commands ran successfully. Dependencies were installed using the existing lockfile; package manifests and lockfile were not changed. npm reported that installed Node 24.13.1 is below the locked jsdom 30.0.1 engine requirement (`^22.22.2 || ^24.15.0 || >=26.0.0`). Tests and build nevertheless passed; use a matching Node release in CI.

Backend build warnings are existing nullable-reference warnings in procurement/material-request code. They were not changed as part of this work.

### PostgreSQL isolation

The first default-configuration attempt failed PostgreSQL authentication. Inspection found an earlier temporary cluster, but its bootstrap login role was unknown. It was returned to its original stopped state. A fresh temporary PostgreSQL 17 cluster was then initialized for these tests, bound to loopback on a temporary port, and stopped after testing.

The existing integration fixture creates a unique `buildwise_inspection_test_<guid>` database per test instance, connects its test HTTP server to that database, and drops only that generated database during teardown. Six PostgreSQL cases passed: duplicate prevention and concurrent starts for Received/DiscrepancyReported deliveries, plus two new end-to-end API cases covering inspection history, canonical quotation evidence, NCR create/list/detail, corrective action, resolve, close, and invalid transitions. No `buildwise_dev` or shared application database was modified. No PostgreSQL tests remain blocked.

These are real ASP.NET controller/service/JWT requests through TestServer with a real PostgreSQL provider. They are not a browser-to-deployed-API test or a live Gemini success test. React component tests use explicit test fixtures; production quality code has no mock fallback.

## Shared React / Flutter contract

- React uses the existing `AuthContext` and `authApi` session, attaching its JWT as `Authorization: Bearer ...`. A 401 clears that same session and dispatches the existing `buildwise:unauthorized` event.
- Flutter's existing `core/api/api_client.dart` uses the same bearer-token contract and shared auth service. Its quality service calls the same ASP.NET inspection and quality-agent routes.
- Quality controllers enforce `QualityInspector` or `Administrator` for both clients. React additionally gates the feature before fetching data. Flutter's current shell displays its Quality navigation entry without a client role check; backend authorization still applies. No Flutter code was changed.
- Neither client connects directly to PostgreSQL. Both use the API's single `ApplicationDbContext` / `DefaultConnection`. Point React and Flutter at the same API deployment to use the same database.
- Configure React using `VITE_API_BASE_URL`, including `/api`, and Flutter using `--dart-define=API_BASE_URL=...`. Existing defaults remain localhost:5078 for React and the Android emulator host alias 10.0.2.2:5078 for Flutter. No temporary testing port was added to shared defaults.

## Remaining limits / review notes

- Successful live Gemini analysis was not run. Provider credentials, the internal agent service, and deployment configuration still need a real-service smoke test. Success/failure rendering is covered by React tests; schema and advisory restrictions are covered by backend validator tests.
- Browser visual verification and a new simultaneous React/Flutter device-to-API run were not performed. The existing Flutter live test result supplied by the user is not claimed as a new result.
- History and existing NCR listing return full arrays. Server-side pagination can be coordinated later for large datasets; this change keeps the new read endpoint minimal.
- There is no per-inspection workflow-history endpoint. Existing workflow retrieval requires its saved ID; the UI does not invent an endpoint or an approval operation.
- NCR creation has no server idempotency key. The UI prevents repeated clicks while pending and warns that an ambiguous network failure may already have reached the server; reviewers should refresh the NCR list before resubmitting.
- Navigation preserves the existing state-based shell. React Router/deep links/browser-history support remains a team-wide decision if required by assignment criteria.

## Changed-file inventory

| File | Change |
| --- | --- |
| [App.jsx](../../web/buildwise-web/src/App.jsx) | Connect existing quality sidebar selections |
| [QualityApp.jsx](../../web/buildwise-web/src/Features/quality/QualityApp.jsx) | Role-gated inspection and NCR screens |
| [QualityRiskPanel.jsx](../../web/buildwise-web/src/Features/quality/QualityRiskPanel.jsx) | Advisory workflow execution and results |
| [quality.css](../../web/buildwise-web/src/Features/quality/quality.css) | Scoped layout additions using shared components |
| [qualityApi.js](../../web/buildwise-web/src/Features/quality/services/qualityApi.js) | Real authenticated quality API requests |
| [QualityApp.test.jsx](../../web/buildwise-web/src/Features/quality/QualityApp.test.jsx) | Screen, validation, transition, and authorization tests |
| [QualityNavigation.test.jsx](../../web/buildwise-web/src/Features/quality/QualityNavigation.test.jsx) | Shared shell/sidebar/login integration tests |
| [QualityRiskPanel.test.jsx](../../web/buildwise-web/src/Features/quality/QualityRiskPanel.test.jsx) | Advisory output, failures, retrieval, duplicate-click tests |
| [qualityApi.test.js](../../web/buildwise-web/src/Features/quality/services/qualityApi.test.js) | Routes, environment base URL, JWT, 401/403/validation/failure tests |
| [InspectionsController.cs](../../backend/BuildWise.Api/Controllers/InspectionsController.cs) | Authorized history endpoint |
| [QualityInspectionDtos.cs](../../backend/BuildWise.Api/Models/Dtos/QualityInspectionDtos.cs) | History DTO and additive inspector name |
| [QualityInspectionService.cs](../../backend/BuildWise.Api/Services/QualityInspectionService.cs) | No-tracking history projection and inspector-name lookup |
| [QualityRiskEvidenceService.cs](../../backend/BuildWise.Api/Services/QualityRiskEvidenceService.cs) | Canonical quotation-based evidence relationships |
| [QualityAuthorizationTests.cs](../../backend/BuildWise.Api.Tests/QualityAuthorizationTests.cs) | History permissions, ordering, fields, empty result, no writes |
| [QualityRiskEvidenceTests.cs](../../backend/BuildWise.Api.Tests/QualityRiskEvidenceTests.cs) | Canonical provenance overrides stale direct fields |
| [InspectionDuplicatePostgresTests.cs](../../backend/BuildWise.Api.Tests/InspectionDuplicatePostgresTests.cs) | Isolated PostgreSQL history/evidence/NCR lifecycle coverage |
| [quality-react-verification.md](quality-react-verification.md) | This verification report and PR draft |

## Pull-request draft

**Title:** Complete React quality inspection history, NCR management, and advisory risk analysis

**Base:** `main`
**Head:** `feature/quality-inspection`

Quality sidebar entries previously displayed placeholders, and no inspection-history read endpoint existed. This change adds authenticated inspection history/detail and human-controlled NCR creation, corrective actions, resolution, and closure within the existing React shell. It integrates the existing quality-agent analysis and workflow retrieval endpoints, retaining failed-run audit details and requiring successful backend validation before presenting advisory recommendations.

The backend adds a read-only history DTO/endpoint and corrects quality evidence queries to follow current quotation relationships, preserving legacy records without quotation links. No procurement, shared authentication, schema/migration, or ERD changes are included.

Validation: 54 React tests, production build, 112 backend tests including isolated PostgreSQL integration/concurrency cases, and 17 offline agent-validator checks pass. Lint has only existing warnings. Live provider success and browser/device smoke testing remain to be verified; see this report for scope and reproduction details.

**Readiness:** Ready for code review. Complete deployment/provider and browser/device smoke checks before claiming full end-to-end integration. Changes are uncommitted; the user will perform Git operations manually.
