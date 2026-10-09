# BuildWise internal Quality Risk & Non-Conformance Agent

One specialized advisory agent, implemented with FastAPI, LangGraph, LangChain and Gemini.
React/Flutter call ASP.NET Core only. This service has no PostgreSQL driver, connection string,
SQL tool, browser, shell tool, or business-write endpoint. Do not expose it publicly.

## Setup

Verified locally with Python 3.14.3 and the exact versions in requirements.txt:
LangChain 1.4.0, LangGraph 1.2.11, langchain-google-genai 4.4.0,
FastAPI 0.141.1 and Pydantic 2.13.5. The full resolved environment is pinned.

From this directory (PowerShell):

```powershell
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements.txt
Copy-Item .env.example .env
# Fill .env with actual local secrets. Never commit it.
.venv/Scripts/python -m uvicorn app.main:app --host 127.0.0.1 --port 8001
```

Required Python configuration:

- GOOGLE_API_KEY: Gemini credential, kept only on the Python server.
- CHAT_MODEL: configurable model identifier; default gemini-2.5-flash. Availability depends on your provider account.
- QUALITY_AGENT_SERVICE_KEY: a long random shared server-to-server secret. Missing configuration fails closed.

ASP.NET Core configuration (environment, user secrets, or deployment configuration):

- AgentService__BaseUrl: for local development, http://127.0.0.1:8001/.
- AgentService__ApiKey: same shared secret, or QUALITY_AGENT_SERVICE_KEY as a fallback.

ASP.NET's configuration keys are AgentService:BaseUrl and AgentService:ApiKey. Do not send either secret
to React/Flutter. Use private networking and TLS between hosts. This service enables no CORS,
Swagger UI or arbitrary browser-facing routes. GET /health is a liveness check, not Gemini readiness.
POST /quality-risk/analyse requires X-Quality-Agent-Key; it accepts evidence, never DB lookup instructions.

## Public ASP.NET API

- POST /api/quality-risk-agent/inspections/{inspectionId}/analyse
- GET /api/quality-risk-agent/workflows/{workflowId}

The POST has no caller-supplied prompt, provider URL, evidence, or approval instruction. ASP.NET queries
its own database and constructs the evidence. A successful run returns HTTP 200; a persisted failed run
returns HTTP 502 with its workflow ID, sanitized errors and earlier evidence. Invalid subject IDs return
404; incomplete inspections return 409. The GET returns the persisted ordered steps.

Shared JWT/RBAC is not implemented here. These routes follow the current repository authentication
state; group authentication must authorize analysis and audit access before deployment. No user ID
is fabricated: InitiatedByUserId remains null until authenticated identity is integrated.

## Evidence and scope

ASP.NET requires Completed, a consistent OverallDecision, full positive-received item coverage,
valid supplier provenance, and 1..100 inspection items. Current item arithmetic is decimal:

- rejectionRate = rejected / (accepted + rejected)
- inspectionCoverage = (accepted + rejected) / received
- 0 < accepted + rejected <= received; damaged quantity is not subtracted.

History includes at most 20 earlier completed inspection events for the same supplier, using
UpdatedAt as the existing completion marker (there is no dedicated CompletedAt on Inspection).
Re-inspections are distinct events, never extra delivered volume. History exposes decisions and
item/rejected-item counts; no quantity-weighted percentage combines different material units.

Prior NCR count covers records created before the current inspection's completion marker; the
latest 50 summaries are included, with a truncation flag. Their severity/status are current values
at evidence collection, not reconstructed historical states. Up to 20 discrepancy deliveries and
20 recorded DeliveryIssues are supplied, with truncation flags; they may include the current delivery.
Discrepancy status is not a full delivery status history. Empty history is explicitly insufficient evidence.
Text is bounded (remarks/issue/action 2000 characters, condition 100). Evidence IDs and snapshot time
are persisted for audit; no laboratory values, certifications or external ratings are invented.

## Exact graph and narrow tools

START -> agent/model -> conditional edge:

- evidence tool calls -> tools -> observations -> agent/model
- final submission -> final Pydantic validation -> END

The initial model context contains only the objective and inspection ID. It never receives the complete
evidence as a one-shot prompt. Three request-local, read-only LangChain tools provide typed observations:

1. get_current_inspection_evidence(): no arguments.
2. get_supplier_quality_history(limit): strict integer 1..20, this supplier only.
3. get_prior_non_conformance_summary(): no arguments.

The model chooses evidence-call order and history limit, but must consult all three categories before
completion. QualityRiskRecommendation is bound as a Pydantic final-submission schema, not a business
execution tool. Plain final JSON is also accepted only if it passes the same strict Pydantic schema.
There are no extra autonomous agents. Nodes are workflow operations of the same Quality agent.

Hard bounds: 6 model turns, 6 evidence tool calls, 90-second overall Python timeout, 30-second provider
request timeout, zero automatic provider retries, and 4096 output tokens per model call. ASP.NET's
100-second request timeout includes reading a response capped at 1 MB. These bounds limit execution,
not a guarantee of a particular monetary charge. LangGraph recursion is capped independently at 20.

## Guardrails and output

Pydantic input models bound quantities, sizes, history and item counts. Output models forbid unknown
fields and type coercion. The final recommendation contains inspectionId, riskLevel, riskFlags,
evidenceSummary, ncrRecommended, itemRecommendations and rationaleSummary. NCR suggestions are
item-specific with nullable severity/description/action, rationale and evidence references.

ASP.NET independently validates exact identity, all references, duplicate/foreign item IDs, enums,
lengths, conditional nullability, overall/item recommendation agreement and positive rejected quantity.
Each suggested item cites its own inspection-item reference. NCR recommendation with no rejected
material is rejected. Unknown JSON members, including changed OverallDecision or mutation commands,
are rejected. Invalid output is never repaired into success.

All database text is explicitly untrusted data, wrapped as untrustedEvidence observations. Instructions
inside remarks cannot grant capabilities. Prompt instructions are only one defence: the runner validates
every tool name and argument before execution; tools close over request-local evidence and have no
write/network capabilities. Summaries are advisory natural language and still require human review.
Do not render model/evidence strings as executable HTML.

Machine-readable traces contain only iteration, allow-listed action, validated integer parameters,
success and duration. Model messages/private reasoning are never returned or persisted. Hosted
LangSmith tracing is disabled. Service errors do not echo requests, keys, provider bodies or exceptions.

## Persistence, failure and human control

ASP.NET first persists AgentWorkflow and all three planned AgentWorkflowSteps:
Collect Quality Evidence -> Run Agentic Quality Analysis -> Validate Quality Recommendation.
Step 1 stores evidence, Step 2 stores the structured recommendation and sanitized tool trace, and Step 3
stores deterministic validation. InspectionId is in structured data and the objective; DeliveryId is the
existing workflow field. A workflow ID identifies a run. No schema changes or migrations are included.

Success means Completed with ApprovalStatus Pending and an explicit advisory-only outcome.
There is no AwaitingApproval state or Quality-specific approval endpoint. Users review suggestions
and explicitly call the existing NonConformance API; the agent never calls it.

Missing credentials, unavailable Python/provider, rate limits, timeout, invalid tool calls, iteration caps,
invalid JSON and failed business validation mark the current step Failed, later steps Skipped, and the
workflow Failed. Earlier evidence is retained. There is no deterministic recommendation fallback.
Database outages can prevent audit persistence; the public API returns a generic error in that case.

The graph is request-scoped: no human pause/resume or hidden-message checkpoint store is added.
Durable audit state lives in ASP.NET/PostgreSQL, not Python. A process crash may leave a persisted
Running step; automatic recovery/resume is outside this step. Retry by explicitly starting a new run;
never represent an interrupted run as Completed.

The existing repository migrations/snapshot lag the current model (including workflow DeliveryId).
A matching database schema is required to run end-to-end. Do not generate/apply a migration as part
of this service setup without the separately agreed group migration work.

## Verification (no Gemini calls needed)

From this directory:

```powershell
.venv/Scripts/python -m unittest discover -s tests -v
.venv/Scripts/python -m pip check
```

From the repository root:

```powershell
dotnet build backend/BuildWise.Api/BuildWise.Api.csproj
dotnet run --project backend/BuildWise.QualityAgent.Tests/BuildWise.QualityAgent.Tests.csproj
```

Python tests run the real LangGraph with a fake model, validate FastAPI startup/health/auth/request
handling, Pydantic contracts, bounded tools, iteration/tool caps, timeouts and sanitized failures.
The .NET dependency-free test executable tests authoritative validation using shared JSON fixtures.
These are offline checks, not proof of live Gemini or PostgreSQL end-to-end success.
