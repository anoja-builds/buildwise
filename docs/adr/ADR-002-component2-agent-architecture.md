# ADR-002 — Component 2: Agentic Quotation Analysis Architecture

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-09-19 |
| **Deciders** | IT24102414 (Supplier, Quotation & Procurement Management) |
| **Scope** | Component 2 — Supplier, Quotation & Procurement Management |
| **Related** | `docs/component2_spec.md` §4.5, §5, §7, §10, §11 · commits `38bbca9`, `6161ecc` |
| **Supersedes** | n/a *(first ADR recorded for this component — no ADR-001 exists in `docs/`)* |

## 1. Context & problem statement

SE3090 requires an Agentic AI contribution to procurement: analysing supplier
quotations and recommending an award. The tension is that procurement decisions
are commercially sensitive while the AI is advisory. Two failure modes had to be
designed out:

1. **The AI overriding deterministic business rules** — e.g. recommending a
   suspended supplier because it is cheapest.
2. **Silent, unauditable AI output** — a recommendation that reaches a purchase
   order without an evidence trail or a human decision.

The quotation set must also be re-evaluated against live data: a supplier can be
suspended *after* the AI has run, so the AI's answer can be stale.

## 2. Decision

**D1 — A separate Python FastAPI microservice for the analysis agent.**
`backend/agent_service/quotation_agent.py` (FastAPI, uvicorn on `127.0.0.1:8001`)
exposes `GET /health` and `POST /analyze`. Python was chosen over extending the
C# API because the agentic layer is exploratory by nature (tool selection,
prompting, LLM rationale) and is expected to iterate independently of the
stable, rule-governed API. It also keeps AI dependencies out of the .NET
deployable, and lets the agent be scaled, restarted or replaced without
touching procurement logic.

**D2 — C# ASP.NET Core remains the single source of truth.**
`ProcurementValidationService.cs` independently re-derives eligibility (§5.1–5.10)
and re-validates the agent's answer. The agent is advisory; it cannot write to
`purchase_orders`, cannot approve anything, and cannot advance a workflow past
`AwaitingApproval`. The schema gate (§5.7) rejects any payload that does not
match the §10 contract before it is stored.

**D3 — Two-phase HTTP with an in-process fallback.**
`QuotationAgentClient` calls the agent with a 10 s budget (`cts.CancelAfter(10s)`,
HttpClient timeout 12 s). On non-success or unreachability it executes
`ExecuteFallbackAnalysis`, an in-process mirror of the same deterministic rules,
so procurement continues when the microservice is down.

**D4 — Workflow state persisted in PostgreSQL, shared table structure.**
Execution state lives in `agent_workflows`, `agent_workflow_steps` and
`agent_approvals` — deliberately *shared* across all four components' agents
per the ERD change-control note. Three ordered steps are recorded:
`ProcurementPlanningAgent` → `QuotationSupplierAnalysisAgent` →
`ProcurementValidationAgent`. Each step keeps `structured_result` /
`validation_result` (jsonb), `status` and `error_message`. Only observable
structured summaries are stored — no raw model chain-of-thought. Status
transitions are `Pending → Running → AwaitingApproval → Completed | Failed`.

**D5 — Human-in-the-loop approval gate.**
`POST /api/procurement-workflow/{id}/decision` (Manager/Administrator only)
records an `agent_approvals` row. Rule §5.8 blocks purchase-order creation until
an `Approved` row exists; §5.10 blocks duplicate POs; §5.9 locks the source
quotation and request once the PO exists.

## 3. Alternatives considered

| Alternative | Why rejected |
|---|---|
| Agent logic inside the C# API | Couples AI iteration cadence to the stable API; agent cannot be run or scaled independently |
| LLM with tool-use autonomy (agent decides and acts) | Violates §10 "what it never does"; untestable against §5; risks selecting a suspended/expired supplier |
| Storing full model output / chain-of-thought | Unnecessary, non-auditable, prompt-injection surface |
| Fire-and-forget agent call | No evidence trail; §7/§11 require step-by-step history |
| Using the in-process fallback as the only implementation | Loses the independent cross-check that the C# and Python layers agree |

## 4. Consequences

**Positive**
- Two independent implementations of the same rules cross-check each other; a
  divergence surfaces as a validation failure instead of a bad purchase order.
- Every recommendation is reproducible and auditable from `agent_workflow_steps`.
- Zero silent purchase orders: §5.8/§5.10 make a bad path fail loudly.
- The agent is independently testable (`pytest`) and deployable.

**Negative / accepted risks**
- A second runtime (Python 3.12, uvicorn) must be running; a dead agent silently
  degrades to the fallback rather than erroring.
- **Contract drift is the main risk class.** The C#→Python request uses
  snake_case property names; the Python→C# response originally deserialized into
  a PascalCase DTO with no mapping, silently producing an all-null
  recommendation that then failed schema validation. Fixed in `6161ecc` by
  reading the response with `JsonNamingPolicy.SnakeCaseLower`, scoped to that
  read only.
- `Program.cs` originally defaulted `AgentService:Url` to port **8000** while
  `appsettings.json` set **8001** — a mismatch that would have silently skipped
  the agent and used the in-process fallback. Now aligned on **8001** in both.
- Notification e-mails are best-effort and wrapped so a failure never rolls back
  the transaction they report on.

## 5. Verification evidence

| Check | Result |
|---|---|
| C# tests (`dotnet test backend/BuildWise.Api.Tests`) | 20 passed, 0 failed — includes agent-boundary schema tests |
| Python agent tests (`pytest test_quotation_agent.py`) | 9 passed, 0 failed |
| React tests (`npx vitest run`) | 4 files, 15 passed |
| `npm run build` | exit 0, 58 modules |
| End-to-end replay (spec §11, request #1) | workflow #51 → `AwaitingApproval` → Approved → `purchase_orders` row (250 bags @ 2 100.00 = 525 000.00); quotations #3 Selected, #6/#7 Rejected |

CI (`.github/workflows/ci.yml`) runs the backend, agent, web and mobile suites on push/PR to `main`.

## 6. Known gaps

- An HTTP 400 was observed on the PO-creation call that nonetheless persisted a
  valid PO. Not reproducible on a clean run (the identical request returned
  `200 OK` with the correct payload). Suspected EF Core tracking conflict when
  `CreateFromWorkflow` re-enters `GetById` on the same scoped `DbContext`.
  Open, non-blocking.
- `component2_spec.md` §10's response schema omits `recommended_supplier_name`,
  which the implementation (and the React UI) do include.
- A quotation marked `valid = false` was previously ranked and could win; fixed
  in `38bbca9` (skip + strict fallback screening), with regression tests.

