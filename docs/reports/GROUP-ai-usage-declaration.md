# BuildWise — Group AI Usage Declaration

**SE3090 Software Engineering Frameworks · Group Assignment 1**  
**Repository:** https://github.com/IT24102414/build  
**Submission date:** 2026-09-30

---

## Group Declaration

We, the members of the BuildWise project group, declare that:

1. **All AI tool usage is disclosed** in this document and in individual member logs referenced below.
2. **All submitted code was reviewed, understood, and tested** by the student who owns it before committing.
3. **No AI tool was used to produce academic text, reflections, or declarations** — those sections are written by the named student.
4. **No AI tool was given access to exam questions, viva questions, or marking schemes** beyond the publicly distributed SE3090 specification.
5. **All code that AI tools generated or assisted with is identified** in the session logs, including the specific tool, prompt intent, and how the output was verified.
6. **We take individual responsibility** for our respective component code, documentation, tests, and the correctness of what we submit.
7. **The AI tools used include** GitHub Copilot, Anthropic Claude (claude-3-5-haiku-20241022), Google Gemini (Gemini 2.0 Flash), and ChatGPT. Credentials for any live API keys are stored in `.env` (git-ignored) and were never committed to the repository.

---

## AI tool inventory

| Tool | Version / Model | Used by | Primary purpose |
|---|---|---|---|
| GitHub Copilot | GPT-4o (inline) | All members | Code completion, boilerplate scaffolding |
| Anthropic Claude | claude-3-5-haiku-20241022 | Theebika (C2) | LLM rationale in QuotationSupplierAnalysisAgent (2–3 sentences per workflow, sanitised before storage) |
| Google Gemini | Gemini 2.0 Flash | Anoja (C4), group | QualityRiskAnalysisAgent rationale, documentation drafting |
| ChatGPT | GPT-4o | Peiris (C1), Ramya (C3) | Documentation, ERD review, debugging discussion |
| Antigravity (AGY) | Claude Sonnet 4.6 | All | Code generation, ADR writing, CI configuration, deployment config |

---

## Individual AI usage logs

| Student | Component | Log file |
|---|---|---|
| Peiris DPSS | Component 1 — Material Request & Approval Management | [`docs/reports/IT24XXXXX-Peiris-ai-usage-log.md`](IT24XXXXX-Peiris-ai-usage-log.md) |
| Theebika | Component 2 — Supplier, Quotation, RFQ & Procurement | [`docs/reports/IT24102414-ai-usage-log.md`](IT24102414-ai-usage-log.md) |
| Ramya | Component 3 — Delivery & Material Receiving Management | [`docs/reports/IT24102513-Ramya-ai-usage-log.md`](IT24102513-Ramya-ai-usage-log.md) |
| Anoja | Component 4 — Quality Inspection & Non-Conformance | [`docs/reports/IT24XXXXX-Anoja-ai-usage-log.md`](IT24XXXXX-Anoja-ai-usage-log.md) |

---

## Boundaries of AI use in BuildWise

### What AI tools did

- Scaffolded entity classes, EF Core configurations, and controller boilerplate from spec requirements
- Generated initial test stubs (reviewed and corrected before use)
- Produced the LLM rationale sentences inside the QuotationSupplierAnalysisAgent and QualityRiskAnalysisAgent (2–3 sentences each, sanitised, never used for decisions)
- Suggested ADR structure and helped draft ADR content (reviewed against actual code before committing)
- Generated CI workflow YAML, Dockerfile, and deployment configs (reviewed and tested)
- Assisted with writing documentation sections (reviewed for accuracy against code)

### What AI tools did NOT do

- Make procurement decisions — all supplier selection is deterministic C# code; the LLM only explains
- Approve purchase orders — human approval (ProcurementManager) is required before any PO is created
- Design the system architecture — the four-component structure, agent responsibilities, and database schema were designed by the group
- Write viva answers — each student is responsible for explaining their own work
- Access production credentials or deployment secrets

### Where AI tools failed and were corrected

- **Deserialization mismatch (Component 2):** Agent emitted snake_case JSON; C# DTO expected PascalCase — all 50 workflows silently failed. Fixed in commit `6161ecc`.
- **Eligibility gate missing (Component 2):** Expired quotations entered the ranked list as warnings instead of being excluded. Fixed in `38bbca9`.
- **Degraded path too permissive (Component 2):** Fallback path allowed suspended suppliers. Fixed with regression test.
- **Schema type mismatch (Component 4):** Quality agent returned severity as string ("high") instead of float (0.0–1.0). Fixed in quality_agent.py.
- **Port confusion (Component 3):** DeliveryAgentService initially called port 8001 (quotation agent) instead of 8003.
- **RBAC filter gap (Component 1):** SiteEngineer role returned all requests instead of own requests only. Fixed in MaterialRequestsController.

---

## Group sign-off

| Member | Signature | Date |
|---|---|---|
| Peiris DPSS | _[sign before submission]_ | 2026-09-30 |
| Theebika (IT24102414) | _[sign before submission]_ | 2026-09-30 |
| Ramya (IT24102513) | _[sign before submission]_ | 2026-09-30 |
| Anoja | _[sign before submission]_ | 2026-09-30 |

---

*This declaration was prepared collectively and reviewed by all group members before submission.*
