export default function TextInput({ label, error, hint, required, multiline = false, id, ...props }) {
  const inputId = id || props.name
  const Control = multiline ? 'textarea' : 'input'
  return <label className="field" htmlFor={inputId}><span className="field__label">{label}{required && <span className="field__required"> *</span>}</span><Control id={inputId} className="field__control" required={required} aria-invalid={Boolean(error)} rows={multiline ? 4 : undefined} {...props}/>{error ? <span className="field__error">{error}</span> : hint && <span className="field__hint">{hint}</span>}</label>
}
