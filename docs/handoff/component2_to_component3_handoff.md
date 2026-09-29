# Component 2 → Component 3 handoff — Delivery & Material Receiving

**From:** Component 2 (Supplier, Quotation & Procurement Management) · **To:** Component 3 (Delivery & Material Receiving)
**Branch:** `feature/supplier-procurement` · **Latest code commit:** `6161ecc` · **Date:** 2026-09-20
**Status:** current authorization contract updated for the five-role model on 2026-09-28. The original branch/commit/date above are historical handoff evidence.

## 1. What is ready for you

`purchase_orders` and `purchase_order_items` are populated only by Component 2, only after a Procurement Manager approves an AI recommendation that passed deterministic validation (spec §4.6). You can treat them as the "expected" baseline for delivery reconciliation.

| `purchase_orders` | Type / values |
|---|---|
| `id` | int, PK |
| `quotation_id` | FK → `quotations.id` (the winning quotation) |
| `order_date` | date |
| `expected_delivery_date` | date, currently `order_date + 7 days` |
| `status` | `Created` / `Confirmed` / `InProgress` / `Completed` / `Cancelled` |
| `total_amount` | numeric(18,2), copied from the winning quotation |
| `created_at`, `updated_at` | timestamps |

| `purchase_order_items` | Type / values |
|---|---|
| `id` | int, PK |
| `purchase_order_id` | FK → `purchase_orders.id` |
| `quotation_item_id` | FK → `quotation_items.id` (traces back to the request line and material) |
| `ordered_quantity` | numeric — **use this as the expected quantity per delivery** |
| `unit_price` | numeric — price agreed at award time |

## 2. Current API contract (2026-09-28)

The approved Scenario supersedes the older handoff authorization model.
SiteEngineer is the site-receiving actor; ProcurementManager reviews material
requests. Refer to [the integrated delivery contract](component3-handoff.md)
for the endpoint permission matrix.

SiteEngineer reads `GET /api/Deliveries/expected` and
`GET /api/Deliveries/{id}`, then submits quantities to
`POST /api/Deliveries/{id}/receive`. These responses contain limited PO/material
context, including `orderedQuantity`, `previouslyReceivedUsableQuantity` and
`outstandingQuantity`. No pricing or quotation data is exposed.

Full `GET /api/purchase-orders` and `GET /api/purchase-orders/{id}` remain available
only to ProcurementOfficer, ProcurementManager and Administrator. PO creation
from an approved workflow allows ProcurementManager and Administrator, while
status PATCH allows procurement roles and Administrator subject to state rules.
SiteEngineer cannot access any of these full procurement endpoints.

React and Flutter use the same ASP.NET JWT. Flutter shows Requests/Deliveries for
SiteEngineer and Deliveries/Quality for QualityInspector. UI visibility does not
replace API authorization. Existing Administrator endpoint permissions are preserved.

## 3. How a purchase order comes into existence (so you can seed or reproduce one)

1. A `material_request` reaches `status = Approved` (Component 1).
2. A Procurement Officer records quotations for eligible suppliers, then starts the workflow: `POST /api/material-requests/{id}/procurement-workflow`.
3. The Quotation & Supplier Analysis Agent recommends a supplier (spec §10), and ASP.NET Core independently re-validates the recommendation against every business rule in spec §5.
4. If validation passes, the workflow moves to `AwaitingApproval`; a Procurement Manager calls `POST /api/procurement-workflow/{id}/decision` with `Approve` (the endpoint allows ProcurementManager and Administrator, `403` for the Officer).
5. On `Approve`, the PO and its items are created in a single transaction, the winning quotation becomes `Selected`, losing quotations become `Rejected`, and the requester is notified (email, or a log entry when SMTP is unconfigured).

A PO can never exist without an `Approved` `agent_approvals` row, and a second non-cancelled PO for the same material request is blocked (§5.8, §5.10).

## 4. Component boundaries

- Component 2 owns order creation, pricing, quotation selection, confirmation and cancellation.
- Component 3 reads order items internally and owns receipt-driven PO progress/completion.
  A received delivery cannot be resubmitted; over-receipt and invalid damaged quantities fail.
- Delivery scheduling requires a Confirmed or InProgress PO. Newly created POs must first
  be confirmed by procurement; do not change approval/state rules to skip this step.
- Keep shared workflow/audit table structures and human approval gates intact.
- Link deliveries to the PO and each delivery item to its PO item. Clients obtain material
  and quantity context from the Delivery API, without calling the full PO API.

## 5. Fastest way to verify from your machine

```powershell
# 1. API + agent service (two terminals, both from the repository root)
dotnet run --project backend/BuildWise.Api
python -m uvicorn quotation_agent:app --app-dir backend/agent_service --port 8001

# 2. Login (seeded demo accounts, password Passw0rd!)
$tok = (Invoke-RestMethod -Method Post -Uri http://localhost:5078/api/auth/login `
  -ContentType 'application/json' `
  -Body '{"email":"procurement.officer@buildwise.demo","password":"Passw0rd!"}').token
$h = @{ Authorization = "Bearer $tok" }

# 3. Start the workflow for the seeded cement request (id 1, 250 bags)
Invoke-RestMethod -Method Post -Uri http://localhost:5078/api/material-requests/1/procurement-workflow -Headers $h

# 4. Approve as the Manager (button/role-gated in React), then read the PO
$mgr = (Invoke-RestMethod -Method Post -Uri http://localhost:5078/api/auth/login -ContentType 'application/json' `
  -Body '{"email":"procurement.manager@buildwise.demo","password":"Passw0rd!"}').token
Invoke-RestMethod -Uri http://localhost:5078/api/purchase-orders -Headers @{ Authorization = "Bearer $mgr" }
```

The seeded scenario (spec §11) is: Suppliers A (Active), B (Suspended), C (Active), one `Approved` request for 250 bags of cement — Supplier A wins with 250 bags at 2,100 (total 525,000) while B is excluded and C is flagged for covering only 200/250.

## 6. Known gaps and open questions for you

1. **Demo users:** fresh databases seed the five application roles. Development seeding backfills missing demo accounts/assignments while preserving existing passwords; it does not remap legacy roles. Public registration permits only SiteEngineer; other assignments require controlled administration.
2. **No "partially received" PO status exists** in the ERD enum (`Created/Confirmed/InProgress/Completed/Cancelled`). If deliveries need it, raise it as an ERD change so we add it consistently instead of you tracking it in `deliveries` alone.
3. **`expected_delivery_date` is fixed at `order_date + 7 days`** and is not editable yet; tell us if the Manager should set it during approval.
4. **Tracing a PO line back to the material request** goes `purchase_order_items.quotation_item_id → quotation_items.material_request_item_id → material_request_items.material_id`; `material_request_id` and `supplier_id` are also exposed directly on the PO DTO.
5. **Notifications:** Component 2 emails the requester on PO creation through `IEmailService` (logs instead when SMTP is unset). Reuse that interface for "delivery expected" / "materials received" so notification behaviour stays consistent.

**Questions for Component 2?** Reply on the PR for `feature/supplier-procurement` — please avoid editing the PO tables directly while we are still on this branch.
