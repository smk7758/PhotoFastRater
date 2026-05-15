using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoFastRater.UI.ViewModels;

public partial class FolderModeSettingsViewModel : ObservableObject
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoFastRater", "folder-mode-settings.json");

    [ObservableProperty] private int _defaultThumbnailSize = 200;
    [ObservableProperty] private bool _showExifInItem = false;
    [ObservableProperty] private bool _exifShowLens = true;
    [ObservableProperty] private bool _exifShowSettings = true;
    [ObservableProperty] private bool _exifShowDate = false;
    [ObservableProperty] private bool _exifShowCamera = false;
    [ObservableProperty] private bool _groupRawJpeg = true;
    [ObservableProperty] private bool _showMemoryWarning = true;
    [ObservableProperty] private int _maxFullImageMemoryMB = 1024;

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var json = File.ReadAllText(SettingsPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("defaultThumbnailSize", out var v) && v.TryGetInt32(out var i)) DefaultThumbnailSize = i;
            if (root.TryGetProperty("showExifInItem", out v)) ShowExifInItem = v.GetBoolean();
            if (root.TryGetProperty("exifShowLens", out v)) ExifShowLens = v.GetBoolean();
            if (root.TryGetProperty("exifShowSettings", out v)) ExifShowSettings = v.GetBoolean();
            if (root.TryGetProperty("exifShowDate", out v)) ExifShowDate = v.GetBoolean();
            if (root.TryGetProperty("exifShowCamera", out v)) ExifShowCamera = v.GetBoolean();
            if (root.TryGetProperty("groupRawJpeg", out v)) GroupRawJpeg = v.GetBoolean();
            if (root.TryGetProperty("showMemoryWarning", out v)) ShowMemoryWarning = v.GetBoolean();
            if (root.TryGetProperty("maxFullImageMemoryMB", out v) && v.TryGetInt32(out var m)) MaxFullImageMemoryMB = m;
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var data = new
            {
                defaultThumbnailSize = DefaultThumbnailSize,
                showExifInItem = ShowExifInItem,
                exifShowLens = ExifShowLens,
                exifShowSettings = ExifShowSettings,
                exifShowDate = ExifShowDate,
                exifShowCamera = ExifShowCamera,
                groupRawJpeg = GroupRawJpeg,
                showMemoryWarning = ShowMemoryWarning,
                maxFullImageMemoryMB = MaxFullImageMemoryMB
            };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
