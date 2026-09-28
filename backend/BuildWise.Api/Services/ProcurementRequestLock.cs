using BuildWise.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BuildWise.Api.Services;

internal static class ProcurementRequestLock
{
    public static async Task<IDbContextTransaction?> AcquireAsync(ApplicationDbContext db, int requestId)
    {
        if (!db.Database.IsRelational()) return null;
        var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // All PO writers use the same request-scoped lock. Read committed
            // queries after acquisition see the previous writer's committed PO.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(230902, {requestId})");
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
}
