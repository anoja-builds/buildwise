using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildWise.Api.Tests;

public class ProcurementPlanningHistoryTests
{
    [Fact]
    public async Task Repeated_planning_preserves_procurement_steps_and_prior_planning_history()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var scenario = await TestDbFactory.SeedStandardScenarioDataAsync(db);
        var procurement = new AgentWorkflow
        {
            MaterialRequestId = scenario.Request.Id,
            Status = WorkflowStatus.AwaitingApproval,
            Steps = [new AgentWorkflowStep
            {
                AgentRole = "QuotationSupplierAnalysisAgent", StepOrder = 1,
                StructuredResult = "{\"rationale\":\"Existing recommendation\"}"
            }]
        };
        db.AgentWorkflows.Add(procurement);
        await db.SaveChangesAsync();
        var service = new ProcurementPlanningAgentService(db);
        await service.EvaluateMaterialRequestPlanAsync(scenario.Request.Id, 77);
        var firstPlan = await db.AgentWorkflows.Include(w => w.Steps).SingleAsync(w => w.Id != procurement.Id);
        var firstResult = firstPlan.Steps.Single().StructuredResult;
        await service.EvaluateMaterialRequestPlanAsync(scenario.Request.Id, 88);
        db.ChangeTracker.Clear();

        var workflows = await db.AgentWorkflows.Include(w => w.Steps).OrderBy(w => w.Id).ToListAsync();
        Assert.Equal(3, workflows.Count);
        var original = workflows.Single(w => w.Id == procurement.Id);
        Assert.Equal(WorkflowStatus.AwaitingApproval, original.Status);
        Assert.Equal("QuotationSupplierAnalysisAgent", original.Steps.Single().AgentRole);
        Assert.Equal("{\"rationale\":\"Existing recommendation\"}", original.Steps.Single().StructuredResult);
        Assert.Equal(firstResult, workflows.Single(w => w.Id == firstPlan.Id).Steps.Single().StructuredResult);
        Assert.Equal(new[] { 77, 88 }, workflows.Where(w => w.Id != procurement.Id).Select(w => w.InitiatedByUserId));
        Assert.All(workflows.Where(w => w.Id != procurement.Id), w => Assert.Equal(WorkflowStatus.Completed, w.Status));
    }
}
