using System.Security.Claims;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly NotificationService _service;
    public NotificationsController(NotificationService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] bool unreadOnly = false)
    {
        var userId = ParseUserId();
        return Ok(await _service.GetForUserAsync(userId, unreadOnly));
    }

    [HttpPut("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        try { await _service.MarkReadAsync(id, ParseUserId()); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    private int ParseUserId()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id <= 0)
            throw new InvalidOperationException("Authenticated user identifier is missing or invalid.");
        return id;
    }
}
