using System.Text.Json.Serialization;
using BuildWise.Api.Data;
using BuildWise.Api.Middleware;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
JwtAccountValidation.RequireSigningKey(builder.Configuration);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

// Component 2 services: deterministic validation, agent client, workflow orchestration.
builder.Services.AddScoped<ProcurementValidationService>();

builder.Services.AddHttpClient<QuotationAgentClient>().RemoveAllLoggers();
builder.Services.AddHttpClient<IProcurementAdvisoryClient, ProcurementAdvisoryClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(100);
}).RemoveAllLoggers();
builder.Services.AddScoped<ProcurementAdvisoryService>();

builder.Services.AddScoped<ProcurementWorkflowService>();

builder.Services.AddScoped<IEmailService, SmtpEmailService>();

// Component 3 services: delivery risk analysis and receiving discrepancy agent.
builder.Services.AddScoped<DeliveryRiskAgentService>();
builder.Services.AddScoped<DeliveryDiscrepancyAgentService>();
builder.Services.AddHttpClient<IDeliveryDiscrepancyAgentClient, DeliveryDiscrepancyAgentClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(100);
}).RemoveAllLoggers();
builder.Services.AddScoped<QualityInspectionService>();
builder.Services.AddScoped<NonConformanceService>();
builder.Services.AddScoped<QualityRiskEvidenceService>();
builder.Services.AddScoped<QualityRiskRecommendationValidator>();
builder.Services.AddScoped<QualityRiskAgentService>();
builder.Services.AddHttpClient<QualityRiskAgentClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(100);
}).RemoveAllLoggers();

builder.Services.AddScoped<ProcurementPlanningAgentService>();
builder.Services.AddHttpClient<IPlanningAgentClient, PlanningAgentClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(100);
}).RemoveAllLoggers();

// Shared authentication (Core, used by every component controllers, React and Flutter)
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<AuthService>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options => JwtAccountValidation.Configure(options, builder.Configuration));

builder.Services.AddAuthorization();

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
    // Keep schema names unambiguous across API DTO namespaces.
    var defaultSchemaId = new Swashbuckle.AspNetCore.SwaggerGen.SchemaGeneratorOptions().SchemaIdSelector;
    c.CustomSchemaIds(type => $"{type.Namespace}.{defaultSchemaId(type)}".Replace('+', '.'));
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

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
