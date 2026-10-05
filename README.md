# BuildWise

## Construction Materials Procurement, Delivery and Quality Management System

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
