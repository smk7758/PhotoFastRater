using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Database;

namespace PhotoFastRater.Core.Services;

/// <summary>
/// データ移行サービス
/// 既存のデータベースレコードを新しいスキーマに移行する
/// </summary>
public class DataMigrationService
{
    private readonly PhotoDbContext _context;

    public DataMigrationService(PhotoDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// FolderPathとFolderNameを既存の写真に設定
    /// </summary>
    public async Task MigrateFolderPathsAsync(IProgress<MigrationProgress>? progress = null)
    {
        // FolderPathが空の写真を取得
        var photos = await _context.Photos
            .Where(p => p.FolderPath == "" || p.FolderPath == null)
            .ToListAsync();

        var totalPhotos = photos.Count;
        if (totalPhotos == 0)
        {
            progress?.Report(new MigrationProgress
            {
                CurrentIndex = 0,
                TotalCount = 0,
                Status = "移行が必要な写真はありません"
            });
            return;
        }

        int migratedCount = 0;
        int errorCount = 0;

        for (int i = 0; i < totalPhotos; i++)
        {
            try
            {
                var photo = photos[i];
                var directoryPath = Path.GetDirectoryName(photo.FilePath);

                if (!string.IsNullOrEmpty(directoryPath))
                {
                    photo.FolderPath = directoryPath;
                    photo.FolderName = Path.GetFileName(directoryPath);
                    migratedCount++;
                }

                progress?.Report(new MigrationProgress
                {
                    CurrentIndex = i + 1,
                    TotalCount = totalPhotos,
                    Status = $"移行中: {photo.FileName}"
                });
            }
            catch (Exception ex)
            {
                errorCount++;
                progress?.Report(new MigrationProgress
                {
                    CurrentIndex = i + 1,
                    TotalCount = totalPhotos,
                    Status = $"エラー: {ex.Message}"
                });
            }
        }

        // データベースに保存
        await _context.SaveChangesAsync();

        progress?.Report(new MigrationProgress
        {
            CurrentIndex = totalPhotos,
            TotalCount = totalPhotos,
            Status = $"完了: {migratedCount}件を移行しました（エラー: {errorCount}件）"
        });
    }

    /// <summary>
    /// すべての保留中のマイグレーションを実行
    /// </summary>
    public async Task RunAllMigrationsAsync(IProgress<MigrationProgress>? progress = null)
    {
        progress?.Report(new MigrationProgress
        {
            CurrentIndex = 0,
            TotalCount = 1,
            Status = "FolderPath移行を開始..."
        });

        await MigrateFolderPathsAsync(progress);

        progress?.Report(new MigrationProgress
        {
            CurrentIndex = 1,
            TotalCount = 1,
            Status = "すべての移行が完了しました"
        });
    }
}

/// <summary>
/// 移行の進捗情報
/// </summary>
public class MigrationProgress
{
    public int CurrentIndex { get; set; }
    public int TotalCount { get; set; }
    public string Status { get; set; } = string.Empty;
}
