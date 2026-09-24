# ADR-001 — Quotation Analysis Agent runtime and procurement workflow state

**Status:** Accepted · **Date:** 2026-09-19 · **Owner:** Component 2 (Supplier, Quotation & Procurement Management), SLIIT IT24102414
**Scope:** spec §4 (supplier selection), §5 (deterministic rules), §10 (agent) · **Evidence:** commits `abdc6d5`, `38bbca9`, `6161ecc` on `feature/supplier-procurement`

## Context

Component 2 must turn an **approved** material request into an auditable supplier recommendation that a Procurement Manager approves **before** any purchase order exists. Three forces shaped the design:

1. **Determinism is a hard requirement.** Spec §5 lists ten business rules that ASP.NET Core must enforce as a gate; the agent may only recommend.
2. **A real agent layer is required** (SE3090): planning, tool use, structured JSON output, observable structured summaries only — no chain-of-thought.
3. **The workflow pauses on a human.** Between "recommendation produced" and "manager decides" the process idles for an unbounded time while other requests keep being processed, and every step must be reconstructable for audit.

## Options considered

| # | Option | Verdict |
|---|---|---|
| A | ASP.NET Core calls an LLM directly; the LLM picks the winner | **Rejected** — a non-deterministic, networked component on the critical path, untestable without provider mocking, and it would let a model decide an award. |
| B | Rule-based filter/rank only in C# (no agent service) | **Rejected as the primary path** (fails the agentic-AI requirement) but **kept as the degraded path** when the agent service is unreachable. |
| C | Python FastAPI agent microservice exposing allow-listed deterministic tools + optional LLM rationale; the C# API re-validates everything | **Chosen.** |
| D | LangGraph-style multi-agent orchestration framework | **Rejected** — a two-tool decision with a human-in-the-loop pause does not need a graph runtime or a checkpointer; here the pause is a database row, not a graph checkpoint. |

## Decision

1. **Runtime.** The agent runs as its own Python FastAPI service (`backend/agent_service/quotation_agent.py`), addressed by `AgentService:Url` (`http://127.0.0.1:8001`) through `/health` and `/analyze`, called by `QuotationAgentClient` under a 10-second cancellation budget.
2. **Tools are allow-listed.** `ALLOWED_TOOLS = {filter_eligible, rank_by_total}`; `/analyze` refuses to run when the required tools are not in the allow-list, and supplier names pass through `sanitize_text()` before use (prompt-injection defence).
3. **The winner is decided deterministically.** Eligible = Active supplier **and** unexpired quotation. Full-coverage quotations rank first by `total_amount` ascending; a partially covering quotation can never be awarded as the sole winner — it is surfaced as a warning during comparison/enforcement.
4. **The LLM explains; it does not decide.** With `ANTHROPIC_API_KEY` set, Claude rewrites only the rationale sentence (2–3 sentences, sanitized, ≤600 chars); any failure falls back to the deterministic template rationale. A default checkout runs with **no API key and no external call**.
5. **C# never trusts the agent.** `ValidateRecommendationSchema` checks the JSON contract (§5.7), then `ValidateRecommendationAsync` independently re-derives §5.1–§5.6 from the database. Only if both pass does the workflow reach `AwaitingApproval`.
6. **Failure is contained, never silent.** Agent unreachable / timeout / non-success → in-process deterministic fallback (`QuotationAgentClient.ExecuteFallbackAnalysis`) so the pipeline still completes; schema or rule-validation failure → workflow `Failed` with `error_message` recorded, and `AwaitingApproval` is never reached.

## Workflow state persistence (PostgreSQL)

| Table | Role in the workflow |
|---|---|
| `agent_workflows` | One row per run: `material_request_id`, `initiated_by_user_id`, `objective`, `status`, `approval_status`, `final_outcome`, `started_at` / `completed_at`. Survives process restarts. |
| `agent_workflow_steps` | Append-only, ordered audit trail: `agent_role`, `step_name`, `step_order`, `status`, `structured_result` (jsonb), `validation_result` (jsonb), `error_message`, timestamps. |
| `agent_approvals` | Append-only human decision: `reviewed_by_user_id`, `decision` (Approve / Reject / RevisionRequested), `comment`, `decision_date`. |

- **Why the database, not memory.** The workflow is paused for an indefinite human window and must be re-readable later through `GET /api/procurement-workflow/{id}/history`; an in-memory queue or background scheduler would lose both the paused state and the audit trail.
- **Step isolation.** Planning (`ProcurementPlanningAgent`), analysis (`QuotationSupplierAnalysisAgent`) and validation (`ProcurementValidationAgent`) are separate rows moving `Running → Completed/Failed`, so a failure is attributable to a specific step.
- **Status model.** `Pending → Running → AwaitingApproval → Completed | Failed`; `RevisionRequested` returns the workflow to `Pending` for another pass instead of creating a PO.
- **Atomicity.** Purchase-order creation validates preconditions, reads the winning recommendation from the step's `structured_result`, then writes `purchase_orders` + `purchase_order_items` and repoints quotation statuses (winner `Selected`, losers `Rejected`) inside one explicit transaction whenever the provider is relational (the in-memory test provider supports no transactions).
- **Idempotency and the approval gate.** `ValidatePurchaseOrderCreationAsync` blocks a second non-cancelled PO for the same request (§5.10) and blocks creation when no `Approved` `agent_approvals` row exists (§5.8).
- **Schema ownership.** EF Core migrations stay the single source of truth; the jsonb columns are mapped to `string` properties on the entities.

## Consequences

**Positive** — decisions remain deterministic and unit-testable (15 xUnit tests, including a full replay of the cement scenario); an agent outage degrades instead of blocking; LLM usage is one short sentence per run, or zero by default; per-step audit trail; the cross-language JSON boundary is validated in exactly one place.

**Negative / accepted** — two runtimes to launch locally (mitigated by the in-process fallback and a dedicated CI job for `agent_service`); the snake_case ↔ PascalCase boundary did bite once, because `AgentRecommendationDto` is PascalCase while the agent emits snake_case, so every field bound to `null` (fixed in `6161ecc` with `JsonNamingPolicy.SnakeCaseLower` scoped to that read); rationale text is model-generated, therefore sanitized and never used for decisions; `agent_workflows` / `agent_workflow_steps` / `agent_approvals` are shared with the other components, so column changes need team agreement.

## References

- `docs/component2_spec.md` §5.7 (schema validation), §5.8 (approval gate), §5.10 (idempotency), §10 (agent contract)
- Tests: `ProcurementValidationServiceTests` (8), `ScenarioReplayTests` (1), `AuthServiceTests` (6) → 15 xUnit; `backend/agent_service/test_quotation_agent.py` (9); React `vitest` (15)
- Agent contract tests that caught the two agent defects: `test_invalid_quotation_is_not_ranked_or_recommended`, `test_fallback_does_not_resurrect_ineligible_suppliers`
