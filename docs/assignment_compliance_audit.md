# BuildSupply LK / BuildWise SE3090 assignment compliance audit

**Audit date:** 24 September 2026
**Scope:** local working tree only; no GitHub push or commit was made.
**Decision:** **Conditional Go for the local demonstration; external submission evidence remains outstanding.**

This document is an evidence index, not invented evidence. A green test is not treated as proof of public deployment, APK installation, Git contribution, Firebase provisioning, or viva understanding.

## Verified technical evidence

| Specification area | Current evidence | Status |
|---|---|---|
| ASP.NET Core + PostgreSQL | 73/73 Release backend tests; live PostgreSQL migration `20260924153433_AddRfqAdminAudit`; authenticated API checks | Verified locally |
| Four components | C1 request/approval, C2 supplier/quotation/RFQ/procurement, C3 delivery/receiving, C4 inspection/NCR | Verified locally |
| React | 21/21 tests; production build; React Router; RFQ page; Administration page; protected Agent Workflows page | Verified locally |
| Flutter | `flutter analyze` passed; 4/4 tests passed; role-aware request, procurement, receiving, inspection, secure token, local notification screens | Static verified; real device/APK evidence outstanding |
| Four agents | 27/27 Python tests; ports 8001–8004 healthy; persisted workflow summaries | Verified locally |
| Agent state/observability | Protected workflow list/detail endpoints, React history page, validation, timings, approvals, outcomes | Verified locally |
| Complete workflow | `verify-full-journey.ps1`: 14/14 from request through PO, delivery discrepancy, inspection, NCR, and initiating-user status | Verified locally |
| RBAC | `verify-rbac.ps1`: 24/24; RFQ/Admin live check confirms Procurement Officer receives 403 from Administrator endpoints | Verified locally |
| RFQ | PostgreSQL `rfqs` and `rfq_suppliers`; create, invite Active suppliers, close; quotation optional `RfqId` | Verified locally |
| Administrator | Protected user directory, active-state and role controls, system health, audit log page | Verified locally |
| Auditability | Authenticated state-changing requests persist to `audit_logs` without request bodies/tokens | Verified locally |
| Performance | Local concurrent performance evidence exists; local results are not a production SLA | Verified locally |
| CI | GitHub Actions workflow includes backend, Python, React, and Flutter checks | Workflow present; remote run not claimed |

## Live acceptance commands

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/start-dev.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-rbac.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-rfq-admin.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-full-journey.ps1
```

`verify-full-journey.ps1` writes fresh local demonstration records. Use it only for demonstration/evidence runs.

## Implemented PRD additions

- RFQ lifecycle: `POST/GET /api/rfqs`, supplier invitations, close operation, and React `/rfqs` page.
- Administrator workspace: `/admin`, user directory, role/active controls, system health, and recent audit events.
- Audit persistence: `audit_logs` table and `AuditLoggingMiddleware` for authenticated state-changing requests.
- Product naming: visible web branding is BuildSupply LK while BuildWise namespaces/routes remain compatible.
- Database migration: RFQ, RFQ supplier, audit tables, quotation RFQ link, indexes, and foreign keys.

## Remaining PRD gaps and external evidence

These are not silently marked complete:

1. Firebase Cloud Messaging is not configured. Flutter currently demonstrates a local notification feature, not remote FCM delivery.
2. A separate Supplier self-service portal/RFQ invitation inbox is not implemented; supplier functionality remains intentionally limited for the MVP.
3. Camera/image evidence upload is not yet wired into the Flutter delivery/inspection forms. Delivery evidence entity exists in the database model, but the live evidence flow still needs implementation.
4. Android SDK, release APK, and physical-device/emulator walkthrough remain outstanding on this machine.
5. Public API, React, PostgreSQL, and evaluator-facing deployment URLs remain outstanding.
6. Remaining ADRs, consolidated group report, individual contribution sections, AI usage declarations, performance/deployment reports, diagrams, GitHub issue/PR evidence, and demonstration video remain to be produced from truthful project evidence.
7. The existing external AI key supplied for Gemini was rejected with HTTP 401 and is not configured. The application remains operational in deterministic/safe-fallback mode.

## Excellent-mark priorities

- Demonstrate one complete Flutter → ASP.NET Core → PostgreSQL → Agentic AI → React approval → ASP.NET Core → Flutter status workflow.
- Show PostgreSQL rows before and after the workflow and the persisted agent audit trail.
- Explain the difference between an agent recommendation and the ASP.NET deterministic business-rule gate.
- Demonstrate allowed and denied operations using real role tokens.
- Add a real FCM project, camera evidence flow, and Android device walkthrough if the team wants to close the remaining product gaps.
- Run the test and performance harness in front of the evaluator, clearly labelling local-machine measurements.
- Maintain a truthful individual AI usage log; do not back-fill commit or test evidence.
