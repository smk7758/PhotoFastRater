using PhotoFastRater.Core.Database.Repositories;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Services;

/// <summary>
/// 管理フォルダサービス
/// </summary>
public class ManagedFolderService
{
    private readonly ManagedFolderRepository _folderRepository;
    private readonly FolderExclusionPatternRepository _patternRepository;
    private readonly PhotoRepository _photoRepository;

    public ManagedFolderService(
        ManagedFolderRepository folderRepository,
        FolderExclusionPatternRepository patternRepository,
        PhotoRepository photoRepository)
    {
        _folderRepository = folderRepository;
        _patternRepository = patternRepository;
        _photoRepository = photoRepository;
    }

    /// <summary>
    /// フォルダを追加
    /// </summary>
    public async Task<ManagedFolder> AddFolderAsync(string folderPath, bool isRecursive = true)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"フォルダが見つかりません: {folderPath}");
        }

        if (await _folderRepository.ExistsAsync(folderPath))
        {
            throw new InvalidOperationException($"フォルダは既に登録されています: {folderPath}");
        }

        var folder = new ManagedFolder
        {
            FolderPath = folderPath,
            IsRecursive = isRecursive,
            AddedDate = DateTime.Now,
            IsActive = true,
            PhotoCount = 0
        };

        return await _folderRepository.AddAsync(folder);
    }

    /// <summary>
    /// フォルダを削除
    /// </summary>
    public async Task RemoveFolderAsync(int folderId)
    {
        await _folderRepository.DeleteAsync(folderId);
    }

    /// <summary>
    /// すべてのフォルダを取得
    /// </summary>
    public async Task<List<ManagedFolder>> GetAllFoldersAsync()
    {
        return await _folderRepository.GetAllAsync();
    }

    /// <summary>
    /// 有効なフォルダのみを取得
    /// </summary>
    public async Task<List<ManagedFolder>> GetActiveFoldersAsync()
    {
        return await _folderRepository.GetActiveAsync();
    }

    /// <summary>
    /// フォルダの有効/無効を切り替え
    /// </summary>
    public async Task ToggleFolderActiveAsync(int folderId)
    {
        var folder = await _folderRepository.GetByIdAsync(folderId);
        if (folder != null)
        {
            folder.IsActive = !folder.IsActive;
            await _folderRepository.UpdateAsync(folder);
        }
    }

    /// <summary>
    /// フォルダの写真数を更新
    /// </summary>
    public async Task UpdatePhotoCountAsync(int folderId)
    {
        var folder = await _folderRepository.GetByIdAsync(folderId);
        if (folder == null) return;

        // このフォルダに含まれる写真の数をカウント
        var photos = await _photoRepository.GetAllAsync();
        var count = photos.Count(p =>
            p.FilePath.StartsWith(folder.FolderPath, StringComparison.OrdinalIgnoreCase));

        await _folderRepository.UpdatePhotoCountAsync(folderId, count);
    }

    /// <summary>
    /// フォルダをスキャン（写真数のカウントと最終スキャン日時の更新）
    /// </summary>
    public async Task<ScanResult> ScanFolderAsync(
        int folderId,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var folder = await _folderRepository.GetByIdAsync(folderId);
        if (folder == null)
        {
            throw new InvalidOperationException($"フォルダが見つかりません: ID={folderId}");
        }

        if (!Directory.Exists(folder.FolderPath))
        {
            throw new DirectoryNotFoundException($"フォルダが存在しません: {folder.FolderPath}");
        }

        // 除外パターンを取得
        var exclusionPatterns = await _patternRepository.GetEnabledAsync();

        // サポートする画像形式
        var supportedExtensions = new[]
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff",
            ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".raf", ".rw2"
        };

        // ファイルを取得
        var searchOption = folder.IsRecursive
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        var allFiles = Directory.GetFiles(folder.FolderPath, "*.*", searchOption);

        var imageFiles = allFiles
            .Where(f => supportedExtensions.Contains(
                Path.GetExtension(f).ToLowerInvariant()))
            .Where(f => !PatternMatcher.IsMatchAny(f, exclusionPatterns))
            .ToList();

        var result = new ScanResult
        {
            TotalFiles = imageFiles.Count,
            NewFiles = 0,
            ExistingFiles = 0,
            ExcludedFiles = allFiles.Length - imageFiles.Count
        };

        // 新規ファイルと既存ファイルをカウント
        for (int i = 0; i < imageFiles.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var filePath = imageFiles[i];
            var exists = await _photoRepository.ExistsAsync(filePath);

            if (exists)
            {
                result.ExistingFiles++;
            }
            else
            {
                result.NewFiles++;
            }

            progress?.Report(new ScanProgress
            {
                CurrentFile = filePath,
                ProcessedCount = i + 1,
                TotalCount = imageFiles.Count,
                Status = $"スキャン中: {i + 1}/{imageFiles.Count}"
            });
        }

        // フォルダ情報を更新
        await UpdatePhotoCountAsync(folderId);

        return result;
    }

    /// <summary>
    /// すべての有効なフォルダを一括スキャン
    /// </summary>
    public async Task<Dictionary<int, ScanResult>> ScanAllActiveFoldersAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var folders = await GetActiveFoldersAsync();
        var results = new Dictionary<int, ScanResult>();

        for (int i = 0; i < folders.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var folder = folders[i];
            progress?.Report(new ScanProgress
            {
                CurrentFile = folder.FolderPath,
                ProcessedCount = i,
                TotalCount = folders.Count,
                Status = $"フォルダスキャン中: {i + 1}/{folders.Count}"
            });

            try
            {
                var result = await ScanFolderAsync(folder.Id, progress, cancellationToken);
                results[folder.Id] = result;
            }
            catch (Exception ex)
            {
                results[folder.Id] = new ScanResult
                {
                    Error = ex.Message
                };
            }
        }

        return results;
    }
}

/// <summary>
/// スキャン進捗情報
/// </summary>
public class ScanProgress
{
    public string CurrentFile { get; set; } = string.Empty;
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// スキャン結果
/// </summary>
public class ScanResult
{
    public int TotalFiles { get; set; }
    public int NewFiles { get; set; }
    public int ExistingFiles { get; set; }
    public int ExcludedFiles { get; set; }
    public string? Error { get; set; }
}
