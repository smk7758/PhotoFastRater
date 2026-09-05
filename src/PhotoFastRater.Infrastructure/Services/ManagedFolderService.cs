using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Database.Repositories;

namespace PhotoFastRater.Infrastructure.Services;

/// <summary>Maintains watched roots without loading an entire catalog or directory listing.</summary>
public sealed class ManagedFolderService
{
    private const int LookupBatchSize = 500;
    private readonly ManagedFolderRepository _folderRepository;
    private readonly FolderExclusionPatternRepository _patternRepository;
    private readonly PhotoRepository _photoRepository;
    private readonly IFolderScanner _folderScanner;

    /// <summary>Creates the managed-folder use case.</summary>
    public ManagedFolderService(
        ManagedFolderRepository folderRepository,
        FolderExclusionPatternRepository patternRepository,
        PhotoRepository photoRepository,
        IFolderScanner folderScanner)
    {
        _folderRepository = folderRepository;
        _patternRepository = patternRepository;
        _photoRepository = photoRepository;
        _folderScanner = folderScanner;
    }

    /// <summary>Adds one existing root. Duplicate roots are rejected.</summary>
    public async Task<ManagedFolder> AddFolderAsync(string folderPath, bool isRecursive = true)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"フォルダーが見つかりません: {folderPath}");
        if (await _folderRepository.ExistsAsync(folderPath))
            throw new InvalidOperationException($"フォルダーは既に登録されています: {folderPath}");

        return await _folderRepository.AddAsync(new ManagedFolder
        {
            FolderPath = Path.GetFullPath(folderPath),
            IsRecursive = isRecursive,
            AddedDate = DateTime.Now,
            IsActive = true
        });
    }

    /// <summary>Removes only the managed-root record; source photos are not deleted.</summary>
    public Task RemoveFolderAsync(int folderId) => _folderRepository.DeleteAsync(folderId);

    /// <summary>Gets all configured roots.</summary>
    public Task<List<ManagedFolder>> GetAllFoldersAsync() => _folderRepository.GetAllAsync();

    /// <summary>Gets enabled roots.</summary>
    public Task<List<ManagedFolder>> GetActiveFoldersAsync() => _folderRepository.GetActiveAsync();

    /// <summary>Toggles whether a root participates in bulk scans.</summary>
    public async Task ToggleFolderActiveAsync(int folderId)
    {
        var folder = await _folderRepository.GetByIdAsync(folderId);
        if (folder is null)
            return;
        folder.IsActive = !folder.IsActive;
        await _folderRepository.UpdateAsync(folder);
    }

    /// <summary>Updates a root count with one SQL aggregate.</summary>
    public async Task UpdatePhotoCountAsync(int folderId, CancellationToken cancellationToken = default)
    {
        var folder = await _folderRepository.GetByIdAsync(folderId);
        if (folder is null)
            return;
        var count = await _photoRepository.CountUnderPathAsync(folder.FolderPath, cancellationToken);
        await _folderRepository.UpdatePhotoCountAsync(folderId, count);
    }

    /// <summary>Scans incrementally and checks existence in bounded SQL batches.</summary>
    public async Task<ScanResult> ScanFolderAsync(
        int folderId,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var folder = await _folderRepository.GetByIdAsync(folderId)
            ?? throw new InvalidOperationException($"フォルダーが見つかりません: ID={folderId}");
        if (!Directory.Exists(folder.FolderPath))
            throw new DirectoryNotFoundException($"フォルダーが存在しません: {folder.FolderPath}");

        var exclusions = await _patternRepository.GetEnabledAsync();
        var result = new ScanResult();
        var pendingPaths = new List<string>(LookupBatchSize);

        await foreach (var item in _folderScanner.ScanAsync(
            folder.FolderPath,
            new ScanOptions(folder.IsRecursive),
            cancellationToken))
        {
            if (item.Error is not null && item.Photo is null)
            {
                result.ErrorCount++;
                result.Error ??= item.Error.Message;
                continue;
            }
            if (PatternMatcher.IsMatchAny(item.Path, exclusions))
            {
                result.ExcludedFiles++;
                continue;
            }

            pendingPaths.Add(item.Path);
            result.TotalFiles++;
            if (pendingPaths.Count == LookupBatchSize)
                await CountBatchAsync(pendingPaths, result, cancellationToken);

            progress?.Report(new ScanProgress
            {
                CurrentFile = item.Path,
                ProcessedCount = result.TotalFiles,
                TotalCount = 0,
                Status = "走査中（総数を逐次検出）"
            });
        }

        await CountBatchAsync(pendingPaths, result, cancellationToken);
        await UpdatePhotoCountAsync(folderId, cancellationToken);
        return result;
    }

    /// <summary>Scans every active root sequentially so SQLite keeps one logical writer.</summary>
    public async Task<Dictionary<int, ScanResult>> ScanAllActiveFoldersAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var folders = await GetActiveFoldersAsync();
        var results = new Dictionary<int, ScanResult>();
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results[folder.Id] = await ScanFolderAsync(folder.Id, progress, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                results[folder.Id] = new ScanResult { Error = exception.Message, ErrorCount = 1 };
            }
        }
        return results;
    }

    private async Task CountBatchAsync(List<string> paths, ScanResult result, CancellationToken cancellationToken)
    {
        if (paths.Count == 0)
            return;
        var existing = await _photoRepository.GetExistingNormalizedPathsAsync(paths, cancellationToken);
        result.ExistingFiles += existing.Count;
        result.NewFiles += paths.Count - existing.Count;
        paths.Clear();
    }
}

/// <summary>Reports streaming scan progress; zero total means enumeration is incomplete.</summary>
public sealed class ScanProgress
{
    public string CurrentFile { get; set; } = string.Empty;
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>Summarizes one scan including recoverable per-path failures.</summary>
public sealed class ScanResult
{
    public int TotalFiles { get; set; }
    public int NewFiles { get; set; }
    public int ExistingFiles { get; set; }
    public int ExcludedFiles { get; set; }
    public int ErrorCount { get; set; }
    public string? Error { get; set; }
}
