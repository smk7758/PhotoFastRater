using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Infrastructure.Export;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Domain;

namespace PhotoFastRater.UI.ViewModels;

public partial class ExportViewModel : ViewModelBase, IDisposable
{
    private readonly SocialMediaExporter _exporter;
    private readonly IExportService _batchExporter;
    private readonly IUserInteractionService _interaction;
    private CancellationTokenSource? _batchCancellation;
    private int[] _lastFailedIds = [];

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

    [ObservableProperty] private string _outputFormat = "JPEG";
    [ObservableProperty] private string _fileNamePattern = "{name}-export";
    [ObservableProperty] private int _quality = 92;
    [ObservableProperty] private int _rotationDegrees;
    [ObservableProperty] private int? _maximumWidth;
    [ObservableProperty] private int? _maximumHeight;
    [ObservableProperty] private bool _preserveMetadata = true;
    [ObservableProperty] private bool _cropEnabled;
    [ObservableProperty] private double _cropX;
    [ObservableProperty] private double _cropY;
    [ObservableProperty] private double _cropWidth = 1;
    [ObservableProperty] private double _cropHeight = 1;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelBatchCommand))]
    private bool _isBatchExporting;
    [ObservableProperty] private string _batchStatus = "一括対象を選択して書き出します。";
    [ObservableProperty] private ObservableCollection<string> _batchResults = [];

    public ExportViewModel(
        SocialMediaExporter exporter,
        IExportService batchExporter,
        IUserInteractionService interaction)
    {
        _exporter = exporter;
        _batchExporter = batchExporter;
        _interaction = interaction;
        InitializeDefaultTemplates();
        SelectedTemplate = Templates[0];
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

    public async Task<string> ExportPhotoAsync(Photo photo, string outputPath)
    {
        if (SelectedTemplate == null)
            throw new InvalidOperationException("エクスポートテンプレートが選択されていません。");

        // テンプレート設定を更新
        SelectedTemplate.EnableFrame = EnableFrame;
        SelectedTemplate.FrameWidth = FrameWidth;
        SelectedTemplate.FrameColor = FrameColor;
        SelectedTemplate.EnableExifOverlay = EnableExifOverlay;
        SelectedTemplate.Position = OverlayPosition;
        SelectedTemplate.TargetPlatform = TargetPlatform;

        return await _exporter.ExportAsync(photo, SelectedTemplate, GetAvailablePath(outputPath));
    }

    /// <summary>Exports a bounded photo selection and retains failed IDs for an explicit retry.</summary>
    public async Task ExportBatchAsync(IReadOnlyCollection<int> photoIds)
    {
        if (IsBatchExporting)
            return;
        var outputDirectory = await _interaction.SelectFolderAsync("バッチ書き出し先を選択してください");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;
        await RunBatchAsync(photoIds, outputDirectory);
    }

    /// <summary>Retries only failures from the immediately preceding batch with the current recipe.</summary>
    public async Task RetryFailuresAsync()
    {
        if (_lastFailedIds.Length == 0)
        {
            await _interaction.NotifyAsync("再試行", "再試行できる失敗項目はありません。", UserNotificationKind.Warning);
            return;
        }
        await ExportBatchAsync(_lastFailedIds);
    }

    [RelayCommand(CanExecute = nameof(CanCancelBatch))]
    private void CancelBatch() => _batchCancellation?.Cancel();

    private bool CanCancelBatch() => IsBatchExporting;

    [RelayCommand]
    private void ApplyCropPreset(string? preset)
    {
        (CropWidth, CropHeight) = preset switch
        {
            "1:1" => (1d, 1d),
            "4:3" => (1d, 0.75d),
            "3:2" => (1d, 2d / 3d),
            "16:9" => (1d, 9d / 16d),
            _ => (CropWidth, CropHeight)
        };
        CropX = (1 - CropWidth) / 2;
        CropY = (1 - CropHeight) / 2;
        CropEnabled = true;
    }

    private async Task RunBatchAsync(IReadOnlyCollection<int> photoIds, string outputDirectory)
    {
        _batchCancellation?.Dispose();
        _batchCancellation = new CancellationTokenSource();
        IsBatchExporting = true;
        BatchResults.Clear();
        var progress = new Progress<ExportProgress>(value =>
            BatchStatus = $"{value.Completed:N0}/{value.Total:N0} 完了、失敗 {value.Failed:N0}");
        try
        {
            var results = await _batchExporter.ExportAsync(
                photoIds,
                CreateRecipe(outputDirectory),
                progress,
                _batchCancellation.Token);
            _lastFailedIds = results.Where(result => result.Error is not null).Select(result => result.PhotoId).ToArray();
            foreach (var result in results)
                BatchResults.Add(result.Error is null ? $"完了: {result.OutputPath}" : $"失敗 #{result.PhotoId}: {result.Error}");
            BatchStatus = $"{results.Count - _lastFailedIds.Length:N0}件完了、{_lastFailedIds.Length:N0}件失敗";
        }
        catch (OperationCanceledException) when (_batchCancellation.IsCancellationRequested)
        {
            BatchStatus = "キャンセルしました。完了済みファイルは保持されています。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            BatchStatus = exception.Message;
            await _interaction.NotifyAsync("バッチ書き出し", exception.Message, UserNotificationKind.Error);
        }
        finally
        {
            IsBatchExporting = false;
        }
    }

    private ExportRecipe CreateRecipe(string outputDirectory) => new(
        outputDirectory,
        FileNamePattern,
        OutputFormat,
        Quality,
        CropEnabled ? new CropRect(CropX, CropY, CropWidth, CropHeight) : null,
        RotationDegrees,
        MaximumWidth,
        MaximumHeight,
        PreserveMetadata,
        EnableFrame ? FrameWidth : 0,
        FrameColor,
        EnableExifOverlay);

    private static string GetAvailablePath(string requestedPath)
    {
        if (!File.Exists(requestedPath))
            return requestedPath;

        var directory = Path.GetDirectoryName(requestedPath)!;
        var fileName = Path.GetFileNameWithoutExtension(requestedPath);
        var extension = Path.GetExtension(requestedPath);
        for (var suffix = 2; suffix <= 10_000; suffix++)
        {
            var candidate = Path.Combine(directory, $"{fileName}-{suffix}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
        throw new IOException("同名の出力ファイルが多すぎるため、安全なファイル名を決定できません。");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _batchCancellation?.Cancel();
        _batchCancellation?.Dispose();
        GC.SuppressFinalize(this);
    }

}
