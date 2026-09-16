using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class QualityRiskAgentService(ApplicationDbContext db, QualityRiskEvidenceService evidenceService,
    QualityRiskAgentClient client, QualityRiskRecommendationValidator validator,
    ILogger<QualityRiskAgentService> logger)
{
    public const string AgentRole = "Quality Risk & Non-Conformance Agent";

    public async Task<QualityRiskWorkflowResponse> AnalyseAsync(int inspectionId, CancellationToken ct)
    {
        var subject = await evidenceService.GetSubjectAsync(inspectionId, ct);
        var workflow = new AgentWorkflow
        {
            DeliveryId = subject.DeliveryId,
            Objective = $"Analyse quality risk for completed Inspection #{inspectionId} and provide advisory NCR/corrective-action recommendations.",
            Status = WorkflowStatus.Running,
            ApprovalStatus = AgentApprovalStatus.Pending,
            StartedAt = DateTime.UtcNow
        };
        string[] names = ["Collect Quality Evidence", "Run Agentic Quality Analysis", "Validate Quality Recommendation"];
        for (var index = 0; index < names.Length; index++)
            workflow.Steps.Add(new AgentWorkflowStep
            {
                AgentRole = AgentRole, StepName = names[index], StepOrder = index + 1,
                Status = WorkflowStepStatus.Pending,
                StructuredResultJson = JsonSerializer.Serialize(new { inspectionId }, QualityAgentJson.Options)
            });
        db.AgentWorkflows.Add(workflow);
        // Persist all three planned steps before executing any of them.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // Do not log provider exceptions, connection strings or claim a durable failed run.
            logger.LogError("Initial quality-agent plan persistence failed for inspection {InspectionId}. No agent execution was started; persistence is unconfirmed.", inspectionId);
            throw new QualityRiskException(500, "Unable to persist the quality-agent plan. Analysis was not started; workflow persistence could not be confirmed.");
        }
        var steps = workflow.Steps.OrderBy(s => s.StepOrder).ToList();
        var current = steps[0];
        try
        {
            await StartAsync(current, ct);
            var evidence = await evidenceService.CollectAsync(inspectionId, ct);
            Complete(current, evidence);
            await db.SaveChangesAsync(ct);

            current = steps[1];
            await StartAsync(current, ct);
            var result = await client.AnalyseAsync(evidence, ct);
            validator.ValidateEnvelope(result);
            if (!result.Success)
            {
                // Only validated trace fields and a fixed error vocabulary may be persisted.
                current.StructuredResultJson = JsonSerializer.Serialize(new
                {
                    inspectionId, result.Trace, result.IterationCount, result.ModelIdentifier, success = false
                }, QualityAgentJson.Options);
                throw new QualityRiskException(502, FailureMessage(result.ErrorCode));
            }
            Complete(current, result);
            await db.SaveChangesAsync(ct);

            current = steps[2];
            await StartAsync(current, ct);
            validator.Validate(result.Recommendation!, evidence);
            Complete(current, new { inspectionId, valid = true, advisoryOnly = true });
            current.ValidationResultJson = current.StructuredResultJson;
            workflow.Status = WorkflowStatus.Completed;
            workflow.FinalOutcome = $"Advisory quality-risk analysis completed for Inspection #{inspectionId}. No business action was executed.";
            workflow.CompletedAt = workflow.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            var message = ex switch
            {
                QualityRiskException known => known.Message,
                OperationCanceledException => "Quality agent analysis timed out or was cancelled.",
                HttpRequestException => "Internal quality agent service is unavailable.",
                JsonException => "Internal quality agent returned an invalid structured response.",
                _ => "Quality agent analysis failed. No business action was executed."
            };
            current.Status = WorkflowStepStatus.Failed;
            current.ErrorMessage = message;
            current.ValidationResultJson = JsonSerializer.Serialize(new { inspectionId, valid = false, error = message }, QualityAgentJson.Options);
            current.CompletedAt = current.UpdatedAt = DateTime.UtcNow;
            foreach (var next in steps.Where(s => s.StepOrder > current.StepOrder))
            {
                next.Status = WorkflowStepStatus.Skipped;
                next.CompletedAt = next.UpdatedAt = DateTime.UtcNow;
            }
            workflow.Status = WorkflowStatus.Failed;
            workflow.FinalOutcome = $"Quality-risk analysis failed for Inspection #{inspectionId}: {message}";
            workflow.CompletedAt = workflow.UpdatedAt = DateTime.UtcNow;
            // One bounded best-effort audit attempt, independent of caller cancellation.
            // An unavailable database cannot guarantee a durable Failed state.
            using var auditTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await db.SaveChangesAsync(auditTimeout.Token);
            }
            catch (Exception)
            {
                logger.LogError("Failure-audit persistence failed for quality-agent workflow {WorkflowId}, step {StepOrder}. Durable failure state is unconfirmed; no further write will be attempted.", workflow.Id, current.StepOrder);
                throw new QualityRiskException(500, "Quality analysis failed and its failure audit could not be persisted. Workflow status could not be confirmed.");
            }
        }
        return ToResponse(workflow, inspectionId);
    }

    public async Task<QualityRiskWorkflowResponse> GetAsync(int workflowId, CancellationToken ct)
    {
        var workflow = await db.AgentWorkflows.AsNoTracking().Include(w => w.Steps)
            .SingleOrDefaultAsync(w => w.Id == workflowId, ct);
        if (workflow == null || workflow.Steps.Count != 3 || workflow.Steps.Any(s => s.AgentRole != AgentRole))
            throw new QualityRiskException(404, "Quality-agent workflow not found.");
        var first = workflow.Steps.Single(s => s.StepOrder == 1);
        using var json = JsonDocument.Parse(first.StructuredResultJson!);
        var inspectionId = json.RootElement.GetProperty("inspectionId").GetInt32();
        return ToResponse(workflow, inspectionId);
    }

    private async Task StartAsync(AgentWorkflowStep step, CancellationToken ct)
    {
        step.Status = WorkflowStepStatus.Running;
        step.StartedAt = step.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
    private static void Complete(AgentWorkflowStep step, object result)
    {
        step.Status = WorkflowStepStatus.Completed;
        step.CompletedAt = step.UpdatedAt = DateTime.UtcNow;
        step.StructuredResultJson = JsonSerializer.Serialize(result, QualityAgentJson.Options);
    }
    private static string FailureMessage(string? code) => code switch
    {
        "missing_api_key" => "Gemini API key is not configured.",
        "timeout" => "Agent execution timed out.",
        "iteration_limit" => "Agent execution reached its iteration or tool-call limit.",
        "invalid_tool_call" => "Agent requested an invalid tool call.",
        "invalid_output" => "Agent returned invalid structured output.",
        "insufficient_tool_use" => "Agent did not collect the required evidence.",
        _ => "Agent provider or execution failed."
    };
    private static JsonElement? Parse(string? json) => json == null ? null : JsonSerializer.Deserialize<JsonElement>(json);
    private static QualityRiskWorkflowResponse ToResponse(AgentWorkflow w, int inspectionId) => new(
        w.Id, w.Objective, inspectionId, w.DeliveryId, w.Status.ToString(), w.ApprovalStatus.ToString(),
        w.FinalOutcome, w.CreatedAt, w.UpdatedAt, w.StartedAt, w.CompletedAt,
        w.Steps.OrderBy(s => s.StepOrder).Select(s => new QualityRiskStepResponse(s.StepOrder, s.StepName,
            s.Status.ToString(), s.StartedAt, s.CompletedAt, Parse(s.StructuredResultJson), Parse(s.ValidationResultJson), s.ErrorMessage)).ToList());
}
