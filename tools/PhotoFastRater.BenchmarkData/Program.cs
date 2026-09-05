using System.Diagnostics;

if (args.Length != 2 || !int.TryParse(args[1], out var count) || count is < 1 or > 100_000)
{
    Console.Error.WriteLine("Usage: PhotoFastRater.BenchmarkData <output-directory> <count: 1..100000>");
    return 2;
}

var outputDirectory = Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);
var stopwatch = Stopwatch.StartNew();
string? currentShard = null;
for (var index = 0; index < count; index++)
{
    // Empty files exercise enumeration, indexing and failure isolation without pretending to benchmark image decoding.
    var shard = Path.Combine(outputDirectory, (index / 1_000).ToString("D3"));
    if (!string.Equals(shard, currentShard, StringComparison.Ordinal))
    {
        Directory.CreateDirectory(shard);
        currentShard = shard;
    }
    var path = Path.Combine(shard, $"benchmark-{index:D6}.jpg");
    await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous);
}

stopwatch.Stop();
Console.WriteLine($"Created {count:N0} files in {stopwatch.Elapsed} under {outputDirectory}");
return 0;
