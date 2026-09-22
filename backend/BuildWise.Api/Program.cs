using System.Text;
using System.Text.Json.Serialization;
using BuildWise.Api.Data;
using BuildWise.Api.Middleware;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=buildwise;Username=postgres;Password=postgres";

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

builder.Services.AddHttpClient<QuotationAgentClient>(client =>
{
    // Keep this default in step with appsettings.json ("AgentService:Url"), the agent
    // service uvicorn port and the setup guide.
    var agentUrl = builder.Configuration["AgentService:Url"] ?? "http://127.0.0.1:8001";
    client.BaseAddress = new Uri(agentUrl);
    client.Timeout = TimeSpan.FromSeconds(12);
});

builder.Services.AddScoped<ProcurementWorkflowService>();

builder.Services.AddScoped<IEmailService, SmtpEmailService>();

// Component 3 service: delivery risk analysis over confirmed purchase orders.
builder.Services.AddScoped<DeliveryRiskAgentService>();

// Shared authentication (Core, used by every component controllers, React and Flutter)
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<AuthService>();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured. Set it in appsettings.json or user-secrets.");
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
