using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PhotoFastRater.Infrastructure.Export;
using PhotoFastRater.Core.Models;
using PhotoFastRater.UI.ViewModels;
using PhotoFastRater.UI.Services;
using PhotoFastRater.Core.Abstractions;

namespace PhotoFastRater.UI.Views;

public partial class PhotoViewerWindow : Window
{
    private readonly PhotoViewModel _viewModel;
    private readonly SocialMediaExporter _exporter;
    private readonly PhotoRatingEditor _ratingEditor;
    private readonly IImageDecodeService _decoder;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isDragging;
    private System.Windows.Point _dragStartPoint;

    public PhotoViewerWindow(PhotoViewModel viewModel, PhotoRatingEditor ratingEditor, IImageDecodeService decoder)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _ratingEditor = ratingEditor;
        _decoder = decoder;
        Closed += (_, _) => _lifetime.Cancel();
        DataContext = _viewModel;

        // SocialMediaExporter インスタンスを作成
        _exporter = new SocialMediaExporter();

        // プラットフォーム選択変更時に出力サイズを更新
        PlatformComboBox.SelectionChanged += PlatformComboBox_SelectionChanged;

        // フル解像度の画像を非同期で読み込み
        LoadFullImageAsync();

        // プレビューのEXIFテキストを更新
        UpdateExifPreviewText();
    }

    private async void LoadFullImageAsync()
    {
        try
        {
            var bytes = await _decoder.DecodeAsync(_viewModel.FilePath, 4096, _lifetime.Token);
            using var stream = new MemoryStream(bytes.ToArray());
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            _viewModel.FullImageSource = bitmap;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _viewModel.FullImageSource = _viewModel.Thumbnail;
            StatusText.Text = $"画像を読み込めません: {exception.Message}";
        }
    }

    private async void Rating_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string tag } && int.TryParse(tag, out var stars))
            await _ratingEditor.SetStarsAsync(_viewModel, stars);
    }

    private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (KeyboardInputPolicy.IsControlInput(e.OriginalSource as DependencyObject)) return;
        if (e.Key is Key.Escape or Key.Q) { Close(); e.Handled = true; return; }
        int stars = e.Key >= Key.D0 && e.Key <= Key.D5 ? e.Key - Key.D0
            : e.Key >= Key.NumPad0 && e.Key <= Key.NumPad5 ? e.Key - Key.NumPad0 : -1;
        if (stars >= 0) await _ratingEditor.SetStarsAsync(_viewModel, stars);
        else if (e.Key == Key.F) await _ratingEditor.ToggleFavoriteAsync(_viewModel);
        else if (e.Key == Key.X) await _ratingEditor.ToggleRejectedAsync(_viewModel);
        else return;
        e.Handled = true;
    }
    private void PlatformComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlatformComboBox.SelectedItem is ComboBoxItem item && item.Tag is string platformTag)
        {
            var sizeText = platformTag switch
            {
                "Instagram" => "1080 × 1080 px",
                "Twitter" => "1200 × 675 px",
                "Facebook" => "1200 × 630 px",
                _ => "カスタム"
            };
            OutputSizeText.Text = sizeText;
        }
    }

    private void OverlayPositionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAMLの初期化中はCustomPositionPanelがまだnullの可能性があるのでチェック
        if (CustomPositionPanel == null)
            return;

        if (OverlayPositionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string positionTag)
        {
            // カスタム位置が選択された時だけスライダーを表示
            CustomPositionPanel.Visibility = positionTag == "Custom"
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // エクスポート設定を構築
            var template = BuildExportTemplate();

            // 保存先ダイアログを表示
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JPEG画像|*.jpg",
                FileName = $"{Path.GetFileNameWithoutExtension(_viewModel.FileName)}_export.jpg",
                Title = "エクスポート先を選択"
            };

            if (saveDialog.ShowDialog() == true)
            {
                // エクスポート実行
                StatusText.Text = "エクスポート中...";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 193, 7)); // 黄色

                ExportButton.IsEnabled = false;

                var photo = _viewModel.GetModel();
                var exportedPath = await _exporter.ExportAsync(photo, template, saveDialog.FileName);

                StatusText.Text = $"完了: {exportedPath}";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(76, 175, 80)); // 緑色

                ExportButton.IsEnabled = true;

                // Keep the actual collision-free destination visible so users can find the output.
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"エラー: {ex.Message}";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(244, 67, 54)); // 赤色
            ExportButton.IsEnabled = true;
        }
    }

    private ExportTemplate BuildExportTemplate()
    {
        // プラットフォーム取得
        var platform = SocialMediaPlatform.Instagram;
        if (PlatformComboBox.SelectedItem is ComboBoxItem platformItem && platformItem.Tag is string platformTag)
        {
            platform = platformTag switch
            {
                "Instagram" => SocialMediaPlatform.Instagram,
                "Twitter" => SocialMediaPlatform.Twitter,
                "Facebook" => SocialMediaPlatform.Facebook,
                _ => SocialMediaPlatform.Instagram
            };
        }

        // オーバーレイ位置取得
        var position = ExifOverlayPosition.BottomLeft;
        if (OverlayPositionComboBox.SelectedItem is ComboBoxItem positionItem && positionItem.Tag is string positionTag)
        {
            position = positionTag switch
            {
                "TopLeft" => ExifOverlayPosition.TopLeft,
                "TopRight" => ExifOverlayPosition.TopRight,
                "BottomLeft" => ExifOverlayPosition.BottomLeft,
                "BottomRight" => ExifOverlayPosition.BottomRight,
                "Custom" => ExifOverlayPosition.Custom,
                _ => ExifOverlayPosition.BottomLeft
            };
        }

        // テンプレート作成
        var template = new ExportTemplate
        {
            Name = "PhotoViewer Export",
            TargetPlatform = platform,
            EnableFrame = EnableFrameCheckBox.IsChecked ?? false,
            FrameWidth = (int)FrameWidthSlider.Value,
            FrameColor = "#FFFFFF",
            EnableExifOverlay = EnableExifOverlayCheckBox.IsChecked ?? false,
            Position = position,
            CustomX = (int)CustomXSlider.Value,
            CustomY = (int)CustomYSlider.Value,
            MaintainAspectRatio = true
        };

        // プラットフォーム別の出力サイズ設定
        (template.OutputWidth, template.OutputHeight) = platform switch
        {
            SocialMediaPlatform.Instagram => (1080, 1080),
            SocialMediaPlatform.Twitter => (1200, 675),
            SocialMediaPlatform.Facebook => (1200, 630),
            _ => (1080, 1080)
        };

        return template;
    }

    private void UpdateExifPreviewText()
    {
        if (ExifPreviewText == null)
            return;

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(_viewModel.CameraModel))
            lines.Add(_viewModel.CameraModel);
        if (!string.IsNullOrEmpty(_viewModel.LensModel))
            lines.Add(_viewModel.LensModel);

        var exifLine = new List<string>();
        if (_viewModel.FocalLength.HasValue)
            exifLine.Add($"{_viewModel.FocalLength:F0}mm");
        if (_viewModel.Aperture.HasValue)
            exifLine.Add($"f/{_viewModel.Aperture:F1}");
        if (!string.IsNullOrEmpty(_viewModel.ShutterSpeed))
            exifLine.Add($"{_viewModel.ShutterSpeed}s");
        if (_viewModel.ISO.HasValue)
            exifLine.Add($"ISO{_viewModel.ISO}");

        if (exifLine.Count > 0)
            lines.Add(string.Join(" ", exifLine));

        ExifPreviewText.Text = lines.Count > 0 ? string.Join("\n", lines) : "EXIF情報なし";
    }

    private void UpdateExifPreviewPosition()
    {
        if (ExifPreviewBorder == null || PreviewGrid == null)
            return;

        var gridWidth = PreviewGrid.ActualWidth;
        var gridHeight = PreviewGrid.ActualHeight;

        if (gridWidth == 0 || gridHeight == 0)
            return;

        var x = gridWidth * CustomXSlider.Value / 100.0;
        var y = gridHeight * CustomYSlider.Value / 100.0;

        var margin = new Thickness(x, y, 0, 0);
        ExifPreviewBorder.Margin = margin;
    }

    private void UpdatePreview()
    {
        if (PreviewFrameBorder == null || ExifPreviewBorder == null)
            return;

        // 枠の表示/非表示と幅を更新
        var enableFrame = EnableFrameCheckBox?.IsChecked ?? false;
        if (enableFrame)
        {
            // プレビューサイズに合わせてスケールダウン（実際の枠幅の約1/6）
            var scaledFrameWidth = FrameWidthSlider.Value / 6.0;
            PreviewFrameBorder.BorderThickness = new Thickness(scaledFrameWidth);
            PreviewFrameBorder.BorderBrush = System.Windows.Media.Brushes.White;
        }
        else
        {
            PreviewFrameBorder.BorderThickness = new Thickness(0);
        }

        // EXIF オーバーレイの表示/非表示を更新
        var enableExifOverlay = EnableExifOverlayCheckBox?.IsChecked ?? false;
        ExifPreviewBorder.Visibility = enableExifOverlay ? Visibility.Visible : Visibility.Collapsed;

        // EXIF テキストと位置を更新
        if (enableExifOverlay)
        {
            UpdateExifPreviewText();
            UpdateExifPreviewPosition();
        }
    }

    private void PreviewSetting_Changed(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private void CustomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateExifPreviewPosition();
    }

    private void ExifPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStartPoint = e.GetPosition(PreviewGrid);
        ExifPreviewBorder.CaptureMouse();
    }

    private void ExifPreview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging)
            return;

        var currentPoint = e.GetPosition(PreviewGrid);
        var gridWidth = PreviewGrid.ActualWidth;
        var gridHeight = PreviewGrid.ActualHeight;

        if (gridWidth == 0 || gridHeight == 0)
            return;

        // パーセンテージに変換
        var xPercent = Math.Clamp(currentPoint.X / gridWidth * 100, 0, 100);
        var yPercent = Math.Clamp(currentPoint.Y / gridHeight * 100, 0, 100);

        // スライダーの値を更新（これがUpdateExifPreviewPositionを呼び出す）
        CustomXSlider.Value = (int)xPercent;
        CustomYSlider.Value = (int)yPercent;
    }

    private void ExifPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ExifPreviewBorder.ReleaseMouseCapture();
    }
}
