using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

/// <summary>
/// Shared registration/login for every BuildWise client (React, Flutter). Uses
/// ASP.NET Core's own <see cref="PasswordHasher{TUser}"/> (PBKDF2) — no plaintext
/// or reversibly-encrypted passwords are ever stored.
/// </summary>
public class AuthService
{
    private readonly ApplicationDbContext _db;
    private readonly JwtTokenService _tokenService;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(ApplicationDbContext db, JwtTokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            throw new ArgumentException("Full name, email and password are all required.");

        if (dto.Password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters long.");

        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

        var exists = await _db.Users.AnyAsync(u => u.Email == normalizedEmail);
        if (exists)
            throw new InvalidOperationException($"An account with email '{normalizedEmail}' already exists.");

        var selfRegisterRoles = new[] { "SiteEngineer", "SiteOfficer", "ProcurementOfficer", "QualityInspector" };
        if (!selfRegisterRoles.Contains(dto.RoleName))
            throw new ArgumentException("Manager and Administrator accounts must be provisioned by an existing administrator.");

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == dto.RoleName);
        if (role is null)
            throw new ArgumentException($"Unknown role '{dto.RoleName}'.");

        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Email = normalizedEmail,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);
        user.UserRoles.Add(new UserRole { Role = role });

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return BuildAuthResponse(user, [role.Name]);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto dto)
    {
        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

        var user = await _db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user is null || !user.IsActive)
            throw new UnauthorizedAccessException("Invalid email or password.");

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new UnauthorizedAccessException("Invalid email or password.");

        var roleNames = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        return BuildAuthResponse(user, roleNames);
    }

    private AuthResponseDto BuildAuthResponse(User user, IReadOnlyList<string> roles)
    {
        var (token, expiresAt) = _tokenService.GenerateToken(user, roles);
        var summary = new UserSummaryDto(user.Id, user.FullName, user.Email, roles.ToList());
        return new AuthResponseDto(token, expiresAt, summary);
    }
}
