# AI Usage Log — Component 1: Material Request & Approval Management
**Student:** Peiris DPSS · **ID:** IT24XXXXX · **Date range:** 2026-09-10 – 2026-09-30

## 1. Declaration
I used AI tools (GitHub Copilot, Claude, and ChatGPT) to assist in the development of Component 1 (Material Request & Approval Management). All architectural and domain decisions were my own. All generated code, database configurations, and test assertions were reviewed, tested, and modified before committing to version control.

---

## 2. Session Log

| # | Date | What was built / investigated | Issues found | How verified | Evidence |
|---|---|---|---|---|---|
| 1 | 2026-09-10 | Domain entity design (`MaterialRequest`, `MaterialRequestItem`, `Approval`, `MaterialRequestHistory`) | Copilot omitted composite FK constraints for multi-item requests. | Configured EF Core `EntityTypeConfiguration` manually. | `MaterialRequest.cs`, `MaterialRequestItem.cs` |
| 2 | 2026-09-14 | .NET 8 `MaterialRequestsController` CRUD and approval workflow | Status transition guard was missing on re-approval; allowed duplicate approvals. | Added state transition checks and unit tests. | `MaterialRequestsController.cs` |
| 3 | 2026-09-18 | `RequestAnalysisAgent` (Python FastAPI on Port 8002) for site urgency and quantity risk analysis | Agent initially returned unparsed text instead of typed JSON flags. | Implemented Pydantic schema validation for output structure. | `request_agent.py`, `test_request_agent.py` |
| 4 | 2026-09-22 | React `MaterialRequestsPage.jsx` with search, filter, and review drawer | Initial table rendered without responsive mobile wrapping or loading spinners. | Added loading states, status badge tones, and client-side filters. | `MaterialRequestsPage.jsx`, `MaterialRequestsPage.test.jsx` |
| 5 | 2026-09-25 | Flutter mobile screen (`material_requests_screen.dart`) | Form allowed negative quantities and lacked unit selection. | Added input validation and unit dropdown with discrete integer checks. | `material_requests_screen.dart` |
| 6 | 2026-09-28 | RBAC integration and approval security (`Policies.MaterialRequestApprovalOnly`) | SiteEngineer was initially able to trigger approval endpoint if token was forged. | Enforced ASP.NET Core policy returning `403 Forbidden`. | `RbacAuthorizationTests.cs` |
| 7 | 2026-09-30 | Full lifecycle E2E testing & documentation | Material request history rows were not written on revision status. | Updated `MaterialRequestService.cs` to commit audit history. | `MaterialRequestServiceTests.cs`, `FullLifecycleScenarioTests.cs` |

---

## 3. Reflection

### What the AI tools did well
- **Scaffolding:** Rapidly generated initial DTO boilerplate and REST controller action templates in C#.
- **Test Generation:** Assisted in creating edge-case test vectors for quantity boundaries and invalid date submissions.
- **Python Fast-API:** Streamlined FastAPI schema setup for the Request Analysis Agent.

### Where the AI tools needed correction
- **EF Core Mapping:** AI models frequently forgot navigation property includes (`.Include(mr => mr.Items)`), leading to null reference exceptions in query projections.
- **RBAC Security:** AI suggested putting role authorization inside controller bodies rather than using declarative `[Authorize(Policy = "...")]` attributes.
- **State Machine Guardrails:** AI did not implement business rules preventing modifications to already-approved requests.

### Lessons Learned
1. AI output must always be verified against formal domain specifications.
2. Declarative policy-based security in ASP.NET Core is far superior to ad-hoc procedural role checks.
3. Strict schema validation (Pydantic / C# DTOs) is essential at every AI agent boundary.
4. Testing edge cases (negative quantities, past dates, duplicate submissions) catches subtle integration bugs early.
5. Cross-platform API integration requires consistent DTO contract versioning across React and Flutter.

### What I would do differently
In future projects, I would establish contract-first OpenAPI schemas and generate client types automatically before implementing UI screens. I would also write integration tests earlier in the sprint.

---

## 4. Signed Declaration
I confirm that this log is an accurate record of my AI tool usage for SE3090 Assignment 1. I can explain, modify, and debug every line of code submitted under Component 1.

**Student Name:** Peiris DPSS  
**Student ID:** IT24XXXXX  
**Date:** 30 September 2026  
**Signature:** *Peiris DPSS*
