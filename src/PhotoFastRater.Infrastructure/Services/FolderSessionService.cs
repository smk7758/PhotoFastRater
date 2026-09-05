using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Infrastructure.Services;

/// <summary>
/// フォルダセッションサービス
/// </summary>
public class FolderSessionService
{
    private static readonly JsonSerializerOptions SessionJsonOptions = new() { WriteIndented = true };
    private readonly ExifService _exifService;
    private readonly string _sessionRoot;
    private readonly string[] _supportedExtensions = new[]
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff",
        ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".raf", ".rw2"
    };

    private static readonly HashSet<string> _rawExtSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".raf", ".rw2"
    };

    /// <summary>Creates the legacy session adapter. A custom root is intended for isolated tests and migrations.</summary>
    public FolderSessionService(ExifService exifService, string? sessionRoot = null)
    {
        _exifService = exifService;
        _sessionRoot = sessionRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoFastRater", "Sessions");
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
        IProgress<int>? progress = null,
        IProgress<FolderSessionPhoto>? progressPhoto = null)
    {
        return await Task.Run(() =>
        {
            var allFiles = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories);

            var imageFiles = allFiles
                .Where(f => _supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

            var photos = new List<FolderSessionPhoto>();

            var processed = 0;
            foreach (var filePath in imageFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);

                    var photo = new FolderSessionPhoto
                    {
                        FilePath = filePath,
                        FileName = fileInfo.Name,
                        FileSize = fileInfo.Length,
                        DateTaken = fileInfo.LastWriteTime,
                        IsRawFile = _rawExtSet.Contains(Path.GetExtension(filePath).ToLowerInvariant())
                    };

                    try
                    {
                        var exifPhoto = _exifService.ExtractExifData(filePath);
                        photo.DateTaken = exifPhoto.DateTaken;
                        photo.Width = exifPhoto.Width;
                        photo.Height = exifPhoto.Height;
                        photo.CameraModel = exifPhoto.CameraModel;
                        photo.LensModel = exifPhoto.LensModel;
                        photo.Aperture = exifPhoto.Aperture;
                        photo.ShutterSpeed = exifPhoto.ShutterSpeed;
                        photo.ISO = exifPhoto.ISO;
                        photo.FocalLength = exifPhoto.FocalLength;
                        photo.ExposureCompensation = exifPhoto.ExposureCompensation;
                        photo.Rating = exifPhoto.Rating;
                        photo.IsRejected = exifPhoto.IsRejected;
                    }
                    catch
                    {
                        // EXIF読み取りエラーは無視
                    }

                    photos.Add(photo);
                    progressPhoto?.Report(photo);
                    progress?.Report(++processed);
                }
                catch
                {
                    // エラーは無視して次へ
                }
            }

            DetectRawJpegPairs(photos);
            return photos;
        });
    }

    /// <summary>
    /// RAW+JPEGペアを検出して設定
    /// </summary>
    private void DetectRawJpegPairs(List<FolderSessionPhoto> photos)
    {
        var rawExtensions = new[] { ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".raf", ".rw2" };
        var jpegExtensions = new[] { ".jpg", ".jpeg" };

        // ファイルパスでグループ化（拡張子を除く）
        var photosByBaseName = photos
            .GroupBy(p => new
            {
                Directory = Path.GetDirectoryName(p.FilePath),
                BaseName = Path.GetFileNameWithoutExtension(p.FilePath)
            })
            .Where(g => g.Count() >= 2) // 2つ以上のファイルがある場合のみ
            .ToList();

        foreach (var group in photosByBaseName)
        {
            var rawFile = group.FirstOrDefault(p =>
                rawExtensions.Contains(Path.GetExtension(p.FilePath).ToLowerInvariant()));

            var jpegFile = group.FirstOrDefault(p =>
                jpegExtensions.Contains(Path.GetExtension(p.FilePath).ToLowerInvariant()));

            if (rawFile != null && jpegFile != null)
            {
                // ペアを設定
                rawFile.PairedFilePath = jpegFile.FilePath;
                rawFile.IsRawFile = true;

                jpegFile.PairedFilePath = rawFile.FilePath;
                jpegFile.IsRawFile = false;
            }
        }
    }

    /// <summary>
    /// セッションを保存
    /// </summary>
    public async Task SaveSessionAsync(FolderSession session, CancellationToken cancellationToken = default)
    {
        session.LastModifiedDate = DateTime.Now;

        var sessionPath = GetSessionPath(session.FolderPath);
        var sessionDir = Path.GetDirectoryName(sessionPath);

        if (sessionDir != null && !Directory.Exists(sessionDir))
        {
            Directory.CreateDirectory(sessionDir);
        }

        var json = JsonSerializer.Serialize(session, SessionJsonOptions);
        var temporaryPath = sessionPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, sessionPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// セッションを読み込み
    /// </summary>
    public async Task<FolderSession?> LoadSessionAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var sessionPath = GetSessionPath(folderPath);
        var isLegacySession = false;
        if (!File.Exists(sessionPath))
        {
            sessionPath = GetLegacySessionPath(folderPath);
            isLegacySession = true;
        }
        if (!File.Exists(sessionPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(sessionPath, cancellationToken);
            var session = JsonSerializer.Deserialize<FolderSession>(json, SessionJsonOptions);
            if (session is not null && isLegacySession)
            {
                // Import is idempotent and intentionally leaves the legacy file untouched for recovery.
                await SaveSessionAsync(session, cancellationToken);
            }
            return session;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// セッションファイルのパスを取得
    /// </summary>
    private string GetSessionPath(string folderPath)
    {
        var folderHash = GetFolderHash(folderPath);
        var sessionDir = Path.Combine(_sessionRoot, folderHash);
        return Path.Combine(sessionDir, "session.json");
    }

    /// <summary>
    /// フォルダパスのハッシュを生成
    /// </summary>
    private static string GetFolderHash(string folderPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(folderPath).ToLowerInvariant()));
        return Convert.ToHexStringLower(hash)[..24];
    }

    private static string GetLegacySessionPath(string folderPath)
    {
#pragma warning disable CA5351 // MD5 is required only to locate files written by previous releases.
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(folderPath.ToLowerInvariant()));
#pragma warning restore CA5351
        var folderHash = Convert.ToHexStringLower(hash)[..16];
        return Path.Combine(Path.GetTempPath(), "PhotoFastRater", "Sessions", folderHash, "session.json");
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
