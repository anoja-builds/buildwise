using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace BuildWise.Api.Controllers;

[ApiController]
[Authorize(Roles = "QualityInspector,Administrator")]
[Route("api/[controller]")]
public class InspectionsController : ControllerBase
{
    private readonly QualityInspectionService _service;
    private readonly ILogger<InspectionsController> _logger;

    public InspectionsController(QualityInspectionService service, ILogger<InspectionsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("pending-deliveries")]
    public Task<IActionResult> GetPendingDeliveries() => ExecuteAsync(async () =>
        Ok(await _service.GetPendingDeliveriesAsync()));

    [HttpPost]
    public Task<IActionResult> StartInspection([FromBody] StartInspectionDto dto) => ExecuteAsync(async () =>
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Unauthorized();
        var inspection = await _service.StartInspectionAsync(dto, userId);
        return CreatedAtAction(nameof(GetInspectionById), new { id = inspection.Id }, inspection);
    });

    [HttpGet("{id:int}")]
    public Task<IActionResult> GetInspectionById(int id) => ExecuteAsync(async () =>
        Ok(await _service.GetInspectionByIdAsync(id)));

    [HttpPost("{id:int}/complete")]
    public Task<IActionResult> CompleteInspection(int id, [FromBody] CompleteInspectionDto dto) => ExecuteAsync(async () =>
        Ok(await _service.CompleteInspectionAsync(id, dto)));

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (QualityInspectionException ex)
        {
            return Problem(statusCode: ex.StatusCode, detail: ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quality inspection operation failed.");
            return Problem(statusCode: 500, detail: "Unable to process the quality inspection request.");
        }
    }
}
