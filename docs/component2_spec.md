# Component Specification
## Component 2: Supplier, Quotation & Procurement Management
### BuildWise — Construction Materials Procurement, Delivery and Quality Management System

**Scope statement (from the scenario):** Evaluate eligible quotations and create a purchase order only after the AI recommendation passes deterministic validation and receives authorized (human) approval. This component owns `suppliers`, `quotations`, `quotation_items`, `purchase_orders`, `purchase_order_items`, and the Component-2 slice of `agent_workflows` / `agent_workflow_steps` / `agent_approvals`. It **consumes** approved `material_requests` / `material_request_items` from Component 1 and **hands off** approved `purchase_orders` to Component 3 (read-only for them).

---

## 1. User Roles (relevant to this component)

| Role | What they can do in this component |
|---|---|
| **Procurement Officer** | Create/edit/deactivate suppliers, record quotations against approved material requests, enter quotation items, start the Quotation & Supplier Analysis Agent workflow, view comparisons and AI recommendations, cannot approve procurement or create a PO themselves. |
| **Procurement Manager** | Everything a Procurement Officer can view, plus: review the AI recommendation, structured rationale, warnings and validation results, and record **Approve / Reject / Request Revision** on the agent workflow. Only their approval unlocks purchase-order creation. |
| **Site Engineer / Site Officer** | Read-only, and only for their own project: sees procurement status of their material request (e.g. "Procurement in progress", "PO created") via Flutter — no supplier or quotation detail. |
| **Administrator** | User/role administration only (shared component, not owned by Component 2), but implicitly controls who can act as Procurement Officer/Manager via `roles`/`user_roles`. |
| **Quality Inspector** | No access to this component. |

Role checks are enforced server-side on every endpoint via the shared JWT/role middleware (Component 2 does not re-implement authentication, only authorizes against roles already issued).

---

## 2. Supplier Management Features

- **Create supplier**: name, contact_person, email, phone, address, status (defaults to `Active`).
- **Edit supplier** details (Procurement Officer or Manager).
- **Change supplier status**: `Active → Inactive/Suspended` and back. Status changes are logged (who/when) because status is a hard gate in procurement business rules (see §5) and in AI eligibility filtering.
- **Deactivate, not delete**: suppliers are never hard-deleted once they have at least one quotation, to preserve traceability for audits — status is set to `Inactive`/`Suspended` instead.
- **List/search/filter suppliers**: by name, status, and (optionally) material category they've historically quoted for.
- **Supplier quotation history**: a read-only view of all past quotations for a supplier, to support the "supplier-quality evidence" the AI agent references in later inspection stages (Component 4 reads this, does not own it).

---

## 3. Quotation Request & Quotation Comparison Workflow

**Trigger:** a `material_request` has already passed request approval in Component 1 (`material_request.status = Approved`). Component 2 cannot act on a request that hasn't reached that state.

### 3.1 Recording quotations
1. Procurement Officer opens an approved material request in React.
2. For each supplier approached, the Officer creates a `quotation` record: `material_request_id`, `supplier_id`, `quotation_date`, `valid_until`, `status = Submitted`.
3. For each `material_request_item` covered, the Officer adds a `quotation_item`: `quotation_id`, `material_request_item_id`, `quantity`, `unit_price`.
4. `quotations.total_amount` is **calculated by the API** (sum of `quantity × unit_price` across items), never entered manually, so it can be verified as a deterministic rule (§5).
5. A quotation may cover only part of a request's quantity (as in the scenario's Supplier C, quoting 200 of 250 bags) — this is allowed to be *recorded*, but affects eligibility in comparison/selection.
6. Quotation `status` transitions: `Submitted → UnderReview → Selected` (winner) or `Rejected` (losers, on PO creation) / `Expired` (past `valid_until`, system-set).

### 3.2 Comparison
- React shows a **side-by-side comparison table**: one row per requested material item, one column per quotation, showing quantity offered, unit price, supplier status, validity, and computed total — plus a coverage indicator ("covers 250/250" vs "covers 200/250").
- Comparison is available manually (procurement staff can compare without AI) **and** is the same structured data set fed to the Quotation & Supplier Analysis Agent.

---

## 4. Supplier Selection Process

1. **Eligible quotation filter** (deterministic, before AI even runs): quotation belongs to the request, supplier exists, quotation not expired.
2. **Quotation & Supplier Analysis Agent** (Agentic AI) runs over eligible quotations and produces:
   - a **recommended supplier + quotation**,
   - **ranked alternatives**,
   - a short structured rationale/evidence summary (not chain-of-thought),
   - **warnings** (e.g. "Supplier B is Suspended", "Supplier C covers only 200/250 units").
3. **Deterministic backend validation** (ASP.NET Core, hard gate — see §5) re-checks the recommendation independently of the AI output.
4. If validation passes, the `agent_workflow.status` becomes `AwaitingApproval` and is routed to the Procurement Manager in React.
5. **Procurement Manager decision** (`agent_approvals`): Approve / Reject / Request Revision.
   - **Approve** → PO creation is unlocked (§4.6).
   - **Reject** → workflow ends as `Failed`/closed; Officer must record new quotations or re-run the agent.
   - **Request Revision** → workflow returns to Officer/agent for another pass (e.g. after adding a missing quotation).
6. **Purchase order creation**: only after an `Approved` `agent_approvals` decision, ASP.NET Core creates the `purchase_order` from the selected `quotation`, and `purchase_order_items` from that quotation's `quotation_items`. The losing quotations for that request are set to `Rejected`.

Manual override note: procurement staff are never *forced* to accept the AI recommendation — the Manager's Approve/Reject/Revision decision is the actual authorization; the AI only recommends.

---

## 5. Procurement Business Rules (deterministic, enforced by ASP.NET Core)

These mirror exactly the "Deterministic validation" list in the scenario, scoped to this component:

1. The `material_request` exists and has `status = Approved`.
2. The recommended `supplier` exists and has `status = Active` (Suspended/Inactive suppliers are always excluded, regardless of price).
3. The recommended `quotation` exists and `valid_until >= today` (not expired).
4. Every `quotation_item` on the recommended quotation references a `material_request_item` that belongs to the same `material_request`.
5. The recommended quotation's total covering quantity per item **≥** the corresponding `material_request_item.requested_quantity` (a quotation that only partially covers a line is not eligible to be recommended as the sole winner unless the business explicitly allows split POs — out of scope for v1, flagged as a warning instead).
6. `quotation.total_amount` **must equal** the sum of `quotation_items.quantity × quotation_items.unit_price` (recalculated server-side; mismatches block the quotation from being selectable).
7. The AI recommendation payload must conform to the required structured JSON schema (§10) before it is accepted into `agent_workflow_steps.structured_result`.
8. Purchase-order creation is **blocked** until an `agent_approvals` row exists with `decision = Approved` for that workflow.
9. Once a `purchase_order` is created, its source `quotation` and `material_request` become locked from further edits (no silent changes after PO creation — same principle Component 1 applies to approved requests).
10. A `purchase_order` cannot be created twice from the same `material_request` (idempotency: check for an existing non-cancelled PO first).

---

## 6. Database Entities (from the shared ERD)

Component 2 **owns** (full CRUD via this component's API):

| Entity | Key columns |
|---|---|
| `suppliers` | id, name, contact_person, email, phone, address, status (`supplier_status`: Active/Inactive/Suspended), created_at, updated_at |
| `quotations` | id, material_request_id → material_requests, supplier_id → suppliers, quotation_date, valid_until, status (`quotation_status`: Submitted/UnderReview/Selected/Rejected/Expired), total_amount, created_at, updated_at |
| `quotation_items` | id, quotation_id → quotations, material_request_item_id → material_request_items, quantity, unit_price |
| `purchase_orders` | id, quotation_id → quotations, order_date, expected_delivery_date, status (`purchase_order_status`: Created/Confirmed/InProgress/Completed/Cancelled), total_amount, created_at, updated_at |
| `purchase_order_items` | id, purchase_order_id → purchase_orders, quotation_item_id → quotation_items, ordered_quantity, unit_price |

Component 2 **partially owns** (writes its own workflow rows, shared table structure):

| Entity | Notes |
|---|---|
| `agent_workflows` | filtered by `material_request_id`; this component creates rows for the Quotation & Supplier Analysis Agent |
| `agent_workflow_steps` | `agent_role = "QuotationSupplierAnalysisAgent"`; `structured_result` / `validation_result` jsonb hold the recommendation + validation output |
| `agent_approvals` | Procurement Manager's Approve/Reject/RevisionRequested decision on this workflow |

Component 2 **reads only** (owned elsewhere):

| Entity | Owned by |
|---|---|
| `material_requests`, `material_request_items`, `approvals` | Component 1 |
| `users`, `roles`, `user_roles` | Shared/Admin |
| `projects`, `materials` | Component 1 |

---

## 7. API Endpoints (ASP.NET Core)

All endpoints require a valid JWT; role restrictions noted per endpoint.

### Suppliers
| Method | Route | Role(s) | Notes |
|---|---|---|---|
| GET | `/api/suppliers` | Officer, Manager | filter by status/name, paginated |
| GET | `/api/suppliers/{id}` | Officer, Manager | includes quotation history summary |
| POST | `/api/suppliers` | Officer, Manager | create |
| PUT | `/api/suppliers/{id}` | Officer, Manager | update details |
| PATCH | `/api/suppliers/{id}/status` | Officer, Manager | Active/Inactive/Suspended, logged |

### Quotations
| Method | Route | Role(s) | Notes |
|---|---|---|---|
| GET | `/api/material-requests/{id}/quotations` | Officer, Manager | all quotations for a request, with computed totals |
| GET | `/api/quotations/{id}` | Officer, Manager | with items |
| POST | `/api/material-requests/{id}/quotations` | Officer | blocked unless request is `Approved` |
| POST | `/api/quotations/{id}/items` | Officer | add/update quotation items; recalculates total_amount |
| DELETE | `/api/quotations/{id}` | Officer | only while `status = Submitted`/`UnderReview` |
| GET | `/api/material-requests/{id}/quotations/compare` | Officer, Manager | structured side-by-side comparison payload |

### Agentic AI workflow
| Method | Route | Role(s) | Notes |
|---|---|---|---|
| POST | `/api/material-requests/{id}/procurement-workflow` | Officer | starts Quotation & Supplier Analysis Agent |
| GET | `/api/procurement-workflow/{workflowId}` | Officer, Manager | status, structured_result, validation_result, warnings |
| GET | `/api/procurement-workflow/{workflowId}/history` | Officer, Manager | step-by-step execution history |
| POST | `/api/procurement-workflow/{workflowId}/decision` | **Manager only** | body: `{decision: Approve|Reject|RevisionRequested, comment}` |

### Purchase orders
| Method | Route | Role(s) | Notes |
|---|---|---|---|
| POST | `/api/procurement-workflow/{workflowId}/purchase-order` | system-triggered on Manager Approve, or Manager-initiated | enforces rules §5.8–5.10 |
| GET | `/api/purchase-orders` | Officer, Manager | filter/search/pagination |
| GET | `/api/purchase-orders/{id}` | Officer, Manager, (read-only for Component 3 / Site roles) | with items |
| PATCH | `/api/purchase-orders/{id}/status` | Officer, Manager | Confirmed/InProgress/Completed/Cancelled |

---

## 8. React Screens (web — procurement & management)

1. **Supplier List** — searchable/filterable table, status badges, "Add Supplier" action.
2. **Supplier Detail / Edit** — profile form + quotation history tab.
3. **Approved Requests Queue** — approved material requests awaiting quotations, per project.
4. **Quotation Entry Form** — select supplier, enter items against request lines, live total calculation.
5. **Quotation Comparison View** — side-by-side table (per §3.2), "Run AI Analysis" button.
6. **AI Recommendation Review** — recommended supplier, ranked alternatives, rationale, warnings, validation results, execution-history timeline.
7. **Procurement Approval Panel** (Manager only) — Approve / Reject / Request Revision with comment box.
8. **Purchase Order List & Detail** — status, items, linked quotation/request, expected delivery date.
9. **Procurement Dashboard** — pending quotations, workflows awaiting approval, recent POs (uses search/filter/sort/pagination as required by scenario).

## 9. Flutter Screens (mobile — site, read-only for this component)

1. **Request Procurement Status** — on the Site Engineer's material request detail screen, a status section showing: "Quotations in progress" / "Awaiting manager approval" / "Purchase Order Created (PO #...)" — no supplier prices or names shown, per role scoping.
2. **Push notification handling** — receives FCM notifications for "procurement recommendation awaiting approval" and "purchase order created" (notification content only; no procurement actions taken on mobile).

*(No supplier/quotation entry or editing screens exist in Flutter — that is explicitly a React/office responsibility per the scenario.)*

---

## 10. Agentic AI Contribution — Quotation & Supplier Analysis Agent

- **Input (structured):** eligible quotations for the request, each with supplier status, quotation validity, quantity coverage per item, unit prices, computed totals.
- **Output (structured JSON schema, stored in `agent_workflow_steps.structured_result`):**
```json
{
  "recommended_quotation_id": 0,
  "recommended_supplier_id": 0,
  "rationale": "string",
  "ranked_alternatives": [
    {"quotation_id": 0, "supplier_id": 0, "rank": 1, "reason": "string"}
  ],
  "warnings": ["string"]
}
```
- **What it does:** filters out ineligible suppliers/quotations, ranks the rest, explains the choice in a short evidence summary — mirrors the scenario's example (Supplier B excluded for being Suspended, Supplier C flagged for partial coverage, Supplier A recommended).
- **What it never does:** approve anything, write to `purchase_orders`, or bypass deterministic validation (§5) or Manager approval (§4.5). Only observable structured summaries are stored — no raw model chain-of-thought.
- **Failure handling:** if the LLM call fails or returns a schema-invalid payload, `agent_workflow_steps.status = Failed`, `error_message` is recorded, and the workflow does **not** advance to `AwaitingApproval`.

---

## 11. Required Tests

### Unit tests (business rules, §5)
- Quotation total recalculation matches sum of items (accept/reject mismatches).
- Suspended/Inactive supplier is excluded from eligible recommendation set.
- Expired quotation (`valid_until < today`) excluded.
- Partial-coverage quotation flagged, not silently accepted as sole winner.
- PO creation blocked without an `Approved` `agent_approvals` row.
- PO creation blocked if request is not `Approved`.
- Duplicate PO creation for the same request/quotation is rejected.

### Integration tests (API)
- Full flow: create supplier → record 3 quotations (mirroring the scenario's cement example) → run AI workflow → Manager Approve → PO created with correct items/total.
- Manager Reject/RevisionRequested paths do not create a PO.
- Role authorization: Officer cannot call the Manager-only decision endpoint (403).
- Site role cannot access supplier/quotation endpoints (403), only the read-only status.

### AI-specific tests
- Schema validation rejects a malformed agent output (missing `recommended_quotation_id`, extra/wrong types).
- Deterministic validation independently re-derives the recommendation eligibility and disagrees correctly when the agent output is stale (e.g. supplier was suspended after the AI ran).
- Workflow correctly transitions `Pending → Running → AwaitingApproval → Completed/Failed`.

### Frontend tests (React)
- Comparison table renders correct totals/coverage per supplier.
- Approval panel only renders action buttons for Manager role.
- Form validation: quotation item quantity/price must be positive; cannot submit a quotation against a non-Approved request.

### End-to-end
- Scenario replay: Site Engineer's 250-bag cement request → 3 quotations (A active/full/525k, B suspended/full/510k, C active/partial-200/cheapest) → AI recommends A with warnings on B and C → Manager approves → PO created for Supplier A, 250 bags, 525,000 → Site Engineer sees "Purchase Order Created" in Flutter.

---

## Integration Notes (with Components 1 & 3)

- **From Component 1:** consume `material_requests`/`material_request_items` strictly in `Approved` state; never write to them.
- **To Component 3:** expose `purchase_orders`/`purchase_order_items` read-only once `status` is `Confirmed` or later — Component 3 uses this as the "expected" baseline for delivery reconciliation. Do not let Component 3 write to these tables.
- **Shared:** authentication/roles, `agent_workflows`/`agent_workflow_steps`/`agent_approvals` table *structure* (shared across all 4 agents) — do not alter columns without team agreement, per the ERD change-control note.
