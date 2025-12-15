using PhotoFastRater.Core.Database.Repositories;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Services;

public class ImportService
{
    private readonly PhotoRepository _photoRepository;
    private readonly ExifService _exifService;
    private readonly string[] _supportedExtensions = new[]
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff",
        ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".raf", ".rw2"
    };

    public ImportService(PhotoRepository photoRepository, ExifService exifService)
    {
        _photoRepository = photoRepository;
        _exifService = exifService;
    }

    public async Task<List<Photo>> ImportFromFolderAsync(
        string folderPath,
        bool includeSubfolders = true,
        List<FolderExclusionPattern>? exclusionPatterns = null,
        IProgress<ImportProgress>? progress = null)
    {
        var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var allFiles = Directory.GetFiles(folderPath, "*.*", searchOption);

        var imageFiles = allFiles
            .Where(f => _supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Where(f => exclusionPatterns == null || !PatternMatcher.IsMatchAny(f, exclusionPatterns))
            .ToList();

        var importedPhotos = new List<Photo>();
        var totalFiles = imageFiles.Count;

        for (int i = 0; i < totalFiles; i++)
        {
            var filePath = imageFiles[i];

            // 既にインポート済みかチェック
            if (await _photoRepository.ExistsAsync(filePath))
            {
                progress?.Report(new ImportProgress
                {
                    CurrentFile = filePath,
                    ProcessedCount = i + 1,
                    TotalCount = totalFiles,
                    Status = "スキップ（既存）"
                });
                continue;
            }

            try
            {
                // EXIF情報抽出
                var photo = _exifService.ExtractExifData(filePath);

                // データベースに追加
                var added = await _photoRepository.AddAsync(photo);
                importedPhotos.Add(added);

                progress?.Report(new ImportProgress
                {
                    CurrentFile = filePath,
                    ProcessedCount = i + 1,
                    TotalCount = totalFiles,
                    Status = "インポート完了"
                });
            }
            catch (Exception ex)
            {
                progress?.Report(new ImportProgress
                {
                    CurrentFile = filePath,
                    ProcessedCount = i + 1,
                    TotalCount = totalFiles,
                    Status = $"エラー: {ex.Message}"
                });
            }
        }

        return importedPhotos;
    }

    public async Task<Photo?> ImportSingleFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (!_supportedExtensions.Contains(extension))
            return null;

        if (await _photoRepository.ExistsAsync(filePath))
            return null;

        var photo = _exifService.ExtractExifData(filePath);
        return await _photoRepository.AddAsync(photo);
    }
}

public class ImportProgress
{
    public string CurrentFile { get; set; } = string.Empty;
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public string Status { get; set; } = string.Empty;
}
