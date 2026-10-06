using System.Text.Json;
using PhotoFastRater.Core.UI;
using PhotoFastRater.Infrastructure.Cache;

namespace PhotoFastRater.UI.Services;

/// <summary>Persists mutable user preferences outside the installed application directory.</summary>
public sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    /// <summary>Uses LocalApplicationData so installed files remain read-only and upgrades preserve preferences.</summary>
    public UserSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoFastRater",
            "user-settings.json"))
    {
    }

    /// <summary>Uses an explicit path, primarily for portable deployments and isolated tests.</summary>
    public UserSettingsStore(string settingsPath)
    {
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    /// <summary>Saves one consistent snapshot by replacing the destination only after serialization succeeds.</summary>
    public async Task SaveAsync(
        CacheConfiguration cache,
        UIConfiguration ui,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(ui);
        Validate(cache, ui);

        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _settingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new UserSettingsSnapshot(cache, ui),
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <summary>Overlays valid user values onto defaults; malformed files leave defaults unchanged.</summary>
    public bool TryLoad(CacheConfiguration cache, UIConfiguration ui)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(ui);
        if (!File.Exists(_settingsPath))
            return false;

        try
        {
            var snapshot = JsonSerializer.Deserialize<UserSettingsSnapshot>(File.ReadAllText(_settingsPath));
            if (snapshot is null)
                return false;

            if (snapshot.Cache is null || snapshot.UI is null)
                return false;
            Validate(snapshot.Cache, snapshot.UI);
            Copy(snapshot.Cache, cache);
            Copy(snapshot.UI, ui);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Validate(CacheConfiguration cache, UIConfiguration ui)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cache.CachePath);
        if (cache.MaxMemoryCacheSizeMB is < 64 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(cache));
        if (cache.MaxDiskCacheSizeGB is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(cache));
        if (cache.ThumbnailSize is < 80 or > 2048)
            throw new ArgumentOutOfRangeException(nameof(cache));
        if (cache.JpegQuality is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(cache));
        if (cache.MaxParallelGenerations is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(cache));
        if (ui.GridThumbnailSize is < 80 or > 600)
            throw new ArgumentOutOfRangeException(nameof(ui));
        if (ui.ArrowKeyNavigationMode is not ("GridFocus" or "SelectionOnly"))
            throw new ArgumentOutOfRangeException(nameof(ui));
    }

    private static void Copy(CacheConfiguration source, CacheConfiguration destination)
    {
        destination.CachePath = source.CachePath;
        destination.MaxMemoryCacheSizeMB = source.MaxMemoryCacheSizeMB;
        destination.MaxDiskCacheSizeGB = source.MaxDiskCacheSizeGB;
        destination.ThumbnailSize = source.ThumbnailSize;
        destination.JpegQuality = source.JpegQuality;
        destination.MaxParallelGenerations = source.MaxParallelGenerations;
        destination.EnableRAWSupport = source.EnableRAWSupport;
    }

    private static void Copy(UIConfiguration source, UIConfiguration destination)
    {
        destination.GridThumbnailSize = source.GridThumbnailSize;
        destination.EnableGPUAcceleration = source.EnableGPUAcceleration;
        destination.ArrowKeyNavigationMode = source.ArrowKeyNavigationMode;
    }

    private sealed record UserSettingsSnapshot(CacheConfiguration Cache, UIConfiguration UI);
}
