using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildWise.Api.Tests;

public class OperationalAgentAuditServiceTests
{
    [Fact]
    public async Task RecordAsync_PersistsPlanAgentValidationAndAdvisoryStatus()
    {
        var db = TestDbFactory.CreateInMemory();
        var user = new User { FullName = "Manager", Email = "manager@example.test", PasswordHash = "test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = new OperationalAgentAuditService(db);

        var workflowId = await service.RecordAsync(
            user.Id,
            "Assess a request",
            "RequestAnalysisAgent",
            new[] { "Read facts", "Call allow-listed tool" },
            new[] { "analyze_request" },
            new { flags = new[] { "LARGE_QUANTITY_ORDER" } },
            "PythonRequestAgent",
            new { valid = true });

        var workflow = await db.AgentWorkflows.Include(w => w.Steps).SingleAsync(w => w.Id == workflowId);
        Assert.Equal(WorkflowStatus.Completed, workflow.Status);
        Assert.Equal(AgentApprovalStatus.NotRequired, workflow.ApprovalStatus);
        Assert.Equal(3, workflow.Steps.Count);
        Assert.Equal(new[] { "OperationalPlanningAgent", "RequestAnalysisAgent", "OperationalValidationAgent" },
            workflow.Steps.OrderBy(s => s.StepOrder).Select(s => s.AgentRole));
        Assert.All(workflow.Steps, step =>
        {
            Assert.Equal(WorkflowStepStatus.Completed, step.Status);
            Assert.NotNull(step.StructuredResult);
            Assert.NotNull(step.ValidationResult);
            Assert.NotNull(step.StartedAt);
            Assert.NotNull(step.CompletedAt);
        });
    }
}
