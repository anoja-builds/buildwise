export default function PageHeader({ title, description, actions, eyebrow }) {
  return <header className="page-header"><div>{eyebrow && <div className="page-header__eyebrow">{eyebrow}</div>}<h1>{title}</h1>{description && <p>{description}</p>}</div>{actions && <div className="actions">{actions}</div>}</header>
}
