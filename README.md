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

- Node.js and npm
- .NET SDK 8.0+
- PostgreSQL running on `localhost:5432`
- Python 3.14+
- Flutter 3.47.5 with Dart 3.13+

The Flutter SDK is installed at `C:\src\flutter`. Open a new PowerShell
session after adding Flutter to `PATH`, or use the full path shown below.

### Web Frontend

```powershell
cd E:\SEF_Project\buildwise\web\buildwise-web
npm install
npm run dev
```

The Vite development server runs at `http://localhost:5173`.

### Backend API

Set the PostgreSQL connection string in .NET user-secrets. Replace the
placeholder with the local PostgreSQL password; do not commit credentials.

```powershell
cd E:\SEF_Project\buildwise\backend\BuildWise.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=buildwise;Username=postgres;Password=REPLACE_WITH_LOCAL_PASSWORD"
dotnet run
```

The API runs at `http://localhost:5078` and Swagger is available at
`http://localhost:5078/swagger`.

### AI Agent Service

```powershell
cd E:\SEF_Project\buildwise\ai\buildwise-agent-service
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe -m uvicorn app.main:app --reload --port 8001
```

### Flutter Mobile App

For Flutter web in Chrome:

```powershell
cd E:\SEF_Project\buildwise\mobile\buildwise_mobile
C:\src\flutter\bin\flutter.bat pub get
C:\src\flutter\bin\flutter.bat run -d chrome
```

The mobile API client uses `localhost:5078` in Chrome and `10.0.2.2:5078`
for the Android emulator. Override the API URL when needed:

```powershell
C:\src\flutter\bin\flutter.bat run -d chrome --dart-define=API_BASE_URL=http://localhost:5078/api
```

### Tests and Validation

```powershell
cd E:\SEF_Project\buildwise\web\buildwise-web
npm run build
npm run lint

cd E:\SEF_Project\buildwise\backend\BuildWise.Api
dotnet build

cd E:\SEF_Project\buildwise\mobile\buildwise_mobile
C:\src\flutter\bin\flutter.bat test

cd E:\SEF_Project\buildwise\ai\buildwise-agent-service
.\.venv\Scripts\python.exe -m pip install pytest
.\.venv\Scripts\python.exe -m pytest -q
```

The current local validation results are: web build succeeds, API build
succeeds, Flutter tests pass, and AI service tests pass. Web lint currently
reports warnings only.
