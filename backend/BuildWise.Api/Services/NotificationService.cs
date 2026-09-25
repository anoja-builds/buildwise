using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class NotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ApplicationDbContext context, ILogger<NotificationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task CreateInspectionEventAsync(int userId, int inspectionId, int deliveryId, int ncrId, string decision)
    {
        try
        {
            _context.NotificationEvents.Add(new NotificationEvent
            {
                UserId = userId, Type = "QualityInspectionCompleted", Title = "Quality inspection completed",
                Body = ncrId > 0
                    ? $"Delivery #{deliveryId} inspection is {decision}. NCR #{ncrId} requires follow-up."
                    : $"Delivery #{deliveryId} inspection is {decision}. No corrective action is required.",
                DeliveryId = deliveryId, InspectionId = inspectionId, NonConformanceId = ncrId
            });
            await _context.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not persist quality notification event."); }
    }

    public async Task<List<NotificationDto>> GetForUserAsync(int userId, bool unreadOnly = false)
    {
        var query = _context.NotificationEvents.Where(n => n.UserId == userId);
        if (unreadOnly) query = query.Where(n => !n.IsRead);
        return await query.OrderByDescending(n => n.CreatedAt).Take(50)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.MaterialRequestId, n.DeliveryId, n.InspectionId, n.NonConformanceId, n.IsRead, n.CreatedAt))
            .ToListAsync();
    }

    public async Task MarkReadAsync(int id, int userId)
    {
        var row = await _context.NotificationEvents.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (row == null) throw new KeyNotFoundException("Notification not found.");
        row.IsRead = true; row.ReadAt = DateTime.UtcNow; row.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }
}
