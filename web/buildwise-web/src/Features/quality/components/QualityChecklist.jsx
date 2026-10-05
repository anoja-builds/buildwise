// The five quality criteria, in report order, mapped to their structured field.
// These are the same five the inspector ticks on every inspection.
const CHECK_POINTS = [
  ['Quantity', 'quantityCheck'],
  ['Visual condition', 'visualConditionCheck'],
  ['Moisture', 'moistureCheck'],
  ['Packaging', 'packagingCheck'],
  ['Defects', 'defectsCheck'],
]

/**
 * Renders the structured five-point checklist for one inspection.
 *
 * The three states are deliberately distinct because they mean different
 * things:
 *   true  -> checked and passed  (✓)
 *   false -> checked and failed  (✗)
 *   null  -> never recorded      (legacy inspection predating the checklist)
 *
 * A legacy row is explicitly marked as not recorded rather than shown as a
 * pass — rendering "✓ Moisture" for an inspection that never recorded it would
 * fabricate a quality result.
 */
export default function QualityChecklist({ inspection }) {
  const recorded = CHECK_POINTS.filter(([, key]) => inspection[key] !== null && inspection[key] !== undefined)
  if (recorded.length === 0) {
    return <span className="muted" title="This inspection predates the structured checklist">Not recorded</span>
  }
  return (
    <ul style={{ margin: 0, paddingLeft: 0, listStyle: 'none', display: 'grid', gap: 2 }}>
      {CHECK_POINTS.map(([label, key]) => {
        const value = inspection[key]
        if (value === null || value === undefined) {
          return (
            <li key={key} className="muted" style={{ fontSize: '0.8rem' }}>
              {label} · —
            </li>
          )
        }
        return (
          <li
            key={key}
            style={{
              fontSize: '0.8rem',
              color: value ? '#15803d' : '#b91c1c',
              fontWeight: 600,
            }}
          >
            {value ? '✓' : '✗'} {label}
          </li>
        )
      })}
    </ul>
  )
}