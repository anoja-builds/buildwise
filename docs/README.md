# Component 2 documentation index

Everything for **Component 2 — Supplier, Quotation & Procurement Management** (SE3090 group assignment).

| Document | What it is for |
|---|---|
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
| [`scripts\start-dev.ps1`](../scripts/start-dev.ps1) | Builds (optional) and starts the API, the Python agent service and the Vite web server in the background; logs to `logs\`, then calls the checker. |
| [`scripts\check-services.ps1`](../scripts/check-services.ps1) | Reports whether each of the four services is up (ports + real HTTP calls + a database query). Exit code 0 = all up. |
| [`scripts\smoke-test.ps1`](../scripts/smoke-test.ps1) | Proves the stack *works*: login, suppliers, purchase orders and the agent's cement-scenario decision, with pass/fail assertions. |
| [`scripts\stop-dev.ps1`](../scripts/stop-dev.ps1) | Stops the three background services (leaves PostgreSQL running). |

Test suites referenced throughout the docs (all green on `feature/supplier-procurement` @ `6161ecc`):

| Suite | Command | Count |
|---|---|---|
| Backend (.NET) | `dotnet test backend/BuildWise.Api.Tests` | 15 passed |
| Agent service (Python) | `pytest backend/agent_service/test_quotation_agent.py` | 9 passed |
| Web (React) | `npm test -- --run` in `web/buildwise-web` | 15 passed |
| Mobile (Flutter) | `flutter test` in `mobile/buildwise_mobile` | requires the Flutter SDK locally |
