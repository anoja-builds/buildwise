import './shared.css'

export default function Button({ variant = 'primary', type = 'button', children, className = '', ...props }) {
  return <button type={type} className={`bw-button bw-button--${variant} ${className}`.trim()} {...props}>{children}</button>
}
