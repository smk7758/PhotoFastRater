using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Views;

public partial class PhotoViewerWindow : Window
{
    private readonly PhotoViewModel _viewModel;

    public PhotoViewerWindow(PhotoViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

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
}
