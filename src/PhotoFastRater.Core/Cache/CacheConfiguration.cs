namespace PhotoFastRater.Core.Cache;

public class CacheConfiguration
{
    public string CachePath { get; set; } = string.Empty;
    public int MaxMemoryCacheSizeMB { get; set; } = 500;
    public int ThumbnailSize { get; set; } = 512;
    public int JpegQuality { get; set; } = 85;
    public int MaxParallelGenerations { get; set; } = 4;
    public bool EnableRAWSupport { get; set; } = true;
}
