# ADR-003 — React State Management Strategy

**Status:** Accepted · **Date:** 2026-09-23 · **Owner:** Component 2 (IT24102414), agreed by all component owners  
**Scope:** SE3090 spec §7 (React), §9 (cross-platform workflow)

---

## Context

BuildWise's React front-end serves five role-based dashboards (Admin, Procurement Manager, Site Engineer, Delivery Staff, Quality Inspector). Each feature area has independent data lifecycles:

- **Component 1** — material requests, approval queues  
- **Component 2** — suppliers, quotations, RFQs, procurement workflows, agent analysis  
- **Component 3** — deliveries, receiving records  
- **Component 4** — quality inspections, non-conformances  

The spec requires: routing with protected routes by role, client-side validation, API error handling, loading/empty states, and testability without a running backend.

Three cross-cutting concerns shaped the choice:

1. **Auth context** — JWT, role, and logout must be available everywhere without prop drilling.
2. **Feature isolation** — each component team owns its feature folder; state solutions must not create hard cross-team coupling.
3. **Test simplicity** — vitest + React Testing Library must be able to test components without complex store setup.

---

## Options considered

| # | Option | Verdict |
|---|---|---|
| A | **React Context + `useState`/`useReducer` per feature** | **Chosen** — enough for SE3090 scale, testable, no extra dependencies |
| B | Redux Toolkit | Rejected — significant boilerplate; adds a required peer dependency; overkill for assignment scale |
| C | Zustand | Rejected — not a React built-in; adds a dependency; team unfamiliar |
| D | React Query / TanStack Query | Rejected — good for server state caching but adds complexity beyond spec requirements |
| E | Single global Context for all state | Rejected — causes unnecessary re-renders across all four feature areas on any state change |

---

## Decision

1. **Auth is a single global `AuthContext`** (`src/auth/AuthContext.jsx`) providing `user`, `token`, `login`, `logout`. All protected routes read from this context. No auth state is duplicated in feature components.

2. **RBAC guard** is a separate `accessControl.js` module with a `canAccess(role, resource)` function, keeping permission logic testable independently of components.

3. **Feature-local state** uses `useState` and `useReducer` within feature components. No cross-feature shared state exists — components communicate through the API, not through shared client-side state.

4. **API calls** use thin `fetch` wrappers in `src/services/*.js` (e.g., `procurementApi.js`, `qualityApi.js`). Components call these services in `useEffect`; loading/error/data are local `useState` variables.

5. **No external state library** is added. The single `react-router-dom` dependency handles routing; all other state is native React.

---

## Workflow state (read-only in React)

The agentic AI workflow state (`agent_workflows`, `agent_workflow_steps`, `agent_approvals`) is **server-owned and server-persisted**. React only reads it via GET endpoints and displays it. No client-side caching of workflow state was needed.

---

## Consequences

**Positive**
- Zero extra state library dependencies — consistent with the existing `package.json`
- Every component is independently unit-testable with a simple mock `AuthContext` wrapper
- Feature isolation: Component 2's procurement state does not affect Component 4's quality state
- Vitest runs pass with simple `render(<Component />)` patterns

**Negative / accepted**
- No built-in cache invalidation — components re-fetch on mount; accepted for assignment scale
- Large list pages do not paginate on the client — pagination is a server-side API parameter

---

## References

- `web/buildwise-web/src/auth/AuthContext.jsx`
- `web/buildwise-web/src/auth/accessControl.js`
- `web/buildwise-web/src/services/procurementApi.js`
- React Testing Library tests: `src/Features/procurement/components/*.test.jsx`
