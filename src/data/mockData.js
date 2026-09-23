// Seed data for local development / demoing Component 1 before the
// ASP.NET Core + PostgreSQL backend is wired in. Shapes mirror the
// core entities listed in the assignment: Project, Material,
// MaterialRequest, RequestItem, Approval.

export const ROLES = {
  SITE_ENGINEER: 'SiteEngineer',
  PROCUREMENT_MANAGER: 'ProcurementManager',
  ADMINISTRATOR: 'Administrator',
};

export const STATUS = {
  DRAFT: 'Draft',
  SUBMITTED: 'Submitted',
  AWAITING_APPROVAL: 'AwaitingApproval',
  APPROVED: 'Approved',
  REJECTED: 'Rejected',
  REVISION_REQUESTED: 'RevisionRequested',
  IN_PROCUREMENT: 'InProcurement',
};

export const projects = [
  { id: 'PRJ-001', name: 'Colombo Apartment Complex – Block C', location: 'Colombo 05' },
  { id: 'PRJ-002', name: 'Kandy Hillside Residences', location: 'Kandy' },
  { id: 'PRJ-003', name: 'Galle Road Commercial Tower', location: 'Colombo 03' },
];

export const materials = [
  { id: 'MAT-001', name: 'OPC Cement', unit: 'bag' },
  { id: 'MAT-002', name: 'River Sand', unit: 'cube' },
  { id: 'MAT-003', name: '20mm Aggregate', unit: 'cube' },
  { id: 'MAT-004', name: '12mm Reinforcement Bar', unit: 'kg' },
  { id: 'MAT-005', name: 'Formwork Plywood', unit: 'sheet' },
  { id: 'MAT-006', name: 'Concrete Blocks (6")', unit: 'unit' },
];

export const users = [
  { id: 'USR-101', name: 'N. Perera', role: ROLES.SITE_ENGINEER },
  { id: 'USR-102', name: 'S. Fernando', role: ROLES.SITE_ENGINEER },
  { id: 'USR-201', name: 'R. Jayasuriya', role: ROLES.PROCUREMENT_MANAGER },
];

const daysFromNow = (n) => {
  const d = new Date();
  d.setDate(d.getDate() + n);
  return d.toISOString().slice(0, 10);
};

const daysAgo = (n) => daysFromNow(-n);

export const initialRequests = [
  {
    id: 'MR-2026-0041',
    projectId: 'PRJ-001',
    requestedBy: 'N. Perera',
    requiredDate: daysFromNow(2),
    reason: 'Ground-floor column concreting scheduled to start this week; on-site cement stock will run out.',
    status: STATUS.AWAITING_APPROVAL,
    createdAt: daysAgo(1),
    items: [
      { id: 'RI-1', materialId: 'MAT-001', materialName: 'OPC Cement', quantity: 250, unit: 'bag' },
    ],
    approvals: [
      {
        id: 'APR-1',
        stage: 'Request Approval',
        decision: null,
        decidedBy: null,
        comment: null,
        date: null,
      },
    ],
    aiSummary: {
      agent: 'Request Validation & Planning Agent',
      completeness: 'Complete',
      urgency: 'High',
      flags: [
        { type: 'warn', text: 'Required date is 2 days out — expedited procurement likely needed.' },
        { type: 'info', text: 'Quantity is consistent with prior concreting-stage requests for this project.' },
      ],
      note: 'Structured planning output shown here; the agent does not approve requests.',
    },
  },
  {
    id: 'MR-2026-0038',
    projectId: 'PRJ-002',
    requestedBy: 'S. Fernando',
    requiredDate: daysFromNow(6),
    reason: 'Retaining wall formwork for terrace 2.',
    status: STATUS.APPROVED,
    createdAt: daysAgo(4),
    items: [
      { id: 'RI-2', materialId: 'MAT-005', materialName: 'Formwork Plywood', quantity: 40, unit: 'sheet' },
      { id: 'RI-3', materialId: 'MAT-004', materialName: '12mm Reinforcement Bar', quantity: 1200, unit: 'kg' },
    ],
    approvals: [
      {
        id: 'APR-2',
        stage: 'Request Approval',
        decision: 'Approved',
        decidedBy: 'R. Jayasuriya',
        comment: 'Quantities match the terrace 2 BOQ. Proceed to procurement.',
        date: daysAgo(3),
      },
    ],
    aiSummary: {
      agent: 'Request Validation & Planning Agent',
      completeness: 'Complete',
      urgency: 'Medium',
      flags: [
        { type: 'ok', text: 'All required fields present; quantities within normal range for this material.' },
      ],
      note: 'Structured planning output shown here; the agent does not approve requests.',
    },
  },
  {
    id: 'MR-2026-0035',
    projectId: 'PRJ-003',
    requestedBy: 'N. Perera',
    requiredDate: daysAgo(1),
    reason: 'Blockwork for boundary wall, phase 1.',
    status: STATUS.REVISION_REQUESTED,
    createdAt: daysAgo(6),
    items: [
      { id: 'RI-4', materialId: 'MAT-006', materialName: 'Concrete Blocks (6")', quantity: 3000, unit: 'unit' },
    ],
    approvals: [
      {
        id: 'APR-3',
        stage: 'Request Approval',
        decision: 'RevisionRequested',
        decidedBy: 'R. Jayasuriya',
        comment: 'Required date has already passed — resubmit with a realistic date and confirm block grade.',
        date: daysAgo(5),
      },
    ],
    aiSummary: {
      agent: 'Request Validation & Planning Agent',
      completeness: 'Complete',
      urgency: 'High',
      flags: [
        { type: 'warn', text: 'Required date is in the past relative to submission — likely a data entry issue.' },
      ],
      note: 'Structured planning output shown here; the agent does not approve requests.',
    },
  },
  {
    id: 'MR-2026-0029',
    projectId: 'PRJ-001',
    requestedBy: 'S. Fernando',
    requiredDate: daysAgo(10),
    reason: 'Foundation pour, footing F12–F18.',
    status: STATUS.REJECTED,
    createdAt: daysAgo(14),
    items: [
      { id: 'RI-5', materialId: 'MAT-002', materialName: 'River Sand', quantity: 15, unit: 'cube' },
      { id: 'RI-6', materialId: 'MAT-003', materialName: '20mm Aggregate', quantity: 15, unit: 'cube' },
    ],
    approvals: [
      {
        id: 'APR-4',
        stage: 'Request Approval',
        decision: 'Rejected',
        decidedBy: 'R. Jayasuriya',
        comment: 'Duplicate of MR-2026-0027, already fulfilled. Please verify before resubmitting.',
        date: daysAgo(13),
      },
    ],
    aiSummary: {
      agent: 'Request Validation & Planning Agent',
      completeness: 'Complete',
      urgency: 'Medium',
      flags: [
        { type: 'info', text: 'Similar quantities to a recently fulfilled request on the same project.' },
      ],
      note: 'Structured planning output shown here; the agent does not approve requests.',
    },
  },
  {
    id: 'MR-2026-0044',
    projectId: 'PRJ-002',
    requestedBy: 'S. Fernando',
    requiredDate: daysFromNow(9),
    reason: 'Draft — quantities still being confirmed with the site foreman.',
    status: STATUS.DRAFT,
    createdAt: daysAgo(0),
    items: [
      { id: 'RI-7', materialId: 'MAT-001', materialName: 'OPC Cement', quantity: 80, unit: 'bag' },
    ],
    approvals: [],
    aiSummary: null,
  },
];
