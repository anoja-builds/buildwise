# BuildWise

## Construction Materials Procurement, Delivery and Quality Management System

BuildWise is a full-stack system developed for the SE3090 - Software Engineering Frameworks group assignment.

The system is designed to support construction companies in managing material requests, procurement, deliveries, material receiving, and quality inspections through one integrated platform.

## Main Components

1. Material Request & Approval Management
2. Supplier, Quotation & Procurement Management
3. Delivery & Material Receiving Management
4. Quality Inspection & Non-Conformance Management

## Technology Stack

- ASP.NET Core Web API
- PostgreSQL
- React
- Flutter
- Agentic AI

## Main Workflow

Material Request  
→ Approval  
→ Supplier Quotation  
→ Procurement  
→ Delivery  
→ Material Receiving  
→ Quality Inspection  
→ Accept / Reject / Corrective Action

## Team

| Member | Primary Component |
|---|---|
| Peiris DPSS | Material Request & Approval Management |
| Theebika | Supplier, Quotation & Procurement Management |
| Ramya | Delivery & Material Receiving Management |
| Anoja | Quality Inspection & Non-Conformance Management |

## Project Status

Implemented and locally validated across the web frontend, ASP.NET Core API,
Flutter mobile app, and AI agent service.

## Run Locally

### Prerequisites

- Node.js 24 and npm (the Node version used by the web CI job).
- .NET SDK 8.0 (the backend targets `net8.0`).
- PostgreSQL running on `localhost:5432`, with `psql`/`createdb` available.
- Python 3.14 for the shared AI service's locally validated environment.
- Flutter with Dart compatible with `^3.13.0`, as required by `pubspec.yaml`.
- Chrome for Flutter web; Android Studio, the Android SDK, and a running
  Android emulator for mobile testing. Check the SDK setup with `flutter doctor`.

Add the SDK tools to `PATH` and open a new PowerShell session. The commands
below use relative paths: start each service in a separate terminal at the
repository root. Replace angle-bracket placeholders locally; they are not
working credentials. No deployment is implied by these local instructions.

### Startup Order and Local URLs

After completing configuration, start services in this order:

**PostgreSQL → Python AI → ASP.NET API → React → Flutter**

| Service | Local address |
|---|---|
| PostgreSQL | `localhost:5432`, database `buildwise` |
| Shared Python AI | `http://127.0.0.1:8001` |
| AI health check | `http://127.0.0.1:8001/health` |
| ASP.NET API, HTTP launch profile | `http://localhost:5078/api` |
| Swagger, Development only | `http://localhost:5078/swagger` |
| React | `http://localhost:5173` |

The optional API `https` launch profile also listens on `https://localhost:7263`.
The commands below use the `http` profile consistently. Flutter Chrome uses
the URL printed by Flutter; it has no fixed web port configured in this repository.

### PostgreSQL Setup

Start your PostgreSQL service, then create the local database once:

```powershell
createdb -h localhost -p 5432 -U postgres -W buildwise
```

Use your own PostgreSQL administrative role if it is not `postgres`; `-W`
prompts for its password. Alternatively, create `buildwise` in pgAdmin.
Configure the API connection using the appropriate `Host`, `Port`, `Database`,
`Username`, and `Password` fields. Its database role needs permission to apply
migrations. Do not put the connection string in tracked files.

### Secure Local Configuration

The API reads normal ASP.NET configuration, including user-secrets in
Development and environment variables. It does not load the Python `.env`.
The existing project already has a `UserSecretsId`; no new ID is needed.

| Configuration name | Environment equivalent | Purpose |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | PostgreSQL connection; required |
| `Jwt:Key` | `Jwt__Key` | Required random JWT signing key, at least 32 bytes |
| `AgentService:BaseUrl` | `AgentService__BaseUrl` | Shared AI service address; set to `http://127.0.0.1:8001` |
| `AgentService:ApiKey` | `AgentService__ApiKey` | Internal shared secret matching Python's `QUALITY_AGENT_SERVICE_KEY` |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes` | `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpiryMinutes` | Optional overrides of tracked non-secret JWT defaults |
| `BUILDWISE_DEMO_PASSWORD` | `BUILDWISE_DEMO_PASSWORD` | Optional environment-only password for new Development demo accounts; at least 8 characters |

From the repository root, configure user-secrets with your local values:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<local-postgresql-connection-string>" --project backend/BuildWise.Api
dotnet user-secrets set "Jwt:Key" "<random-signing-key-at-least-32-bytes>" --project backend/BuildWise.Api
dotnet user-secrets set "AgentService:BaseUrl" "http://127.0.0.1:8001" --project backend/BuildWise.Api
dotnet user-secrets set "AgentService:ApiKey" "<shared-internal-service-secret>" --project backend/BuildWise.Api
```

Alternatively, configure the environment equivalents above in the API process.
Environment variables override user-secrets. `Jwt:Key` is validated before the
HTTP listener starts: a missing, blank, or short key prevents startup.
Use `AgentService:BaseUrl` for the current shared AI clients; the older
`AgentService:Url` setting alone is insufficient.

If demo logins are needed, set `BUILDWISE_DEMO_PASSWORD` securely in the API
terminal before its first Development startup. Supply that same configured
password to live-test processes; it is not a public default password.

### Web Frontend

```powershell
cd web/buildwise-web
npm ci
npm run dev -- --port 5173 --strictPort
```

The Vite development server runs at `http://localhost:5173`.
The API default is `http://localhost:5078/api`. Set `VITE_API_BASE_URL` in
the web terminal or a local ignored `.env` before starting Vite if the API
address changes. Never put secret keys in `VITE_*` variables: they are
included in browser code.

### Backend API

Complete PostgreSQL and secure configuration above, and start the Python
service before the API. From a new terminal at the repository root:

```powershell
cd backend/BuildWise.Api
dotnet restore
dotnet run --launch-profile http
```

The API runs at `http://localhost:5078` and Swagger is available at
`http://localhost:5078/swagger`.

The `http` launch profile selects Development. In that environment, startup
runs `Database.MigrateAsync()` followed by `DbSeeder.SeedAsync()`. It seeds
missing demo accounts for the five application roles, the material catalogue,
and the procurement sample when appropriate. Existing account passwords,
roles, and activation status are preserved. If `BUILDWISE_DEMO_PASSWORD` is
unset, new demo accounts receive an unreported random password; the login
buttons only fill their email addresses.

To apply existing migrations explicitly, run from the repository root:

```powershell
# Install once if dotnet-ef is not already available.
dotnet tool install --global dotnet-ef --version 8.0.11
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --project backend/BuildWise.Api --startup-project backend/BuildWise.Api
```

Normal setup uses the existing migrations; do not generate a new migration.
Automatic migration/seeding does not run outside Development. Startup logs
seeding failures but can still open the HTTP listener, so check those logs
and database access rather than relying only on Swagger loading.

### AI Agent Service

The shared service is `ai/buildwise-agent-service` and exposes planning,
procurement, delivery-discrepancy, and quality-risk analysis endpoints.

#### Gemini API key and local environment

1. Sign in to [Google AI Studio](https://aistudio.google.com/apikey) with your
   Google account and open the API Keys page.
2. Create an API key for a project you can access. If your existing Cloud
   project is not listed, import it through AI Studio's Projects page first.
   See Google's [API key instructions](https://ai.google.dev/gemini-api/docs/api-key)
   for project permissions and current key-management steps.
3. Store the key only on your machine in
   `ai/buildwise-agent-service/.env` as `GOOGLE_API_KEY`.

Create or edit that `.env` without overwriting any existing local secrets.
This is a placeholder example only:

```dotenv
GOOGLE_API_KEY=<your-gemini-api-key>
QUALITY_AGENT_SERVICE_KEY=<shared-internal-service-secret>
CHAT_MODEL=<gemini-model-id-available-to-your-project>
```

`GOOGLE_API_KEY` enables provider-backed analysis.
`QUALITY_AGENT_SERVICE_KEY` authenticates internal API-to-agent calls and
**must match ASP.NET's `AgentService:ApiKey` exactly**. It is a separate secret
from both the Gemini key and `Jwt:Key`. `CHAT_MODEL` selects the model; if
omitted, the current code defaults to `gemini-3.5-flash`. Choose a model
available to your project rather than assuming quota or access.

#### Start and verify the shared service

```powershell
cd ai/buildwise-agent-service
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe -m uvicorn app.main:app --reload --host 127.0.0.1 --port 8001
```

In another terminal, verify availability:

```powershell
Invoke-RestMethod http://127.0.0.1:8001/health
```

Expect `status` to be `ok`. This unauthenticated health check confirms that
the service is listening; it does not verify Gemini quota or the internal
shared-secret configuration. Restart the service after changing `.env`.

Gemini can return quota/rate-limit errors such as HTTP 429. Check your
project's usage and model limits in AI Studio; see Google's
[rate-limit documentation](https://ai.google.dev/gemini-api/docs/rate-limits).
AI features may safely use deterministic fallback when provider quota or
service availability prevents AI advice. This is not a live-provider result.
Fallback support depends on the feature: quality-risk analysis can instead
report a failed/unavailable advisory. Neither fallback nor an AI failure
bypasses deterministic validation or the required human decisions.

### Flutter Mobile App

For Flutter web in Chrome:

```powershell
cd mobile/buildwise_mobile
flutter pub get
flutter run -d chrome --dart-define=API_BASE_URL=http://localhost:5078/api
```

The mobile API client uses `localhost:5078` in Chrome and `10.0.2.2:5078`
for the Android emulator. Override the API URL when needed:

```powershell
# From mobile/buildwise_mobile; start an Android emulator first.
flutter devices
flutter run -d "<android-device-id>" --dart-define=API_BASE_URL=http://10.0.2.2:5078/api
```

Replace `<android-device-id>` with the ID printed by `flutter devices`.
Android emulator `localhost` refers to the emulator itself; `10.0.2.2`
reaches the development computer. For a physical device, supply the computer's
reachable LAN address and appropriate API binding/firewall settings. Restart
the Flutter run after changing `API_BASE_URL`; keep the `/api` suffix.

### Tests and Validation

```powershell
# Run this block from the repository root after installing dependencies.
dotnet test backend/BuildWise.Api.Tests/BuildWise.Api.Tests.csproj
dotnet test backend/BuildWise.Tests/BuildWise.Tests.csproj

Push-Location ai/buildwise-agent-service
.\.venv\Scripts\python.exe -m pip install pytest
.\.venv\Scripts\python.exe -m pytest
Pop-Location

Push-Location web/buildwise-web
npm test -- --run
npm run lint
npm run build
Pop-Location

Push-Location mobile/buildwise_mobile
flutter analyze
flutter test
Pop-Location
```

The current local validation results are: web build succeeds, API build
succeeds, Flutter tests pass, and AI service tests pass. Web lint currently
reports warnings only.

Some PostgreSQL and live-client integration tests are opt-in and may be
skipped without their environment configuration. A green unit-test run does
not mean every live integration or provider call was exercised.

### Common Setup Problems

- **API exits with a JWT configuration error:** configure `Jwt:Key` or
  `Jwt__Key` with a random secret of at least 32 bytes. Check for an empty
  environment override. User-secrets are loaded in Development.
- **Database connection or seeding fails:** start PostgreSQL, verify the
  database/role and `ConnectionStrings:DefaultConnection`, check migration
  permissions, and inspect the API startup log. A listening API does not
  guarantee successful seeding.
- **Demo login fails:** the configured `BUILDWISE_DEMO_PASSWORD` must match
  the stored account password. Restarting/seeding does not reset an existing
  password or reactivate an inactive account. Stale tokens after account
  status/role changes are rejected; sign in again with an active account.
- **AI health passes but analysis returns 401/503:** confirm
  `AgentService:BaseUrl` and matching `AgentService:ApiKey` /
  `QUALITY_AGENT_SERVICE_KEY` values. Keep provider quota errors separate
  from internal-service authentication failures.
- **Gemini quota, rate limit, or unavailable model:** check `GOOGLE_API_KEY`,
  `CHAT_MODEL`, and AI Studio usage. Respect retry delays; deterministic
  fallback or an explicit advisory failure may be the expected safe outcome.
- **Port already occupied:** stop the process you started on that port, or
  update the service and all client base URLs consistently. React's command
  above fails instead of silently moving away from port 5173.
- **Flutter cannot reach the API:** use `localhost` in Chrome and `10.0.2.2`
  in an Android emulator. Verify `flutter devices`, the API listener, and
  the `/api` suffix before retrying.
- **PowerShell blocks `npm.ps1`:** use `npm.cmd` for the same commands.
  Calling the virtual environment's Python executable directly avoids
  requiring PowerShell virtual-environment activation.

### Security Notes

Never commit `.env`, database passwords/connection strings, JWT signing keys,
provider API keys, or internal service secrets. The repository ignores local
`.env` files and `appsettings.Development.json`; `.env.example` must contain
placeholders only. Keep secrets in user-secrets or process environment/local
ignored configuration, and do not print them in logs or screenshots. Rotate
previously exposed credentials; removing a value from a file does not remove
it from Git history. Browser/mobile configuration must contain no private keys.

### GitHub Actions

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on pushes and pull
requests to `main`. It builds/tests `BuildWise.Api.Tests`, runs React
lint/tests/build, and runs Flutter analysis/tests. Its Python job currently
tests the older `backend/agent_service` directory, not the shared
`ai/buildwise-agent-service` documented above; run the shared-service tests
locally as shown. This workflow does not deploy the application.
