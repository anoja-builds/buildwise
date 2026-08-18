export default function Card({ title, subtitle, children, className = '', ...props }) {
  return <section className={`card ${className}`.trim()} {...props}>{title && <h2 className="card__title">{title}</h2>}{subtitle && <p className="card__subtitle">{subtitle}</p>}{children}</section>
}
