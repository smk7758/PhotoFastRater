using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Database;
using PhotoFastRater.Infrastructure.Database.Repositories;

if (!TryParseArguments(args, out var mode, out var outputDirectory, out var count))
{
    Console.Error.WriteLine("Usage: PhotoFastRater.BenchmarkData [generate|measure|recheck] <output-directory> <count: 1..100000>");
    return 2;
}

Directory.CreateDirectory(outputDirectory);
if (mode == "generate")
    return await GenerateFilesAsync(outputDirectory, count);
if (mode == "recheck") return await RecheckCatalogAsync(outputDirectory);
return await MeasureCatalogAsync(outputDirectory, count);

static bool TryParseArguments(string[] arguments, out string mode, out string outputDirectory, out int count)
{
    mode = arguments.Length == 2 ? "generate" : arguments.FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;
    count = 0;
    var pathIndex = arguments.Length == 2 ? 0 : 1;
    var countIndex = arguments.Length == 2 ? 1 : 2;
    outputDirectory = arguments.Length > pathIndex ? Path.GetFullPath(arguments[pathIndex]) : string.Empty;
    return arguments.Length is 2 or 3 &&
           mode is "generate" or "measure" or "recheck" &&
           int.TryParse(arguments[countIndex], NumberStyles.None, CultureInfo.InvariantCulture, out count) &&
           count is >= 1 and <= 100_000;
}

static async Task<int> GenerateFilesAsync(string outputDirectory, int count)
{
    var stopwatch = Stopwatch.StartNew();
    string? currentShard = null;
    for (var index = 0; index < count; index++)
    {
        // Empty files isolate enumeration overhead without pretending to benchmark image decoding.
        var shard = Path.Combine(outputDirectory, (index / 1_000).ToString("D3", CultureInfo.InvariantCulture));
        if (!string.Equals(shard, currentShard, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(shard);
            currentShard = shard;
        }
        var path = Path.Combine(shard, $"benchmark-{index:D6}.jpg");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, true);
    }
    stopwatch.Stop();
    Console.WriteLine($"Created {count:N0} files in {stopwatch.Elapsed} under {outputDirectory}");
    return 0;
}

static async Task<int> MeasureCatalogAsync(string outputDirectory, int count)
{
    var databasePath = Path.Combine(outputDirectory, "benchmark.db");
    if (File.Exists(databasePath))
    {
        Console.Error.WriteLine($"Refusing to overwrite existing benchmark database: {databasePath}");
        return 3;
    }

    var options = new DbContextOptionsBuilder<PhotoDbContext>()
        .UseSqlite($"Data Source={databasePath};Cache=Shared;Default Timeout=5")
        .Options;
    var factory = new BenchmarkContextFactory(options);
    await using (var context = factory.CreateDbContext())
    {
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;");
    }
    var repository = new PhotoRepository(factory);

    var insert = Stopwatch.StartNew();
    for (var start = 0; start < count; start += 500)
    {
        var size = Math.Min(500, count - start);
        await repository.UpsertBatchAsync(Enumerable.Range(start, size).Select(CreatePhoto).ToArray());
    }
    insert.Stop();

    var firstPage = Stopwatch.StartNew();
    var first = await repository.SearchAsync(new PhotoSearchQuery(), null, 256);
    firstPage.Stop();
    var searchSamples = new List<double>(100);
    for (var index = 0; index < 100; index++)
    {
        var timer = Stopwatch.StartNew();
        _ = await repository.SearchAsync(new PhotoSearchQuery("benchmark"), null, 256);
        timer.Stop();
        searchSamples.Add(timer.Elapsed.TotalMilliseconds);
    }
    var ratingSamples = new List<double>(100);
    foreach (var id in first.Items.Take(100).Select(photo => photo.Id))
    {
        var timer = Stopwatch.StartNew();
        await repository.CommitRatingAsync(id, new RatingState(4, false, false));
        timer.Stop();
        ratingSamples.Add(timer.Elapsed.TotalMilliseconds);
    }

    var result = new BenchmarkResult(
        DateTime.UtcNow,
        Environment.OSVersion.VersionString,
        Environment.ProcessorCount,
        GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        count,
        insert.Elapsed.TotalSeconds,
        firstPage.Elapsed.TotalMilliseconds,
        Percentile95(searchSamples),
        Percentile95(ratingSamples),
        Process.GetCurrentProcess().PeakWorkingSet64);
    var reportPath = Path.Combine(outputDirectory, "catalog-result.json");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(result));
    Console.WriteLine($"Report: {reportPath}");
    return 0;
}

/// <summary>Measures an existing synthetic catalog without reindexing or touching user catalog paths.</summary>
static async Task<int> RecheckCatalogAsync(string directory)
{
    var database = Path.Combine(directory, "benchmark.db");
    if (!File.Exists(database)) return 3;
    var options = new DbContextOptionsBuilder<PhotoDbContext>().UseSqlite(
        new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = database, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly }.ToString()).Options;
    var repository = new PhotoRepository(new BenchmarkContextFactory(options));
    var timings = new List<double>(100);
    for (var index = 0; index < 100; index++)
    {
        var watch = Stopwatch.StartNew();
        var page = await repository.SearchAsync(new PhotoSearchQuery("benchmark"), null, 256);
        watch.Stop(); timings.Add(watch.Elapsed.TotalMilliseconds);
    }
    var result = new { Trials = timings.Count, SearchP95Milliseconds = Percentile95(timings), SearchSamples = timings };
    await File.WriteAllTextAsync(Path.Combine(directory, "recheck-result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Search p95: {result.SearchP95Milliseconds:F2}ms ({timings.Count} trials)");
    return result.SearchP95Milliseconds <= 300 ? 0 : 1;
}
static Photo CreatePhoto(int index)
{
    var path = Path.GetFullPath(Path.Combine("C:\\PFR-Benchmark-Photos", (index / 1000).ToString("D3", CultureInfo.InvariantCulture), $"benchmark-{index:D6}.jpg"));
    return new Photo
    {
        FilePath = path,
        FileName = Path.GetFileName(path),
        FolderPath = Path.GetDirectoryName(path)!,
        FolderName = Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty,
        FileSize = 4_000_000 + index,
        FileModifiedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(index),
        DateTaken = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(index),
        ImportDate = DateTime.UtcNow,
        CameraModel = index % 2 == 0 ? "Benchmark A" : "Benchmark B",
        Rating = index % 6
    };
}

static double Percentile95(IReadOnlyCollection<double> values)
{
    var ordered = values.Order().ToArray();
    return ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
}

internal sealed class BenchmarkContextFactory(DbContextOptions<PhotoDbContext> options)
    : IDbContextFactory<PhotoDbContext>
{
    public PhotoDbContext CreateDbContext() => new(options);
}

internal sealed record BenchmarkResult(
    DateTime MeasuredUtc,
    string OperatingSystem,
    int LogicalProcessorCount,
    long AvailableMemoryBytes,
    int PhotoCount,
    double InitialIndexSeconds,
    double FirstPageMilliseconds,
    double SearchP95Milliseconds,
    double RatingP95Milliseconds,
    long PeakWorkingSetBytes);
