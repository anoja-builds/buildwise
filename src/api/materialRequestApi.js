// ---------------------------------------------------------------
// Mock data layer for Component 1 (Material Request & Approval).
//
// Every function here is written to match a real ASP.NET Core /
// PostgreSQL endpoint 1:1 (see the comment above each function).
// When the backend is ready, replace the body of each function
// with the matching `apiClient` call from ./client.js — the
// pages/components that call this module do not need to change.
// ---------------------------------------------------------------

import { initialRequests, projects, materials, STATUS } from '../data/mockData';
// import { apiClient } from './client'; // <- uncomment when wiring the real API

const LATENCY_MS = 300;
const STORAGE_KEY = 'buildwise_material_requests_v1';

function delay(value) {
  return new Promise((resolve) => setTimeout(() => resolve(value), LATENCY_MS));
}

function loadStore() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) return JSON.parse(raw);
  } catch {
    /* fall through to seed data */
  }
  return structuredClone(initialRequests);
}

function saveStore(requests) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(requests));
  } catch {
    /* storage unavailable — demo still works in-memory for this session */
  }
}

let requests = loadStore();

function persist() {
  saveStore(requests);
}

function nextRequestId() {
  const year = new Date().getFullYear();
  const seq = requests.length + 1 + Math.floor(Math.random() * 5);
  return `MR-${year}-${String(seq).padStart(4, '0')}`;
}

// GET /api/projects
export async function listProjects() {
  return delay(projects);
}

// GET /api/materials
export async function listMaterials() {
  return delay(materials);
}

// GET /api/material-requests?status=&projectId=&search=&page=&pageSize=
export async function listRequests({ status, projectId, search, requestedBy, page = 1, pageSize = 8 } = {}) {
  let result = [...requests];

  if (status) result = result.filter((r) => r.status === status);
  if (projectId) result = result.filter((r) => r.projectId === projectId);
  if (requestedBy) result = result.filter((r) => r.requestedBy === requestedBy);
  if (search) {
    const q = search.trim().toLowerCase();
    result = result.filter(
      (r) =>
        r.id.toLowerCase().includes(q) ||
        r.reason.toLowerCase().includes(q) ||
        r.items.some((i) => i.materialName.toLowerCase().includes(q))
    );
  }

  result.sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1));

  const total = result.length;
  const start = (page - 1) * pageSize;
  const pageItems = result.slice(start, start + pageSize);

  return delay({ items: pageItems, total, page, pageSize });
}

// GET /api/material-requests/{id}
export async function getRequest(id) {
  const found = requests.find((r) => r.id === id);
  if (!found) throw new Error(`Material request ${id} was not found.`);
  return delay(structuredClone(found));
}

// POST /api/material-requests
export async function createRequest({ projectId, requestedBy, requiredDate, reason, items }) {
  const project = projects.find((p) => p.id === projectId);
  const enrichedItems = items.map((item, idx) => {
    const material = materials.find((m) => m.id === item.materialId);
    return {
      id: `RI-new-${Date.now()}-${idx}`,
      materialId: item.materialId,
      materialName: material ? material.name : 'Unknown material',
      quantity: Number(item.quantity),
      unit: material ? material.unit : '',
    };
  });

  const newRequest = {
    id: nextRequestId(),
    projectId,
    projectName: project ? project.name : projectId,
    requestedBy,
    requiredDate,
    reason,
    status: STATUS.AWAITING_APPROVAL,
    createdAt: new Date().toISOString().slice(0, 10),
    items: enrichedItems,
    approvals: [
      { id: `APR-${Date.now()}`, stage: 'Request Approval', decision: null, decidedBy: null, comment: null, date: null },
    ],
    aiSummary: runPlanningAgentSync({ requiredDate, items: enrichedItems, reason }),
  };

  requests = [newRequest, ...requests];
  persist();
  return delay(structuredClone(newRequest));
}

// PUT /api/material-requests/{id}
// Only Draft or RevisionRequested requests may be edited — approved
// requests must stay immutable (see assignment: "prevent silent
// changes after approval").
export async function updateRequest(id, updates) {
  const idx = requests.findIndex((r) => r.id === id);
  if (idx === -1) throw new Error(`Material request ${id} was not found.`);

  const current = requests[idx];
  if (![STATUS.DRAFT, STATUS.REVISION_REQUESTED].includes(current.status)) {
    throw new Error('Only draft or revision-requested requests can be edited.');
  }

  const material = (id2) => materials.find((m) => m.id === id2);
  const items = (updates.items || current.items).map((item, idx2) => ({
    id: item.id || `RI-edit-${Date.now()}-${idx2}`,
    materialId: item.materialId,
    materialName: material(item.materialId)?.name || item.materialName,
    quantity: Number(item.quantity),
    unit: material(item.materialId)?.unit || item.unit,
  }));

  const updated = {
    ...current,
    ...updates,
    items,
    status: STATUS.AWAITING_APPROVAL,
    aiSummary: runPlanningAgentSync({ requiredDate: updates.requiredDate || current.requiredDate, items, reason: updates.reason || current.reason }),
  };

  requests[idx] = updated;
  persist();
  return delay(structuredClone(updated));
}

// POST /api/material-requests/{id}/approve
// POST /api/material-requests/{id}/reject
// POST /api/material-requests/{id}/request-revision
// (Modelled here as one function with a `decision` argument; the
// real backend exposes three distinct, auditable endpoints so that
// authorization and validation rules differ per action.)
export async function decideRequest(id, { decision, comment, decidedBy }) {
  const idx = requests.findIndex((r) => r.id === id);
  if (idx === -1) throw new Error(`Material request ${id} was not found.`);

  const current = requests[idx];
  if (current.status !== STATUS.AWAITING_APPROVAL) {
    throw new Error('Only requests awaiting approval can receive a decision.');
  }

  const statusByDecision = {
    Approved: STATUS.APPROVED,
    Rejected: STATUS.REJECTED,
    RevisionRequested: STATUS.REVISION_REQUESTED,
  };

  const approvalRecord = {
    id: `APR-${Date.now()}`,
    stage: 'Request Approval',
    decision,
    decidedBy,
    comment,
    date: new Date().toISOString().slice(0, 10),
  };

  const updated = {
    ...current,
    status: statusByDecision[decision],
    approvals: [...current.approvals.filter((a) => a.decision), approvalRecord],
  };

  requests[idx] = updated;
  persist();
  return delay(structuredClone(updated));
}

// DELETE /api/material-requests/{id}
// Only Draft requests may be deleted — once a request has been
// submitted (AwaitingApproval / Approved / Rejected / RevisionRequested)
// it must stay in the audit trail, so this intentionally does not
// allow deleting anything past Draft.
export async function deleteRequest(id, { requestedBy } = {}) {
  const idx = requests.findIndex((r) => r.id === id);
  if (idx === -1) throw new Error(`Material request ${id} was not found.`);

  const current = requests[idx];
  if (current.status !== STATUS.DRAFT) {
    throw new Error('Only draft requests can be deleted. Submitted requests must stay in the audit trail.');
  }
  if (requestedBy && current.requestedBy !== requestedBy) {
    throw new Error('You can only delete your own draft requests.');
  }

  requests = requests.filter((r) => r.id !== id);
  persist();
  return delay({ id, deleted: true });
}

// ---------------------------------------------------------------
// Mock "Request Validation & Planning Agent" (component-owned AI).
// This stands in for the real agent call the backend will make
// (checks completeness, quantities, dates, urgency; returns a
// structured plan/flags — it never approves anything itself).
// ---------------------------------------------------------------
function runPlanningAgentSync({ requiredDate, items, reason }) {
  const flags = [];
  const today = new Date();
  const required = new Date(requiredDate);
  const daysUntil = Math.ceil((required - today) / (1000 * 60 * 60 * 24));

  let urgency = 'Low';
  if (daysUntil <= 2) {
    urgency = 'High';
    flags.push({ type: 'warn', text: `Required date is only ${Math.max(daysUntil, 0)} day(s) away — flag for expedited procurement.` });
  } else if (daysUntil <= 5) {
    urgency = 'Medium';
  }

  if (daysUntil < 0) {
    flags.push({ type: 'warn', text: 'Required date is in the past. Verify before this proceeds to approval.' });
  }

  if (!reason || reason.trim().length < 10) {
    flags.push({ type: 'warn', text: 'Reason is brief — a fuller justification will speed up manager review.' });
  }

  const zeroQty = items.some((i) => !i.quantity || i.quantity <= 0);
  if (zeroQty) {
    flags.push({ type: 'warn', text: 'One or more items have an invalid quantity.' });
  }

  if (flags.length === 0) {
    flags.push({ type: 'ok', text: 'All required fields present; quantities look consistent for this material.' });
  }

  return {
    agent: 'Request Validation & Planning Agent',
    completeness: zeroQty || !reason ? 'Incomplete' : 'Complete',
    urgency,
    flags,
    note: 'Structured planning output shown here; the agent does not approve requests.',
  };
}
