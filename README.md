# BuildWise — Component 1: Material Request & Approval Management (React Web)

This is the **React web client** for your component of the BuildWise assignment
(SE3090, Assignment 1). It covers only what the assignment table assigns to
"React Web" for Component 1: **manage requests, approval queue, request
history.** It does not include the ASP.NET Core API, PostgreSQL schema, or the
Flutter app — those are separate deliverables (yours or teammates').

<<<<<<< HEAD
Right now it runs against an **in-memory mock API** (`src/api/materialRequestApi.js`)
so you can build, demo and screenshot the UI before the backend exists. Every
mock function is written to mirror a real REST endpoint 1:1, so swapping to the
real API later is a small, contained change — see "Connecting the real backend"
below.

---

## 1. Setup

Requirements: Node.js 18+.

```bash
npm install
npm run dev       # starts at http://localhost:5173
```

Other scripts:

```bash
npm run build      # production build to dist/
npm run preview    # preview the production build locally
```

Data persists in your browser's `localStorage` between reloads (key
`buildwise_material_requests_v1`), so you can create/approve requests, refresh,
and still see them. Clear that key (or your browser storage) to reset to the
seed data in `src/data/mockData.js`.

There's no real login yet (auth is a separate shared feature per the
assignment). Use the **role switcher in the top-right corner** to act as a
Site Engineer or a Procurement Manager — the UI shows/hides actions based on
that role, the same way it will once real JWT-based auth is wired in.

---

## 2. What's implemented, mapped to the assignment

| Assignment requirement | Where it lives |
|---|---|
| Create material request | `pages/RequestFormPage.jsx` (`mode="create"`), `POST`-equivalent `createRequest()` |
| View request | `pages/RequestDetailPage.jsx`, `GET`-equivalent `getRequest()` |
| Update request | `pages/RequestFormPage.jsx` (`mode="edit"`), only allowed while `Draft` / `RevisionRequested` and only by the original requester — matches "prevent silent changes after approval" |
| Delete request | `materialRequestApi.deleteRequest()`, exposed as a **Delete** button on the list and detail pages — only for your own `Draft` requests, so anything already submitted stays in the audit trail |
| List requests | `pages/RequestListPage.jsx`: search, status filter, project filter, "my requests" filter, pagination |
| Approve / reject request | `pages/RequestDetailPage.jsx` decision panel — Approve / Reject / Request Revision, comment required for the latter two |
| Approval queue | `pages/ApprovalQueuePage.jsx` — Procurement Manager worklist, sorted by AI urgency flag |
| Request history | Approval timeline in `RequestDetailPage.jsx` (`request.approvals`) |
| Preserve approval history | Approvals are appended, never overwritten, in `materialRequestApi.decideRequest()` |
| AI Contribution — Request Validation & Planning Agent | `components/AgentPanel.jsx` (submitted result) + live preview in the create/edit form. Mocked client-side for now; **it only ever displays a summary — it never sets status**, matching "does not approve the request" in the brief |
| Entities: Project, Material, MaterialRequest, RequestItem, Approval | `src/data/mockData.js` — shapes match names used in the assignment's core-data table |

Out of scope for this component (owned elsewhere per the brief): authentication/
authorization, suppliers/quotations/purchase orders, deliveries, quality
inspection.

---

## 3. Project structure

```
src/
  api/
    client.js                 fetch wrapper for the real API (JWT-ready, unused for now)
    materialRequestApi.js     mock data layer — swap this file's internals for real calls
  components/
    AppShell.jsx               sidebar + topbar + role switcher
    AgentPanel.jsx              renders the planning agent's structured output
    StatusBadge.jsx, Pagination.jsx, Toast.jsx
  context/
    AuthContext.jsx             mock "who's logged in" — replace with real shared auth later
  data/
    mockData.js                 seed data: projects, materials, users, requests
  pages/
    RequestListPage.jsx
    RequestFormPage.jsx         used for both create and edit
    RequestDetailPage.jsx
    ApprovalQueuePage.jsx
  App.jsx                       routes
  main.jsx                      entry point
```

---

## 4. Connecting the real backend

When your ASP.NET Core endpoints exist, open `src/api/materialRequestApi.js`
and replace each function body with the matching call via `src/api/client.js`,
for example:

```js
// before (mock)
export async function listRequests(params) {
  // ...filters an in-memory array...
}

// after (real API)
export async function listRequests(params) {
  return apiClient.get('/material-requests', params);
}
```

Suggested endpoint shapes (adjust to match your actual API contract):

- `GET    /api/material-requests?status=&projectId=&search=&page=&pageSize=`
- `GET    /api/material-requests/{id}`
- `POST   /api/material-requests`
- `PUT    /api/material-requests/{id}`
- `DELETE /api/material-requests/{id}`   (Draft only — return 409/403 for anything else)
- `POST   /api/material-requests/{id}/approve`
- `POST   /api/material-requests/{id}/reject`
- `POST   /api/material-requests/{id}/request-revision`
- `GET    /api/projects`
- `GET    /api/materials`

No page or component needs to change — they only ever import from
`materialRequestApi.js`. Also set `VITE_API_BASE_URL` in a `.env` file if your
API isn't proxied at `/api` (see `vite.config.js` for the dev proxy).

Once real auth exists, replace `context/AuthContext.jsx` with your shared auth
context/hook and remove the role `<select>` in `AppShell.jsx`.

---

## 5. Suggested demo script (for your video/live demo)

1. As **Site Engineer**, open **New Material Request**, fill in a project,
   required date, reason and one or two item lines. Point out the live AI
   preview panel updating as you type.
2. Submit — land on the detail page, showing status `Awaiting approval` and
   the full structured AI summary (completeness, urgency, flags).
3. Switch role to **Procurement Manager** (top-right). Open **Approval
   Queue** — the new request appears, sorted by urgency.
4. Open the request, add a comment, click **Approve** (or **Request
   revision** to show the round-trip). Point out the approval history
   timeline recording who decided what and when.
5. Switch back to **Site Engineer**, go to **Material Requests**, filter by
   status/project/search to show the list view, then open a
   `RevisionRequested` request and show that only that request (not approved
   ones) can be edited.

This keeps the demo end-to-end without needing the backend running yet, and
maps directly onto the "assessed procurement workflow" language in the brief
for the request/approval half of it (steps 1–4 and 8 in the scenario).
=======
BuildWise is the integrated SE3090 Software Engineering Frameworks group assignment system. It manages the complete material lifecycle: **material request → approval → RFQ/quotation → AI supplier recommendation → manager approval → purchase order → delivery → receiving → quality inspection → non-conformance resolution**.

## Four business components

| # | Component | Owner |
|---|---|---|
| 1 | Material Request & Approval Management | Peiris DPSS |
| 2 | Supplier, Quotation, RFQ & Procurement Management | Theebika (IT24102414) |
| 3 | Delivery & Material Receiving Management | Ramya |
| 4 | Quality Inspection & Non-Conformance Management | Anoja |

## Technology stack

| Layer | Technology |
|---|---|
| API | ASP.NET Core 8 Web API, EF Core, PostgreSQL |
| Web | React 19, React Router 7, Vite 8 |
| Mobile | Flutter 3, flutter_secure_storage, flutter_local_notifications |
| Agentic AI | Python 3.12 FastAPI microservices (ports 8001–8004) |
| Auth | JWT (HS256), RBAC (5 roles) |
| Email | SMTP (configurable; logs to console in dev) |
| CI | GitHub Actions (backend, Python, React, Flutter) |

## Agentic AI architecture

Four distinct agents, each with different responsibilities, tools and state:

```
Domain objective
      ↓
ProcurementPlanningAgent        ← Plan + delegate (4 read tools)
      ↓
QuotationSupplierAnalysisAgent  ← Rank suppliers deterministically (+LLM rationale)
      ↓
ProcurementValidationAgent      ← Re-validate all business rules (C#, no trust)
      ↓
Human approval (Procurement Manager)
      ↓
DeliveryDiscrepancyAgent        ← Detect delivery shortfalls
      ↓
QualityRiskAnalysisAgent        ← Flag non-conformance risk
      ↓
Auditable result / safe failure
```

All workflow state is persisted in PostgreSQL (`agent_workflows`, `agent_workflow_steps`, `agent_approvals`). The AI never creates a purchase order — a human must approve first.

## Cross-platform workflow

```
Flutter (mobile) → BuildWise API → PostgreSQL → Agentic AI → React (web) → API → Flutter
```

## Running locally

```powershell
# Start all services (API + agents + web)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/start-dev.ps1

# Verify
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-full-journey.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-rbac.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-planning-agent.ps1
```

## Local evidence

| Suite | Passed |
|---|---|
| Backend (.NET xUnit) | 230/230 |
| Python agent (pytest) | 28/28 |
| React (vitest) | 109/109 |
| Flutter (flutter test) | 4/4 |
| Full E2E business journey | 15/15 |
| Component 4 checks | 5/5 |

## Environment variables

Copy `.env.example` → `.env` and fill in your values. Never commit credentials.

| Variable | Purpose |
|---|---|
| `POSTGRES_*` | Database connection |
| `Jwt__Key` | JWT signing key |
| `VITE_API_BASE_URL` | Web → API base URL |
| `GEMINI_API_KEY` | LLM rationale (optional) |
| `ANTHROPIC_API_KEY` | LLM rationale alternative (optional) |

## API & Swagger

- API: `http://localhost:5078`
- Swagger: `http://localhost:5078/swagger`
- Health: `http://localhost:5078/health`

## Deployment

See [`docs/adr/ADR-005-deployment-architecture.md`](docs/adr/ADR-005-deployment-architecture.md) for the chosen deployment platform and rationale.

## Architecture Decision Records

| ADR | Decision |
|---|---|
| [ADR-001](docs/adr/0001-quotation-analysis-agent-and-workflow-state.md) | Quotation agent runtime & workflow state persistence |
| [ADR-002](docs/adr/ADR-002-component2-agent-architecture.md) | Component 2 multi-agent architecture |
| [ADR-003](docs/adr/ADR-003-react-state-management.md) | React state management strategy |
| [ADR-004](docs/adr/ADR-004-flutter-state-management.md) | Flutter state management strategy |
| [ADR-005](docs/adr/ADR-005-deployment-architecture.md) | Deployment platform & cloud architecture |

## Documentation

- [`docs/assignment_compliance_audit.md`](docs/assignment_compliance_audit.md) — Rubric compliance checklist
- [`docs/project_verification_guide.md`](docs/project_verification_guide.md) — How to verify each component
- [`docs/environment_and_agents.md`](docs/environment_and_agents.md) — Agent setup & environment guide
- [`docs/BuildWise_ERD.pdf`](docs/BuildWise_ERD.pdf) — Entity-relationship diagram

## GitHub

Repository: https://github.com/IT24102414/build  
CI: GitHub Actions runs on every push and pull request to `main`.
>>>>>>> origin/intergration/final-buildwise
