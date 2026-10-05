using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Security;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.Administrator)]
public class AdministrationController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEmailService _emailService;
    private readonly ILogger<AdministrationController> _logger;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AdministrationController(
        ApplicationDbContext db,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IEmailService emailService,
        ILogger<AdministrationController> logger)
    {
        _db = db;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _emailService = emailService;
        _logger = logger;
    }

    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<AdminUserDto>>> Users([FromQuery] string? search)
    {
        var query = _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(u => u.FullName.Contains(term) || u.Email.Contains(term)); }
        var users = await query.OrderBy(u => u.FullName).ToListAsync();
        return Ok(users.Select(MapUser));
    }

    [HttpPost("users")]
    public async Task<ActionResult<AdminUserDto>> CreateUser([FromBody] CreateUserRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Full name, email, and password are required." });

        if (dto.Password.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters long." });

        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
        var exists = await _db.Users.AnyAsync(u => u.Email == normalizedEmail);
        if (exists)
            return Conflict(new { message = $"An account with email '{normalizedEmail}' already exists." });

        var roleName = string.IsNullOrWhiteSpace(dto.Role) ? Roles.SiteEngineer : dto.Role.Trim();
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        if (role is null)
            return BadRequest(new { message = $"Unknown role '{roleName}'." });

        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Email = normalizedEmail,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);
        user.UserRoles.Add(new UserRole { User = user, Role = role });

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Send welcome email to the newly created user
        try
        {
            var subject = "Welcome to BuildWise — Your Account Has Been Created";
            var body = $"""
                Hello {user.FullName},

                Your BuildWise account has been created by an Administrator.

                Account Credentials:
                - Full Name: {user.FullName}
                - Email: {user.Email}
                - Assigned Role: {role.Name}
                - Temporary Password: {dto.Password}

                You can now sign in at the BuildWise Portal or via the BuildWise Mobile App:
                Web Portal: http://127.0.0.1:5174/login

                Please remember to change your password after initial login.

                Best regards,
                BuildWise Administration Team
                """;
            await _emailService.SendAsync(user.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send welcome email to {Email}", user.Email);
        }

        return Ok(MapUser(user));
    }


    [HttpPatch("users/{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, [FromBody] UpdateUserActiveRequestDto request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound(new { message = "User not found." });
        user.IsActive = request.IsActive; user.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync();
        return Ok(MapUser(user));
    }

    [HttpPut("users/{id:int}/roles")]
    public async Task<IActionResult> SetRoles(int id, [FromBody] UpdateUserRolesRequestDto request)
    {
        var user = await _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { message = "User not found." });
        var roles = await _db.Roles.Where(r => request.Roles.Contains(r.Name)).ToListAsync();
        if (roles.Count != request.Roles.Distinct().Count()) return BadRequest(new { message = "One or more roles do not exist." });
        user.UserRoles.Clear();
        foreach (var role in roles) user.UserRoles.Add(new UserRole { User = user, Role = role });
        user.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync();
        return Ok(MapUser(user));
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<IEnumerable<AuditLogDto>>> AuditLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var rows = await _db.AuditLogs.AsNoTracking().OrderByDescending(a => a.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(rows.Select(a => new AuditLogDto(a.Id, a.UserId, a.Action, a.EntityType, a.EntityId, a.HttpMethod, a.RequestPath, a.StatusCode, a.IpAddress, a.CreatedAt)));
    }

    [HttpGet("health")]
    public async Task<ActionResult<SystemHealthDto>> Health()
    {
        var database = false;
        try { database = await _db.Database.CanConnectAsync(); } catch (Exception ex) { _logger.LogWarning(ex, "Admin health check could not connect to PostgreSQL."); }
        var services = new Dictionary<string, bool>();
        foreach (var (name, key) in new[] { ("quotation-agent", "AgentService:Url"), ("request-agent", "AgentService:RequestUrl"), ("delivery-agent", "AgentService:DeliveryUrl"), ("quality-agent", "AgentService:QualityUrl") })
        {
            // Trim trailing slashes before appending the path. The configured
            // base URLs are inconsistent (AgentService:Url has no trailing
            // slash, the other three do), so concatenating naively produced
            // "http://127.0.0.1:8002//health" for three of the four agents.
            // That double slash is a 404, so the check reported healthy agents
            // as unreachable and flipped the whole system to "degraded".
            // Normalising here fixes the probe regardless of how the URL is
            // written in configuration or overridden by environment variable.
            var configured = _configuration[key];
            if (string.IsNullOrWhiteSpace(configured))
            {
                services[name] = false;
                continue;
            }
            var baseUrl = configured!.TrimEnd('/');
            try
            {
                using var response = await _httpClientFactory.CreateClient()
                    .GetAsync($"{baseUrl}/health", new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
                services[name] = response.IsSuccessStatusCode;
            }
            catch
            {
                services[name] = false;
            }
        }
        return Ok(new SystemHealthDto(database && services.Values.All(v => v) ? "healthy" : "degraded", database, services, DateTime.UtcNow));
    }

    private static AdminUserDto MapUser(User user) => new(user.Id, user.FullName, user.Email, user.IsActive, user.UserRoles.Select(ur => ur.Role.Name).OrderBy(n => n).ToList(), user.CreatedAt);
}
