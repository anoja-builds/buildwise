# Agent 2: bounded procurement advisory

The existing React/API procurement workflow and manager approval remain authoritative.
`QuotationAgentClient` now executes the existing C# deterministic eligibility/ranking
routine locally; it no longer obtains a winner from the legacy port-8001 service.
Eligibility, ranking, totals, selected quotation and deterministic rationale are unchanged.
The existing business validation runs before advisory analysis. Human approval and PO
creation/revalidation are unchanged.

`ProcurementAdvisoryService` constructs request-scoped evidence with at most 100 request
items, 50 quotations, 100 lines per quotation, and the associated suppliers only. Evidence
contains recorded supplier status and prior order counts, not invented performance ratings.
Eligibility/ranks reflect the deterministic result; per-quotation validation also reports
totals/coverage/business-rule errors without changing that ranking.

ASP.NET calls authenticated `POST /procurement/analyse` on the shared internal Python service.
The model chooses its evidence-tool order and must consult all three read-only tools:

- `get_material_request_requirements`
- `get_validated_quotation_comparison`
- `get_supplier_procurement_evidence`

Tools accept no identifiers or scope overrides. There are no write, approval or PO tools.
The model has six turns, six tool calls and 90 seconds. ASP.NET has a 100-second timeout
and 128 KB response limit. Existing Gemini schema projection is reused without changing
Agents 1, 3 or 4.

The only advisory fields are `summary`, `riskLevel`, `risks`, `clarificationQuestions`,
`recommendedFollowUps`, and `evidenceRefs`. ASP.NET rejects extra/missing fields, unknown
references, missing request/selected-quotation citations, invalid tools and execution bounds.
It never accepts winner/ranking/approval fields from AI. Suggestions are presented separately
from the deterministic rationale; the manager continues to own the approval decision.

The existing analysis step retains the deterministic result plus additive advisory metadata.
A fourth workflow step records bounded evidence, validated advisory, `executionMode`,
`modelIdentifier`, `iterationCount`, `toolTrace`, and `fallbackReason`. No raw provider
messages, private reasoning or exception payloads are persisted.

Validated provider success is `AgenticAI`. Provider/configuration failure, timeout or invalid
output is `DeterministicFallback` with the existing deterministic rationale. Valid business
results can still await human approval; invalid business results cannot. Oversized evidence
also falls back without calling the model. Old workflow modes remain readable in the UI.

Configuration reuses `AgentService:BaseUrl`, `AgentService:ApiKey` (trimmed), the internal
`X-Quality-Agent-Key` header, and Python `QUALITY_AGENT_SERVICE_KEY`, `GOOGLE_API_KEY`,
`CHAT_MODEL`. No new secrets, schema migrations or dependencies are needed. Restart the
API and internal Python service after deployment; the React update recognizes the new mode.
The old `backend/agent_service/quotation_agent.py` remains for historical compatibility but
is no longer used by the current procurement workflow.

Focused verification covers model-directed tools, provider declaration binding, bounds,
invalid/unauthorized output, fallback, fixed selection/ranking, workflow persistence,
supplier scoping, and the manager/PO gate. Synthetic fixtures support a live provider check
without creating procurement records or triggering manager notifications.
