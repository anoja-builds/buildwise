# ADR-004 — Flutter State Management Strategy

**Status:** Accepted · **Date:** 2026-09-23 · **Owner:** All component owners (Flutter is shared)  
**Scope:** SE3090 spec §8 (Flutter mobile), §9 (device feature, secure storage, API integration)

---

## Context

BuildWise's Flutter application runs on Android (primary submission target: APK) and optionally on Windows desktop and Chrome (web). It must:

1. Authenticate against the BuildWise ASP.NET Core API and store the JWT securely on-device.
2. Navigate role-conditionally to feature areas (procurement, deliveries, quality, supplier portal, operations).
3. Display live procurement workflow status with **local push notifications** when status changes (device feature — spec §9.2).
4. Support login → authenticated workflow → logout without leaking tokens.

The assignment scale is a team of four working independently on feature folders. Flutter state management must not create hard coupling between feature teams.

---

## Options considered

| # | Option | Verdict |
|---|---|---|
| A | **`StatefulWidget` + `setState` per screen, shared `AuthService` singleton** | **Chosen** — simplest, testable, no extra packages needed |
| B | Provider / Riverpod | Rejected — adds dependency; team unfamiliar; overkill for four screens |
| C | BLoC / Cubit | Rejected — heavy boilerplate; difficult to learn quickly mid-sprint |
| D | GetX | Rejected — opinionated, non-idiomatic, conflicts with Flutter lint rules in `analysis_options.yaml` |
| E | InheritedWidget (manual) | Rejected — lower-level than needed; Provider wraps this already |

---

## Decision

1. **`AuthService`** (`lib/core/auth/`) is a singleton holding the JWT, parsed user role, and base API URL. It wraps `flutter_secure_storage` for encrypted on-device token storage. All screens and services read the token from `AuthService`; none store it locally.

2. **Per-screen `StatefulWidget` + `setState`** for loading, error, and data state. Each screen owns its own state; no global state store is used.

3. **`NotificationService`** (`lib/features/procurement/services/notification_service.dart`) wraps `flutter_local_notifications` to display local push notifications when procurement status changes. This is the required **device feature**: on-device notification scheduling without a remote push server, satisfying spec §9.2 without requiring a Firebase project.

4. **`ProcurementStatusPoller`** (`lib/features/procurement/services/procurement_status_poller.dart`) polls the API on a timer and fires `NotificationService` when status transitions are detected. This is the only background-style process; it runs within the app lifecycle.

5. **Navigation** uses Flutter's built-in `Navigator` with named routes defined in `lib/routes/`. Role-based screen access is enforced in the route resolution logic by reading `AuthService.currentRole`.

6. **No external state management package** beyond the three packages already in `pubspec.yaml` (`http`, `flutter_secure_storage`, `flutter_local_notifications`).

---

## Device feature detail

```
ProcurementStatusPoller (Timer.periodic)
        ↓
GET /api/procurement-workflow/{id}
        ↓
Status changed?  ──No──→ (wait)
        ↓ Yes
NotificationService.showStatusUpdate(title, body)
        ↓
flutter_local_notifications → System notification tray
```

This satisfies the spec requirement for a meaningful device feature (local notifications) without any Firebase dependency, matching the team's decision to avoid a Firebase project for local assignment submission.

---

## Consequences

**Positive**
- No extra dependencies — `pubspec.yaml` stays minimal and auditable
- `flutter test` works without mocking a state container
- `AuthService` is the single source of truth for the token; no risk of stale tokens in multiple stores
- Notification feature works offline (local, no remote push server)

**Negative / accepted**
- `setState` causes full-screen rebuilds on data change; acceptable at assignment scale
- Polling instead of WebSocket for status changes; polling interval is configurable
- No persistent offline cache — app requires network for all data; accepted

---

## References

- `mobile/buildwise_mobile/lib/core/auth/`
- `mobile/buildwise_mobile/lib/features/procurement/services/notification_service.dart`
- `mobile/buildwise_mobile/lib/features/procurement/services/procurement_status_poller.dart`
- `mobile/buildwise_mobile/pubspec.yaml`
- Spec §8 (Flutter), §9.2 (device feature)
