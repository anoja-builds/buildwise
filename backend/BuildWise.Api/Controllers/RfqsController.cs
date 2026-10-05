using System.Security.Claims;
using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BuildWise.Api.Security;
using BuildWise.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/rfqs")]
[Authorize(Policy = Policies.ProcurementStaffAndAdmin)]
public class RfqsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly ILogger<RfqsController> _logger;

    public RfqsController(
        ApplicationDbContext db,
        IEmailService emailService,
        ILogger<RfqsController> logger)
    {
        _db = db;
        _emailService = emailService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RfqDto>>> GetAll([FromQuery] string? status)
    {
        var query = _db.Rfqs
            .Include(r => r.MaterialRequest).ThenInclude(r => r.Project)
            .Include(r => r.Suppliers).ThenInclude(s => s.Supplier)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<RfqStatus>(status, true, out var parsed))
            query = query.Where(r => r.Status == parsed);
        var rows = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return Ok(rows.Select(Map));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RfqDto>> GetById(int id)
    {
        var rfq = await _db.Rfqs
            .Include(r => r.MaterialRequest).ThenInclude(r => r.Project)
            .Include(r => r.Suppliers).ThenInclude(s => s.Supplier)
            .FirstOrDefaultAsync(r => r.Id == id);
        return rfq is null ? NotFound(new { message = $"RFQ #{id} not found." }) : Ok(Map(rfq));
    }

    [HttpPost]
    [Authorize(Policy = Policies.SupplierAdministrationOnly)]
    public async Task<ActionResult<RfqDto>> Create([FromBody] CreateRfqRequestDto dto)
    {
        var request = await _db.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items).ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(r => r.Id == dto.MaterialRequestId);

        if (request is null) return NotFound(new { message = $"Material request #{dto.MaterialRequestId} not found." });
        if (request.Status != MaterialRequestStatus.Approved)
            return BadRequest(new { message = "RFQs can only be issued for Approved material requests." });

        // Response date must be strictly after today (today leaves suppliers no
        // time to respond) and no later than the date the material is needed on
        // site — an RFQ that stays open past the deadline cannot produce a usable
        // quotation.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dto.RequiredResponseDate <= today)
            return BadRequest(new { message = $"Required response date must be after today ({today:yyyy-MM-dd})." });
        if (request.RequiredDate != default && dto.RequiredResponseDate > request.RequiredDate)
            return BadRequest(new { message = $"Required response date '{dto.RequiredResponseDate:yyyy-MM-dd}' cannot be later than the material required date '{request.RequiredDate:yyyy-MM-dd}'." });

        if (dto.SupplierIds is null || dto.SupplierIds.Count == 0)
            return BadRequest(new { message = "Select at least one supplier for the RFQ." });

        var supplierIds = dto.SupplierIds.Distinct().ToList();
        var suppliers = await _db.Suppliers.Where(s => supplierIds.Contains(s.Id)).ToListAsync();
        if (suppliers.Count != supplierIds.Count) return BadRequest(new { message = "One or more suppliers do not exist." });
        if (suppliers.Any(s => s.Status != SupplierStatus.Active)) return BadRequest(new { message = "Only Active suppliers can be invited to an RFQ." });

        var rfq = new Rfq
        {
            MaterialRequestId = dto.MaterialRequestId,
            IssuedByUserId = ParseUserId(),
            RequiredResponseDate = dto.RequiredResponseDate,
            Notes = dto.Notes?.Trim(),
            Status = RfqStatus.Issued,
            Suppliers = suppliers.Select(s => new RfqSupplier { Supplier = s, SupplierId = s.Id, Status = RfqSupplierStatus.Invited }).ToList()
        };
        _db.Rfqs.Add(rfq);
        await _db.SaveChangesAsync();
        _logger.LogInformation("RFQ #{RfqId} issued for material request #{RequestId} to {SupplierCount} suppliers", rfq.Id, request.Id, suppliers.Count);

        // Send Email Invitations to Suppliers and Procurement Managers
        await SendRfqCreationEmailsAsync(rfq, request, suppliers, dto.AdditionalEmail);

        return CreatedAtAction(nameof(GetById), new { id = rfq.Id }, Map(await Load(rfq.Id)));
    }

    [HttpPost("{id:int}/suppliers")]
    [Authorize(Policy = Policies.SupplierAdministrationOnly)]
    public async Task<ActionResult<RfqDto>> AddSuppliers(int id, [FromBody] AddRfqSuppliersRequestDto dto)
    {
        var rfq = await _db.Rfqs
            .Include(r => r.MaterialRequest).ThenInclude(r => r.Project)
            .Include(r => r.MaterialRequest).ThenInclude(r => r.Items).ThenInclude(i => i.Material)
            .Include(r => r.Suppliers)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (rfq is null) return NotFound(new { message = $"RFQ #{id} not found." });
        if (rfq.Status != RfqStatus.Issued) return BadRequest(new { message = "Suppliers can only be added while the RFQ is Issued." });
        var existing = rfq.Suppliers.Select(s => s.SupplierId).ToHashSet();
        var ids = dto.SupplierIds.Distinct().Where(id => !existing.Contains(id)).ToList();
        var suppliers = await _db.Suppliers.Where(s => ids.Contains(s.Id) && s.Status == SupplierStatus.Active).ToListAsync();
        if (suppliers.Count != ids.Count) return BadRequest(new { message = "All new suppliers must exist and be Active." });
        foreach (var supplier in suppliers)
            rfq.Suppliers.Add(new RfqSupplier { Rfq = rfq, SupplierId = supplier.Id, Status = RfqSupplierStatus.Invited });
        rfq.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (rfq.MaterialRequest != null)
        {
            await SendSupplierInvitationEmailsAsync(rfq, rfq.MaterialRequest, suppliers);
        }

        return Ok(Map(await Load(id)));
    }

    [HttpPost("{id:int}/send-email")]
    [Authorize(Policy = Policies.ProcurementStaffOnly)]
    public async Task<IActionResult> SendRfqEmail(int id, [FromBody] SendRfqEmailRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RecipientEmail))
            return BadRequest(new { message = "Recipient email address is required." });

        var rfq = await _db.Rfqs
            .Include(r => r.MaterialRequest).ThenInclude(r => r.Project)
            .Include(r => r.MaterialRequest).ThenInclude(r => r.Items).ThenInclude(i => i.Material)
            .Include(r => r.Suppliers).ThenInclude(s => s.Supplier)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (rfq is null) return NotFound(new { message = $"RFQ #{id} not found." });

        var req = rfq.MaterialRequest;
        var projectName = req?.Project?.Name ?? "General Procurement";
        var itemsSummary = req?.Items != null
            ? string.Join("\n", req.Items.Select(i => $" - {i.Material?.Name ?? "Material"}: {i.RequestedQuantity:0.##} {i.Unit ?? "units"}"))
            : "Materials specified in Material Request";

        var subject = $"[BuildWise] Request for Quotation (RFQ #{rfq.Id}) — {projectName}";
        var body =
            $"Dear Supplier / Partner,\n\n" +
            $"You have received a Request for Quotation (RFQ #{rfq.Id}) from BuildWise Procurement.\n\n" +
            $"Project: {projectName}\n" +
            $"Material Request: #{rfq.MaterialRequestId}\n" +
            $"Response Deadline: {rfq.RequiredResponseDate:yyyy-MM-dd}\n\n" +
            $"Requested Materials:\n{itemsSummary}\n\n" +
            (!string.IsNullOrWhiteSpace(dto.CustomMessage) ? $"Message from Procurement:\n{dto.CustomMessage}\n\n" : "") +
            (!string.IsNullOrWhiteSpace(rfq.Notes) ? $"Notes:\n{rfq.Notes}\n\n" : "") +
            $"Please submit your formal quotation via the BuildWise Supplier Portal or reply directly to procurement.\n\n" +
            $"Thank you,\nBuildWise Procurement Team";

        var sent = await _emailService.SendAsync(dto.RecipientEmail.Trim(), subject, body);
        if (sent)
        {
            _logger.LogInformation("RFQ #{RfqId} email sent to {Recipient}", rfq.Id, dto.RecipientEmail);
            return Ok(new { message = $"RFQ notification email sent successfully to {dto.RecipientEmail}", emailSent = true });
        }

        _logger.LogWarning("RFQ #{RfqId} email for {Recipient} was not sent. Check SMTP configuration and server logs.", rfq.Id, dto.RecipientEmail);
        return Ok(new { message = $"RFQ #{rfq.Id} notification email was NOT sent. Check the server's SMTP configuration and email provider connection. You can retry sending the email.", emailSent = false });
    }

    [HttpPost("{id:int}/close")]
    [Authorize(Policy = Policies.ProcurementStaffOnly)]
    public async Task<ActionResult<RfqDto>> Close(int id, [FromBody] CloseRfqRequestDto? request)
    {
        var rfq = await _db.Rfqs.FirstOrDefaultAsync(r => r.Id == id);
        if (rfq is null) return NotFound(new { message = $"RFQ #{id} not found." });
        if (rfq.Status is RfqStatus.Closed or RfqStatus.Cancelled) return BadRequest(new { message = "RFQ is already closed or cancelled." });
        rfq.Status = RfqStatus.Closed;
        rfq.Notes = string.IsNullOrWhiteSpace(request?.Reason) ? rfq.Notes : $"{rfq.Notes}\nClosed: {request!.Reason.Trim()}";
        rfq.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(Map(await Load(id)));
    }

    private async Task SendRfqCreationEmailsAsync(
        Rfq rfq,
        MaterialRequest request,
        List<Supplier> suppliers,
        string? additionalEmail)
    {
        try
        {
            var itemsSummary = request.Items.Count > 0
                ? string.Join("\n", request.Items.Select(i => $" - {i.Material?.Name ?? "Material"}: {i.RequestedQuantity:0.##} {i.Unit ?? "units"}"))
                : "Standard project materials";

            // 1. Send to all invited suppliers
            await SendSupplierInvitationEmailsAsync(rfq, request, suppliers);

            // 2. Send to additional email if provided
            if (!string.IsNullOrWhiteSpace(additionalEmail))
            {
                var addSubject = $"[BuildWise] Copy: RFQ #{rfq.Id} Issued — {request.Project?.Name ?? "Procurement"}";
                var addBody =
                    $"A new Request for Quotation (RFQ #{rfq.Id}) has been issued to {suppliers.Count} supplier(s).\n\n" +
                    $"Project: {request.Project?.Name}\n" +
                    $"Material Request: #{request.Id}\n" +
                    $"Required Response Date: {rfq.RequiredResponseDate:yyyy-MM-dd}\n" +
                    $"Invited Suppliers: {string.Join(", ", suppliers.Select(s => s.Name))}\n\n" +
                    $"Requested Materials:\n{itemsSummary}\n\n" +
                    (string.IsNullOrWhiteSpace(rfq.Notes) ? "" : $"Notes:\n{rfq.Notes}\n\n") +
                    $"BuildWise Automated Procurement Notification";

                await _emailService.SendAsync(additionalEmail.Trim(), addSubject, addBody);
            }

            // 3. Notify Procurement Managers
            var managerEmails = await _db.Users
                .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Name == Roles.ProcurementManager))
                .Select(u => u.Email)
                .ToListAsync();

            foreach (var managerEmail in managerEmails)
            {
                if (!string.IsNullOrWhiteSpace(managerEmail) && !string.Equals(managerEmail, additionalEmail, StringComparison.OrdinalIgnoreCase))
                {
                    var mgrSubject = $"[BuildWise] RFQ #{rfq.Id} Issued for Request #{request.Id}";
                    var mgrBody =
                        $"Procurement has issued RFQ #{rfq.Id} for Material Request #{request.Id} ({request.Project?.Name}).\n\n" +
                        $"Invited Suppliers ({suppliers.Count}): {string.Join(", ", suppliers.Select(s => $"{s.Name} ({s.Email ?? "No email"})"))}\n" +
                        $"Required Response Date: {rfq.RequiredResponseDate:yyyy-MM-dd}\n\n" +
                        $"Requested Materials:\n{itemsSummary}\n\n" +
                        $"Track quotation submissions in the BuildWise Procurement Workspace.";

                    await _emailService.SendAsync(managerEmail, mgrSubject, mgrBody);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send email notifications for RFQ #{RfqId}", rfq.Id);
        }
    }

    private async Task SendSupplierInvitationEmailsAsync(
        Rfq rfq,
        MaterialRequest request,
        List<Supplier> suppliers)
    {
        var itemsSummary = request.Items != null && request.Items.Count > 0
            ? string.Join("\n", request.Items.Select(i => $" - {i.Material?.Name ?? "Material"}: {i.RequestedQuantity:0.##} {i.Unit ?? "units"}"))
            : "Standard project materials";

        foreach (var supplier in suppliers)
        {
            if (!string.IsNullOrWhiteSpace(supplier.Email))
            {
                var subject = $"[BuildWise] Request for Quotation (RFQ #{rfq.Id}) — {request.Project?.Name ?? "Procurement"}";
                var body =
                    $"Dear {supplier.ContactPerson ?? supplier.Name},\n\n" +
                    $"You are invited by BuildWise to submit a quotation for RFQ #{rfq.Id}.\n\n" +
                    $"Project: {request.Project?.Name ?? "General"}\n" +
                    $"Material Request: #{request.Id}\n" +
                    $"Required Response Date: {rfq.RequiredResponseDate:yyyy-MM-dd}\n\n" +
                    $"Requested Materials:\n{itemsSummary}\n\n" +
                    (string.IsNullOrWhiteSpace(rfq.Notes) ? "" : $"Notes / Instructions:\n{rfq.Notes}\n\n") +
                    $"Please submit your formal quotation through your BuildWise Supplier Portal account or reply directly.\n\n" +
                    $"Thank you,\nBuildWise Procurement Team";

                await _emailService.SendAsync(supplier.Email, subject, body);
            }
        }
    }

    private async Task<Rfq> Load(int id) => await _db.Rfqs
        .Include(r => r.MaterialRequest).ThenInclude(r => r.Project)
        .Include(r => r.Suppliers).ThenInclude(s => s.Supplier)
        .FirstAsync(r => r.Id == id);

    private int ParseUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture);

    private static RfqDto Map(Rfq r) => new(
        r.Id,
        r.MaterialRequestId,
        r.MaterialRequest?.Project?.Name ?? $"Project #{r.MaterialRequest?.ProjectId ?? 0}",
        r.RequiredResponseDate,
        r.Notes,
        r.Status.ToString(),
        r.IssuedByUserId,
        r.CreatedAt,
        r.Suppliers.Select(s => new RfqSupplierDto(
            s.Id,
            s.SupplierId,
            s.Supplier?.Name ?? $"Supplier #{s.SupplierId}",
            s.Supplier?.Email,
            s.Supplier?.ContactPerson,
            s.Supplier?.Status.ToString() ?? "Active",
            s.Status.ToString()
        )).ToList()
    );
}
