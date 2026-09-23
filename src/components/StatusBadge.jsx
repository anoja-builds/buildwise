const STATUS_CLASS = {
  Draft: 'badge-draft',
  Submitted: 'badge-submitted',
  AwaitingApproval: 'badge-awaiting',
  Approved: 'badge-approved',
  Rejected: 'badge-rejected',
  RevisionRequested: 'badge-revision',
  InProcurement: 'badge-procurement',
};

const STATUS_LABEL = {
  Draft: 'Draft',
  Submitted: 'Submitted',
  AwaitingApproval: 'Awaiting approval',
  Approved: 'Approved',
  Rejected: 'Rejected',
  RevisionRequested: 'Revision requested',
  InProcurement: 'In procurement',
};

export default function StatusBadge({ status }) {
  return (
    <span className={`badge ${STATUS_CLASS[status] || 'badge-draft'}`}>
      {STATUS_LABEL[status] || status}
    </span>
  );
}
