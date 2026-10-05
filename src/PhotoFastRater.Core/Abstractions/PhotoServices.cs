using PhotoFastRater.Core.Domain;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Abstractions;

/// <summary>Streams photos from a folder without materializing the complete file list.</summary>
public interface IFolderScanner
{
    /// <summary>Enumerates supported files and reports individual failures without ending the scan.</summary>
    /// <remarks>Runs in O(n) time and uses bounded working memory. Cancellation is observed between I/O operations.</remarks>
    IAsyncEnumerable<FolderScanItem> ScanAsync(string rootPath, ScanOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Controls resource limits for one folder scan.</summary>
public sealed record ScanOptions(bool RecurseSubdirectories = true, int MetadataParallelism = 4, int BufferCapacity = 512);

/// <summary>Represents either one discovered photo or one recoverable per-path error.</summary>
public sealed record FolderScanItem(Photo? Photo, string Path, Exception? Error)
{
    /// <summary>Gets whether a catalogable photo was produced; <see cref="Error"/> may still describe partial metadata failure.</summary>
    public bool IsSuccess => Photo is not null;
}

/// <summary>Provides indexed, transactional access to the photo catalog.</summary>
public interface IPhotoCatalog
{
    /// <summary>Reads a stable keyset page. The operation is O(log n + page size) with the required indexes.</summary>
    Task<PagedResult<PhotoSummary>> SearchAsync(PhotoSearchQuery query, PageCursor? after, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates a bounded batch by normalized path in one transaction.</summary>
    Task UpsertBatchAsync(IReadOnlyCollection<Photo> photos, CancellationToken cancellationToken = default);
}

/// <summary>Coordinates one rating operation so the database remains the durable source of truth.</summary>
public interface IRatingCoordinator
{
    /// <summary>Commits the database state before scheduling sidecar synchronization.</summary>
    Task SetRatingAsync(int photoId, RatingState state, CancellationToken cancellationToken = default);
}

/// <summary>Reads and atomically updates PhotoFastRater-owned XMP fields while preserving unknown XML.</summary>
public interface IXmpSidecarStore
{
    /// <summary>Reads a sidecar, returning <see langword="null"/> when no sidecar exists.</summary>
    Task<XmpRatingDocument?> ReadAsync(string photoPath, CancellationToken cancellationToken = default);

    /// <summary>Writes through a temporary file and atomic replacement; never rewrites the source image.</summary>
    Task WriteAsync(string photoPath, XmpRatingDocument document, CancellationToken cancellationToken = default);
}

/// <summary>Contains the application-owned fields stored in an XMP sidecar.</summary>
public sealed record XmpRatingDocument(RatingState Rating, DateTime UpdatedUtc, long Revision, string UpdateSource);

/// <summary>Writes metadata inside an existing image only when no pixel re-encoding is required.</summary>
public interface IEmbeddedMetadataWriter
{
    /// <summary>Attempts an explicit metadata update and reports why it could not be completed.</summary>
    Task<MetadataWriteResult> WriteRatingAsync(string filePath, RatingState state, CancellationToken cancellationToken = default);
}

/// <summary>Describes the result of a non-reencoding embedded metadata operation.</summary>
public sealed record MetadataWriteResult(bool Succeeded, string? ErrorCode = null, string? ErrorMessage = null);

/// <summary>Retrieves bounded, deduplicated thumbnail data.</summary>
public interface IThumbnailService
{
    /// <summary>Gets a thumbnail. Identical concurrent keys share one generation task.</summary>
    Task<ReadOnlyMemory<byte>> GetAsync(ThumbnailRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Defines a versioned thumbnail request and its scheduling priority.</summary>
public sealed record ThumbnailRequest(string FilePath, int Width, int Height, ThumbnailPriority Priority, int GeneratorVersion = 1);

/// <summary>Determines when image work is scheduled.</summary>
public enum ThumbnailPriority { Visible, Prefetch, Idle }

/// <summary>Decodes a display-sized image with cooperative cancellation.</summary>
public interface IImageDecodeService
{
    /// <summary>Returns encoded display data sized for the requested viewport.</summary>
    Task<ReadOnlyMemory<byte>> DecodeAsync(string filePath, int maximumDimension, CancellationToken cancellationToken = default);
}

/// <summary>Executes repeatable single or batch exports without modifying source images.</summary>
public interface IExportService
{
    /// <summary>Exports all requested photos and reports each partial failure independently.</summary>
    Task<IReadOnlyList<ExportResult>> ExportAsync(IReadOnlyCollection<int> photoIds, ExportRecipe recipe, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Reports one export result without discarding successful siblings.</summary>
public sealed record ExportResult(int PhotoId, string? OutputPath, string? Error);

/// <summary>Reports monotonic progress for a batch export.</summary>
public sealed record ExportProgress(int Completed, int Total, int Failed);

/// <summary>Broadcasts committed catalog changes between independent window scopes.</summary>
public interface IPhotoChangeNotifier
{
    /// <summary>Raised after a photo change is durably committed.</summary>
    event EventHandler<PhotoChangedEventArgs>? PhotoChanged;

    /// <summary>Publishes a committed change. Implementations must not retain the sender scope.</summary>
    void Publish(PhotoChangedEventArgs change);
}

/// <summary>Identifies one committed photo change.</summary>
public sealed class PhotoChangedEventArgs : EventArgs
{
    /// <summary>Initializes a committed photo change notification.</summary>
    public PhotoChangedEventArgs(int photoId, string changeKind)
    {
        PhotoId = photoId;
        ChangeKind = changeKind;
    }

    /// <summary>Gets the stable catalog identifier.</summary>
    public int PhotoId { get; }

    /// <summary>Gets the application-defined change category.</summary>
    public string ChangeKind { get; }
}
