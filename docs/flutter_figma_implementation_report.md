# BuildWise mobile implementation report

Date: 30 September 2026  
Branch: `ui/final-figma-react`  
Figma: [BuildWise Mobile, page 39:2](https://www.figma.com/design/JuykyQM6sTdSzM4b1G7qWY/Untitled?node-id=39-2)

## A. Implementation result

**PARTIAL — implementation and automated checks completed; live integration/device acceptance remains unverified.**

The existing Flutter application now covers all four components, including real procurement workspaces for both procurement roles. The original API client, secure JWT storage, material-request services, receiving flow, inspection safeguards, and procurement notifications are retained. No backend, database, React, or Python changes were made.

All 32 requested Figma screen nodes were inspected. The shared amber/warm-neutral theme, Geist font, original Figma logo, compact headers, bordered cards, status badges, and dark role navigation are implemented. Representative screens were rendered and inspected; this is not a claim that every screen has passed a pixel-by-pixel visual acceptance review.

## B. Figma screen coverage

Paths below are relative to `mobile/buildwise_mobile/lib/`. Variants reuse a single screen driven by API state.

| Node | Screen | Dart file | Result |
|---|---|---|---|
| 39:10 | mobile-login | `features/auth/screens/login_screen.dart` | Existing-restyled; demo credentials removed |
| 39:46 | mobile-site-engineer-home | `common/screens/operational_home_screen.dart` — SiteEngineerHomeScreen | Implemented; Requests header home action |
| 39:123 | mobile-quality-inspector-home | `common/screens/operational_home_screen.dart` — QualityHomeScreen | Implemented; Quality header home action |
| 39:180 | mobile-material-requests-list | `features/material_requests/screens/material_request_list_screen.dart` | Existing-restyled; search, filters, refresh |
| 39:269 | mobile-create-material-request | `features/material_requests/screens/create_material_request_screen.dart` | Existing-restyled; date, multiple materials, draft/submit |
| 39:325 | mobile-material-request-status | `features/material_requests/screens/material_request_detail_screen.dart` | Implemented; retains MaterialRequestProcurementView |
| 39:555 | mobile-deliveries-list | `features/deliveries/screens/delivery_list_screen.dart` | Existing-restyled; expected/history |
| 39:626 | mobile-delivery-detail | `features/deliveries/screens/delivery_detail_screen.dart` | Implemented |
| 39:708 | mobile-receive-delivery | `features/deliveries/screens/receive_delivery_screen.dart` | Existing-restyled; quantity validation and lifecycle fixes |
| 39:775 | mobile-delivery-receipt-result | `features/deliveries/screens/receive_delivery_screen.dart` — saved receipt state | Existing-restyled; only actual returned outcome |
| 39:858 | mobile-delivery-discrepancy-agent | `features/deliveries/screens/receive_delivery_screen.dart` — discrepancy section | Existing-restyled; API analysis, advisory banner |
| 39:935 | mobile-pending-inspections | `features/quality/screens/pending_inspections_screen.dart` | Existing-restyled; pending/in-progress/completed tabs |
| 39:1082 | mobile-start-inspection | `features/quality/screens/start_inspection_screen.dart` | Existing-restyled; original start flow retained |
| 39:1146 | mobile-complete-inspection | `features/quality/screens/inspection_record_screen.dart` | Existing-restyled; explicit completion confirmation |
| 39:1228 | mobile-inspection-detail | `features/quality/screens/inspection_record_screen.dart`, `quality_risk_screen.dart` | Existing-restyled + advisory risk view |
| 39:1297 | mobile-admin-orders | `features/procurement/screens/po_list_screen.dart` | Existing-restyled; shared secured PO list |
| 39:1392 | mobile-procurement-web-portal | `features/procurement/screens/procurement_workflow_screen.dart` — WebPortalScreen | Implemented as human-review handoff |
| 39:1415 | mobile-shared-states | `core/widgets/workspace_widgets.dart`, existing loading/empty/error/confirm widgets | Implemented across operational screens |
| 72:70 | mobile-procurement-officer-home | `features/procurement/screens/procurement_home_screen.dart` | Existing screen extended with API overview |
| 72:118 | mobile-procurement-manager-home | `features/procurement/screens/procurement_home_screen.dart` — manager mode | Implemented |
| 72:185 | mobile-suppliers-list | `features/procurement/screens/supplier_screens.dart` — SuppliersScreen | Implemented |
| 72:245 | mobile-supplier-detail | `features/procurement/screens/supplier_screens.dart` — SupplierDetailScreen | Implemented |
| 72:320 | mobile-procurement-queue | `features/procurement/screens/procurement_queue_screen.dart` | Implemented; approved requests |
| 72:377 | mobile-quotation-comparison | `features/procurement/screens/quotation_comparison_screen.dart` | Implemented; mobile cards and server totals |
| 72:431 | mobile-ai-procurement-recommendation | `features/procurement/screens/procurement_workflow_screen.dart` | Implemented |
| 72:491 | mobile-procurement-recommendation-status | `features/procurement/screens/procurement_workflow_screen.dart` — statusOnly | Implemented |
| 72:547 | mobile-procurement-orders | `features/procurement/screens/po_list_screen.dart` | Existing-restyled |
| 72:638 | mobile-purchase-order-detail | `features/procurement/screens/purchase_order_detail_screen.dart` | Implemented |
| 79:493 | mobile-ai-procurement-recommendation-manager | `features/procurement/screens/procurement_workflow_screen.dart` — manager mode | Implemented; no final decision controls |
| 79:555 | mobile-procurement-recommendation-status-rejected | `features/procurement/screens/procurement_workflow_screen.dart` | Implemented from server approvalStatus |
| 79:612 | mobile-procurement-recommendation-status-revision-requested | `features/procurement/screens/procurement_workflow_screen.dart` | Implemented from server approvalStatus |
| 79:669 | mobile-procurement-shared-states | `core/widgets/workspace_widgets.dart`, procurement views | Implemented; loading/empty/error/retry/access states |

The original list and form screens were extended rather than duplicated. Receipt/result and inspection/edit/detail variants share their existing screens. The site and quality overview screens are reached through header home actions, preserving the requested bottom destinations.

## C. Role navigation

| Role | Bottom destinations | Additional access |
|---|---|---|
| SiteEngineer | Requests, Deliveries | Site overview; create/draft/submit; receive eligible delivery |
| ProcurementOfficer | Procurement, Suppliers, Orders | Queue, comparison, start analysis, recommendation/status, supplier/PO details |
| ProcurementManager | Procurement, Suppliers, Orders | Recommendations, status refresh, web review handoff; no final approval controls |
| QualityInspector | Deliveries, Quality | Delivery read views; quality overview, NCR counts, inspection history, completion, risk assistance |
| Administrator | Requests, Procurement, Suppliers, Orders, More | More → Deliveries / Quality; receiving enabled; manager procurement view |

Multiple roles receive the union of permitted destinations. Unknown/unassigned roles receive an empty workspace with sign-out. Hidden UI never grants permission: ASP.NET still authorizes every request and validates each transition.

## D. API integration

All paths below use the existing `/api` base and authenticated bearer JWT. `API_BASE_URL` remains configurable using `--dart-define`.

| Component / service | Existing ASP.NET endpoints | Consumers |
|---|---|---|
| Authentication — AuthService / ApiClient | `POST /auth/login` | Login; secure token/user storage; all services attach JWT |
| C1 — MaterialRequestService | `GET /materialrequests`, `GET /materialrequests/{id}`, `GET /materialrequests/options` | List, detail, real project/material choices, site overview |
| C1 — MaterialRequestService | `POST /materialrequests`, `POST /materialrequests/{id}/submit` | Create, save draft, submit draft; actor comes from JWT |
| C1/C2 — ProcurementStatusService | `GET /material-requests/{id}/procurement-status` | Site-visible status and existing notification flow |
| C2 — ProcurementService | `GET /suppliers?search&status&page&pageSize`, `GET /suppliers/{id}` | Supplier directory/detail, quotation history |
| C2 — ProcurementService | `GET /materialrequests`, `GET /material-requests/{id}/quotations`, `GET /material-requests/{id}/quotations/compare` | Overview, approved queue, comparison, recommendation totals |
| C2 — ProcurementService | `POST /material-requests/{id}/procurement-workflow`, `GET /material-requests/{id}/procurement-workflow` | Start analysis, latest recommendation, human decision status, refresh/resume |
| C2 — ProcurementService | `GET /procurement-workflow/{id}`, `GET /procurement-workflow/{id}/history` | Service methods available; current UI uses the latest response including steps |
| C2 — ProcurementService | `GET /purchase-orders?page&pageSize`, `GET /purchase-orders/{id}` | All PO pages, detail/lines, supplier-linked orders, overview counts |
| C3 — DeliveryService | `GET /deliveries/expected`, `GET /deliveries/history`, `GET /deliveries/{id}` | Expected/history lists, detail, site overview |
| C3 — DeliveryService | `POST /deliveries/{id}/receive` | Receives actual quantities; displays returned receipt status |
| C3 — DeliveryService | `POST /deliveries/{id}/discrepancy-analysis` | Actual Agent 3 analysis after a discrepancy receipt |
| C3 — DeliveryService | `GET /deliveries/{id}/discrepancy-history`, `POST /deliveries/{id}/evidence` | Existing service methods retained; no fabricated evidence or new file-upload UI |
| C4 — QualityApiService | `GET /inspections/pending-deliveries`, `GET /inspections`, `GET /inspections/{id}` | Pending/history tabs, overview, saved record and editability checks |
| C4 — QualityApiService | `POST /inspections`, `POST /inspections/{id}/complete` | Start and confirmed completion; server-derived inspector identity |
| C4 — QualityApiService | `POST /quality-risk-agent/inspections/{id}/analyse` | Completed-inspection advisory risk view |
| C4 — QualityApiService | `GET /quality-risk-agent/workflows/{id}` | Existing workflow-read method retained |
| C4 — QualityApiService | `GET /non-conformances` | Real open-NCR count and recent descriptions on quality overview |

Search on the request list filters the fetched API records locally. Supplier search is API-backed. Quotations and PO monetary totals come directly from the server. Comparison coverage uses the API's per-offer flags. Supplier and PO pagination fetch all reported pages and reject incomplete page sequences.

401 responses invalidate the stored session and return to sign-in with an expiry message. 403 responses remain access-denied errors. Refresh is supported manually, by pull-to-refresh in shared API views, and on app resume. This provides the Flutter → ASP.NET → AI → React human decision → ASP.NET → refreshed Flutter path without adding a mobile decision endpoint.

## E. AI safety / human approval

- Flutter → Python direct calls: **0**.
- Final procurement approval/rejection/revision in Flutter: **NO**.
- Agent recommendations remain advisory: **YES**.
- Mobile automatic supplier selection / PO creation / NCR creation: **NO**.
- Public recommendation summaries and named workflow steps are displayed; raw agent traces and hidden reasoning are not rendered.
- Quality recommendations are shown only for a completed analysis workflow. Inspection decisions remain independent human submissions.
- Completed inspections remain read only even when opened directly. Reinspection eligibility is obtained from the existing server-filtered pending-delivery endpoint.
- Set `WEB_PORTAL_URL` at build time to show/copy your deployed management portal link. No deployment URL is invented. The handoff explains how to return and refresh after review.

## F. Hardcode check

**NONE for runtime business records in the operational app.**

Removed `_demoMaterialRequestId`, the seeded login accounts, and the plaintext demo password from runtime code. User, request, project, material, supplier, workflow, and PO IDs come from API records or selected navigation arguments. Figma sample suppliers, request numbers, totals, and dashboard counts were not copied into runtime data.

Test fixtures contain explicit sample IDs and records. The pre-existing opt-in integration test retains its isolated test-host credentials; those are outside `lib/` and are not shipped as app accounts. Static headings, enum display labels, API defaults, and the suggested required date are presentation/configuration values.

## G. Backend changes

**NONE.** No backend, migrations, schema, React, Python, or GitHub Actions files were changed. `backend/BuildWise.sln` remains the pre-existing unrelated untracked file, untouched and unstaged.

## H. Test results

Final command results are recorded below. Logs and screenshots are local ignored build artifacts under `mobile/buildwise_mobile/build/`.

| Command | Actual result |
|---|---|
| `flutter analyze` | PASS — no issues found |
| `flutter test --reporter expanded` | PASS — **143 passed, 1 skipped, 0 failed** |
| `flutter test test/mobile_layout_test.dart --dart-define=EXPORT_VISUAL_REVIEW=true --reporter expanded` | PASS — **21 passed**, seven rendered screenshot exports |
| `flutter devices` | Windows, Chrome, Edge only; **0 Android/iOS devices** |
| `flutter build apk --debug` | CANCELLED — prolonged Gradle dependency download; no APK produced |
| `flutter build web --debug` | PASS — compiled `build/web` |
| `git diff --check` | PASS — no whitespace errors; Git printed line-ending normalization notices |

The full-suite total already includes the layout tests; do not add their 21 passes again. Procurement decision responsive checks add 12 cases within the same full-suite count. No emulator/device smoke workflow is claimed. The one skipped test is the pre-existing opt-in staged live procurement integration test.

Local evidence: [analysis log](../mobile/buildwise_mobile/build/final-analyze.log), [full test log](../mobile/buildwise_mobile/build/flutter-test.log), [web build log](../mobile/buildwise_mobile/build/web-debug-build.log), [Android attempt log](../mobile/buildwise_mobile/build/debug-build.log). Screenshots: [login](../mobile/buildwise_mobile/build/visual-review/login.png), [requests](../mobile/buildwise_mobile/build/visual-review/requests.png), [request form](../mobile/buildwise_mobile/build/visual-review/create-request.png), [procurement](../mobile/buildwise_mobile/build/visual-review/procurement.png), [deliveries](../mobile/buildwise_mobile/build/visual-review/deliveries.png), [receiving](../mobile/buildwise_mobile/build/visual-review/receive.png), [quality](../mobile/buildwise_mobile/build/visual-review/quality.png).

Coverage includes all five role destinations, unknown roles, administrator More navigation, multi-role union, supplier mapping/navigation, server quotation coverage/totals, workflow launch identity, missing workflow, all human decision states, PO detail, actual decision refresh without POST, multiple-material draft creation, invalid receiving values, completed inspection read-only rules, confirmation/duplicate submission behavior, and public quality-risk output safety.

Responsive checks cover seven representative screens at 360/390/412 pixels, plus four procurement decision variants at each width. Rendered screenshots use test fixtures and the actual Geist/logo assets. They are visual review artifacts, not live API/device evidence.

## I. Git status

No staging, commit, or push was performed. Full `git status --short` output is recorded in the companion status section below. Changes are limited to Flutter code, assets, dependencies, tests, and this report.

## J. Remaining gaps

### Product verification / limitations

- No failing automated checks are known after the final run. Live five-role sign-in and the React-to-Flutter decision round trip still require the configured API and a mobile device/emulator. Mocked tests do not establish live integration success.
- Representative screenshots were inspected; full visual acceptance across every Figma variant remains a review step.
- Site/quality overviews are header-linked; the requested Requests/Deliveries and Deliveries/Quality bottom destinations are preserved.
- The quality risk result is shown for the analysis opened in the current screen. A cross-session quality-workflow discovery/history UI is not added.
- The portal action copies a configured URL; it does not launch an external browser automatically.

### Existing API capability / contract limitations

- Request priority and top-level site notes appear in the input DTO but are not persisted/returned by the existing request service. The UI omits a misleading priority selector and saves material/delivery notes on actual request items.
- Supplier performance ratings, quotation lead times, procurement risk badges, decision reviewer names, and complete logistics timelines are not returned by the inspected procurement contracts. They are omitted rather than invented.
- The manager metric is labeled **Approved Recommendations**, not **Approved Today**: the response does not provide a reliable human-decision timestamp. Counts reflect latest workflows for available approved/ordered requests.
- Pending inspection DTOs provide delivery/item references and quantities, not material names or projects. The UI displays actual references where names are unavailable.
- The evidence endpoint registers an existing image URL; it is not a binary upload service. No camera/upload button or fake evidence URL was added.
- No password-reset endpoint or separate remember-device policy was introduced. Existing secure JWT persistence remains in use.

### Figma-only illustrative content

Sample names, IDs, amounts, counts, activity text, relative timestamps, and alternate receipt outcomes were replaced with actual API values, clearly labeled missing fields, or omitted unsupported illustrations. PO filtering uses the actual `Created` status rather than a fictional `Draft` status.

### Environment/tooling

- No Android/iOS device or running emulator was found; Windows, Chrome, and Edge were available.
- Android debug build was cancelled after prolonged Gradle dependency downloading. A process stack showed an HTTPS read inside Gradle's download operation. No APK success is claimed.
- Web debug compilation succeeded. Existing dependency-version/Wasm compatibility notices do not establish an Android build result.
- The opt-in staged live procurement test was skipped because its external integration-host configuration was not supplied.

## Verification and status snapshots

`git status --short`:

```text
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
?? backend/BuildWise.sln
?? docs/flutter_figma_implementation_report.md
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
```
