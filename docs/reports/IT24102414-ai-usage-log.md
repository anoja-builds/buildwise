# AI Usage Log — IT24102414

**Component 2 — Supplier, Quotation & Procurement Management**
**BuildWise · SE3090 Software Engineering Frameworks · branch `feature/supplier-procurement`**

> **Note to author:** the *Chronology* section below is a factual record and can
> be verified against git history and CI runs. The *Reflection* section is a
> **scaffold** — the conclusions offered should be re-read, edited or rejected so
> the final wording is genuinely yours.

## 1. AI tooling used

| Tool | Role |
|---|---|
| Cline (agentic coding agent, Claude model) | Repo inspection, running test suites, driving live HTTP/DB verification, authoring fixes |
| Claude (via the agent's optional LLM rationale path) | Production rationale generation for the agent — **not exercised** in this run (`ANTHROPIC_API_KEY` unset, so `llm_rationale_enabled: false`) |

## 2. Technical chronology

| # | Event | Evidence |
|---|---|---|
| 1 | Initial scope acknowledgement request refused without evidence; repo located at `Downloads/buildwise-component2` | git log `abdc6d5`, author `IT24102414` |
| 2 | Baseline verified: 15 C# tests, 9→7 Python tests, 15 React tests, `npm run build` exit 0 | test runs |
| 3 | **Bug found (Python):** a quotation marked `valid = false` produced an "excluded" warning but was *not* skipped — it was ranked and could win top spot. Demonstrated live against `POST /analyze` | `quotation_agent.py` `filter_eligible` |
| 4 | **Bug found (Python):** the resilient fallback re-admitted every quotation the filter had just rejected, with `covers_all` forced `True` — a fully Suspended/Inactive supplier could be recommended as "lowest-cost compliant … Active standing" | `analyze()` lines ~196–197 |
| 5 | Both fixed in `38bbca9`: `continue` on invalid quotations, fallback gated on `supplier_status == "Active" and valid`; two regression tests added (7 → 9 Python tests) | commit `38bbca9` |
| 6 | **Doc/code mismatch found:** checklist expected `{"status":"healthy","service":"quotation_agent"}` but the endpoint returned `{"status":"ok","agent":"..."}`. Verified nothing in the repo calls `/health`, so the doc was wrong, not the code | grep of `.cs/.js/.jsx`, `.github/` |
| 7 | `/health` updated to the documented contract as a superset; the existing `test_health_endpoint` assertion updated alongside it (it asserted `"ok"` and would otherwise have failed pytest) | commit `38bbca9` |
| 8 | **Bug found (C#↔Python integration):** every `POST /procurement-workflow` failed with `"recommended_quotation_id is missing or non-positive"` and **0 of 50** workflows ever succeeded, 0 purchase orders ever created. Root cause: `AgentRecommendationDto` is PascalCase with no `JsonPropertyName`, while the agent emits snake_case — so the HTTP 200 response deserialized to an all-null object. Proven by the success log line `"Agent analysis received from external microservice"` plus the absence of any "unreachable" warning | `agent_workflow_steps` = 0 stored results; `purchase_orders` count 0 |
| 9 | Fixed in `6161ecc`: response read with `JsonNamingPolicy.SnakeCaseLower`, scoped to that read only (React camelCase contract preserved) | commit `6161ecc` |
| 10 | Full E2E replay after the fix: workflow #51 → `AwaitingApproval` → Approved → purchase order created (250 bags @ 2 100.00 = 525 000.00); quotations #3 Selected, #6/#7 Rejected | DB rows + API responses |
| 11 | **Anomaly left open:** the PO-creation call that succeeded server-side returned HTTP 400 to the client. Not reproducible — the identical request returned `200 OK`. Suspected EF Core tracking conflict when `CreateFromWorkflow` re-enters `GetById` on the same scoped `DbContext`. Recorded in `docs/adr/ADR-002` §6 | logs + reproduction attempt |
| 12 | Test baseline after fixes: 15 C#, 9 Python, 15 React, build exit 0 | test runs |

## 2b. Addendum — changes observed in the working tree after this session

The following edits appeared in the working tree afterwards and were **not
authored in this session**. They are recorded because they bear directly on the
findings above:

- `Program.cs` now defaults `AgentService:Url` to port **8001** (was **8000**),
  closing the "silently skip the agent and use the fallback" risk described in
  §5 of `docs/adr/ADR-002`.
- `ProcurementWorkflowService.GetWorkflowDetailsAsync` now reads the
  recommendation only from the analysis agent's step
  (`AgentRole == QuotationSupplierAnalysisAgent`), so the planning step's plan
  JSON can no longer be mistaken for a recommendation payload.
- `backend/BuildWise.Api.Tests/AgentRecommendationSchemaTests.cs` adds the
  boundary test that was missing: a stubbed `HttpMessageHandler` returns a
  snake_case agent payload and asserts it survives the real deserialization
  path, plus §5.7 schema-gate and §10 failure-handling cases. The C# suite is
  now **20** tests (was 15).
- `scripts/verify-component2.ps1` (with `check-services.ps1`, `smoke-test.ps1`,
  `start-dev.ps1`, `stop-dev.ps1`) provides one-command verification of the
  whole component, including the approval → purchase-order path.

## 3. What the AI tooling did well

- **Found both bugs by execution, not by reading.** Neither the "invalid
  quotation could win" defect nor the deserialization failure was discoverable
  from the unit tests, which were green throughout. Both were surfaced by
  driving the real HTTP endpoint with a deliberately adversarial payload
  (cheapest supplier suspended, cheapest alternative partial, one quote expired)
  and by comparing the agent's raw answer with what the API had stored.
- **Proved causation instead of guessing.** The deserialization diagnosis was
  established from four independent facts: the Python answer was correct, the
  stored recommendation was all-null, the "success" log line was present, and no
  "unreachable" warning was. The conclusion followed from evidence, not
  intuition.
- **Checked side effects.** After `npm run build` rewrote `dist/`, it confirmed
  the output hashes were byte-identical to what was committed and that the tree
  stayed clean.
- **Resisted an over-broad change.** When the `/health` payload mismatched the
  checklist, it first grepped the codebase for consumers before concluding the
  documentation was the wrong side — and it also caught that Step 2 of the
  intended fix would have broken an existing test.

## 4. Where the AI tooling needed human correction

- **A strict "do not run tools" instruction had to be refused.** Acknowledging
  "Component 2 is 100% complete" would have asserted passing tests and pushed
  commits that had not been checked.
- **Scope discipline needed.** It initially wanted to patch the Python bugs
  unprompted; the branch was already pushed and graded, so the edits were
  deferred until explicitly approved.
- **Two claims had to be walked back.** `dotnet test` without a project path
  failed with `MSB1003` (no solution file at the repo root) and produced a
  silently empty result; and the HTTP 400 root cause was reported before a
  reproduction attempt existed.
- **Unrequested state changes.** The verification mutated the local dev database
  (supplier status changed, quotations deleted/added, a PO created). This was
  disclosed, but it is a real hazard of letting an agent run against a database.
- **Partial verification only.** The React UI was never clicked in a browser;
  the "end-to-end" run was API-level. Visual rendering was not verified.

## 5. Lessons

1. **Green tests do not mean a working system.** All 37 tests passed while the
   two most important features (expired-quote exclusion, agent→API integration)
   were broken. The tests that mattered were integration tests that did not
   exist yet.
2. **Cross-language boundaries are where contracts break.** The request DTO was
   deliberately snake_case to match the agent, and the response DTO was
   PascalCase with no mapping — the asymmetry is invisible until you look at the
   wire format. A schema test on the actual HTTP boundary would have caught it;
   one has since been added (`AgentRecommendationSchemaTests.cs`), stubbing the
   agent's HTTP handler so the boundary is tested without the network.
3. **Defense in depth is what made the failure survivable.** The C# validation
   agent rejected every bad recommendation, so the integration bug produced
   "workflow failed" instead of a wrong purchase order.
4. **Degrade loudly, not silently.** A dead agent silently falls back rather
   than erroring — resilient, but it also hides an outage behind a working
   system.
5. **An agent must not write data it is not accountable for** — hence the
   approval gate and the read-only exposure of `purchase_orders` to Component 3.

## 6. Reflection (draft — personalise before submission)

<!--
  DRAFT SCAFFOLD. The claims above in sections 2–5 are factual and verifiable;
  the wording below is offered as a starting point. Edit it so it reads as your
  own account of your own decisions, and delete this comment.
-->

Working with an AI coding agent on Component 2 changed what I could verify, not
just how fast I wrote code. The most valuable thing it did was **not** writing
code — it was refusing to accept "everything is done" on my say-so. When I asked
it to confirm the component was complete, it declined and asked to verify first,
and that verification is what found a bug in the AI agent itself: a quotation
marked invalid was being warned about as "excluded" while still winning the
recommendation. That is exactly the failure the spec (§5.3) is supposed to
prevent, and all 37 existing tests were green while it happened.

The second finding was more instructive. Every one of the 50 procurement
workflows had failed, and no purchase order had ever been created, even though
all the tests passed. The cause was a mismatch between how the C# API named its
fields and how the Python agent named its — the request direction had been
carefully aligned and the response direction had not. What I take from that is
that an integration between two languages needs a test on the boundary itself,
not just on either side of it. The fix was three lines, but finding it took
running the real system, not reading the real system.

There are things I would do differently. I let the agent change a development
database during verification, which was necessary to reproduce the scenario but
should have been sandboxed first. I also accepted an "HTTP 400 on success" as
explained for a while before admitting I had not actually reproduced it, and I
eventually recorded it as an open question in the ADR instead of guessing a
cause. That discipline — separating what I proved from what I suspect — is the
part of this exercise I expect to keep using.

