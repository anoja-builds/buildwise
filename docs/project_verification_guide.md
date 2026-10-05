# BuildSupply LK complete-project verification guide

Last code audit: **24 September 2026**.

This guide explains how to verify BuildSupply LK as one system: ASP.NET Core API/PostgreSQL, React, Flutter, the four internal agents, RBAC, RFQ, administration, and audit evidence. The existing `BuildWise` namespace and local URL names remain for compatibility.

## Current release decision: CONDITIONAL GO — external submission evidence remains

The integrated local system passes the complete C1 → C2 → C3 → C4 journey, RFQ/Administrator checks, RBAC, React/Flutter/backend tests, and all agent health checks. It is not yet an excellent submission until public deployment, Android device evidence, Firebase/camera integration, remaining ADRs, and final reports are completed truthfully.

### Verified in the audited workspace

| Area | Result |
|---|---|
| Backend tests | 73/73 passed; Release build passed. |
| Python agents | 27/27 tests passed across all four agent test files. |
| React web | 21/21 tests passed; production build passed; lint has 10 warnings and 0 errors. |
| Flutter checks | `flutter analyze` passed; 4/4 tests passed. |
| Live services | PostgreSQL, API, React, and all four agents are up. |
| Full business journey | `scripts/verify-full-journey.ps1` passed 14/14 checks. |
| RFQ and administration | `scripts/verify-rfq-admin.ps1` passed: RFQ create/close, Active supplier invitation, user directory, system health, 403 role boundary, and audit event. |
| Agent observability | Protected history endpoints and React Agent Workflows page expose roles, timings, validation, errors, approvals, and outcomes. |
| Live RBAC | `scripts/verify-rbac.ps1` passed 24/24 checks. |
| Database migration | `20260924153433_AddRfqAdminAudit` creates RFQ, RFQ supplier, audit tables, indexes, and quotation RFQ relationship. |
| Performance | Local concurrent evidence exists; local measurements are not a production SLA. |

### Remaining submission blockers

1. **Mobile packaging/device evidence:** install Android Studio/SDK, build a release APK, and run the four mobile workflows on an emulator/device.
2. **Firebase Cloud Messaging:** configure a real Firebase project and replace/augment the current local notification demonstration.
3. **Camera evidence:** wire image picker/camera capture and validated upload to delivery/inspection evidence.
4. **Supplier self-service:** implement the limited supplier RFQ/quotation portal or explicitly document the MVP limitation.
5. **Deployment:** deploy API, PostgreSQL, React, and agent services and record public evaluator URLs.
6. **Documentation:** add remaining ADRs, consolidated report, individual contribution sections, AI usage declarations, performance/deployment reports, diagrams, GitHub issue/PR evidence, and demonstration video.
7. **External AI key:** the supplied Gemini credential returned HTTP 401 and is not configured; deterministic/safe-fallback mode remains active.

## 1. Prerequisites

Install and verify:

```powershell
git --version
dotnet --version                 # project requires .NET 8
node --version
npm --version
python --version
flutter --version
psql --version
docker --version                 # optional if PostgreSQL is installed locally
```

Python dependencies:

```powershell
python -m pip install -r backend/agent_service/requirements.txt
```

Web dependencies:

```powershell
Push-Location web/buildwise-web
npm install
Pop-Location
```

Flutter dependencies:

```powershell
Push-Location mobile/buildwise_mobile
flutter pub get
Pop-Location
```

For Android packaging, install Android Studio/Android SDK, then set `ANDROID_HOME` to the SDK directory and accept the Android licenses. Confirm with `flutter doctor -v`.

## 2. Start the complete stack

The existing `scripts/start-dev.ps1` starts PostgreSQL, the API, all four Python agents, and Vite. The complete live stack is verified by `scripts/check-services.ps1`.

Terminal 1, database:

```powershell
docker compose up -d postgres
```

Terminal 2, all agents (run each command in its own terminal, or use `Start-Process`):

```powershell
Push-Location backend/agent_service
python -m uvicorn quotation_agent:app --host 127.0.0.1 --port 8001
python -m uvicorn request_agent:app --host 127.0.0.1 --port 8002
python -m uvicorn delivery_agent:app --host 127.0.0.1 --port 8003
python -m uvicorn quality_agent:app --host 127.0.0.1 --port 8004
```

Terminal 3, API:

```powershell
dotnet run --project backend/BuildWise.Api --no-launch-profile --urls http://localhost:5078
```

Terminal 4, web:

```powershell
Push-Location web/buildwise-web
npm run dev
Pop-Location
```

Open:

- API Swagger: `http://localhost:5078/swagger`
- React app: `http://localhost:5173`
- Agent OpenAPI/health: ports `8001`-`8004` at `/docs` and `/health`

Check the original stack:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/check-services.ps1
```

Then check all four agents; the old checker intentionally only knows about Component 2 on 8001:

```powershell
8001,8002,8003,8004 | ForEach-Object {
  $h = Invoke-RestMethod "http://127.0.0.1:$_/health" -TimeoutSec 5
  [pscustomobject]@{ Port = $_; Status = $h.status; Service = $h.service }
}
```

Expected: four rows, all `healthy`, with services `quotation_agent`, `request_agent`, `delivery_agent`, and `quality_risk_agent`.

## 3. Run the complete automated verifier

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-project.ps1
```

The script runs the .NET, Python, React, and Flutter gates; checks PostgreSQL/API/web/agent 8001; checks all four agent health contracts; calls representative contracts for agents 8002-8004; and runs the Component 2 live smoke test. Useful narrower runs are:

```powershell
# Build/static/unit checks only; services do not need to be running
.\scripts\verify-project.ps1 -SkipLive

# CI-like run without a local Flutter SDK
.\scripts\verify-project.ps1 -SkipMobile

# Use a known seeded workflow ID for the Component 2 smoke test
.\scripts\verify-project.ps1 -WorkflowId 1
```

A zero exit code means the requested automated checks passed, not that the app is release-complete. Always complete the manual checks below. A nonzero result is a release blocker; preserve the command and full first error in the evidence log.

## 4. Demo accounts and expected HTTP behavior

The development seed creates these accounts. The shared password is `Passw0rd!`.

| Scenario role | Email | Assigned JWT role |
|---|---|---|
| Administrator | `admin@buildwise.demo` | `Administrator` |
| Site Engineer / Site Officer | `site.engineer@buildwise.demo` | `SiteEngineer`, `SiteOfficer` |
| Site Officer | `site.officer@buildwise.demo` | `SiteOfficer` |
| Procurement Officer | `procurement.officer@buildwise.demo` | `ProcurementOfficer` |
| Procurement Manager | `procurement.manager@buildwise.demo` | `ProcurementManager` |
| Quality Inspector | `quality.inspector@buildwise.demo` | `QualityInspector` |

There is no seeded `SiteManager`; add a test identity and role only through a controlled seed/fixture, never by changing production data by hand. Passwords and these accounts are development-only.

For every protected API check:

- no token => `401 Unauthorized`;
- valid token with the wrong role => `403 Forbidden`;
- allowed action => `200 OK` or `201 Created` as appropriate;
- `404` is acceptable only for an intentionally missing fixture.

Log in and inspect the returned claims:

```powershell
$body = @{ email = 'procurement.officer@buildwise.demo'; password = 'Passw0rd!' } |
  ConvertTo-Json
$login = Invoke-RestMethod -Method Post -Uri http://localhost:5078/api/auth/login `
  -ContentType application/json -Body $body
$token = $login.token
Invoke-RestMethod -Uri http://localhost:5078/api/auth/me `
  -Headers @{ Authorization = "Bearer $token" }
```

Do not paste JWTs into screenshots or commit them. Store evidence as redacted response/status snapshots.

### RBAC acceptance matrix

Use this table as the **target contract** from the supplied specification. `View` means read-only, `Own` means only records owned by the current site user, and a dash means no access. API authorization is authoritative; hidden or disabled UI controls do not pass by themselves.

| Feature / workspace | Platform | Site Engineer / Officer | Procurement Officer | Procurement / Site Manager | Quality Inspector | Administrator |
| --- | --- | --- | --- | --- | --- | --- |
| Authentication / own profile | Web & mobile | Own | Own | Own | Own | Full user and role administration |
| C1 create material request | Mobile | Full | - | - | - | - |
| C1 review / approve request | Web | Own status | Own status | Approve / reject / revise | - | View |
| C2 supplier directory CRUD | Web | - | Full | View | - | Full |
| C2 quotations and AI analysis | Web | - | Full | View analysis | - | View |
| C2 decision and PO creation | Web | PO status | Proposal view | Approve / reject / revise | - | View |
| C3 record delivery receiving | Mobile | Full | - | View history | View | View |
| C3 delivery discrepancy monitoring | Web | Status view | Status view | Status view | Status view | View |
| C4 quality inspection | Mobile | - | - | Results view | Full | View |
| C4 NCR management | Web & mobile | Status view | - | Active NCR view | Create and manage | View |

Systematically test both the positive and negative side of every cell:

1. Log in with each seeded account and confirm `/api/auth/me` contains the expected role claim.
2. Exercise the allowed action once and confirm `200/201`, the persisted row/event, and the expected UI.
3. Exercise the same action with each disallowed role and confirm `403`; omit the token once and confirm `401`.
4. Re-query the database after a denied mutation to prove no row changed.
5. For `View` cells, confirm the response excludes secrets and fields the role must not see. Navigation may be hidden, but the API must enforce the same rule.
6. Exercise both `ProcurementManager` and `SiteManager` for manager-only endpoints. The seeded `SiteManager` account is included in the idempotent development seed; production role provisioning remains an administrator concern.

The live RBAC gate currently passes 24/24 checks, including the protected agent history endpoint. A browser/device walkthrough is still required before submission.

## 5. Database and API checks

Check the container and apply pending migrations from the repository root:

```powershell
docker compose up -d postgres
dotnet ef database update --project backend/BuildWise.Api
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/check-services.ps1
```

Verify manually in Swagger at `http://localhost:5078/swagger`:

1. Login succeeds and `/api/auth/me` returns the expected role claims.
2. Unauthenticated access to a protected endpoint returns `401`.
3. A wrong-role write returns `403` and creates no database row.
4. A valid material request has at least one item, a future required date, and an active project.
5. Manager approval/rejection updates request status and audit history.
6. A quotation from an inactive supplier or expired quote is excluded.
7. The Component 2 smoke test passes 12 assertions:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File scripts/smoke-test.ps1 -WorkflowId 1
```

Run backend tests after stopping the API if a DLL is locked:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/stop-dev.ps1
dotnet test backend/BuildWise.Api.Tests/BuildWise.Api.Tests.csproj --nologo
```

The complete local scenario is now covered by `scripts/verify-full-journey.ps1`; run it separately because it writes fresh demo records:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-full-journey.ps1
```

### Four-component business acceptance

| Component | End-to-end evidence required | Current assessment |
| --- | --- | --- |
| C1 Material Request & Approval | Site user creates a valid request for an active project/future date; manager approves, rejects, or requests revision; site user sees only its status; request/approval history and API RBAC persist correctly | **Live journey passed** for C1; mobile device walkthrough still required |
| C2 Supplier, Quotation & Procurement | Officer manages suppliers and records eligible/expired/suspended quotes; agent 8001 recommends; API independently validates; manager decides; selected quote and PO persist with correct totals | **Live journey passed**; approval and PO are transactionally persisted |
| C3 Delivery & Material Receiving | Confirmed PO loads; site user records full/short/damaged receipt; status is correct; discrepancy history is visible only to allowed roles; agent 8003 result is persisted when integration is added | **Live journey passed**; mobile device walkthrough still required |
| C4 Quality Inspection & NCR | Inspector validates quantities, completes an inspection, creates a unique NCR for rejected material, updates/closes it, and removes it from active results; agent 8004 recommendation is persisted | **Live journey passed** for creation/NCR; mobile device and close/resolve walkthrough still required |

The project passes only when one traceable fixture can be followed through all four rows: C1 request -> C2 approved PO -> C3 delivery -> C4 inspection/NCR, with every transition authorized, persisted, and visible on the correct platform.

## 6. React web application

### Start and build checks

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/start-dev.ps1
Set-Location web/buildwise-web
npm install
npm run lint
npm test -- --run
npm run build
```

Open `http://127.0.0.1:5173`. Confirm the login screen, seeded-account login, session restoration after refresh, and logout. For every later web check, keep DevTools open on **Network** and record the request, HTTP status, and redacted response.

### Web scenario walkthrough

1. Sign in as Procurement Officer. Open **Suppliers**; create, edit, filter, suspend/reactivate, and inspect one supplier. A mutation from another role must return `403` and must not change the row.
2. Open **Material Requests**, choose an approved request, and record quotations for at least two eligible suppliers. Include one expired quotation and one suspended supplier. Confirm the comparison view ranks eligible full-coverage quotes by total and never awards a partial-only quote.
3. Start agent 8001 and run `scripts/smoke-test.ps1 -WorkflowId 1`. The agent response must pass the API's independent schema and database-rule validation before approval.
4. Sign in as Procurement Manager. Approve the analyzed recommendation, revise it, and reject a second fixture where useful. Confirm the decision, comments, workflow audit state, selected quotation, and purchase-order totals agree in PostgreSQL.
5. Open **Purchase Orders**, verify detail/items/total, then exercise the implemented status update. Do not assume a missing status transition is valid: compare the UI action with the API contract.
6. Open **Deliveries** and **Quality Inspections** as the intended roles. Verify lists, detail, valid writes, `400` validation errors, and NCR creation/closure for rejected quantities.
7. Test browser reload, empty states, API failure messages, long supplier/material names, and narrow viewport layout.

### Web target-route and UI checks

The supplied specification names URL routes such as `/suppliers`, `/quotations`, `/procurement`, `/material-requests`, `/purchase-orders`, and `/quality-inspections`. BuildWise now uses URL-backed navigation and a role-aware `ProtectedRoute`; verify deep links in the browser before submission. Navigation hiding is treated as a usability control only—the API remains the security boundary.

## 7. Flutter mobile application

### Static, unit, build, and device checks

```powershell
Set-Location mobile/buildwise_mobile
flutter doctor
flutter pub get
flutter analyze
flutter test
flutter devices
```

Run at least one real Android emulator/device and, when the target includes it, one iOS device/simulator. Build a non-debug artifact as packaging evidence:

```powershell
flutter build apk --release --dart-define=API_BASE_URL=http://10.0.2.2:5078/api
# Physical Android/iOS device: use the computer's LAN address, for example
# flutter run --dart-define=API_BASE_URL=http://192.168.1.20:5078/api
```

`10.0.2.2` reaches the host from the standard Android emulator; it is not a physical-device address. Before device testing, add Internet permission to the main Android manifest. For iOS local HTTP, configure an appropriately scoped ATS development exception and use HTTPS in staging/production.

### Mobile scenario walkthrough

1. Launch with no saved session; verify the login screen. Sign in, kill/background the app, reopen it, and confirm the session is restored from secure storage. Sign out and confirm protected data is removed.
2. Exercise keyboard behavior, rotation, small-phone and tablet layouts, offline/API-timeout messaging, loading indicators, empty lists, and server validation errors. Do not accept a UI that stays on an infinite spinner.
3. **Create Request:** enter project, future required date, reason, at least one item, quantity, unit, and notes. Submit as Site Engineer/Site Officer. Verify the request appears in the site user's list and no Site Manager/Quality Inspector can create one.
4. **My Requests:** show only the current user's requests and allowed statuses. Procurement Officer and Quality Inspector must not gain supplier prices, quotation details, or other users' private requests.
5. **Receiving:** load confirmed purchase orders, enter ordered/received/damaged quantities, submit, and verify a clean receipt is `Received`; shortage or damage is `DiscrepancyReported`. A receiving user without a confirmed PO must receive `400` and create no delivery.
6. **Inspection/NCR:** as Quality Inspector, inspect a delivery, accept/reject quantities, and submit. Verify `accepted + rejected <= inspected`; a rejection creates a unique NCR with correct severity/status. Close the NCR and confirm it leaves the active list.
7. Repeat permission checks using Site Engineer, Procurement Officer/Manager, Quality Inspector, and Administrator tokens. Backend `401`/`403` must be reflected as safe UI behavior; hidden controls alone are not authorization.

**Current result:** the app includes the operational Create Request, My Requests, Delivery Receiving, and Quality Inspection flows plus secure-session support and notifications. Static checks and the API journey pass; Android/iOS device execution and a release APK remain required before submission.

## 8. All four AI-agent services

Agents 1–4 are separate Python processes. Start each from `backend/agent_service` in its own terminal (or background process) after installing `requirements.txt`:

```powershell
python -m uvicorn quotation_agent:app --host 127.0.0.1 --port 8001
python -m uvicorn request_agent:app --host 127.0.0.1 --port 8002
python -m uvicorn delivery_agent:app --host 127.0.0.1 --port 8003
python -m uvicorn quality_agent:app --host 127.0.0.1 --port 8004
```

`scripts/start-dev.ps1` starts all four agent processes. `scripts/verify-project.ps1` checks their health and sample analysis contracts, and `scripts/verify-full-journey.ps1` proves the API-to-agent integration.

### Automated and health checks

```powershell
Set-Location backend/agent_service
python -m pytest -q
Set-Location ../..
$ports = 8001,8002,8003,8004
foreach ($port in $ports) {
  Invoke-RestMethod "http://127.0.0.1:$port/health" |
    Select-Object status,service,agent,llm_rationale_enabled
}
```

Expected service identities are `quotation_agent`, `request_agent`, `delivery_agent`, and `quality_risk_agent`. Then call the three standalone analysis contracts used by the verifier:

```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8002/api/agent/analyze-request `
  -ContentType application/json -Body (@{
    request_id=1; project_name='Verification'; required_date='2030-01-01'
    reason='urgent foundation work'; items_count=3
  } | ConvertTo-Json)

Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8003/api/agent/analyze-discrepancy `
  -ContentType application/json -Body (@{
    purchase_order_id=1; material_name='Cement'; ordered_qty=100
    received_qty=90; damaged_qty=2
  } | ConvertTo-Json)

Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8004/api/agent/analyze-quality-risk `
  -ContentType application/json -Body (@{
    delivery_id=1; items=@(@{
      material_name='Cement'; inspected_qty=100; accepted_qty=70
      rejected_qty=30; major_defects=3; critical_defects=0
      rejection_reason='Cracked bags'
    })
  } | ConvertTo-Json -Depth 8)
```

Expected highlights are `HIGH_URGENCY`, shortage+damage detected, and an NCR-required quality response respectively. These deterministic tests prove agent contract behavior; they do **not** prove API integration, persistence, authorization, or optional LLM rationale.

### Agent integration acceptance

| Agent | Standalone check | Required system proof | Current state |
| --- | --- | --- | --- |
| 1 Request analysis (8002) | Tests and sample contract pass | Manager-triggered API analysis appears in workflow/audit and influences request approval | **Integrated; live journey passed** |
| 2 Quotation analysis (8001) | Tests/health pass; Component 2 smoke test | API receives recommendation, independently revalidates it, manager decides, PO persists | **Integrated; live journey passed** |
| 3 Delivery discrepancy (8003) | Tests and sample contract pass | Receiving/API workflow calls 8003 and persists its result | **Integrated; live journey passed** |
| 4 Quality risk/NCR (8004) | Tests and sample contract pass | Inspection/NCR API calls 8004 and persists/reuses its recommendation | **Integrated; live journey passed** |

A healthy port is therefore necessary but not sufficient. A release PASS requires a traceable ID/timestamp proving the API invoked the correct agent, validated the response, saved the outcome, and displayed it to an authorized user.
