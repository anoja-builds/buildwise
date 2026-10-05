# ADR-005 — Deployment Architecture

**Status:** Accepted · **Date:** 2026-09-23 · **Owner:** Group (all component owners)  
**Scope:** SE3090 spec §11 (deployment), §12 (health, Swagger, public URLs)

---

## Context

The SE3090 spec requires:
- ASP.NET Core API deployed with a public HTTPS URL
- Working `/health` endpoint
- Working `/swagger` endpoint  
- PostgreSQL deployed securely (credentials via environment variables, not committed)
- React web app deployed with a public HTTPS URL
- Flutter Android APK available for download or sideload
- Agentic AI agent services accessible to the deployed API

Four forces shaped the choice:

1. **Cost** — student project; free tier is a hard requirement.
2. **Simplicity** — no DevOps experience on the team; managed platforms preferred.
3. **PostgreSQL support** — managed Postgres must be available on the same platform.
4. **Environment variable support** — no secrets in code or config files.

---

## Options considered

| # | Option | Verdict |
|---|---|---|
| A | **Railway** (API + PostgreSQL + Python services) + **Vercel** (React) | **Chosen** — free tier sufficient, managed Postgres included, GitHub-connected deploys |
| B | Render (API + PostgreSQL) + Netlify (React) | Viable alternative; rejected because Railway's Dockerfile-free deploy is simpler for .NET |
| C | Azure App Service + Azure Database for PostgreSQL | Rejected — free tier expires after 12 months; credit card required |
| D | Heroku + Heroku Postgres | Rejected — free dynos removed; costs money |
| E | AWS Elastic Beanstalk + RDS | Rejected — complex IAM setup; exceeds team's DevOps capacity |
| F | VPS (e.g., DigitalOcean Droplet) | Rejected — requires manual nginx/SSL setup; maintenance burden |

---

## Decision

### API — Railway

- ASP.NET Core 8 deployed as a Railway service connected to the GitHub repository.
- `railway.json` (or `Dockerfile` if required) at repository root.
- Environment variables set in Railway dashboard (never in code):
  - `ConnectionStrings__DefaultConnection` — Railway-provided PostgreSQL URL
  - `Jwt__Key` — signing key
  - `AgentService__Url` / `AgentService__RequestUrl` / `AgentService__DeliveryUrl` / `AgentService__QualityUrl` — Railway internal URLs for agent services

### PostgreSQL — Railway Managed Postgres

- Railway-provisioned PostgreSQL database.
- EF Core migrations run on startup (`app.MigrateAsync()` in `Program.cs`).
- Credentials injected via `ConnectionStrings__DefaultConnection` environment variable.

### Python Agent Services — Railway (4 services)

- Four Railway services (`quotation_agent.py`, `request_agent.py`, `delivery_agent.py`, `quality_agent.py`) each exposed on internal Railway URLs.
- SMTP — configured via `Smtp__Host` / `Smtp__Username` / `Smtp__Password` environment variables; console-logged if `Smtp__Host` is empty.

### React — Vercel

- `web/buildwise-web` deployed to Vercel via GitHub integration.
- `VITE_API_BASE_URL` set to the Railway API URL in Vercel environment variables.
- Build command: `npm run build`. Output directory: `dist`.

### Flutter APK

- Built locally with `flutter build apk --release`.
- APK (`build/app/outputs/flutter-apk/app-release.apk`) submitted as a binary artifact alongside the repository.
- API base URL in `lib/core/api/api_client.dart` is configurable at build time via a `--dart-define=API_BASE_URL=<url>` flag.

---

## Public URLs (to be populated after deployment)

| Service | URL |
|---|---|
| API | `https://buildwise-api.railway.app` |
| Swagger | `https://buildwise-api.railway.app/swagger` |
| Health | `https://buildwise-api.railway.app/health` |
| React | `https://buildwise-web.vercel.app` |
| APK | Submitted as binary with report |

---

## Security

- All secrets via environment variables; `.env` is `.gitignore`d.
- HTTPS enforced by Railway and Vercel (automatic TLS).
- JWT audience and issuer validated on every request.
- CORS configured to allow only the Vercel origin in production.
- Database credentials never committed.

---

## Consequences

**Positive**
- Zero infrastructure cost (free tiers)
- GitHub-connected: push to `main` triggers automatic redeploy
- Managed Postgres: no DBA work, automatic backups
- Vercel CDN: React assets served from edge nodes

**Negative / accepted**
- Railway free tier has cold-start latency (~5–10 s) after inactivity; acceptable for demonstration
- Python agent services on Railway free tier sleep after 10 min; first agent call after inactivity may timeout (mitigated by the C# in-process fallback in `QuotationAgentClient`)
- APK must be rebuilt and re-submitted manually if the API URL changes

---

## References

- `backend/BuildWise.Api/Program.cs` — `app.MigrateAsync()`, health endpoint registration
- `backend/BuildWise.Api/appsettings.json` — configuration key names (values via env vars)
- `.env.example` — full list of required environment variables
- `web/buildwise-web/package.json` — build scripts
- `mobile/buildwise_mobile/lib/core/api/api_client.dart` — configurable API base URL
