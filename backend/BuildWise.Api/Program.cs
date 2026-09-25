using System.Text;
using System.Text.Json.Serialization;
using BuildWise.Api.Data;
using BuildWise.Api.Middleware;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using BuildWise.Api.Models.Entities;

var builder = WebApplication.CreateBuilder(args);

var configuredConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(configuredConnection))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Use dotnet user-secrets or ConnectionStrings__DefaultConnection.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(configuredConnection));

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

// Component 1 service: material request creation and approval validation.
builder.Services.AddScoped<MaterialRequestService>();

// Integrated service covering all components (Component 1, 2, 3).
builder.Services.AddScoped<IntegratedProcurementService>();

// Component 2 services: deterministic validation, agent client, workflow orchestration.
builder.Services.AddScoped<ProcurementValidationService>();

builder.Services.AddHttpClient<QuotationAgentClient>(client =>
{
    // Keep this default in step with appsettings.json ("AgentService:Url"), the agent
    // service uvicorn port and the setup guide.
    var agentUrl = builder.Configuration["AgentService:Url"] ?? "http://127.0.0.1:8001";
    client.BaseAddress = new Uri(agentUrl);
    client.Timeout = TimeSpan.FromSeconds(12);
});

builder.Services.AddScoped<ProcurementPlanningAgentService>();
builder.Services.AddScoped<ProcurementWorkflowService>();

builder.Services.AddScoped<IEmailService, SmtpEmailService>();

builder.Services.AddHttpClient("RequestAgent", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AgentService:RequestUrl"] ?? "http://127.0.0.1:8002/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient("DeliveryAgent", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AgentService:DeliveryUrl"] ?? "http://127.0.0.1:8003/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient("QualityAgent", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AgentService:QualityUrl"] ?? "http://127.0.0.1:8004/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient("Gemini", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<OperationalAgentClient>();
builder.Services.AddScoped<OperationalAgentAuditService>();

// Component 3 service: delivery recording against confirmed purchase orders.
builder.Services.AddScoped<DeliveryService>();

// Component 3 service: auditable delivery-risk assessment with deterministic fallback.
builder.Services.AddScoped<DeliveryAgentService>();

// Component 4 service: quality inspection, NCR management, and notification events.
builder.Services.AddScoped<QualityInspectionService>();
builder.Services.AddScoped<NotificationService>();

// Shared authentication (Core, used by every component controllers, React and Flutter)
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<AuthService>();

var configuredJwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(configuredJwtKey) || configuredJwtKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is not configured or is too short. Use dotnet user-secrets or Jwt__Key.");
}
var jwtKey = configuredJwtKey;
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "BuildWise";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "BuildWiseClients";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SiteOperationsOnly", policy =>
        policy.RequireRole("SiteEngineer", "SiteOfficer"));
    options.AddPolicy("ProcurementStaffOnly", policy =>
        policy.RequireRole("ProcurementOfficer", "ProcurementManager", "Administrator"));
    options.AddPolicy("QualityControlOnly", policy =>
        policy.RequireRole("QualityInspector"));
    options.AddPolicy("MaterialRequestApprovalOnly", policy =>
        policy.RequireRole("ProcurementManager", "SiteManager", "Administrator"));
    options.AddPolicy("ProcurementDecisionOnly", policy =>
        policy.RequireRole("ProcurementManager", "SiteManager", "Administrator"));
});

// CORS: permissive dev policy (covers React on localhost:5173 and Flutter/Chrome).
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "BuildWise API", Version = "v1", Description = "Supplier, Quotation & Procurement Management, Delivery & Receiving, and shared authentication" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT returned by /api/auth/login (no Bearer prefix needed here)."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "BuildWise API v1");
    });

    using var seedScope = app.Services.CreateScope();
    try
    {
        var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await seedDb.Database.MigrateAsync();
        await DbSeeder.SeedAsync(seedDb);
    }
    catch (Exception ex)
    {
        var logger = seedScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseCors("AllowAll");

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseMiddleware<AuditLoggingMiddleware>();
app.UseAuthorization();

// Public deployment probe. It intentionally exposes no database details,
// secrets, or workflow data; the API's protected business endpoints remain JWT/RBAC protected.
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "BuildWise API",
    version = "1.0.0"
}));

app.MapControllers();

app.Run();
