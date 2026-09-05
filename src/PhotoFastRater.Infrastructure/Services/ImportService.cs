using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Database.Repositories;

namespace PhotoFastRater.Infrastructure.Services;

/// <summary>Streams folder imports into bounded catalog transactions.</summary>
public sealed class ImportService
{
    private const int UpsertBatchSize = 500;
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff",
        ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".raf", ".rw2"
    };
    private readonly IFolderScanner _folderScanner;
    private readonly IPhotoCatalog _photoCatalog;
    private readonly PhotoRepository _photoRepository;
    private readonly ExifService _exifService;

    /// <summary>Creates an importer without retaining a folder-sized file or entity list.</summary>
    public ImportService(
        IFolderScanner folderScanner,
        IPhotoCatalog photoCatalog,
        PhotoRepository photoRepository,
        ExifService exifService)
    {
        _folderScanner = folderScanner;
        _photoCatalog = photoCatalog;
        _photoRepository = photoRepository;
        _exifService = exifService;
    }

    /// <summary>Imports a folder in O(n) time with at most 500 pending photos plus the bounded scan pipeline.</summary>
    public async Task<ImportResult> ImportFromFolderAsync(
        string folderPath,
        bool includeSubfolders = true,
        List<FolderExclusionPattern>? exclusionPatterns = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pending = new List<Photo>(UpsertBatchSize);
        var processed = 0;
        var excluded = 0;
        var errors = new List<ImportError>();

        await foreach (var item in _folderScanner.ScanAsync(
            folderPath,
            new ScanOptions(includeSubfolders),
            cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (exclusionPatterns is not null && PatternMatcher.IsMatchAny(item.Path, exclusionPatterns))
            {
                excluded++;
                continue;
            }

            processed++;
            if (item.Photo is not null)
                pending.Add(item.Photo);
            if (item.Error is not null)
                errors.Add(new ImportError(item.Path, item.Error.Message));

            if (pending.Count == UpsertBatchSize)
            {
                await _photoCatalog.UpsertBatchAsync(pending, cancellationToken);
                pending.Clear();
            }

            progress?.Report(new ImportProgress
            {
                CurrentFile = item.Path,
                ProcessedCount = processed,
                TotalCount = 0,
                Status = item.Error is null ? "インデックス登録中" : "個別エラー（走査は継続）"
            });
        }

        if (pending.Count > 0)
            await _photoCatalog.UpsertBatchAsync(pending, cancellationToken);
        return new ImportResult(processed, excluded, errors);
    }

    /// <summary>Imports one supported file; duplicate normalized paths are not inserted.</summary>
    public async Task<Photo?> ImportSingleFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath) || !SupportedExtensions.Contains(Path.GetExtension(filePath)))
            return null;
        if (await _photoRepository.ExistsAsync(filePath))
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        var photo = _exifService.ExtractExifData(filePath);
        return await _photoRepository.AddAsync(photo);
    }
}

/// <summary>Summarizes one streaming import without retaining all photos in memory.</summary>
public sealed record ImportResult(int ProcessedCount, int ExcludedCount, IReadOnlyList<ImportError> Errors);

/// <summary>Identifies a recoverable per-path import failure.</summary>
public sealed record ImportError(string Path, string Message);

/// <summary>Reports streaming progress. A zero total means enumeration is still discovering files.</summary>
public sealed class ImportProgress
{
    public string CurrentFile { get; set; } = string.Empty;
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public string Status { get; set; } = string.Empty;
}
