export default function SearchInput({ label = 'Search', ...props }) {
  return <label className="search"><span className="search__icon" aria-hidden="true">⌕</span><span className="sr-only">{label}</span><input className="field__control" type="search" aria-label={label} {...props}/></label>
}
