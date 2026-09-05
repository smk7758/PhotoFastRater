using System.Security.Cryptography;
using System.Text;
using PhotoFastRater.Core.ImageProcessing;
using PhotoFastRater.Infrastructure.ImageProcessing;

namespace PhotoFastRater.Infrastructure.Cache;

public class ThumbnailCacheManager
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".nef", ".arw", ".rw2", ".orf", ".raf", ".dng",
        ".pef", ".srw", ".x3f", ".3fr", ".mef", ".mrw", ".nrw", ".rwl"
    };

    private readonly CacheConfiguration _config;
    private readonly JpegThumbnailGenerator _jpegGenerator;
    private readonly RawThumbnailGenerator _rawGenerator;
    private readonly string _effectiveCachePath;

    // LRU: LinkedList (most-recent at front) + Dictionary for O(1) lookup
    private readonly LinkedList<(string key, byte[] data)> _lruList = new();
    private readonly Dictionary<string, LinkedListNode<(string key, byte[] data)>> _lruIndex = new();
    private long _memorySizeBytes;
    private readonly long _maxMemorySizeBytes;
    private readonly object _lock = new();

    public ThumbnailCacheManager(
        CacheConfiguration config,
        JpegThumbnailGenerator jpegGenerator,
        RawThumbnailGenerator rawGenerator)
    {
        _config = config;
        _jpegGenerator = jpegGenerator;
        _rawGenerator = rawGenerator;
        _maxMemorySizeBytes = (long)config.MaxMemoryCacheSizeMB * 1024 * 1024;
        _effectiveCachePath = ResolveAndCreateCachePath(config.CachePath);
    }

    private static string ResolveAndCreateCachePath(string configuredPath)
    {
        if (!string.IsNullOrEmpty(configuredPath))
        {
            try
            {
                Directory.CreateDirectory(configuredPath);
                return configuredPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ThumbnailCacheManager] 設定されたキャッシュパス '{configuredPath}' を作成できません。デフォルトにフォールバックします。理由: {ex.Message}");
            }
        }

        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoFastRater", "Cache");
        Directory.CreateDirectory(defaultPath);
        return defaultPath;
    }

    public async Task<byte[]> GetThumbnailAsync(string filePath)
    {
        var key = NormalizeKey(filePath);

        // L1: memory
        lock (_lock)
        {
            if (_lruIndex.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                return node.Value.data;
            }
        }

        // L2: disk
        var diskPath = GetDiskCachePath(key);
        if (File.Exists(diskPath))
        {
            var bytes = await File.ReadAllBytesAsync(diskPath);
            if (bytes.Length > 0)
            {
                AddToMemoryCache(key, bytes);
                return bytes;
            }
        }

        // L3: generate
        var thumbnail = await GenerateThumbnailAsync(filePath);
        if (thumbnail.Length > 0)
        {
            await File.WriteAllBytesAsync(diskPath, thumbnail);
            AddToMemoryCache(key, thumbnail);
        }

        return thumbnail;
    }

    private async Task<byte[]> GenerateThumbnailAsync(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (_config.EnableRAWSupport && RawExtensions.Contains(ext))
        {
            var raw = await _rawGenerator.GenerateAsync(filePath, _config.ThumbnailSize);
            if (raw.Length > 0)
                return raw;
        }

        return await _jpegGenerator.GenerateAsync(filePath, _config.ThumbnailSize);
    }

    private void AddToMemoryCache(string key, byte[] data)
    {
        lock (_lock)
        {
            if (_lruIndex.ContainsKey(key))
                return;

            var node = _lruList.AddFirst((key, data));
            _lruIndex[key] = node;
            _memorySizeBytes += data.Length;

            while (_memorySizeBytes > _maxMemorySizeBytes && _lruList.Last != null)
            {
                var last = _lruList.Last.Value;
                _lruList.RemoveLast();
                _lruIndex.Remove(last.key);
                _memorySizeBytes -= last.data.Length;
            }
        }
    }

    private string GetDiskCachePath(string key)
    {
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_effectiveCachePath, $"{hash}.jpg");
    }

    private static string NormalizeKey(string filePath) =>
        Path.GetFullPath(filePath).ToLowerInvariant();
}
