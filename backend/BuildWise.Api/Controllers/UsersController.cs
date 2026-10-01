using System.ComponentModel.DataAnnotations;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Administrator")]
public class UsersController(ApplicationDbContext db) : ControllerBase
{
    private static readonly string[] AllowedRoles =
        ["SiteEngineer", "ProcurementOfficer", "ProcurementManager", "QualityInspector", "Administrator"];

    private static ManagedUserDto Summary(User user) => new(user.Id, user.FullName, user.Email,
        user.IsActive, user.UserRoles.Select(ur => ur.Role.Name).OrderBy(n => n).ToList(), user.CreatedAt);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var users = await db.Users.AsNoTracking().Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .OrderBy(u => u.FullName).ThenBy(u => u.Id).ToListAsync();
        return Ok(users.Select(Summary));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserDto dto)
    {
        if (!User.TryGetUserId(out _)) return Unauthorized();

        var email = dto.Email.Trim().ToLowerInvariant();
        if (!new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { error = "A valid email address is required." });
        if (!AllowedRoles.Contains(dto.RoleName))
            return BadRequest(new { error = "Select a supported BuildWise role." });
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Name == dto.RoleName);
        if (role is null) return BadRequest(new { error = "The selected role does not exist." });
        if (await db.Users.AnyAsync(u => u.Email.Trim().ToLower() == email))
            return Conflict(new { error = "An account with this email already exists." });

        var user = new User { FullName = dto.FullName.Trim(), Email = email, IsActive = true };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, dto.Password);
        user.UserRoles.Add(new UserRole { Role = role });
        db.Users.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "An account with this email already exists." });
        }
        return StatusCode(StatusCodes.Status201Created, Summary(user));
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, UserStatusDto dto)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();
        if (id <= 0) return BadRequest(new { error = "User ID must be positive." });
        if (id == actorId && dto.IsActive == false)
            return BadRequest(new { error = "You cannot deactivate your own account." });
        var user = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .SingleOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { error = "User not found." });
        user.IsActive = dto.IsActive!.Value;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(Summary(user));
    }
}
