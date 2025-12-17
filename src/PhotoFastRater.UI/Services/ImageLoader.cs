using System.Threading.Channels;
using System.Windows.Media.Imaging;
using PhotoFastRater.Core.Cache;

namespace PhotoFastRater.UI.Services;

public class ImageLoader
{
    private readonly ThumbnailCacheManager _cacheManager;
    private readonly Channel<LoadRequest> _loadQueue;
    private readonly int _maxParallelLoads = 6;

    public ImageLoader(ThumbnailCacheManager cacheManager)
    {
        _cacheManager = cacheManager;
        _loadQueue = Channel.CreateUnbounded<LoadRequest>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        // 並列ワーカー起動
        for (int i = 0; i < _maxParallelLoads; i++)
        {
            _ = Task.Run(ProcessLoadQueueAsync);
        }
    }

    public Task<BitmapImage?> LoadAsync(string filePath, int priority = 0)
    {
        var tcs = new TaskCompletionSource<BitmapImage?>();
        var request = new LoadRequest
        {
            FilePath = filePath,
            Priority = priority,
            CompletionSource = tcs
        };

        _loadQueue.Writer.TryWrite(request);
        return tcs.Task;
    }

    // プリフェッチ: 次に表示される可能性の高い画像を先読み
    public void PrefetchRange(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _ = LoadAsync(path, priority: -1);  // 低優先度
        }
    }

    private async Task ProcessLoadQueueAsync()
    {
        await foreach (var request in _loadQueue.Reader.ReadAllAsync())
        {
            try
            {
                var thumbnail = await _cacheManager.GetThumbnailAsync(request.FilePath);
                if (thumbnail != null && thumbnail.Length > 0)
                {
                    var imageSource = ConvertToImageSource(thumbnail);
                    request.CompletionSource.SetResult(imageSource);
                }
                else
                {
                    request.CompletionSource.SetResult(null);
                }
            }
            catch (ArgumentException ex)
            {
                // Image data is null or empty
                System.Diagnostics.Debug.WriteLine($"サムネイル読み込みエラー (空データ): {request.FilePath}");
                request.CompletionSource.SetException(new InvalidOperationException($"サムネイル読み込みエラー: {request.FilePath}", ex));
            }
            catch (NotSupportedException ex)
            {
                // WPF BitmapImage cannot decode the image format
                System.Diagnostics.Debug.WriteLine($"サムネイル読み込みエラー (未対応形式): {request.FilePath}");
                request.CompletionSource.SetException(new InvalidOperationException($"サムネイル読み込みエラー (未対応形式): {request.FilePath}", ex));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"サムネイル読み込みエラー: {request.FilePath} - {ex.Message}");
                request.CompletionSource.SetException(new InvalidOperationException($"サムネイル読み込みエラー: {request.FilePath}", ex));
            }
        }
    }

    private static BitmapImage ConvertToImageSource(byte[] imageData)
    {
        if (imageData == null || imageData.Length == 0)
        {
            throw new ArgumentException("Image data is null or empty", nameof(imageData));
        }

        try
        {
            var bitmap = new BitmapImage();
            using (var ms = new MemoryStream(imageData))
            {
                ms.Position = 0;
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
            }
            bitmap.Freeze(); // UI スレッド以外で使用可能にする
            return bitmap;
        }
        catch (NotSupportedException ex)
        {
            System.Diagnostics.Debug.WriteLine($"BitmapImage作成エラー (NotSupportedException): データサイズ={imageData.Length}bytes, エラー={ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BitmapImage作成エラー: データサイズ={imageData.Length}bytes, エラー={ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private class LoadRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public int Priority { get; set; }
        public TaskCompletionSource<BitmapImage?> CompletionSource { get; set; } = null!;
    }
}
