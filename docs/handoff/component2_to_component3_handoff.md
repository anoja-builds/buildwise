# Component 2 → Component 3 handoff — Delivery & Material Receiving

**From:** Component 2 (Supplier, Quotation & Procurement Management) · **To:** Component 3 (Delivery & Material Receiving)
**Branch:** `feature/supplier-procurement` · **Latest commit:** `6161ecc` · **Date:** 2026-09-20
**Status:** purchase-order tables and read API are complete and verified end-to-end, so Component 3 can start building delivery workflows against them now.

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

## 2. Read API

| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/purchase-orders?status=Confirmed&search=&page=1&pageSize=50` | ProcurementOfficer, ProcurementManager, Administrator, **ReceivingOfficer** | paged list (`total`, `page`, `pageSize`, `items`); `status` filter is case-insensitive; `search` matches supplier name or PO id |
| GET | `/api/purchase-orders/{id}` | Officer, Manager, Administrator, **ReceivingOfficer** | detail with items, supplier name, material name and unit |
| POST | `/api/procurement-workflow/{workflowId}/purchase-order` | Officer, Manager, Administrator | explicit/manual trigger; the normal path is automatic on Manager Approve |
| PATCH | `/api/purchase-orders/{id}/status` | Officer, Manager, Administrator **only** | `ReceivingOfficer` correctly receives **403** — status changes are procurement-owned by design |

`ReceivingOfficer` is already a seeded role (`roles.id = 6`), so no schema work is needed to grant read access.

### Response shape (`GET /api/purchase-orders/{id}`)

```json
{
  "id": 2,
  "quotationId": 3,
  "materialRequestId": 1,
  "supplierId": 1,
  "supplierName": "Supplier A Building Materials",
  "orderDate": "2026-09-19",
  "expectedDeliveryDate": "2026-09-26",
  "status": "Created",
  "totalAmount": 525000.00,
  "createdAt": "2026-09-19T05:05:12Z",
  "updatedAt": "2026-09-19T05:05:12Z",
  "items": [
    {
      "id": 3,
      "quotationItemId": 4,
      "materialName": "Cement (50kg bag)",
      "unit": "bag",
      "orderedQuantity": 250.00,
      "unitPrice": 2100.00,
      "lineTotal": 525000.00
    }
  ]
}
```

Field names are camelCase (System.Text.Json web defaults), so the contract is identical for React, Flutter and any .NET client you write. Authentication is the shared JWT from `POST /api/auth/login`; pass it as `Authorization: Bearer <token>`.

## 3. How a purchase order comes into existence (so you can seed or reproduce one)

1. A `material_request` reaches `status = Approved` (Component 1).
2. A Procurement Officer records quotations for eligible suppliers, then starts the workflow: `POST /api/material-requests/{id}/procurement-workflow`.
3. The Quotation & Supplier Analysis Agent recommends a supplier (spec §10), and ASP.NET Core independently re-validates the recommendation against every business rule in spec §5.
4. If validation passes, the workflow moves to `AwaitingApproval`; a Procurement Manager calls `POST /api/procurement-workflow/{id}/decision` with `Approve` (the endpoint is Manager-only, `403` for the Officer).
5. On `Approve`, the PO and its items are created in a single transaction, the winning quotation becomes `Selected`, losing quotations become `Rejected`, and the requester is notified (email, or a log entry when SMTP is unconfigured).

A PO can never exist without an `Approved` `agent_approvals` row, and a second non-cancelled PO for the same material request is blocked (§5.8, §5.10).

## 4. What Component 2 asks of Component 3

- **Read, don't write.** Do not insert or update `purchase_orders` / `purchase_order_items`. If a delivery workflow needs a PO `Confirmed`, `InProgress`, `Completed` or `Cancelled`, ask Component 2 (or call the PATCH endpoint from an Officer/Manager session) rather than writing the table directly — the audit trail depends on that path.
- **Don't alter `quotations`, `quotation_items`, or the shared `agent_workflows` / `agent_workflow_steps` / `agent_approvals` structure.** Per the spec's Integration Notes and the ERD change-control note, column changes need team agreement.
- **Spec's read-only boundary.** Component 2 exposes POs as read-only for reconciliation once a PO is `Confirmed` or later; the current code leaves newly created POs at `Created` until an Officer moves them on. If your flow needs `Confirmed` before deliveries can be recorded, say so and we will set that status on approval instead.
- **Foreign keys.** Link your `deliveries` rows to `purchase_order_id`, and your delivery lines to `purchase_order_items.id` (line-level quantities come from `ordered_quantity` / `unit_price`). Confirm the exact column names on your side before migrating so both components agree on the direction of the FK.

## 5. Fastest way to verify from your machine

```powershell
# 1. API + agent service (two terminals)
cd C:\Users\L O Q\Downloads\buildwise-component2\backend\BuildWise.Api ; dotnet run
cd C:\Users\L O Q\Downloads\buildwise-component2\backend\agent_service ; uvicorn quotation_agent:app --port 8001

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

1. **No `ReceivingOfficer` demo account is seeded.** Register one with `POST /api/auth/register` (role `ReceivingOfficer`) or extend `DbSeeder` demo accounts — roles themselves are already seeded.
2. **No "partially received" PO status exists** in the ERD enum (`Created/Confirmed/InProgress/Completed/Cancelled`). If deliveries need it, raise it as an ERD change so we add it consistently instead of you tracking it in `deliveries` alone.
3. **`expected_delivery_date` is fixed at `order_date + 7 days`** and is not editable yet; tell us if the Manager should set it during approval.
4. **Tracing a PO line back to the material request** goes `purchase_order_items.quotation_item_id → quotation_items.material_request_item_id → material_request_items.material_id`; `material_request_id` and `supplier_id` are also exposed directly on the PO DTO.
5. **Notifications:** Component 2 emails the requester on PO creation through `IEmailService` (logs instead when SMTP is unset). Reuse that interface for "delivery expected" / "materials received" so notification behaviour stays consistent.

**Questions for Component 2?** Reply on the PR for `feature/supplier-procurement` — please avoid editing the PO tables directly while we are still on this branch.
