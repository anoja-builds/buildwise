using System.Security.Claims;
using BuildWise.Api.Security;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Policies.InternalStaffOnly)]
public sealed class DashboardController : ControllerBase
{
    private readonly DashboardService _service;

    public DashboardController(DashboardService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult> Get(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdValue, out var userId) || userId <= 0)
            return Unauthorized(new { message = "Authenticated user identifier is missing or invalid." });

        var roles = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
        return Ok(await _service.GetAsync(userId, roles, cancellationToken));
    }
}
