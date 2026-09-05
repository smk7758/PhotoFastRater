using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.Core.Abstractions;

namespace PhotoFastRater.Infrastructure.Database.Repositories;

public class PhotoRepository : IPhotoCatalog
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

    /// <summary>Returns normalized paths already present for one bounded scan batch.</summary>
    public async Task<IReadOnlySet<string>> GetExistingNormalizedPathsAsync(
        IReadOnlyCollection<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        if (filePaths.Count > 500)
            throw new ArgumentOutOfRangeException(nameof(filePaths));
        var normalized = filePaths.Select(NormalizePath).Distinct(StringComparer.Ordinal).ToArray();
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.Photos.AsNoTracking()
            .Where(photo => photo.NormalizedPath != null && normalized.Contains(photo.NormalizedPath))
            .Select(photo => photo.NormalizedPath!)
            .ToListAsync(cancellationToken);
        return existing.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Counts a folder in SQL so catalog size does not determine UI memory usage.</summary>
    public async Task<int> CountUnderPathAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var normalizedRoot = NormalizePath(folderPath).TrimEnd('\\') + "\\";
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Photos.CountAsync(
            photo => photo.NormalizedPath != null && photo.NormalizedPath.StartsWith(normalizedRoot),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedResult<PhotoSummary>> SearchAsync(
        PhotoSearchQuery query,
        PageCursor? after,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (pageSize is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be between 1 and 256.");

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<Photo> photos = context.Photos.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var phrase = $"\"{query.Text.Trim().Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            photos = context.Photos.FromSqlInterpolated(
                $"SELECT p.* FROM Photos AS p WHERE p.Id IN (SELECT rowid FROM PhotoSearch WHERE PhotoSearch MATCH {phrase}) OR p.Id IN (SELECT pt.PhotoId FROM PhotoTagMappings AS pt INNER JOIN TagSearch ON TagSearch.rowid = pt.TagId WHERE TagSearch MATCH {phrase})")
                .AsNoTracking();
        }

        photos = ApplyFilters(photos, query);
        var totalCount = await photos.LongCountAsync(cancellationToken);
        if (after is not null)
        {
            photos = photos.Where(photo =>
                photo.DateTaken < after.DateTakenUtc ||
                (photo.DateTaken == after.DateTakenUtc && photo.Id < after.PhotoId));
        }

        var page = await photos
            .OrderByDescending(photo => photo.DateTaken)
            .ThenByDescending(photo => photo.Id)
            .Take(pageSize)
            .Select(photo => new
            {
                photo.Id,
                photo.FilePath,
                photo.FileName,
                photo.DateTaken,
                photo.Rating,
                photo.IsFavorite,
                photo.IsRejected,
                photo.IsMissing,
                photo.PairId,
                photo.MetadataSyncStatus
            })
            .ToListAsync(cancellationToken);
        var summaries = page.Select(photo => new PhotoSummary(
            photo.Id,
            photo.FilePath,
            photo.FileName,
            photo.DateTaken,
            new RatingState(photo.Rating, photo.IsFavorite, photo.IsRejected),
            photo.IsMissing,
            photo.PairId,
            photo.MetadataSyncStatus)).ToArray();
        var last = page.LastOrDefault();
        var next = page.Count == pageSize && last is not null
            ? new PageCursor(last.DateTaken, last.Id)
            : null;
        return new PagedResult<PhotoSummary>(summaries, next, totalCount);
    }

    /// <inheritdoc />
    public async Task UpsertBatchAsync(IReadOnlyCollection<Photo> photos, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photos);
        if (photos.Count == 0)
            return;
        if (photos.Count > 500)
            throw new ArgumentOutOfRangeException(nameof(photos), "A catalog batch cannot exceed 500 photos.");

        var incoming = photos
            .Select(photo => { PrepareForPersistence(photo); return photo; })
            .Where(photo => photo.NormalizedPath is not null)
            .DistinctBy(photo => photo.NormalizedPath, StringComparer.Ordinal)
            .ToArray();
        var paths = incoming.Select(photo => photo.NormalizedPath!).ToArray();
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var existing = await context.Photos
            .Where(photo => paths.Contains(photo.NormalizedPath!))
            .ToDictionaryAsync(photo => photo.NormalizedPath!, StringComparer.Ordinal, cancellationToken);

        foreach (var source in incoming)
        {
            if (existing.TryGetValue(source.NormalizedPath!, out var target))
                CopyScanMetadata(source, target);
            else
                context.Photos.Add(source);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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

    private static IQueryable<Photo> ApplyFilters(IQueryable<Photo> photos, PhotoSearchQuery query)
    {
        if (query.MissingOnly)
            photos = photos.Where(photo => photo.IsMissing);
        else if (!query.IncludeMissing)
            photos = photos.Where(photo => !photo.IsMissing);
        if (query.MinimumRating.HasValue)
            photos = photos.Where(photo => photo.Rating >= query.MinimumRating.Value);
        if (query.MaximumRating.HasValue)
            photos = photos.Where(photo => photo.Rating <= query.MaximumRating.Value);
        if (query.TakenFromUtc.HasValue)
            photos = photos.Where(photo => photo.DateTaken >= query.TakenFromUtc.Value);
        if (query.TakenToUtc.HasValue)
            photos = photos.Where(photo => photo.DateTaken <= query.TakenToUtc.Value);
        if (!string.IsNullOrWhiteSpace(query.CameraModel))
            photos = photos.Where(photo => photo.CameraModel == query.CameraModel);
        if (!string.IsNullOrWhiteSpace(query.FileExtension))
            photos = photos.Where(photo => photo.FileName.EndsWith(query.FileExtension));
        return photos;
    }

    private static void CopyScanMetadata(Photo source, Photo target)
    {
        // Ratings stay DB-owned; rescanning refreshes only filesystem and decoded technical metadata.
        target.FilePath = source.FilePath;
        target.FileName = source.FileName;
        target.FolderPath = source.FolderPath;
        target.FolderName = source.FolderName;
        target.FileSize = source.FileSize;
        target.FileModifiedUtc = source.FileModifiedUtc;
        target.DateTaken = source.DateTaken;
        target.ModifiedDate = source.ModifiedDate;
        target.CameraModel = source.CameraModel;
        target.CameraMake = source.CameraMake;
        target.LensModel = source.LensModel;
        target.Width = source.Width;
        target.Height = source.Height;
        target.Aperture = source.Aperture;
        target.ShutterSpeed = source.ShutterSpeed;
        target.ISO = source.ISO;
        target.FocalLength = source.FocalLength;
        target.ExposureCompensation = source.ExposureCompensation;
        target.Latitude = source.Latitude;
        target.Longitude = source.Longitude;
        target.LocationName = source.LocationName;
        target.NormalizedDirectory = source.NormalizedDirectory;
        target.NormalizedBaseName = source.NormalizedBaseName;
        target.IsMissing = false;
    }
}
