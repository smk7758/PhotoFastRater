using System.IO;
using System.Text.Json;
using PhotoFastRater.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoFastRater.UI.ViewModels;

public partial class FolderModeSettingsViewModel : ObservableObject
{
    /// <summary>Settings stay in the owning profile, including dialogs created during a folder session.</summary>
    public string SettingsPath { get; }

    public FolderModeSettingsViewModel() : this(new ApplicationPaths()) { }
    public FolderModeSettingsViewModel(ApplicationPaths paths) => SettingsPath = paths.FolderSettings;

    [ObservableProperty] private int _defaultThumbnailSize = 200;
    [ObservableProperty] private bool _showExifInItem = false;
    [ObservableProperty] private bool _exifShowLens = true;
    [ObservableProperty] private bool _exifShowSettings = true;
    [ObservableProperty] private bool _exifShowDate = false;
    [ObservableProperty] private bool _exifShowCamera = false;
    [ObservableProperty] private bool _groupRawJpeg = true;
    [ObservableProperty] private bool _showMemoryWarning = true;
    [ObservableProperty] private int _maxFullImageMemoryMB = 1024;
    [ObservableProperty] private bool _nameLabelBelow = false;
    [ObservableProperty] private bool _uniformPhotoSize = false;
    [ObservableProperty] private bool _showOriginalImages = false;

    // メモリ推奨プロパティ
    public int SystemTotalRamMB
    {
        get
        {
            try { return (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024); }
            catch { return 0; }
        }
    }

    public int RecommendedMaxFullImageMemoryMB =>
        Math.Clamp(SystemTotalRamMB / 2, 512, 4096);

    public bool IsMemorySettingBelowRecommended =>
        MaxFullImageMemoryMB < RecommendedMaxFullImageMemoryMB;

    partial void OnMaxFullImageMemoryMBChanged(int value)
        => OnPropertyChanged(nameof(IsMemorySettingBelowRecommended));

    /// <summary>Loads a validated snapshot; corrupt or inaccessible files leave all defaults intact.</summary>
    public void Load()
    {
        try
        {
            var candidate = new FolderModeSettingsViewModel(new ApplicationPaths(Path.GetDirectoryName(SettingsPath)!));
            candidate.LoadCore();
            candidate.Validate();
            DefaultThumbnailSize = candidate.DefaultThumbnailSize;
            ShowExifInItem = candidate.ShowExifInItem;
            ExifShowLens = candidate.ExifShowLens;
            ExifShowSettings = candidate.ExifShowSettings;
            ExifShowDate = candidate.ExifShowDate;
            ExifShowCamera = candidate.ExifShowCamera;
            GroupRawJpeg = candidate.GroupRawJpeg;
            ShowMemoryWarning = candidate.ShowMemoryWarning;
            MaxFullImageMemoryMB = candidate.MaxFullImageMemoryMB;
            NameLabelBelow = candidate.NameLabelBelow;
            UniformPhotoSize = candidate.UniformPhotoSize;
            ShowOriginalImages = candidate.ShowOriginalImages;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) { }
    }

    private void Validate()
    {
        if (DefaultThumbnailSize is < 100 or > 2000 || MaxFullImageMemoryMB is < 128 or > 32768)
            throw new ArgumentOutOfRangeException(nameof(DefaultThumbnailSize), "サムネイルは100～2000px、画像メモリは128～32768MBで指定してください。");
    }
    private void LoadCore()
    {
        if (!File.Exists(SettingsPath)) return;
        var json = File.ReadAllText(SettingsPath);
        using var doc = JsonDocument.Parse(json);
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
        if (root.TryGetProperty("nameLabelBelow", out v)) NameLabelBelow = v.GetBoolean();
        if (root.TryGetProperty("uniformPhotoSize", out v)) UniformPhotoSize = v.GetBoolean();
        if (root.TryGetProperty("showOriginalImages", out v)) ShowOriginalImages = v.GetBoolean();
    }



    public void Save()
    {
        Validate();
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
            maxFullImageMemoryMB = MaxFullImageMemoryMB,
            nameLabelBelow = NameLabelBelow,
            uniformPhotoSize = UniformPhotoSize,
            showOriginalImages = ShowOriginalImages
        };
        AtomicPreferences.Write(SettingsPath, data);
    }
}
