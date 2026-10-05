# Component 2 → Component 3 Handoff

**From:** IT24102414 — Supplier, Quotation & Procurement Management
**To:** Component 3 — Delivery & Material Receiving Management
**Branch:** `feature/supplier-procurement` · Last verified 2026-09-19

## 1. What Component 2 provides you

`purchase_orders` and `purchase_order_items` are your **expected-delivery
baseline** for delivery reconciliation. Per the component spec's Integration
Notes, they are exposed **read-only** and only once a PO has reached
`Confirmed` or later.

| Table | Key columns you will consume |
|---|---|
| `purchase_orders` | `id`, `quotation_id` → quotations, `order_date`, `expected_delivery_date`, `status` (`purchase_order_status`: `Created`/`Confirmed`/`InProgress`/`Completed`/`Cancelled`), `total_amount` |
| `purchase_order_items` | `id`, `purchase_order_id` → purchase_orders, `quotation_item_id` → quotation_items, `ordered_quantity`, `unit_price` |

Reach the material behind a line via
`purchase_order_items.quotation_item_id → quotation_items.material_request_item_id → material_request_items.material_id → materials`.

## 2. Endpoints (JWT required; roles in brackets)

| Method | Route | Roles | Notes |
|---|---|---|---|
| GET | `/api/purchase-orders` | ProcurementOfficer, ProcurementManager, Administrator, **ReceivingOfficer** | filter by `status`/`search`, `page`/`pageSize` |
| GET | `/api/purchase-orders/{id}` | same as above | detail + items |
| PATCH | `/api/purchase-orders/{id}/status` | ProcurementOfficer, ProcurementManager, Administrator | `Confirmed`/`InProgress`/`Completed`/`Cancelled` |

Example request and response shape (`camelCase`):

```json
GET /api/purchase-orders/2
{
  "id": 2, "quotationId": 3, "materialRequestId": 1, "supplierId": 1,
  "supplierName": "Supplier A Building Materials",
  "orderDate": "2026-09-19", "expectedDeliveryDate": "2026-09-26",
  "status": "Created", "totalAmount": 525000.00,
  "items": [{ "id": 2, "quotationItemId": 3, "materialName": "Cement (50kg bag)",
              "unit": "bag", "orderedQuantity": 250.00, "unitPrice": 2100.00,
              "lineTotal": 525000.0000 }]
}
```

## 3. ⚠️ Status caveat — read this before assuming the list is empty

A purchase order is **born in `Created` status**, not `Confirmed`. The spec says
Component 3 consumes POs "once `status` is `Confirmed` or later", so:

> A freshly generated PO will **not** appear in a query filtered to
> `status=Confirmed` until someone calls
> `PATCH /api/purchase-orders/{id}/status` with `{"status":"Confirmed"}`.

If your delivery screen looks empty, check whether the PO is still in `Created`
before reporting it as a bug.

## 4. Rules Component 3 must respect

1. **Do not write** to `purchase_orders`, `purchase_order_items`, `quotations`,
   `quotation_items`, `suppliers` — Component 2 owns them.
2. **Do not alter the columns** of `agent_workflows`, `agent_workflow_steps` or
   `agent_approvals`; the *structure* is shared across all four components'
   agents and is under ERD change control.
3. **One PO per material request.** Creation is blocked while a non-cancelled PO
   exists for that request (spec §5.10). Expect at most one live PO per request.
4. **Line quantities are locked at PO creation.** `ordered_quantity` is copied
   from the winning `quotation_items.quantity` at the moment of PO creation —
   it is your reconciliation baseline, not a live editable figure.
5. Source data is traceable: the winning quotation becomes `Selected` and all
   losing quotations become `Rejected` when the PO is created (§4.6).

## 5. Verified handoff state (as of handoff)

| Fact | Value |
|---|---|
| Workflows | #51 `Completed` / `Approved` (the first ever to succeed) |
| Purchase order | #2 · quotation #3 · Supplier A Building Materials · 250 bags @ 2 100.00 = **525 000.00** · `Created` · delivery 2026-09-26 |
| Quotations | #3 `Selected`, #6 `Rejected`, #7 `Rejected` |
| Test suites | 20 C# · 9 Python · 15 React — all passing |

## 6. Open items you may hit

- **Spurious HTTP 400 on PO creation** (ours, not yours, but it affects PO
  availability): one call returned `400` although the PO was persisted
  correctly. Not reproducible on a clean run. If you ever see "Purchase Order
  #N already exists" right after creating one, the PO *does* exist — re-query
  before concluding the creation failed. Recorded in `docs/adr/ADR-002` §6.
- **Do not rely on `agent_service` being up.** It is advisory only; procurement
  completes through an in-process fallback if it is down.

## 7. Contact / escalation

Queries on quotation selection, the approval gate, or the agent contract:
IT24102414 (`feature/supplier-procurement`). Schema changes to shared tables
need team agreement first.
