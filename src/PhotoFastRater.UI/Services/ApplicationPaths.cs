namespace PhotoFastRater.UI.Services;

/// <summary>Owns persistent and legacy locations so isolated runs never consult another user's catalog.</summary>
public sealed class ApplicationPaths
{
    /// <summary>Preserves installed locations unless an explicit absolute data directory is supplied.</summary>
    public ApplicationPaths(string? dataDirectory = null)
    {
        if (dataDirectory is not null && !Path.IsPathFullyQualified(dataDirectory))
            throw new ArgumentException("--data-dirには絶対パスを指定してください。", nameof(dataDirectory));
        IsIsolated = dataDirectory is not null;
        Root = Path.GetFullPath(dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoFastRater"));
        LegacySessionRoot = IsIsolated
            ? Path.Combine(Root, "LegacySessions")
            : Path.Combine(Path.GetTempPath(), "PhotoFastRater", "Sessions");
    }

    public bool IsIsolated { get; }
    public string Root { get; }
    public string Database => Path.Combine(Root, "photos.db");
    public string Settings => Path.Combine(Root, "user-settings.json");
    public string FolderSettings => Path.Combine(Root, "folder-mode-settings.json");
    public string Shortcuts => Path.Combine(Root, "folder-shortcuts.json");
    public string Sessions => Path.Combine(Root, "Sessions");
    public string LegacySessionRoot { get; }
    public string Cache => Path.Combine(Root, "Cache");
    public string Logs => Path.Combine(Root, "Logs");
}

/// <summary>Parses startup arguments before any application data is opened.</summary>
public sealed record StartupOptions(ApplicationPaths Paths, bool FolderMode, string? FolderPath)
{
    /// <summary>Accepts folder and data-directory options in either order; rejects ambiguous or missing values.</summary>
    public static StartupOptions Parse(IReadOnlyList<string> arguments)
    {
        string? dataDirectory = null;
        string? folderPath = null;
        var folderMode = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--data-dir" when dataDirectory is null:
                    if (++index >= arguments.Count || arguments[index].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException("--data-dirの後に絶対パスを指定してください。");
                    dataDirectory = arguments[index];
                    break;
                case "--folder" when !folderMode:
                    folderMode = true;
                    if (index + 1 < arguments.Count && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                        folderPath = Path.GetFullPath(arguments[++index]);
                    break;
                default:
                    throw new ArgumentException($"不明または重複した起動引数: {arguments[index]}");
            }
        }
        return new StartupOptions(new ApplicationPaths(dataDirectory), folderMode, folderPath);
    }
}
