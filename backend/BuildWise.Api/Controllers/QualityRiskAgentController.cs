using BuildWise.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/quality-risk-agent")]
public class QualityRiskAgentController(QualityRiskAgentService service) : ControllerBase
{
    [HttpPost("inspections/{inspectionId:int}/analyse")]
    public async Task<IActionResult> Analyse(int inspectionId, CancellationToken ct)
    {
        try
        {
            var result = await service.AnalyseAsync(inspectionId, ct);
            return StatusCode(result.Status == "Failed" ? 502 : 200, result);
        }
        catch (QualityRiskException ex) { return Problem(statusCode: ex.StatusCode, detail: ex.Message); }
        catch (Exception) { return Problem(statusCode: 500, detail: "Unable to process quality analysis or persist its audit state."); }
    }

    [HttpGet("workflows/{workflowId:int}")]
    public async Task<IActionResult> Get(int workflowId, CancellationToken ct)
    {
        try { return Ok(await service.GetAsync(workflowId, ct)); }
        catch (QualityRiskException ex) { return Problem(statusCode: ex.StatusCode, detail: ex.Message); }
        catch (Exception) { return Problem(statusCode: 500, detail: "Unable to retrieve quality-agent workflow."); }
    }
}
