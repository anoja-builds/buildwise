# Live React Quality Management browser smoke test

Date: 24 September 2026. Branch: `feature/quality-inspection`.

**Result: 21 checks passed, 1 responsive-layout check failed, 1 agent-analysis check unverified.** Ready for PR review with the findings below; this is not a clean responsive/mobile sign-off.

## Environment and isolation

- Real Chrome 153.0.8010.37, headless, fresh temporary browser contexts; desktop 1440 x 1000 and narrow viewport 390 x 844.
- Existing React/Vite application at `http://127.0.0.1:5179`, configured only for this process using `VITE_API_BASE_URL=http://127.0.0.1:5187/api`.
- Existing ASP.NET Core application at `http://127.0.0.1:5187`, with an explicit connection override to PostgreSQL 17 on loopback port 55439.
- Isolated database: `buildwise_browser_test_bd863657e44e4464b4a3ebe89417c31f`. The existing API applied its migrations and development seeds there. Only upstream procurement fixture data was seeded directly; all three inspections were started/completed through the actual API. NCRs were created and transitioned through the browser/API.
- No access to or modification of `buildwise_dev` or any shared application database. No product-code changes were made during this smoke test. No commit, push, merge, or branch switch.
- Browser contexts were closed. Test servers and the temporary PostgreSQL instance were stopped, the generated database was removed, and temporary test credentials were deleted after evidence collection.

## Actual checks

| Check | Result | Evidence / screenshot |
| --- | --- | --- |
| Anonymous access is blocked | **PASS** | Shared login displayed; unauthenticated API history returns HTTP 401. |
| Incorrect login displays the real API error | **PASS** | [Screenshot](../../logs/quality-smoke/screenshots/01-login-error.png) |
| Quality Inspector signs in through shared authentication | **PASS** | Browser POST /api/auth/login returned 200; QualityInspector identity appears in the shared shell. |
| NCR list shows the real empty state | **PASS** | [Screenshot](../../logs/quality-smoke/screenshots/02-empty-ncr-list.png) |
| Inspection history matches all records saved through the API | **PASS** | API and UI IDs match: 3, 2, 1. [Screenshot](../../logs/quality-smoke/screenshots/03-inspection-history.png) |
| Inspection details match API quantities and evidence | **PASS** | Inspection 1, item 1: received 240, accepted 235, rejected 5. [Screenshot](../../logs/quality-smoke/screenshots/04-inspection-detail-agent.png) |
| Agent navigation and real missing-workflow error | **PASS** | GET /api/quality-risk-agent/workflows/999999 returned real HTTP 404. No analysis POST made because secure configuration is absent. [Screenshot](../../logs/quality-smoke/screenshots/05-agent-workflow-not-found.png) |
| NCR whitespace validation prevents submission | **PASS** | [Screenshot](../../logs/quality-smoke/screenshots/06-ncr-validation.png) |
| Explicit browser action creates the rejected-item NCR | **PASS** | NCR 1 created with HTTP 201 and verified through API GET. [Screenshot](../../logs/quality-smoke/screenshots/07-ncr-open.png) |
| Corrective action is saved and unsaved edits block resolution | **PASS** | [Screenshot](../../logs/quality-smoke/screenshots/08-corrective-action.png) |
| Human resolves the NCR | **PASS** | [Screenshot](../../logs/quality-smoke/screenshots/09-ncr-resolved.png) |
| Human closes the NCR and API preserves resolution timestamp | **PASS** | [Screenshot](../../logs/quality-smoke/screenshots/10-ncr-closed.png) |
| Closed NCR persists in list and inspection link works | **PASS** | List -> NCR detail -> source inspection navigation uses persisted records. |
| Accepted items and incomplete inspections do not offer invalid actions | **PASS** | [Screenshot](../../logs/quality-smoke/screenshots/11-incomplete-inspection.png) |
| Real stale-state HTTP 409 is shown without claiming success | **PASS** | Separate real API request resolved the NCR; stale browser POST returned 409, then Refresh recovered the actual state. [Screenshot](../../logs/quality-smoke/screenshots/12-conflict-error.png) |
| Network loss gives an error and retry recovers real history | **PASS** | Actual browser offline mode caused fetch failure; restoring network and retry returned the same three API records. [Screenshot](../../logs/quality-smoke/screenshots/13-network-error.png) |
| API 403 is displayed and does not sign out the session | **PASS** | Sent a real ProcurementOfficer JWT for one history request; API returned real 403. No response was mocked. [Screenshot](../../logs/quality-smoke/screenshots/14-api-forbidden.png) |
| Responsive layout at 390px | **FAIL** | Visual follow-up confirms page-wide horizontal overflow, not just internal table scrolling. [Screenshot](../../logs/quality-smoke/screenshots/15-narrow-inspection.png) [390px viewport](../../logs/quality-smoke/screenshots/18-narrow-viewport-cropped.png) |
| Rejected JWT causes shared session-expiration handling | **PASS** | Actual API returned 401 for invalid JWT; existing auth provider returned the browser to login. |
| ProcurementOfficer is blocked from both quality screens and endpoints | **PASS** | Role gate makes no quality data requests; direct real API requests return 403. [Screenshot](../../logs/quality-smoke/screenshots/16-role-restricted.png) |
| No page crashes or agent business operations occurred | **PASS** | Zero browser pageerror events. Three source inspections preserved. No real analysis attempted without configured credentials. |
| Real Quality Risk Agent analysis and successful result rendering | **UNVERIFIED** | No securely configured API agent URL/key or provider credentials found. No fake success or seeded agent workflow was used. |
| Loading state while real history request is pending | **PASS** | Paused the actual outgoing request, observed loading, then continued it to the real API without mocking a response. [Screenshot](../../logs/quality-smoke/screenshots/17-loading-real-request.png) |

## Database/API/UI consistency

The history contained inspection IDs **3, 2, 1** in newest-first order. Browser rows matched actual API delivery references, inspector names, and statuses. Inspection #1 showed `BROWSER-QA-240`, `Completed`, `PartiallyAccepted`, and received/accepted/rejected quantities **240 / 235 / 5**, matching both the original API completion response and a fresh API GET.

A direct read of the isolated database confirmed:

- Inspection #1: Completed / PartiallyAccepted.
- Inspection #2: Completed / Accepted.
- Inspection #3: UnderInspection / no overall decision.
- NCR #1: Closed, linked to inspection item #1; resolution timestamp persisted after closure.
- NCR #2: Resolved, linked to inspection item #1; used for the real stale-state conflict test.
- Agent workflow count: **0**. No analysis was triggered or fabricated.

[Database witness](../../logs/quality-smoke/database-witness.txt) ? [Structured results and HTTP status log](../../logs/quality-smoke/browser-results.json)

## Findings and remaining blockers

1. **Responsive layout defect:** at a 390px viewport the document measures **766px** wide. The quality workspace is 358px, but its cards expand to 750px and the table container is 700px. This causes whole-page horizontal scrolling and puts content/actions off screen. Desktop screens were readable and functional. Fix and recheck the narrow layout before responsive/mobile acceptance. No shared-shell or style changes were made in this testing task.
2. **Sidebar usability note:** clicking the already-active Quality Inspections sidebar entry preserves the current detail screen. The explicit Back to Quality Inspections button works. This is not a data-integrity blocker, but the intended reselection behavior should be reviewed.
3. **Real agent execution remains unverified:** securely configured API agent BaseUrl/ApiKey and provider credentials were absent in the checked application settings, user-secret configuration, process environment, and agent `.env` location. No analysis POST was sent. Panel navigation, advisory text, completion gating, and an actual missing-workflow 404 were tested. Successful recommendations, persisted failed-run rendering, and live provider behavior still require a configured internal service and credentials.

## Error-test methodology

- Incorrect login, missing workflow, stale NCR transition, and unauthorized endpoints produced real server responses: 401, 404, 409, and 403 respectively.
- The stale transition used a separate authorized API request to resolve the second NCR before the browser attempted resolution with its old state. Refresh recovered the actual resolved record.
- Network failure used real browser offline mode; restoring connectivity and clicking Try again fetched the real history.
- API 403 handling substituted a real ProcurementOfficer JWT for one outgoing browser history request. Session-expiry handling substituted an invalid JWT for one request. Both requests reached the actual API; responses were not mocked.
- Loading-state verification held an outgoing history request, observed the loading UI, and then continued the request to the real API.
- No successful response, inspection data, NCR state, or agent result was mocked. There were **zero browser pageerror events**.

## Tooling issues encountered

- Browser plugin initialization failed with `Importing module "node:process" is not allowed in node_repl`. Used temporary standalone Playwright with installed Chrome and a fresh context instead.
- Initial sandboxed API requests closed because Windows Event Log access was denied. Restarting the same API with approved execution outside the sandbox, still pointing only at the isolated database, resolved this environment issue.
- The first automation attempt used an overly exact Severity label selector and timed out before NCR creation. Adjusted the test selector; actual creation/lifecycle checks then passed.
- An automation step initially expected sidebar reselection to reset the view. The retained detail screen exposed the navigation note above. Continued using the working explicit Back control.
- The initial narrow-screen automation collected overflow without treating it as a failure. Screenshot review and a separate measurement run confirmed the defect; the final result correctly records **FAIL**.

## PR readiness

**Ready for PR review with known findings.** Authentication, inspection read consistency, human-controlled NCR lifecycle, role restrictions, error recovery, and agent-panel navigation have live browser/API/PostgreSQL evidence. Resolve the responsive overflow before claiming mobile-layout readiness, and complete real agent analysis when secure configuration is available. No merge or full provider-integration sign-off is claimed.

Screenshots and raw local test artifacts are under ignored `logs/quality-smoke/`; links here refer to this workspace. Attach selected screenshots to the PR manually if needed.
