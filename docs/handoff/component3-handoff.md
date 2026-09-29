# Component 2 → Component 3 Handoff

**From:** IT24102414 — Supplier, Quotation & Procurement Management
**To:** Component 3 — Delivery & Material Receiving Management
**Branch:** `feature/supplier-procurement` · Last verified 2026-09-19

## Current integrated contract (2026-09-28)

The approved BuildWise Scenario defines five application roles. SiteEngineer owns
site receiving; ProcurementManager owns material-request approval. The original
handoff branch/date above and verification evidence below are historical.

### Delivery context for site users

SiteEngineer uses `GET /api/Deliveries/expected` and `GET /api/Deliveries/{id}`.
These expose PO ID, supplier/project name, expected date, status, material/unit,
ordered quantity, previously received usable quantity, outstanding quantity,
received/damaged quantity and delivery notes. They exclude prices, quotation
comparisons, supplier evaluation and procurement approval controls.

`previouslyReceivedUsableQuantity` is the sum of received minus damaged quantities
on other finalized deliveries. `outstandingQuantity` is the nonnegative difference
from ordered quantity. Receipt validation independently recalculates this value in
a transaction; clients must refresh and handle a rejected stale receipt.

| Operation | Authorized roles |
|---|---|
| Expected deliveries, history, detail, schedules, issues, discrepancy history | SiteEngineer, ProcurementOfficer, ProcurementManager, QualityInspector, Administrator |
| Receive, attach evidence, report issue | SiteEngineer, Administrator |
| Schedule a delivery; pre-delivery risk analysis | ProcurementOfficer, ProcurementManager, Administrator |
| Run post-receipt discrepancy analysis | All five roles above; advisory only |
| Full PO list/detail and permitted status PATCH | ProcurementOfficer, ProcurementManager, Administrator |
| Create PO from approved workflow | ProcurementManager, Administrator |

### Procurement and receiving boundary

1. Procurement creates the PO after authorized human approval and deterministic validation.
2. Procurement confirms the order and schedules the delivery with `POST /api/Deliveries`.
3. SiteEngineer reads the limited Delivery API, then calls `POST /api/Deliveries/{id}/receive`.
4. ASP.NET validates the JWT actor, PO status, line membership, quantities and prior receipts.
   Receiving updates PO progress/completion; clients cannot force those states through PO PATCH.
5. QualityInspector handles inspection/NCR decisions. AI output remains advisory.

SiteEngineer has no Orders module or access to `/api/purchase-orders`. Own-request
procurement progress remains available at
`GET /api/material-requests/{id}/procurement-status`.

Fresh databases seed the five application-role demo accounts. Existing databases
are not remapped by startup seeding; legacy role rows remain pending separately
approved cleanup. No database redesign is required.

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
