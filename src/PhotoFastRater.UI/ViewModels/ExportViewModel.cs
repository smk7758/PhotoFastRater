using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Export;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.UI.ViewModels;

public partial class ExportViewModel : ViewModelBase
{
    private readonly SocialMediaExporter _exporter;

    [ObservableProperty]
    private ObservableCollection<ExportTemplate> _templates = new();

    [ObservableProperty]
    private ExportTemplate? _selectedTemplate;

    [ObservableProperty]
    private bool _enableFrame = false;

    [ObservableProperty]
    private int _frameWidth = 20;

    [ObservableProperty]
    private string _frameColor = "#FFFFFF";

    [ObservableProperty]
    private bool _enableExifOverlay = true;

    [ObservableProperty]
    private ExifOverlayPosition _overlayPosition = ExifOverlayPosition.BottomLeft;

    [ObservableProperty]
    private SocialMediaPlatform _targetPlatform = SocialMediaPlatform.Instagram;

    public ExportViewModel(SocialMediaExporter exporter)
    {
        _exporter = exporter;
        InitializeDefaultTemplates();
    }

    private void InitializeDefaultTemplates()
    {
        Templates.Add(new ExportTemplate
        {
            Name = "Instagram スクエア",
            OutputWidth = 1080,
            OutputHeight = 1080,
            TargetPlatform = SocialMediaPlatform.Instagram,
            EnableFrame = true,
            FrameWidth = 30,
            FrameColor = "#FFFFFF",
            EnableExifOverlay = true,
            Position = ExifOverlayPosition.BottomLeft
        });

        Templates.Add(new ExportTemplate
        {
            Name = "Twitter",
            OutputWidth = 1200,
            OutputHeight = 675,
            TargetPlatform = SocialMediaPlatform.Twitter,
            EnableFrame = false,
            EnableExifOverlay = true,
            Position = ExifOverlayPosition.BottomRight
        });
    }

    public async Task ExportPhotoAsync(Photo photo, string outputPath)
    {
        if (SelectedTemplate == null)
            return;

        // テンプレート設定を更新
        SelectedTemplate.EnableFrame = EnableFrame;
        SelectedTemplate.FrameWidth = FrameWidth;
        SelectedTemplate.FrameColor = FrameColor;
        SelectedTemplate.EnableExifOverlay = EnableExifOverlay;
        SelectedTemplate.Position = OverlayPosition;
        SelectedTemplate.TargetPlatform = TargetPlatform;

        await _exporter.ExportAsync(photo, SelectedTemplate, outputPath);
    }

    [RelayCommand]
    private void SelectOutputFolder()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "エクスポート先フォルダを選択してください"
        };

        dialog.ShowDialog();
    }
}
