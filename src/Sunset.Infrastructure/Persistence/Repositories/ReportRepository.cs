using Microsoft.EntityFrameworkCore;
using Sunset.Application.Common;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;
using Sunset.Infrastructure.Persistence.Cursors;

namespace Sunset.Infrastructure.Persistence.Repositories;

public class ReportRepository(SunsetDbContext context) : IReportRepository
{
    public Task<Report?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Reports.Include(r => r.Reporter).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid reporterId, ReportTargetType targetType, Guid targetId, CancellationToken cancellationToken = default) =>
        context.Reports.AnyAsync(r => r.ReporterId == reporterId && r.TargetType == targetType && r.TargetId == targetId, cancellationToken);

    public async Task AddAsync(Report report, CancellationToken cancellationToken = default)
    {
        await context.Reports.AddAsync(report, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<CursorPagedResult<Report>> GetByStatusAsync(ReportStatus status, string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        var reports = context.Reports
            .Include(r => r.Reporter)
            .Where(r => r.Status == status)
            .AsQueryable();

        var decoded = CreatedAtCursor.TryDecode(cursor);
        if (decoded is { } c)
        {
            reports = reports.Where(r =>
                r.CreatedAt < c.CreatedAt ||
                (r.CreatedAt == c.CreatedAt && r.Id.CompareTo(c.Id) < 0));
        }

        var items = await reports
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        var page = items.Take(limit).ToList();
        var nextCursor = hasMore ? CreatedAtCursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;

        return new CursorPagedResult<Report>(page, nextCursor, hasMore);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
