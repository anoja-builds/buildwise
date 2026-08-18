export default function SelectInput({ label, options = [], id, ...props }) {
  const inputId = id || props.name
  return <label className="field" htmlFor={inputId}><span className="field__label">{label}</span><select id={inputId} className="field__control" {...props}>{options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select></label>
}
