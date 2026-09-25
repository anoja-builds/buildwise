using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildWise.Api.Tests;

public class DeliveryAgentServiceTests
{
    [Fact]
    public async Task EvaluateDeliveryRisk_PersistsThreeAuditableStepsAndSafeAssessment()
    {
        var db = TestDbFactory.CreateInMemory();
        var user = new User { FullName = "Site Officer", Email = "officer@example.test", PasswordHash = "test" };
        var project = new Project { Name = "Riverside Apartments", Status = ProjectStatus.Active };
        var material = new Material { Name = "OPC Cement", Unit = "Bags", IsActive = true };
        var purchaseOrder = new PurchaseOrder
        {
            ProjectId = project.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpectedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = PurchaseOrderStatus.Confirmed,
            TotalAmount = 525000
        };
        db.AddRange(user, project, material, purchaseOrder);
        purchaseOrder.Items.Add(new PurchaseOrderItem { MaterialId = material.Id, OrderedQuantity = 250, UnitPrice = 2100 });
        await db.SaveChangesAsync();

        var configuration = new ConfigurationBuilder().Build();
        var service = new DeliveryAgentService(
            db,
            configuration,
            new TestHttpClientFactory(),
            NullLogger<DeliveryAgentService>.Instance);

        var assessment = await service.EvaluateDeliveryRiskAsync(purchaseOrder.Id, user.Id);

        Assert.Equal("Low", assessment.RiskLevel);
        Assert.InRange(assessment.RiskScore, 0, 100);
        var workflow = await db.AgentWorkflows.Include(w => w.Steps).SingleAsync();
        Assert.Equal(WorkflowStatus.Completed, workflow.Status);
        Assert.Equal(3, workflow.Steps.Count);
        Assert.Equal(
            new[] { "DeliveryDataRetrievalAgent", "DeliveryRiskAnalysisAgent", "DeliveryRiskValidationAgent" },
            workflow.Steps.OrderBy(s => s.StepOrder).Select(s => s.AgentRole));
        Assert.All(workflow.Steps, step =>
        {
            Assert.Equal(WorkflowStepStatus.Completed, step.Status);
            Assert.NotNull(step.StructuredResult);
            Assert.NotNull(step.CompletedAt);
        });
        Assert.NotNull(workflow.Steps.Single(s => s.AgentRole == "DeliveryRiskValidationAgent").ValidationResult);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
