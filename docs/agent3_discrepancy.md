# Component 3: Delivery Discrepancy Agent

Flutter/React continue to call `POST /api/Deliveries/{id}/discrepancy-analysis`.
ASP.NET owns receipt arithmetic, RBAC, delivery state and audit persistence. Clients
never call Python or receive provider credentials.

## Receiving arithmetic

`DeliveryQuantityRules` is shared with `DeliveriesController`:

- Prior fulfilled = prior received minus prior damaged.
- Outstanding before = max(0, ordered minus prior fulfilled).
- Overdelivery = current received exceeds outstanding before.
- Physical shortage = max(0, outstanding before minus current received).
- Outstanding after = max(0, ordered minus prior fulfilled minus current undamaged).

Undamaged/fulfilled here means available for quality inspection, not quality approval.
Physical shortage and damaged quantities remain separate. Ordered 250, received 240,
damaged 5 still produces shortage 10 and current undamaged 235. With ordered 20,
prior received 15/damaged 2, current received 7/damaged 2, the result is no overdelivery,
shortage 0, current undamaged 5 and outstanding after 2.

Analysis uses earlier finalized receipts as of the selected delivery's `ReceivedAt`,
with delivery ID as a tie-breaker. Later deliveries cannot retroactively inflate its
prior receipts. History tools expose at most 20 earlier records each, scoped to the
actual supplier. Quotation supplier/material provenance takes precedence over legacy
direct PO links. Missing or truncated history is explicitly not evidence of good performance.

## Internal agent and configuration

The existing internal Python service exposes `/delivery-discrepancy/analyse` alongside
the separate quality endpoint. Agent 3 has its own prompt, contracts, tools and bounded
model/observation loop; Agent 4 business logic is not called.

Use the existing server-side configuration:

- ASP.NET `AgentService:BaseUrl` and `AgentService:ApiKey` (or `QUALITY_AGENT_SERVICE_KEY`).
- Python `QUALITY_AGENT_SERVICE_KEY`, `GOOGLE_API_KEY`, and `CHAT_MODEL`.
- Internal authentication retains the existing `X-Quality-Agent-Key` header name.

Restart the API and Python service after installing the change. No migration or new
dependencies are required. Do not put keys in source, Flutter, React, or API responses.

The model selects among three request-scoped read-only tools:
`get_current_delivery_evidence`, `get_supplier_delivery_history(limit: 1..20)` and
`get_previous_discrepancy_summary`. All three must be consulted. The last tool reads
recorded earlier discrepant/partial receipts, not previous AI speculation.

The loop permits at most six model turns, six evidence tool calls, and 90 seconds;
ASP.NET allows 100 seconds and caps responses at 128 KB. Evidence accepts at most
100 current lines. No SQL, URLs, writes, approvals, or business action tools exist.
Data is marked untrusted and possible causes must remain hypotheses.

The final advisory has exactly `riskLevel`, `summary`, `likelyCauses`,
`recommendedActions`, `supplierFollowUpRequired`, and `evidenceRefs`.
ASP.NET rejects unexpected fields, invalid/missing fields, unobserved references,
missing required tool calls, and execution-bound violations before persisting advice.

## Result and fallback

Existing quantity, flag, validation, recommendation and workflow fields remain.
`advisory` adds the structured result; `execution` adds validated model/tool metadata
and a fixed fallback reason. Advice is mapped into existing `recommendations` so
React and Flutter can display it without changes.

`executionMode` is `AgenticAI` only after a successful, validated provider run.
Missing configuration, provider/service failure, timeout or invalid output produces
`DeterministicFallback`, null AI advisory, and the deterministic recommendations.
Fallback is a completed advisory workflow, not an AI success. Invalid authoritative
quantity evidence fails the workflow before model execution, rather than falling back.

The four workflow steps persist the snapshot, validated arithmetic, bounded evidence
plus validated advisory/execution summary, and validation outcome. Raw provider
responses, model messages, chain-of-thought and exception payloads are never stored.
Approval remains pending and no business mutation is executed by analysis.

## Verification

Backend tests cover prior-damage replacement receipts through the actual receiving
controller, genuine excess, historical cutoff, evidence scoping/truncation, validated
success, rejected output/fallback, workflow persistence and business-state preservation.
Python tests use controlled model turns to exercise tool choices, observations, bounds,
authentication, schema binding, invalid output and timeouts. These tests do not prove
a live external provider is available; deployment still requires valid server credentials.
