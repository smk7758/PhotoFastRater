using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Core.Domain;

namespace PhotoFastRater.Infrastructure.Database.Repositories;

public class PhotoRepository
{
    private readonly IDbContextFactory<PhotoDbContext> _contextFactory;

    public PhotoRepository(IDbContextFactory<PhotoDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Photo?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .Include(p => p.Events)
            .ThenInclude(e => e.Event)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<List<Photo>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<List<Photo>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .Where(p => p.DateTaken >= startDate && p.DateTaken <= endDate)
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<List<Photo>> GetByCameraAsync(string cameraModel)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .Where(p => p.CameraModel == cameraModel)
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<List<Photo>> GetByLensAsync(string lensModel)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .Where(p => p.LensModel == lensModel)
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<List<Photo>> GetByRatingAsync(int rating)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Photos
            .AsNoTracking()
            .Where(p => p.Rating == rating)
            .OrderByDescending(p => p.DateTaken)
            .ToListAsync();
    }

    public async Task<Photo> AddAsync(Photo photo)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        PrepareForPersistence(photo);
        context.Photos.Add(photo);
        await context.SaveChangesAsync();
        return photo;
    }

    public async Task UpdateAsync(Photo photo)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        PrepareForPersistence(photo);
        context.Photos.Update(photo);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var photo = await context.Photos.FindAsync(id);
        if (photo != null)
        {
            context.Photos.Remove(photo);
            await context.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(string filePath)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var normalizedPath = NormalizePath(filePath);
        return await context.Photos.AnyAsync(p => p.NormalizedPath == normalizedPath);
    }

    public async Task<Photo?> GetByFilePathAsync(string filePath)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var normalizedPath = NormalizePath(filePath);
        return await context.Photos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.NormalizedPath == normalizedPath || (p.NormalizedPath == null && p.FilePath == filePath));
    }

    /// <summary>Commits one rating change and its linked pair in a single transaction.</summary>
    public async Task<IReadOnlyList<int>> CommitRatingAsync(int photoId, RatingState state, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var source = await context.Photos.SingleOrDefaultAsync(photo => photo.Id == photoId, cancellationToken)
            ?? throw new KeyNotFoundException($"Photo {photoId} was not found.");

        var targets = source.PairId.HasValue && source.PairLinkMode == PairLinkMode.Linked
            ? await context.Photos.Where(photo => photo.PairId == source.PairId).ToListAsync(cancellationToken)
            : new List<Photo> { source };
        var updatedUtc = DateTime.UtcNow;
        foreach (var photo in targets)
        {
            photo.Rating = state.Stars;
            photo.IsFavorite = state.IsFavorite;
            photo.IsRejected = state.IsRejected;
            photo.RatingModifiedUtc = updatedUtc;
            photo.RatingRevision++;
            photo.RatingSource = "PhotoFastRater";
            photo.MetadataSyncStatus = MetadataSyncStatus.Pending;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return targets.Select(photo => photo.Id).ToArray();
    }

    /// <summary>Gets IDs whose durable DB state still needs sidecar synchronization.</summary>
    public async Task<IReadOnlyList<int>> GetPendingSyncIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Photos.AsNoTracking()
            .Where(photo => photo.MetadataSyncStatus == MetadataSyncStatus.Pending || photo.MetadataSyncStatus == MetadataSyncStatus.Failed)
            .OrderBy(photo => photo.Id)
            .Select(photo => photo.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Gets the lightweight persistence fields required by the sidecar worker.</summary>
    public async Task<Photo?> GetForSyncAsync(int photoId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Photos.AsNoTracking().SingleOrDefaultAsync(photo => photo.Id == photoId, cancellationToken);
    }

    /// <summary>Records the result of a sidecar attempt without changing the user's rating.</summary>
    public async Task MarkSyncStatusAsync(
        int photoId,
        MetadataSyncStatus status,
        DateTime? sidecarModifiedUtc,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Photos.Where(photo => photo.Id == photoId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(photo => photo.MetadataSyncStatus, status)
                .SetProperty(photo => photo.SidecarModifiedUtc, sidecarModifiedUtc), cancellationToken);
    }

    private static void PrepareForPersistence(Photo photo)
    {
        photo.FilePath = Path.GetFullPath(photo.FilePath);
        photo.NormalizedPath = NormalizePath(photo.FilePath);
        photo.FolderPath = Path.GetDirectoryName(photo.FilePath) ?? string.Empty;
        photo.FolderName = Path.GetFileName(photo.FolderPath);
        photo.NormalizedDirectory = photo.FolderPath.Replace('/', '\\').ToLowerInvariant();
        photo.NormalizedBaseName = Path.GetFileNameWithoutExtension(photo.FilePath).ToLowerInvariant();
        if (File.Exists(photo.FilePath))
            photo.FileModifiedUtc = File.GetLastWriteTimeUtc(photo.FilePath);
    }

    private static string NormalizePath(string filePath) =>
        Path.GetFullPath(filePath).Replace('/', '\\').ToLowerInvariant();
}
