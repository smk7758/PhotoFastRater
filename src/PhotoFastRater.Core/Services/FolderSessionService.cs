using System.Text.Json;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Services;

/// <summary>
/// フォルダセッションサービス
/// </summary>
public class FolderSessionService
{
    private readonly ExifService _exifService;
    private readonly string[] _supportedExtensions = new[]
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff",
        ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".raf", ".rw2"
    };

    public FolderSessionService(ExifService exifService)
    {
        _exifService = exifService;
    }

    /// <summary>
    /// 新しいセッションを作成
    /// </summary>
    public async Task<FolderSession> CreateSessionAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"フォルダが見つかりません: {folderPath}");
        }

        var session = new FolderSession
        {
            SessionId = Guid.NewGuid(),
            FolderPath = folderPath,
            CreatedDate = DateTime.Now
        };

        // 既存のセッションがあれば読み込む
        var existingSession = await LoadSessionAsync(folderPath);
        if (existingSession != null)
        {
            session = existingSession;
        }

        return session;
    }

    /// <summary>
    /// フォルダから写真を読み込み
    /// </summary>
    public async Task<List<FolderSessionPhoto>> LoadPhotosAsync(
        string folderPath,
        IProgress<int>? progress = null)
    {
        var allFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);

        var imageFiles = allFiles
            .Where(f => _supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        var photos = new List<FolderSessionPhoto>();

        for (int i = 0; i < imageFiles.Count; i++)
        {
            try
            {
                var filePath = imageFiles[i];
                var fileInfo = new FileInfo(filePath);

                // EXIF情報を抽出
                var photo = new FolderSessionPhoto
                {
                    FilePath = filePath,
                    FileName = fileInfo.Name,
                    FileSize = fileInfo.Length,
                    DateTaken = fileInfo.LastWriteTime // デフォルトはファイル更新日時
                };

                // 簡易的なEXIF読み取り（詳細はExifServiceを使用）
                try
                {
                    var exifPhoto = _exifService.ExtractExifData(filePath);
                    photo.DateTaken = exifPhoto.DateTaken;
                    photo.Width = exifPhoto.Width;
                    photo.Height = exifPhoto.Height;
                    photo.CameraModel = exifPhoto.CameraModel;
                    photo.Aperture = exifPhoto.Aperture;
                    photo.ShutterSpeed = exifPhoto.ShutterSpeed;
                    photo.ISO = exifPhoto.ISO;
                    photo.FocalLength = exifPhoto.FocalLength;
                }
                catch
                {
                    // EXIF読み取りエラーは無視
                }

                photos.Add(photo);
                progress?.Report(i + 1);
            }
            catch
            {
                // エラーは無視して次へ
            }
        }

        return photos;
    }

    /// <summary>
    /// セッションを保存
    /// </summary>
    public async Task SaveSessionAsync(FolderSession session)
    {
        session.LastModifiedDate = DateTime.Now;

        var sessionPath = GetSessionPath(session.FolderPath);
        var sessionDir = Path.GetDirectoryName(sessionPath);

        if (sessionDir != null && !Directory.Exists(sessionDir))
        {
            Directory.CreateDirectory(sessionDir);
        }

        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(sessionPath, json);
    }

    /// <summary>
    /// セッションを読み込み
    /// </summary>
    public async Task<FolderSession?> LoadSessionAsync(string folderPath)
    {
        var sessionPath = GetSessionPath(folderPath);

        if (!File.Exists(sessionPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(sessionPath);
            return JsonSerializer.Deserialize<FolderSession>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// セッションファイルのパスを取得
    /// </summary>
    private string GetSessionPath(string folderPath)
    {
        // フォルダパスのハッシュを使用してセッションファイル名を生成
        var folderHash = GetFolderHash(folderPath);
        var tempPath = Path.GetTempPath();
        var sessionDir = Path.Combine(tempPath, "PhotoFastRater", "Sessions", folderHash);
        return Path.Combine(sessionDir, "session.json");
    }

    /// <summary>
    /// フォルダパスのハッシュを生成
    /// </summary>
    private string GetFolderHash(string folderPath)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(folderPath.ToLowerInvariant()));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant().Substring(0, 16);
    }

    /// <summary>
    /// セッション内の写真のレーティングを更新
    /// </summary>
    public void UpdatePhotoRating(FolderSession session, string filePath, int rating, bool isFavorite, bool isRejected)
    {
        var photo = session.Photos.FirstOrDefault(p => p.FilePath == filePath);
        if (photo != null)
        {
            photo.Rating = rating;
            photo.IsFavorite = isFavorite;
            photo.IsRejected = isRejected;
        }
    }
}
