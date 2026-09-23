using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BuildWise.Api.Controllers;

[ApiController]
[Authorize(Roles = "QualityInspector,Administrator")]
[Route("api/[controller]")]
public class NonConformancesController : ControllerBase
{
    private readonly NonConformanceService _service;
    private readonly ILogger<NonConformancesController> _logger;

    public NonConformancesController(NonConformanceService service, ILogger<NonConformancesController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // Absolute action routes preserve the requested hyphenated URLs without global routing changes.
    [HttpGet("~/api/non-conformances")]
    public Task<IActionResult> GetAll() => ExecuteAsync(async () =>
        Ok(await _service.GetAllAsync()));

    [HttpGet("~/api/non-conformances/{id:int}")]
    public Task<IActionResult> GetById(int id) => ExecuteAsync(async () =>
        Ok(await _service.GetByIdAsync(id)));

    [HttpPost("~/api/non-conformances")]
    public Task<IActionResult> Create([FromBody] CreateNonConformanceDto dto) => ExecuteAsync(async () =>
    {
        var record = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = record.Id }, record);
    });

    [HttpPatch("~/api/non-conformances/{id:int}/corrective-action")]
    public Task<IActionResult> UpdateCorrectiveAction(int id, [FromBody] UpdateCorrectiveActionDto dto)
        => ExecuteAsync(async () => Ok(await _service.UpdateCorrectiveActionAsync(id, dto)));

    [HttpPost("~/api/non-conformances/{id:int}/resolve")]
    public Task<IActionResult> Resolve(int id) => ExecuteAsync(async () =>
        Ok(await _service.ResolveAsync(id)));

    [HttpPost("~/api/non-conformances/{id:int}/close")]
    public Task<IActionResult> Close(int id) => ExecuteAsync(async () =>
        Ok(await _service.CloseAsync(id)));

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (NonConformanceException ex)
        {
            return Problem(statusCode: ex.StatusCode, detail: ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Non-conformance operation failed.");
            return Problem(statusCode: 500, detail: "Unable to process the non-conformance request.");
        }
    }
}
