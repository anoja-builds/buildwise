# Component 2 documentation index

Everything for **Component 2 — Supplier, Quotation & Procurement Management** (SE3090 group assignment).

| Document | What it is for |
|---|---|
| [project_verification_guide.md](project_verification_guide.md) | **Complete-project release guide:** current pass/fail audit; API/database, React, Flutter, all four agents, four business components, RFQ, administration, RBAC, and manual evidence. |
| [assignment_compliance_audit.md](assignment_compliance_audit.md) | **SE3090 rubric evidence index:** verified local evidence, remaining submission blockers, and excellent-mark priorities. |
| [component2_spec.md](component2_spec.md) | The component specification: roles, features, business rules (§5), entities, endpoints, screens, agent contract, required tests. |
| [component2_build_guide.md](component2_build_guide.md) | Step-by-step implementation guide, phase by phase, with code. |
| [component2_setup_guide.md](component2_setup_guide.md) | Manual setup and verification: prerequisites, migrations, configuration, running the four apps, the end-to-end walkthrough. |
| [adr/0001-quotation-analysis-agent-and-workflow-state.md](adr/0001-quotation-analysis-agent-and-workflow-state.md) | **ADR-001** — why the Quotation Analysis Agent is a Python FastAPI microservice, and how workflow state is persisted and audited in PostgreSQL. |
| [reports/component2_ai_usage_log_and_reflection.md](reports/component2_ai_usage_log_and_reflection.md) | Individual AI usage log (per use: tool, output, change, verification) and the one-page reflection on what the AI tools did well, where they failed, and what was learned. |
| [handoff/component2_to_component3_handoff.md](handoff/component2_to_component3_handoff.md) | Handoff to Component 3 (Delivery & Material Receiving): the `purchase_orders` contract, read API, and the rules for consuming it. |
| [BuildWise_ERD.dbml](BuildWise_ERD.dbml) · [BuildWise_ERD.pdf](BuildWise_ERD.pdf) · [BuildWise_Scenario.pdf](BuildWise_Scenario.pdf) | Team-shared ERD schema, ERD diagram, and the assignment scenario all four components build against. |

## Run / check scripts (Windows PowerShell)

| Script | What it does |
|---|---|
| [`scripts\verify-project.ps1`](../scripts/verify-project.ps1) | Runs backend, four-agent, web, Flutter, live service, agent-contract, RBAC, performance, and Component 2 smoke gates; supports `-SkipLive` and `-SkipMobile`. |
| [`scripts\verify-full-journey.ps1`](../scripts/verify-full-journey.ps1) | Writes a fresh C1 → C2 → C3 → C4 scenario and proves all four agents, approval, PO, discrepancy, NCR, persisted history, and final cross-client status. |
| [`scripts\verify-rfq-admin.ps1`](../scripts/verify-rfq-admin.ps1) | Proves the RFQ lifecycle, protected Administrator user/health/audit APIs, and the 403 role boundary. |
| [`scripts\start-dev.ps1`](../scripts/start-dev.ps1) | Starts the API, all four Python agents, Vite, and PostgreSQL. |
| [`scripts\check-services.ps1`](../scripts/check-services.ps1) | Reports whether each service is up (ports + real HTTP calls + a database query). Exit code 0 = all up. |
| [`scripts\smoke-test.ps1`](../scripts/smoke-test.ps1) | Proves the stack *works*: login, suppliers, purchase orders and the agent's cement-scenario decision, with pass/fail assertions. |
| [`scripts\stop-dev.ps1`](../scripts/stop-dev.ps1) | Stops the three background services (leaves PostgreSQL running). |

Every document is checked against the current working tree, not only the historical Component 2 baseline. The current audit is in [`project_verification_guide.md`](project_verification_guide.md); its release decision is **CONDITIONAL GO** until the listed external and manual evidence is completed.
