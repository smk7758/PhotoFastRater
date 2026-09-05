namespace PhotoFastRater.Core.Domain;

/// <summary>Defines a stable cursor for keyset pagination.</summary>
public sealed record PageCursor(DateTime DateTakenUtc, int PhotoId);

/// <summary>Contains one bounded page and the cursor needed to continue reading.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, PageCursor? NextCursor, long TotalCount);

/// <summary>Defines indexed photo filters without coupling the UI to a database provider.</summary>
public sealed record PhotoSearchQuery(
    string? Text = null,
    int? MinimumRating = null,
    int? MaximumRating = null,
    DateTime? TakenFromUtc = null,
    DateTime? TakenToUtc = null,
    string? CameraModel = null,
    string? FileExtension = null,
    bool IncludeMissing = false);

/// <summary>Provides only the data required to render and select a photo in a list.</summary>
public sealed record PhotoSummary(
    int Id,
    string FilePath,
    string FileName,
    DateTime DateTakenUtc,
    RatingState Rating,
    bool IsMissing,
    Guid? PairId,
    MetadataSyncStatus SyncStatus);
