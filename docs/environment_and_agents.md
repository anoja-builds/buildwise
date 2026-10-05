# Environment and Agent Operation

## Purpose

This guide explains the local environment, PostgreSQL connection, and the Agentic AI subsystem.

## Files

- `.env.example` — safe template that can be committed.
- `.env` — local secret-bearing configuration. It is ignored by Git and must never be submitted.
- `scripts/start-dev.ps1` — loads `.env` and starts the API, four internal agents, and React.
- `scripts/check-services.ps1` — validates PostgreSQL, all four agents, ASP.NET Core, and React.

## PostgreSQL connection

The local development connection is:

```text
Host: 127.0.0.1
Port: 5432
Database: buildwise
Username: postgres
```

The password is stored only in the local `.env` and in the backend's .NET user-secrets. The committed template contains a placeholder.

ASP.NET Core consumes `ConnectionStrings__DefaultConnection`. `start-dev.ps1` builds that value from `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD` when an explicit connection string is not present.

Check the real database without printing credentials:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\check-services.ps1
```

Expected PostgreSQL detail:

```text
buildwise reachable (users=...)
```

## Agentic AI architecture

React and Flutter do not call Python agents directly. They use the protected ASP.NET Core API. ASP.NET Core validates the user and role, loads PostgreSQL facts, calls an internal Python service, validates the result, persists the workflow, and waits for human approval where required.

```text
Flutter or React
      |
      v
ASP.NET Core API + JWT/RBAC + business rules
      |
      +--> PostgreSQL
      |
      +--> port 8001 quotation/supplier analysis
      +--> port 8002 request analysis
      +--> port 8003 delivery discrepancy/risk analysis
      +--> port 8004 quality/NCR analysis
      |
      v
AgentWorkflow + AgentWorkflowStep summaries
      |
      v
Authorized manager approves/rejects/revises
      |
      v
Purchase Order only after approval
```

### The four internal services

| Port | Service | Responsibility |
|---:|---|---|
| `8001` | `quotation_agent` | Filters eligible quotations and ranks suppliers. |
| `8002` | `request_agent` | Detects urgency, bulk-order, and large-quantity flags. |
| `8003` | `delivery_agent` | Detects shortages and damage; the ASP.NET workflow also records delivery-risk audit steps. |
| `8004` | `quality_agent` | Calculates rejection rate, quality risk, and whether an NCR is required. |

The services use allow-listed tools and structured Pydantic output. ASP.NET Core independently validates business invariants and does not trust an agent merely because its JSON is well formed.


### Assessed procurement workflow

1. A Site Engineer creates a material request.
2. ASP.NET Core validates and persists the request.
3. The Procurement Officer records multiple quotations.
4. ASP.NET Core creates an `AgentWorkflow` with an objective and structured steps.
5. The quotation agent filters suspended/invalid suppliers and ranks full-coverage quotations.
6. ASP.NET Core performs deterministic checks against PostgreSQL.
7. A valid recommendation moves to `AwaitingApproval`.
8. Only an authorized Procurement Manager can approve, reject, or request revision.
9. Purchase-order creation is rejected until approval is satisfied.
10. Agent roles, structured results, validation, timings, and final outcome remain auditable.

### Delivery and quality workflow

After receiving, the backend can persist:

1. A discrepancy-analysis workflow for shortage/damage.
2. A three-step delivery-risk workflow: `DeliveryDataRetrievalAgent` → `DeliveryRiskAnalysisAgent` → `DeliveryRiskValidationAgent`.
3. A quality-risk workflow after inspection.
4. An automatically generated NCR when rejected quantity is greater than zero.

The delivery is saved before advisory risk analysis. If the optional model call fails, the record is not silently deleted: the safe fallback is used and the failure is logged.

## AI modes

### Deterministic/safe-fallback mode — recommended for evaluation

Leave these values empty in `.env`:

```dotenv
GEMINI_API_KEY=
ANTHROPIC_API_KEY=
```

This mode is fully runnable and does not require a paid service. The workflow still uses structured business rules, Pydantic schemas, allow-listed tools, deterministic validation, timeouts, safe failures, and persisted audit state.

### Optional external LLM rationale mode

The quotation agent can use Anthropic only to rewrite a short explanation after deterministic rules have selected a supplier. The LLM cannot approve procurement or create a purchase order.

Create a provider-owned key and place it only in the local `.env`:

```dotenv
ANTHROPIC_API_KEY=your-key-here
ANTHROPIC_MODEL=claude-3-5-haiku-20241022
ANTHROPIC_TIMEOUT_S=8
```

The ASP.NET Core delivery-risk workflow can optionally use Gemini:

```dotenv
GEMINI_API_KEY=your-key-here
```

Nobody can safely provide a universal working API key. A key already present in another application may be invalid, restricted, or belong to someone else. Never paste a production key into a public file, report, APK, or Git repository.

## Start and check

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\start-dev.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\check-services.ps1
```

Verify the quotation agent mode:

```powershell
Invoke-RestMethod http://127.0.0.1:8001/health
```

Expected deterministic-mode fields:

```json
{
  "status": "healthy",
  "service": "quotation_agent",
  "agent": "QuotationSupplierAnalysisAgent",
  "llm_rationale_enabled": false,
  "env_file_loaded": true
}
```

`llm_rationale_enabled: false` means the external explanation model is not configured; it does not mean the Agentic AI subsystem is disabled.

## Security rule

If `.env` or the PostgreSQL password is exposed, rotate the PostgreSQL password, JWT signing key, and external AI key immediately. Do not put secrets in `appsettings.json`, React source, Flutter `--dart-define` values, screenshots, or the submitted APK.
