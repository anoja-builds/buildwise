<#
.SYNOPSIS
  Creates individual ZIP submission packages for each of the 4 BuildWise component owners.
  Run from the repository root: .\scripts\create-individual-zips.ps1
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root "submissions"
if (-not (Test-Path $outDir)) { New-Item $outDir -ItemType Directory | Out-Null }

Write-Host "`n=== BuildWise Individual ZIP Creator ===" -ForegroundColor Cyan
Write-Host "Output directory: $outDir`n" -ForegroundColor Gray

# ─────────────────────────────────────────────────────────────────────────────
# Helper: copy files from $src matching $patterns into $dest (preserving tree)
# ─────────────────────────────────────────────────────────────────────────────
function Copy-Component {
    param(
        [string]$Src,
        [string[]]$RelPaths,   # relative to $root
        [string]$Dest
    )
    foreach ($rel in $RelPaths) {
        $full = Join-Path $root $rel
        if (Test-Path $full -PathType Leaf) {
            $target = Join-Path $Dest $rel
            $targetDir = Split-Path $target -Parent
            if (-not (Test-Path $targetDir)) { New-Item $targetDir -ItemType Directory -Force | Out-Null }
            Copy-Item $full $target -Force
        } elseif (Test-Path $full -PathType Container) {
            $target = Join-Path $Dest $rel
            Copy-Item $full $target -Recurse -Force
        } else {
            Write-Host "  [SKIP] Not found: $rel" -ForegroundColor DarkYellow
        }
    }
}

function New-Zip {
    param([string]$SrcDir, [string]$ZipPath)
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($SrcDir, $ZipPath)
    $mb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
    Write-Host "  Created: $(Split-Path $ZipPath -Leaf) ($mb MB)" -ForegroundColor Green
}

# Shared files every member gets
$sharedFiles = @(
    "README.md",
    ".env.example",
    ".github\workflows\ci.yml",
    "Dockerfile",
    "docs\BuildWise_ERD.pdf",
    "docs\BuildWise_ERD.dbml",
    "docs\environment_and_agents.md",
    "docs\project_verification_guide.md",
    "docs\assignment_compliance_audit.md",
    "docs\adr\0001-quotation-analysis-agent-and-workflow-state.md",
    "docs\adr\ADR-002-component2-agent-architecture.md",
    "docs\adr\ADR-003-react-state-management.md",
    "docs\adr\ADR-004-flutter-state-management.md",
    "docs\adr\ADR-005-deployment-architecture.md",
    "docs\reports\GROUP-ai-usage-declaration.md",
    "backend\BuildWise.Api\appsettings.json",
    "backend\BuildWise.Api\Program.cs",
    "backend\BuildWise.Api\BuildWise.Api.csproj",
    "backend\BuildWise.Api\Data\ApplicationDbContext.cs",
    "backend\BuildWise.Api\Models\Entities\User.cs",
    "backend\BuildWise.Api\Models\Entities\Role.cs",
    "backend\BuildWise.Api\Controllers\AuthController.cs",
    "backend\BuildWise.Api\Security",
    "backend\BuildWise.Api\Middleware",
    "backend\BuildWise.Api\Properties",
    "mobile\buildwise_mobile\pubspec.yaml",
    "mobile\buildwise_mobile\lib\core",
    "mobile\buildwise_mobile\lib\main.dart",
    "mobile\buildwise_mobile\lib\routes",
    "mobile\buildwise_mobile\lib\common",
    "web\buildwise-web\package.json",
    "web\buildwise-web\vite.config.js",
    "web\buildwise-web\vercel.json",
    "web\buildwise-web\src\auth",
    "web\buildwise-web\src\services\apiTransport.js",
    "web\buildwise-web\src\services\authApi.js",
    "web\buildwise-web\src\styles",
    "web\buildwise-web\src\layouts"
)

# ─────────────────────────────────────────────────────────────────────────────
# COMPONENT 1 — Peiris DPSS — Material Request & Approval Management
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "Building Component 1 (Peiris — Material Request & Approval)..." -ForegroundColor Yellow
$c1Dir = Join-Path $outDir "Component1-Peiris-MaterialRequest"
if (Test-Path $c1Dir) { Remove-Item $c1Dir -Recurse -Force }
New-Item $c1Dir -ItemType Directory | Out-Null

$c1Files = $sharedFiles + @(
    # Backend — C1 owns
    "backend\BuildWise.Api\Controllers\MaterialRequestsController.cs",
    "backend\BuildWise.Api\Controllers\DashboardController.cs",
    "backend\BuildWise.Api\Controllers\AdministrationController.cs",
    "backend\BuildWise.Api\Controllers\NotificationsController.cs",
    "backend\BuildWise.Api\Models\Entities\MaterialRequest.cs",
    "backend\BuildWise.Api\Models\Entities\MaterialRequestItem.cs",
    "backend\BuildWise.Api\Models\Entities\MaterialRequestHistory.cs",
    "backend\BuildWise.Api\Models\Entities\Approval.cs",
    "backend\BuildWise.Api\Models\Entities\Material.cs",
    "backend\BuildWise.Api\Models\Entities\Project.cs",
    "backend\BuildWise.Api\Models\Entities\AuditLog.cs",
    "backend\BuildWise.Api\Models\Entities\NotificationEvent.cs",
    "backend\BuildWise.Api\Services\MaterialRequestService.cs",
    "backend\BuildWise.Api\Services\NotificationService.cs",
    "backend\BuildWise.Api\Services\DashboardService.cs",
    "backend\BuildWise.Api\Services\SmtpEmailService.cs",
    "backend\BuildWise.Api\Services\IEmailService.cs",
    "backend\BuildWise.Api\DTOs",
    "backend\BuildWise.Api\Data\Configurations",
    "backend\BuildWise.Api\Data\DbSeeder.cs",
    # Tests
    "backend\BuildWise.Api.Tests\MaterialRequestServiceTests.cs",
    "backend\BuildWise.Api.Tests\RbacAuthorizationTests.cs",
    "backend\BuildWise.Api.Tests\FullLifecycleScenarioTests.cs",
    "backend\BuildWise.Api.Tests\AuthServiceTests.cs",
    "backend\BuildWise.Api.Tests\RbacApiFactory.cs",
    "backend\BuildWise.Api.Tests\TestDbFactory.cs",
    "backend\BuildWise.Api.Tests\NoOpEmailService.cs",
    "backend\BuildWise.Api.Tests\BuildWise.Api.Tests.csproj",
    # React — C1 pages
    "web\buildwise-web\src\pages\MaterialRequestsPage.jsx",
    "web\buildwise-web\src\pages\MaterialRequestsPage.test.jsx",
    "web\buildwise-web\src\pages\AdministrationPage.jsx",
    "web\buildwise-web\src\pages\common",
    "web\buildwise-web\src\App.jsx",
    "web\buildwise-web\src\test",
    # Flutter — C1 mobile feature
    "mobile\buildwise_mobile\lib\features\material_requests",
    "mobile\buildwise_mobile\lib\features\auth",
    "mobile\buildwise_mobile\lib\features\operations\screens\dashboard_screen.dart",
    "mobile\buildwise_mobile\lib\features\operations\screens\material_requests_screen.dart",
    "mobile\buildwise_mobile\lib\features\operations\services",
    # APK
    "mobile\buildwise_mobile\build\app\outputs\flutter-apk\app-release.apk",
    # Scripts
    "scripts\start-dev.ps1",
    "scripts\stop-dev.ps1",
    "scripts\verify-rbac.ps1",
    "scripts\verify-project.ps1",
    "scripts\smoke-test.ps1",
    # AI log
    "docs\reports\IT24XXXXX-Peiris-ai-usage-log.md"
)
Copy-Component -Src $root -RelPaths $c1Files -Dest $c1Dir

# Write a README_COMPONENT1.md
@"
# Component 1 — Material Request & Approval Management
**Student:** Peiris DPSS
**Your primary ownership:** Material Request lifecycle, multi-level approvals, RBAC, SMTP notifications

## What you submit
- `backend/BuildWise.Api/Controllers/MaterialRequestsController.cs` — your main controller
- `backend/BuildWise.Api/Services/MaterialRequestService.cs` — business logic
- `backend/BuildWise.Api.Tests/MaterialRequestServiceTests.cs` — your unit tests
- `web/buildwise-web/src/pages/MaterialRequestsPage.jsx` — your React page
- `mobile/.../features/material_requests/` — your Flutter screens
- `docs/reports/IT24XXXXX-Peiris-ai-usage-log.md` — your AI usage log

## Run the system
1. Copy `.env.example` → `.env` and fill in your DB password
2. Run: `powershell -File scripts/start-dev.ps1`
3. Web: http://localhost:5173 | API: http://localhost:5078 | Swagger: http://localhost:5078/swagger

## Test your component
``````powershell
dotnet test backend/BuildWise.Api.Tests/BuildWise.Api.Tests.csproj --filter "MaterialRequest|Rbac|FullLifecycle"
``````

## Install APK on Android phone (same WiFi as this PC)
1. Enable "Install unknown apps" on your phone
2. Transfer `mobile/buildwise_mobile/build/app/outputs/flutter-apk/app-release.apk` to your phone
3. Open the APK file on your phone to install
4. API is running at http://10.110.168.34:5078 (your PC must be on)
"@ | Set-Content (Join-Path $c1Dir "README_COMPONENT1.md") -Encoding UTF8

New-Zip -SrcDir $c1Dir -ZipPath (Join-Path $outDir "Component1-Peiris-MaterialRequest.zip")

# ─────────────────────────────────────────────────────────────────────────────
# COMPONENT 2 — Theebika IT24102414 — Supplier, Quotation, RFQ & Procurement
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`nBuilding Component 2 (Theebika IT24102414 — Procurement)..." -ForegroundColor Yellow
$c2Dir = Join-Path $outDir "Component2-IT24102414-Theebika-Procurement"
if (Test-Path $c2Dir) { Remove-Item $c2Dir -Recurse -Force }
New-Item $c2Dir -ItemType Directory | Out-Null

$c2Files = $sharedFiles + @(
    # Backend — C2 owns
    "backend\BuildWise.Api\Controllers\SuppliersController.cs",
    "backend\BuildWise.Api\Controllers\QuotationsController.cs",
    "backend\BuildWise.Api\Controllers\RfqsController.cs",
    "backend\BuildWise.Api\Controllers\ProcurementWorkflowController.cs",
    "backend\BuildWise.Api\Controllers\PurchaseOrdersController.cs",
    "backend\BuildWise.Api\Controllers\SupplierPortalController.cs",
    "backend\BuildWise.Api\Controllers\ReferenceDataController.cs",
    "backend\BuildWise.Api\Controllers\AgentWorkflowsController.cs",
    "backend\BuildWise.Api\Models\Entities\Supplier.cs",
    "backend\BuildWise.Api\Models\Entities\Quotation.cs",
    "backend\BuildWise.Api\Models\Entities\QuotationItem.cs",
    "backend\BuildWise.Api\Models\Entities\Rfq.cs",
    "backend\BuildWise.Api\Models\Entities\PurchaseOrder.cs",
    "backend\BuildWise.Api\Models\Entities\PurchaseOrderItem.cs",
    "backend\BuildWise.Api\Models\Entities\AgentWorkflow.cs",
    "backend\BuildWise.Api\Models\Entities\AgentWorkflowStep.cs",
    "backend\BuildWise.Api\Models\Entities\AgentApproval.cs",
    "backend\BuildWise.Api\Services\ProcurementPlanningAgentService.cs",
    "backend\BuildWise.Api\Services\ProcurementValidationService.cs",
    "backend\BuildWise.Api\Services\ProcurementWorkflowService.cs",
    "backend\BuildWise.Api\Services\IntegratedProcurementService.cs",
    "backend\BuildWise.Api\Services\QuotationAgentClient.cs",
    "backend\BuildWise.Api\Services\OperationalAgentClient.cs",
    "backend\BuildWise.Api\Services\OperationalAgentAuditService.cs",
    "backend\BuildWise.Api\Services\PurchaseOrderProjection.cs",
    "backend\BuildWise.Api\DTOs",
    "backend\BuildWise.Api\Data\Configurations",
    # Python agent
    "backend\agent_service\quotation_agent.py",
    "backend\agent_service\request_agent.py",
    "backend\agent_service\env_loader.py",
    "backend\agent_service\requirements.txt",
    "backend\agent_service\test_quotation_agent.py",
    "backend\agent_service\test_request_agent.py",
    # Tests
    "backend\BuildWise.Api.Tests\ProcurementValidationServiceTests.cs",
    "backend\BuildWise.Api.Tests\ProcurementPlanningAgentServiceTests.cs",
    "backend\BuildWise.Api.Tests\ScenarioReplayTests.cs",
    "backend\BuildWise.Api.Tests\AgentRecommendationSchemaTests.cs",
    "backend\BuildWise.Api.Tests\BudgetValidationTests.cs",
    "backend\BuildWise.Api.Tests\SupplierPortalIsolationTests.cs",
    "backend\BuildWise.Api.Tests\PurchaseOrderRedactionTests.cs",
    "backend\BuildWise.Api.Tests\BuildWise.Api.Tests.csproj",
    "backend\BuildWise.Api.Tests\TestDbFactory.cs",
    "backend\BuildWise.Api.Tests\NoOpEmailService.cs",
    # React — C2 features
    "web\buildwise-web\src\Features\procurement",
    "web\buildwise-web\src\pages\RfqPage.jsx",
    "web\buildwise-web\src\pages\SupplierPortalPage.jsx",
    "web\buildwise-web\src\pages\AgentWorkflowsPage.jsx",
    "web\buildwise-web\src\services\procurementApi.js",
    "web\buildwise-web\src\services\supplierPortalApi.js",
    "web\buildwise-web\src\services\administrationApi.js",
    "web\buildwise-web\src\App.jsx",
    # Flutter — C2 mobile feature
    "mobile\buildwise_mobile\lib\features\procurement",
    "mobile\buildwise_mobile\lib\features\supplier",
    "mobile\buildwise_mobile\lib\features\operations\screens\agent_workflows_screen.dart",
    "mobile\buildwise_mobile\lib\features\operations\widgets",
    # Scripts
    "scripts\verify-component2.ps1",
    "scripts\verify-planning-agent.ps1",
    "scripts\verify-validation-agent.ps1",
    "scripts\verify-budget-live.ps1",
    "scripts\verify-rfq-admin.ps1",
    "scripts\start-dev.ps1",
    # APK
    "mobile\buildwise_mobile\build\app\outputs\flutter-apk\app-release.apk",
    # Docs
    "docs\component2_spec.md",
    "docs\component2_build_guide.md",
    "docs\component2_setup_guide.md",
    "docs\adr\0001-quotation-analysis-agent-and-workflow-state.md",
    "docs\adr\ADR-002-component2-agent-architecture.md",
    "docs\handoff",
    "docs\reports\IT24102414-ai-usage-log.md",
    "docs\reports\component2_ai_usage_log_and_reflection.md"
)
Copy-Component -Src $root -RelPaths $c2Files -Dest $c2Dir

@"
# Component 2 — Supplier, Quotation, RFQ & Procurement Management
**Student:** Theebika · **ID:** IT24102414
**Your primary ownership:** Suppliers, Quotations, RFQs, Procurement Workflow, 4 AI Agents

## What you submit
- `backend/BuildWise.Api/Controllers/Suppliers|Quotations|Rfqs|ProcurementWorkflow|PurchaseOrders|SupplierPortalController.cs`
- `backend/agent_service/quotation_agent.py` — the main AI agent (Python)
- `backend/BuildWise.Api.Tests/Procurement*Tests.cs` — your unit tests
- `web/buildwise-web/src/Features/procurement/` — all React procurement UI
- `mobile/.../features/procurement/` — Flutter procurement screens
- `docs/reports/IT24102414-ai-usage-log.md` — your AI usage log

## Run & verify your component
``````powershell
powershell -File scripts/start-dev.ps1          # start all services
powershell -File scripts/verify-component2.ps1  # verify all C2 checks
powershell -File scripts/verify-planning-agent.ps1
``````

## Test
``````powershell
dotnet test backend/BuildWise.Api.Tests/BuildWise.Api.Tests.csproj
cd backend/agent_service && pytest -q
cd web/buildwise-web && npm test -- --run
``````

## Install APK on Android phone (same WiFi as this PC)
1. Enable "Install unknown apps" on your phone
2. Transfer `mobile/.../flutter-apk/app-release.apk` to your phone and install
3. API is at http://10.110.168.34:5078 (PC must be on same WiFi)
"@ | Set-Content (Join-Path $c2Dir "README_COMPONENT2.md") -Encoding UTF8

New-Zip -SrcDir $c2Dir -ZipPath (Join-Path $outDir "Component2-IT24102414-Theebika-Procurement.zip")

# ─────────────────────────────────────────────────────────────────────────────
# COMPONENT 3 — Ramya IT24102513 — Delivery & Material Receiving Management
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`nBuilding Component 3 (Ramya IT24102513 — Delivery)..." -ForegroundColor Yellow
$c3Dir = Join-Path $outDir "Component3-IT24102513-Ramya-Delivery"
if (Test-Path $c3Dir) { Remove-Item $c3Dir -Recurse -Force }
New-Item $c3Dir -ItemType Directory | Out-Null

$c3Files = $sharedFiles + @(
    # Backend — C3 owns
    "backend\BuildWise.Api\Controllers\DeliveriesController.cs",
    "backend\BuildWise.Api\Models\Entities\Delivery.cs",
    "backend\BuildWise.Api\Models\Entities\DeliveryItem.cs",
    "backend\BuildWise.Api\Models\Entities\DeliveryIssue.cs",
    "backend\BuildWise.Api\Models\Entities\DeliveryEvidence.cs",
    "backend\BuildWise.Api\Models\Entities\DeliverySchedule.cs",
    "backend\BuildWise.Api\Services\DeliveryService.cs",
    "backend\BuildWise.Api\Services\DeliveryAgentService.cs",
    "backend\BuildWise.Api\DTOs",
    "backend\BuildWise.Api\Data\Configurations",
    # Python agent
    "backend\agent_service\delivery_agent.py",
    "backend\agent_service\request_agent.py",
    "backend\agent_service\env_loader.py",
    "backend\agent_service\requirements.txt",
    "backend\agent_service\test_delivery_agent.py",
    "backend\agent_service\test_request_agent.py",
    # Tests
    "backend\BuildWise.Api.Tests\DeliveryServiceTests.cs",
    "backend\BuildWise.Api.Tests\DeliveryAgentServiceTests.cs",
    "backend\BuildWise.Api.Tests\FullLifecycleScenarioTests.cs",
    "backend\BuildWise.Api.Tests\BuildWise.Api.Tests.csproj",
    "backend\BuildWise.Api.Tests\TestDbFactory.cs",
    "backend\BuildWise.Api.Tests\NoOpEmailService.cs",
    # React — C3 pages
    "web\buildwise-web\src\pages\DeliveriesPage.jsx",
    "web\buildwise-web\src\pages\DeliveriesPage.test.jsx",
    "web\buildwise-web\src\pages\DeliveriesPage.css",
    "web\buildwise-web\src\App.jsx",
    # Flutter — C3 mobile feature
    "mobile\buildwise_mobile\lib\features\deliveries",
    "mobile\buildwise_mobile\lib\features\operations\screens\delivery_receiving_screen.dart",
    # Scripts
    "scripts\start-dev.ps1",
    "scripts\setup_component3.py",
    "scripts\verify-full-journey.ps1",
    # APK
    "mobile\buildwise_mobile\build\app\outputs\flutter-apk\app-release.apk",
    # Docs
    "docs\reports\IT24102513-Ramya-ai-usage-log.md"
)
Copy-Component -Src $root -RelPaths $c3Files -Dest $c3Dir

@"
# Component 3 — Delivery & Material Receiving Management
**Student:** Ramya · **ID:** IT24102513
**Your primary ownership:** Delivery scheduling, receiving, discrepancy detection, DeliveryDiscrepancyAgent

## What you submit
- `backend/BuildWise.Api/Controllers/DeliveriesController.cs` — your main controller
- `backend/agent_service/delivery_agent.py` — your Python AI agent (port 8003)
- `backend/BuildWise.Api.Tests/DeliveryServiceTests.cs` — your unit tests
- `web/buildwise-web/src/pages/DeliveriesPage.jsx` — your React page
- `mobile/.../features/deliveries/` — your Flutter screens
- `docs/reports/IT24102513-Ramya-ai-usage-log.md` — your AI usage log

## Run & verify
``````powershell
powershell -File scripts/start-dev.ps1
powershell -File scripts/verify-full-journey.ps1
``````

## Test your component
``````powershell
dotnet test backend/BuildWise.Api.Tests/BuildWise.Api.Tests.csproj --filter "Delivery"
cd backend/agent_service && pytest test_delivery_agent.py test_request_agent.py -v
``````

## Install APK on Android phone
1. Enable "Install unknown apps" on your phone
2. Transfer `mobile/.../flutter-apk/app-release.apk` and install
3. API at http://10.110.168.34:5078 (PC must be on same WiFi)
"@ | Set-Content (Join-Path $c3Dir "README_COMPONENT3.md") -Encoding UTF8

New-Zip -SrcDir $c3Dir -ZipPath (Join-Path $outDir "Component3-IT24102513-Ramya-Delivery.zip")

# ─────────────────────────────────────────────────────────────────────────────
# COMPONENT 4 — Anoja — Quality Inspection & Non-Conformance Management
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`nBuilding Component 4 (Anoja — Quality Inspection)..." -ForegroundColor Yellow
$c4Dir = Join-Path $outDir "Component4-Anoja-QualityInspection"
if (Test-Path $c4Dir) { Remove-Item $c4Dir -Recurse -Force }
New-Item $c4Dir -ItemType Directory | Out-Null

$c4Files = $sharedFiles + @(
    # Backend — C4 owns
    "backend\BuildWise.Api\Controllers\QualityInspectionsController.cs",
    "backend\BuildWise.Api\Models\Entities\Inspection.cs",
    "backend\BuildWise.Api\Models\Entities\InspectionItem.cs",
    "backend\BuildWise.Api\Models\Entities\InspectionEvidence.cs",
    "backend\BuildWise.Api\Models\Entities\NonConformance.cs",
    "backend\BuildWise.Api\Models\Enums\InspectionDecision.cs",
    "backend\BuildWise.Api\Models\Enums\InspectionStatus.cs",
    "backend\BuildWise.Api\Models\Enums\NonConformanceSeverity.cs",
    "backend\BuildWise.Api\Models\Enums\NonConformanceStatus.cs",
    "backend\BuildWise.Api\Data\Configurations\InspectionConfiguration.cs",
    "backend\BuildWise.Api\Data\Configurations\InspectionItemConfiguration.cs",
    "backend\BuildWise.Api\Data\Configurations\NonConformanceConfiguration.cs",
    "backend\BuildWise.Api\Services\QualityInspectionService.cs",
    "backend\BuildWise.Api\DTOs",
    # Python agent — C4 has the biggest one
    "backend\agent_service\quality_agent.py",
    "backend\agent_service\env_loader.py",
    "backend\agent_service\requirements.txt",
    "backend\agent_service\test_quality_agent.py",
    # Tests
    "backend\BuildWise.Api.Tests\QualityInspectionServiceTests.cs",
    "backend\BuildWise.Api.Tests\FullLifecycleScenarioTests.cs",
    "backend\BuildWise.Api.Tests\BuildWise.Api.Tests.csproj",
    "backend\BuildWise.Api.Tests\TestDbFactory.cs",
    "backend\BuildWise.Api.Tests\NoOpEmailService.cs",
    # React — C4 pages + components
    "web\buildwise-web\src\pages\QualityInspectionsPage.jsx",
    "web\buildwise-web\src\pages\QualityInspectionsPage.test.jsx",
    "web\buildwise-web\src\pages\NonConformancesPage.jsx",
    "web\buildwise-web\src\Features\quality",
    "web\buildwise-web\src\services\qualityApi.js",
    "web\buildwise-web\src\App.jsx",
    # Flutter — C4 mobile feature
    "mobile\buildwise_mobile\lib\features\quality",
    "mobile\buildwise_mobile\lib\features\operations\screens\quality_inspection_screen.dart",
    # Scripts
    "scripts\start-dev.ps1",
    "scripts\verify-component4.ps1",
    "scripts\verify-full-journey.ps1",
    # APK
    "mobile\buildwise_mobile\build\app\outputs\flutter-apk\app-release.apk",
    # Docs
    "docs\reports\IT24XXXXX-Anoja-ai-usage-log.md"
)
Copy-Component -Src $root -RelPaths $c4Files -Dest $c4Dir

@"
# Component 4 — Quality Inspection & Non-Conformance Management
**Student:** Anoja
**Your primary ownership:** Quality inspections, inspection items, non-conformances, QualityRiskAnalysisAgent

## What you submit
- `backend/BuildWise.Api/Controllers/QualityInspectionsController.cs` — your main controller
- `backend/agent_service/quality_agent.py` — your Python AI agent (port 8004, most complex)
- `backend/BuildWise.Api.Tests/QualityInspectionServiceTests.cs` — your unit tests
- `web/buildwise-web/src/pages/QualityInspectionsPage.jsx` + `NonConformancesPage.jsx`
- `web/buildwise-web/src/Features/quality/` — all quality React components
- `mobile/.../features/quality/` — your Flutter screens
- `docs/reports/IT24XXXXX-Anoja-ai-usage-log.md` — your AI usage log

## Run & verify
``````powershell
powershell -File scripts/start-dev.ps1
powershell -File scripts/verify-component4.ps1
``````

## Test your component
``````powershell
dotnet test backend/BuildWise.Api.Tests/BuildWise.Api.Tests.csproj --filter "Quality"
cd backend/agent_service && pytest test_quality_agent.py -v
cd web/buildwise-web && npm test -- --run --reporter verbose
``````

## Install APK on Android phone
1. Enable "Install unknown apps" on your phone
2. Transfer `mobile/.../flutter-apk/app-release.apk` and install
3. API at http://10.110.168.34:5078 (PC must be on same WiFi)
"@ | Set-Content (Join-Path $c4Dir "README_COMPONENT4.md") -Encoding UTF8

New-Zip -SrcDir $c4Dir -ZipPath (Join-Path $outDir "Component4-Anoja-QualityInspection.zip")

# ─────────────────────────────────────────────────────────────────────────────
# Done
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n=== All ZIPs created ===" -ForegroundColor Cyan
Get-ChildItem $outDir -Filter "*.zip" | ForEach-Object {
    $mb = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.Name)  ($mb MB)" -ForegroundColor Green
}
Write-Host "`nOutput folder: $outDir" -ForegroundColor Cyan
Write-Host "APK for phones: mobile\buildwise_mobile\build\app\outputs\flutter-apk\app-release.apk" -ForegroundColor Cyan
Write-Host "Phone API URL: http://10.110.168.34:5078 (phone must be on same WiFi)" -ForegroundColor Yellow
