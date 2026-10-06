using System.Text.Json;

namespace PhotoFastRater.UI.Services;

/// <summary>Replaces preferences only after a complete durable write, preserving the previous file on failure.</summary>
internal static class AtomicPreferences
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Write<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, leaveOpen: true))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                var garbage = Path.Combine(Path.GetDirectoryName(path)!, "_GARBAGE");
                Directory.CreateDirectory(garbage);
                File.Move(temporary, Path.Combine(garbage, Path.GetFileName(temporary)));
            }
        }
    }
}
