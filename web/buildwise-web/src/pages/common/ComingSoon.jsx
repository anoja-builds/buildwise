import { Card, EmptyState, PageHeader } from '../../components/shared'

/// Placeholder for sidebar sections owned by components that are not merged
/// into this shell yet. Keeps the sidebar faithful to the full workflow
/// without crashing on screens that do not exist.
const NOTES = {
  'Material Requests': ['Component 1 — site engineer requests', 'Site engineers create material requests here and approve them for procurement. Approved requests appear in the Quotations queue.'],
  Deliveries: ['Delivery tracking — next component', 'Purchase orders move here for delivery scheduling, receiving, and evidence once the delivery component is merged into this shell.'],
  'Quality Inspections': ['Quality module — next component', 'Inspections against received deliveries will be recorded here.'],
  'Non-Conformances': ['Quality module — next component', 'Non-conformance reports raised from inspections will be tracked here.'],
  'AI Workflows': ['AI workflow history — coming soon', 'Every agent run (quotation analysis) is already stored per procurement workflow; its audit timeline will surface here.'],
  Reports: ['Reporting — coming soon', 'Cross-module reports and exports will live here.'],
  'Users & Roles': ['Administration — coming soon', 'User and role administration will live here; sign-in roles already gate every action in the app.'],
}

export default function ComingSoon({ title }) {
  const [subtitle, description] = NOTES[title] ?? ['Coming soon', 'This area belongs to another BuildWise component and will appear here once it is merged.']
  return (
    <div className="stack">
      <PageHeader title={title} description={description} />
      <Card>
        <EmptyState title={subtitle} message="Nothing to show yet — this screen is delivered by a component that is not part of this build." />
      </Card>
    </div>
  )
}