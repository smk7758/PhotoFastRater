using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using PhotoFastRater.Infrastructure.ImageProcessing;

namespace PhotoFastRater.Infrastructure.Cache;

/// <summary>Provides bounded memory and disk thumbnail caches with concurrent request coalescing.</summary>
public sealed class ThumbnailCacheManager : IDisposable
{
    private const int GeneratorVersion = 2;
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".raw", ".cr2", ".cr3", ".nef", ".arw", ".rw2", ".orf", ".raf", ".dng",
        ".pef", ".srw", ".x3f", ".3fr", ".mef", ".mrw", ".nrw", ".rwl"
    };

    private readonly CacheConfiguration _config;
    private readonly JpegThumbnailGenerator _jpegGenerator;
    private readonly RawThumbnailGenerator _rawGenerator;
    private readonly string _cachePath;
    private readonly string _indexConnectionString;
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _inflight = new(StringComparer.Ordinal);
    private readonly LinkedList<MemoryEntry> _memoryLru = new();
    private readonly Dictionary<string, LinkedListNode<MemoryEntry>> _memoryIndex = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _diskGate = new(1, 1);
    private readonly object _memoryLock = new();
    private readonly long _maximumMemoryBytes;
    private readonly long _maximumDiskBytes;
    private long _memoryBytes;

    /// <summary>Creates cache storage and validates configured capacity limits.</summary>
    public ThumbnailCacheManager(
        CacheConfiguration config,
        JpegThumbnailGenerator jpegGenerator,
        RawThumbnailGenerator rawGenerator)
    {
        _config = config;
        _jpegGenerator = jpegGenerator;
        _rawGenerator = rawGenerator;
        _maximumMemoryBytes = checked((long)Math.Clamp(config.MaxMemoryCacheSizeMB, 64, 4096) * 1024 * 1024);
        _maximumDiskBytes = checked((long)Math.Clamp(config.MaxDiskCacheSizeGB, 1, 100) * 1024 * 1024 * 1024);
        _cachePath = ResolveAndCreateCachePath(config.CachePath);
        _indexConnectionString = $"Data Source={Path.Combine(_cachePath, "cache-index.db")};Default Timeout=5";
        InitializeIndex();
    }

    /// <summary>Gets one thumbnail; equal concurrent cache keys share a single generation task.</summary>
    public async Task<byte[]> GetThumbnailAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = CreateKey(filePath);
        if (TryGetMemory(key, out var cached))
            return cached;

        var lazy = _inflight.GetOrAdd(
            key,
            _ => new Lazy<Task<byte[]>>(
                () => GetOrCreateAsync(filePath, key, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompleted)
                _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]>>>(key, lazy));
        }
    }

    /// <summary>
    /// Clears generated thumbnails without touching source photos. Files are moved to a dated
    /// <c>_GARBAGE</c> directory so an accidental clear remains recoverable.
    /// </summary>
    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        await _diskGate.WaitAsync(cancellationToken);
        try
        {
            var garbageRoot = Path.Combine(
                Path.GetDirectoryName(_cachePath) ?? _cachePath,
                "_GARBAGE",
                $"thumbnail-cache-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            var movedCount = 0;
            foreach (var sourcePath in Directory.EnumerateFiles(_cachePath, "*.jpg", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sourcePath.StartsWith(garbageRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relativePath = Path.GetRelativePath(_cachePath, sourcePath);
                var destinationPath = Path.Combine(garbageRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Move(sourcePath, destinationPath);
                movedCount++;
            }

            await using var connection = new SqliteConnection(_indexConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM CacheEntries;";
            await command.ExecuteNonQueryAsync(cancellationToken);

            lock (_memoryLock)
            {
                _memoryLru.Clear();
                _memoryIndex.Clear();
                _memoryBytes = 0;
            }
            return movedCount;
        }
        finally
        {
            _diskGate.Release();
        }
    }

    private async Task<byte[]> GetOrCreateAsync(string filePath, string key, CancellationToken cancellationToken)
    {
        var diskPath = GetDiskCachePath(key);
        if (File.Exists(diskPath))
        {
            var bytes = await File.ReadAllBytesAsync(diskPath, cancellationToken);
            if (bytes.Length > 0)
            {
                AddToMemory(key, bytes);
                await TouchIndexAsync(key, diskPath, bytes.LongLength, cancellationToken);
                return bytes;
            }
        }

        var thumbnail = await GenerateThumbnailAsync(filePath);
        if (thumbnail.Length == 0)
            return thumbnail;

        Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
        var temporaryPath = diskPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, thumbnail, cancellationToken);
            File.Move(temporaryPath, diskPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        AddToMemory(key, thumbnail);
        await TouchIndexAsync(key, diskPath, thumbnail.LongLength, cancellationToken);
        await TrimDiskCacheAsync(cancellationToken);
        return thumbnail;
    }

    private async Task<byte[]> GenerateThumbnailAsync(string filePath)
    {
        if (_config.EnableRAWSupport && RawExtensions.Contains(Path.GetExtension(filePath)))
        {
            var raw = await _rawGenerator.GenerateAsync(filePath, _config.ThumbnailSize);
            if (raw.Length > 0)
                return raw;
        }
        return await _jpegGenerator.GenerateAsync(filePath, _config.ThumbnailSize);
    }

    private bool TryGetMemory(string key, out byte[] data)
    {
        lock (_memoryLock)
        {
            if (!_memoryIndex.TryGetValue(key, out var node))
            {
                data = [];
                return false;
            }
            _memoryLru.Remove(node);
            _memoryLru.AddFirst(node);
            data = node.Value.Data;
            return true;
        }
    }

    private void AddToMemory(string key, byte[] data)
    {
        // Decoded pixels dominate memory, so budget both encoded bytes and an RGBA thumbnail estimate.
        var estimatedBytes = data.LongLength + checked((long)_config.ThumbnailSize * _config.ThumbnailSize * 4);
        lock (_memoryLock)
        {
            if (_memoryIndex.ContainsKey(key))
                return;
            var node = _memoryLru.AddFirst(new MemoryEntry(key, data, estimatedBytes));
            _memoryIndex[key] = node;
            _memoryBytes += estimatedBytes;
            while (_memoryBytes > _maximumMemoryBytes && _memoryLru.Last is not null)
            {
                var last = _memoryLru.Last;
                _memoryLru.RemoveLast();
                _memoryIndex.Remove(last.Value.Key);
                _memoryBytes -= last.Value.EstimatedBytes;
            }
        }
    }

    private string CreateKey(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath).Replace('/', '\\').ToLowerInvariant();
        var file = new FileInfo(fullPath);
        var source = string.Join('|',
            fullPath,
            file.Exists ? file.Length.ToString(CultureInfo.InvariantCulture) : "missing",
            file.Exists ? file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "missing",
            _config.ThumbnailSize.ToString(CultureInfo.InvariantCulture),
            _config.JpegQuality.ToString(CultureInfo.InvariantCulture),
            GeneratorVersion.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    private string GetDiskCachePath(string key) => Path.Combine(
        _cachePath,
        key[..2],
        key.Substring(2, 2),
        key + ".jpg");

    private async Task TouchIndexAsync(string key, string path, long size, CancellationToken cancellationToken)
    {
        await _diskGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_indexConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO CacheEntries(CacheKey, FilePath, SizeBytes, LastAccessUtc) VALUES($key,$path,$size,$access) ON CONFLICT(CacheKey) DO UPDATE SET FilePath=excluded.FilePath, SizeBytes=excluded.SizeBytes, LastAccessUtc=excluded.LastAccessUtc;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$size", size);
            command.Parameters.AddWithValue("$access", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _diskGate.Release();
        }
    }

    private async Task TrimDiskCacheAsync(CancellationToken cancellationToken)
    {
        await _diskGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_indexConnectionString);
            await connection.OpenAsync(cancellationToken);
            var total = await ExecuteScalarInt64Async(connection, "SELECT COALESCE(SUM(SizeBytes), 0) FROM CacheEntries;", cancellationToken);
            while (total > _maximumDiskBytes)
            {
                await using var select = connection.CreateCommand();
                select.CommandText = "SELECT CacheKey, FilePath, SizeBytes FROM CacheEntries ORDER BY LastAccessUtc LIMIT 128;";
                var victims = new List<(string Key, string Path, long Size)>();
                await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                        victims.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
                }
                if (victims.Count == 0)
                    break;
                foreach (var victim in victims)
                {
                    if (File.Exists(victim.Path))
                        File.Delete(victim.Path);
                    await using var delete = connection.CreateCommand();
                    delete.CommandText = "DELETE FROM CacheEntries WHERE CacheKey=$key;";
                    delete.Parameters.AddWithValue("$key", victim.Key);
                    await delete.ExecuteNonQueryAsync(cancellationToken);
                    total -= victim.Size;
                }
            }
        }
        finally
        {
            _diskGate.Release();
        }
    }

    private void InitializeIndex()
    {
        using var connection = new SqliteConnection(_indexConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS CacheEntries(CacheKey TEXT PRIMARY KEY, FilePath TEXT NOT NULL, SizeBytes INTEGER NOT NULL, LastAccessUtc TEXT NOT NULL); CREATE INDEX IF NOT EXISTS IX_CacheEntries_LastAccessUtc ON CacheEntries(LastAccessUtc);";
        command.ExecuteNonQuery();
    }

    private static async Task<long> ExecuteScalarInt64Async(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static string ResolveAndCreateCachePath(string configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoFastRater", "Cache")
            : Path.GetFullPath(configuredPath);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _diskGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record MemoryEntry(string Key, byte[] Data, long EstimatedBytes);
}
