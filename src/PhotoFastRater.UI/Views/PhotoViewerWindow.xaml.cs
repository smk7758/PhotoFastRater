using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PhotoFastRater.Core.Export;
using PhotoFastRater.Core.Models;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Views;

public partial class PhotoViewerWindow : Window
{
    private readonly PhotoViewModel _viewModel;
    private readonly SocialMediaExporter _exporter;

    public PhotoViewerWindow(PhotoViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        // SocialMediaExporter インスタンスを作成
        _exporter = new SocialMediaExporter();

        // プラットフォーム選択変更時に出力サイズを更新
        PlatformComboBox.SelectionChanged += PlatformComboBox_SelectionChanged;

        // フル解像度の画像を非同期で読み込み
        LoadFullImageAsync();
    }

    private async void LoadFullImageAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(_viewModel.FilePath);
                bitmap.EndInit();
                bitmap.Freeze();

                Dispatcher.Invoke(() =>
                {
                    _viewModel.FullImageSource = bitmap;
                });
            });
        }
        catch
        {
            // エラー時はサムネイルを使用
            _viewModel.FullImageSource = _viewModel.Thumbnail;
        }
    }

    private void Rating_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string tag)
        {
            if (int.TryParse(tag, out int rating))
            {
                _viewModel.Rating = rating;
            }
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Q:
                Close();
                break;
            case Key.D1:
            case Key.NumPad1:
                _viewModel.Rating = 1;
                break;
            case Key.D2:
            case Key.NumPad2:
                _viewModel.Rating = 2;
                break;
            case Key.D3:
            case Key.NumPad3:
                _viewModel.Rating = 3;
                break;
            case Key.D4:
            case Key.NumPad4:
                _viewModel.Rating = 4;
                break;
            case Key.D5:
            case Key.NumPad5:
                _viewModel.Rating = 5;
                break;
        }
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
                await _exporter.ExportAsync(photo, template, saveDialog.FileName);

                StatusText.Text = "エクスポート完了!";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(76, 175, 80)); // 緑色

                ExportButton.IsEnabled = true;

                // 3秒後にステータスをクリア
                await Task.Delay(3000);
                StatusText.Text = "";
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
}
