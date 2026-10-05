using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Xunit;
using System.Collections.Generic;

namespace BuildWise.Api.Tests;

public class MaterialRequestServiceTests
{
    [Fact]
    public async Task CreateRequest_Rejects_NonActive_Project()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // Change project status to Planned (not Active)
        data.Project.Status = ProjectStatus.Planned;
        await db.SaveChangesAsync();

        var service = new MaterialRequestService(db);
        var request = new MaterialRequest
        {
            ProjectId = data.Project.Id,
            RequestedByUserId = 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            Reason = "Test request",
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = data.Material.Id, RequestedQuantity = 10 }
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(request));
    }

    [Fact]
    public async Task CreateRequest_Rejects_PastRequiredDate()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var service = new MaterialRequestService(db);
        var request = new MaterialRequest
        {
            ProjectId = data.Project.Id,
            RequestedByUserId = 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), // Invalid past date
            Reason = "Test request",
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = data.Material.Id, RequestedQuantity = 10 }
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(request));
    }

    [Fact]
    public async Task CreateRequest_Rejects_EmptyItems()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var service = new MaterialRequestService(db);
        var request = new MaterialRequest
        {
            ProjectId = data.Project.Id,
            RequestedByUserId = 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            Reason = "Test request",
            Items = new List<MaterialRequestItem>() // Empty items
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(request));
    }
    [Fact]
    public async Task CreateRequest_Rejects_RequiredDate_Less_Than_Three_Days_Away()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);
        var service = new MaterialRequestService(db);
        var request = new MaterialRequest
        {
            ProjectId = data.Project.Id,
            RequestedByUserId = 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = data.Material.Id, RequestedQuantity = 10 }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(request));
        Assert.Contains("at least 3 days", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateRequest_Rejects_NonPositiveQuantity(decimal quantity)
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);
        var service = new MaterialRequestService(db);
        var request = new MaterialRequest
        {
            ProjectId = data.Project.Id,
            RequestedByUserId = 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = data.Material.Id, RequestedQuantity = quantity }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(request));
        Assert.Contains("greater than zero", ex.Message);
    }

    [Fact]
    public async Task CreateRequest_Uses_Authenticated_User_As_Owner()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);
        var service = new MaterialRequestService(db);
        var request = new MaterialRequest
        {
            ProjectId = data.Project.Id,
            RequestedByUserId = 999,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = data.Material.Id, RequestedQuantity = 10 }
            }
        };

        var result = await service.CreateRequestAsync(request, authenticatedUserId: 42);
        Assert.Equal(42, result.RequestedByUserId);
    }


    [Fact]
    public async Task CreateRequest_Succeeds_WithValidData()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var service = new MaterialRequestService(db);
        var request = new MaterialRequest
        {
            ProjectId = data.Project.Id,
            RequestedByUserId = 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            Reason = "Test request",
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = data.Material.Id, RequestedQuantity = 10 }
            }
        };

        var result = await service.CreateRequestAsync(request);

        Assert.NotNull(result);
        Assert.Equal(MaterialRequestStatus.PendingApproval, result.Status);
        Assert.True(result.Id > 0);
    }

    [Fact]
    public async Task RecordApproval_Rejects_NonExistentRequest()
    {
        var db = TestDbFactory.CreateInMemory();
        await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var service = new MaterialRequestService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.RecordApprovalAsync(9999, 1, ApprovalDecision.Approved, "Test"));
    }

    [Fact]
    public async Task RecordApproval_Rejects_AlreadyApprovedRequest()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // Request is already Approved
        data.Request.Status = MaterialRequestStatus.Approved;
        await db.SaveChangesAsync();

        var service = new MaterialRequestService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordApprovalAsync(data.Request.Id, 1, ApprovalDecision.Approved, "Test"));
    }

    [Fact]
    public async Task RecordApproval_Rejects_AlreadyRejectedRequest()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // Request is already Rejected
        data.Request.Status = MaterialRequestStatus.Rejected;
        await db.SaveChangesAsync();

        var service = new MaterialRequestService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordApprovalAsync(data.Request.Id, 1, ApprovalDecision.Approved, "Test"));
    }

    [Fact]
    public async Task RecordApproval_Succeeds_ApprovingRequest()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // Request is in PendingApproval state
        data.Request.Status = MaterialRequestStatus.PendingApproval;
        await db.SaveChangesAsync();

        var service = new MaterialRequestService(db);
        var approval = await service.RecordApprovalAsync(data.Request.Id, 1, ApprovalDecision.Approved, "Approved for procurement");

        Assert.NotNull(approval);
        Assert.Equal(ApprovalDecision.Approved, approval.Decision);
        Assert.Equal(data.Request.Id, approval.MaterialRequestId);
        Assert.Equal(1, approval.ApprovedByUserId);

        // Verify request status updated
        var updatedRequest = await db.MaterialRequests.FindAsync(data.Request.Id);
        Assert.Equal(MaterialRequestStatus.Approved, updatedRequest.Status);
    }

    [Fact]
    public async Task RecordApproval_Succeeds_RejectingRequest()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // Request is in PendingApproval state
        data.Request.Status = MaterialRequestStatus.PendingApproval;
        await db.SaveChangesAsync();

        var service = new MaterialRequestService(db);
        var approval = await service.RecordApprovalAsync(data.Request.Id, 1, ApprovalDecision.Rejected, "Insufficient budget");

        Assert.NotNull(approval);
        Assert.Equal(ApprovalDecision.Rejected, approval.Decision);
        Assert.Equal(data.Request.Id, approval.MaterialRequestId);

        // Verify request status updated
        var updatedRequest = await db.MaterialRequests.FindAsync(data.Request.Id);
        Assert.Equal(MaterialRequestStatus.Rejected, updatedRequest.Status);
    }

    [Fact]
    public async Task RecordApproval_Rejects_WithoutReason()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);
        data.Request.Status = MaterialRequestStatus.PendingApproval;
        await db.SaveChangesAsync();

        var service = new MaterialRequestService(db);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordApprovalAsync(data.Request.Id, 1, ApprovalDecision.Rejected, ""));
        Assert.Contains("rejection reason", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReviseRequest_CreatesLinkedPendingRevisionAndHistory()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);
        data.Request.Status = MaterialRequestStatus.Approved;
        await db.SaveChangesAsync();
        var service = new MaterialRequestService(db);

        var revision = await service.ReviseRequestAsync(data.Request.Id, 1, new ReviseMaterialRequestRequestDto(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
            "Updated requirement",
            "Deliver to gate 2",
            "High",
            new List<MaterialRequestItemRevisionDto>
            {
                new(data.Material.Id, 300m, "bags", "Updated specification", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)), "Revised")
            }));

        Assert.NotEqual(data.Request.Id, revision.Id);
        Assert.Equal(data.Request.Id, revision.RevisionOfRequestId);
        Assert.Equal(2, revision.RevisionNumber);
        Assert.Equal(MaterialRequestStatus.PendingApproval, revision.Status);
        Assert.Equal("300", revision.Items.Single().RequestedQuantity.ToString("0"));
        var history = await service.GetHistoryAsync(data.Request.Id);
        Assert.Contains(history, h => h.Action == "RevisionCreated" && h.Details!.Contains($"Created revision #{revision.Id}"));
    }

    [Fact]
    public async Task ReviseRequest_RejectsDifferentUser()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);
        var service = new MaterialRequestService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReviseRequestAsync(data.Request.Id, 99, new ReviseMaterialRequestRequestDto(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)), null, null, "Normal",
            new List<MaterialRequestItemRevisionDto> { new(data.Material.Id, 10m, null, null, null, null) })));
    }

    [Fact]
    public async Task RecordApproval_KeepsPendingStatus_ForRevisionRequested()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // Request is in PendingApproval state
        data.Request.Status = MaterialRequestStatus.PendingApproval;
        await db.SaveChangesAsync();

        var service = new MaterialRequestService(db);
        var approval = await service.RecordApprovalAsync(data.Request.Id, 1, ApprovalDecision.RevisionRequested, "Please adjust quantities");

        Assert.NotNull(approval);
        Assert.Equal(ApprovalDecision.RevisionRequested, approval.Decision);

        // Verify request status remains PendingApproval
        var updatedRequest = await db.MaterialRequests.FindAsync(data.Request.Id);
        Assert.Equal(MaterialRequestStatus.PendingApproval, updatedRequest.Status);
    }
}
