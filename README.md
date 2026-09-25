# BuildSupply LK

## Construction Materials Procurement, Delivery and Quality Management System

BuildSupply LK is the product name for the integrated SE3090 - Software Engineering Frameworks group assignment. The existing `BuildWise` API/namespace and local test identifiers are retained for backward compatibility with the running implementation.

The system manages the complete material lifecycle: material request → approval → RFQ/quotation → supplier recommendation → manager approval → purchase order → delivery → receiving → quality inspection → non-conformance resolution.

## Four components

1. Material Request & Approval Management
2. Supplier, Quotation, RFQ & Procurement Management
3. Delivery & Material Receiving Management
4. Quality Inspection & Non-Conformance Management

## Technology stack

- ASP.NET Core Web API
- PostgreSQL with EF Core migrations
- React with React Router and Context authentication
- Flutter with secure token storage and operational screens
- Internal Python Agentic AI services on ports 8001–8004
- Optional SMTP and optional external LLM rationale integration

## Verified local workflow

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/start-dev.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-rbac.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-rfq-admin.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-full-journey.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-planning-agent.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-validation-agent.ps1
```

The current local evidence includes 85 backend tests, 27 Python agent tests, 21 React tests, 4 Flutter tests, 24 RBAC checks, 15/15 full business-journey checks, 13-check Procurement Planning Agent and 10-check Validation & Safety Agent live contract verifications, live RFQ/Administrator checks, and all seven local services running.

## Team ownership

| Member | Primary component |
|---|---|
| Peiris DPSS | Material Request & Approval Management |
| Theebika | Supplier, Quotation, RFQ & Procurement Management |
| Ramya | Delivery & Material Receiving Management |
| Anoja | Quality Inspection & Non-Conformance Management |

## Current submission boundary

The local integrated system is technically working. Public deployment URLs, a Firebase Cloud Messaging project, a real Android APK/device walkthrough, remaining ADRs, and the consolidated report/AI declarations remain external/manual submission evidence and are not claimed as complete in this repository.

See:

- [`docs/project_verification_guide.md`](docs/project_verification_guide.md)
- [`docs/assignment_compliance_audit.md`](docs/assignment_compliance_audit.md)
- [`docs/environment_and_agents.md`](docs/environment_and_agents.md)
