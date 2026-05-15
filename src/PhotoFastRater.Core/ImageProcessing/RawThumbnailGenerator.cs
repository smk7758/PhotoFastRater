using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace PhotoFastRater.Core.ImageProcessing;

/// <summary>
/// RAWファイルから埋め込みJPEGサムネイルを抽出するサムネイルジェネレーター
/// </summary>
public class RawThumbnailGenerator : IThumbnailGenerator
{
    private readonly int _jpegQuality;

    public RawThumbnailGenerator(int jpegQuality = 85)
    {
        _jpegQuality = jpegQuality;
    }

    public async Task<byte[]> GenerateAsync(string filePath, int targetSize)
    {
        return await Task.Run(() =>
        {
            try
            {
                // RAWファイルからメタデータを読み取る
                var directories = ImageMetadataReader.ReadMetadata(filePath);

                // RAWファイル本体のIFD0 orientation を取得（埋め込みJPEGのfallback用）
                int rawOrientation = 1;
                var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                ifd0?.TryGetInt32(ExifDirectoryBase.TagOrientation, out rawOrientation);

                // ExifThumbnailDirectoryから埋め込みJPEGサムネイルを取得
                var thumbnailDirectory = directories.OfType<ExifThumbnailDirectory>().FirstOrDefault();

                if (thumbnailDirectory != null)
                {
                    // オフセットとレングスから直接サムネイルを読み取る
                    if (thumbnailDirectory.TryGetInt32(ExifThumbnailDirectory.TagThumbnailOffset, out var offset) &&
                        thumbnailDirectory.TryGetInt32(ExifThumbnailDirectory.TagThumbnailLength, out var length) &&
                        length > 0)
                    {
                        // ファイルから直接サムネイル部分を読み取る
                        using var fileStream = File.OpenRead(filePath);
                        fileStream.Seek(offset, SeekOrigin.Begin);
                        var buffer = new byte[length];
                        var bytesRead = fileStream.Read(buffer, 0, length);

                        if (bytesRead == length)
                        {
                            return ResizeThumbnail(buffer, targetSize, rawOrientation);
                        }
                    }
                }

                // ExifThumbnailDirectory で見つからない場合はバイトスキャンで埋め込みJPEGを探す
                return FindEmbeddedJpeg(filePath, targetSize, rawOrientation);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RAW thumbnail extraction failed for {filePath}: {ex.Message}");
                return Array.Empty<byte>();
            }
        });
    }

    /// <summary>
    /// RAW ファイルから最大の埋込み JPEG をリサイズなしで抽出する（原画像モード用）
    /// </summary>
    public async Task<byte[]> ExtractEmbeddedJpegBytesAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                // RAWファイル本体のIFD0 orientation を取得
                int rawOrientation = 1;
                try
                {
                    var dirs = ImageMetadataReader.ReadMetadata(filePath);
                    var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
                    ifd0?.TryGetInt32(ExifDirectoryBase.TagOrientation, out rawOrientation);
                }
                catch { }

                const int maxScanBytes = 20 * 1024 * 1024;
                using var fs = File.OpenRead(filePath);
                int readLen = (int)Math.Min(fs.Length, maxScanBytes);
                var buf = new byte[readLen];
                readLen = fs.Read(buf, 0, readLen);

                byte[]? best = null;
                int i = 0;
                while (i < readLen - 3)
                {
                    if (buf[i] == 0xFF && buf[i + 1] == 0xD8 && buf[i + 2] == 0xFF)
                    {
                        int end = FindJpegEnd(buf, i, readLen);
                        int length = end - i;
                        // 50KB 以上の JPEG のみ対象（小さい EXIF サムネイルを除外）
                        if (length > 50_000 && (best == null || length > best.Length))
                            best = buf[i..end];
                        i = end > i + 1 ? end : i + 1;
                    }
                    else i++;
                }

                if (best != null)
                {
                    try
                    {
                        using var ms = new MemoryStream(best);
                        using var image = Image.Load(ms);
                        ApplyOrientation(image, best, rawOrientation);
                        using var outMs = new MemoryStream();
                        image.SaveAsJpeg(outMs, new JpegEncoder { Quality = 92 });
                        return outMs.ToArray();
                    }
                    catch { return best; }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RAW full JPEG extract failed: {filePath}: {ex.Message}");
            }
            return Array.Empty<byte>();
        });
    }

    private byte[] FindEmbeddedJpeg(string filePath, int targetSize, int rawOrientation = 1)
    {
        try
        {
            const int maxScanBytes = 20 * 1024 * 1024; // 20MB 上限
            using var fs = File.OpenRead(filePath);
            int readLen = (int)Math.Min(fs.Length, maxScanBytes);
            var buf = new byte[readLen];
            int bytesRead = fs.Read(buf, 0, readLen);
            readLen = bytesRead;

            byte[]? best = null;
            int i = 0;
            while (i < readLen - 3)
            {
                if (buf[i] == 0xFF && buf[i + 1] == 0xD8 && buf[i + 2] == 0xFF)
                {
                    int end = FindJpegEnd(buf, i, readLen);
                    int length = end - i;
                    // 2KB 以上の JPEG のみ対象（小さい EXIF サムネイルは除外）
                    if (length > 2048 && (best == null || length > best.Length))
                        best = buf[i..end];
                    i = end > i + 1 ? end : i + 1;
                }
                else
                {
                    i++;
                }
            }

            if (best != null)
                return ResizeThumbnail(best, targetSize, rawOrientation);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RAW embedded JPEG scan failed for {filePath}: {ex.Message}");
        }
        return Array.Empty<byte>();
    }

    private static int FindJpegEnd(byte[] buf, int start, int limit)
    {
        for (int i = start + 2; i < limit - 1; i++)
        {
            if (buf[i] == 0xFF && buf[i + 1] == 0xD9)
                return i + 2;
        }
        return limit;
    }

    /// <summary>
    /// 埋め込みJPEGの向きを補正する。
    /// JPEGバイト列自身にorientation情報があればAutoOrient、なければRAWファイルのIFD0 orientationを適用。
    /// </summary>
    private static void ApplyOrientation(Image image, byte[] jpegBytes, int rawOrientation)
    {
        // MetadataExtractor でJPEGバイト列のorientation を読む
        int jpegOrientation = 1;
        try
        {
            using var ms = new MemoryStream(jpegBytes);
            var dirs = ImageMetadataReader.ReadMetadata(ms);
            dirs.OfType<ExifIfd0Directory>().FirstOrDefault()
                ?.TryGetInt32(ExifDirectoryBase.TagOrientation, out jpegOrientation);
        }
        catch { }

        if (jpegOrientation != 1)
        {
            // 埋め込みJPEG自身のorientation情報を使う
            image.Mutate(x => x.AutoOrient());
        }
        else if (rawOrientation != 1)
        {
            // 埋め込みJPEGにorientationがないのでRAW IFD0のorientationを適用
            var rot = rawOrientation switch
            {
                6 => RotateMode.Rotate90,
                3 => RotateMode.Rotate180,
                8 => RotateMode.Rotate270,
                _ => RotateMode.None
            };
            if (rot != RotateMode.None)
                image.Mutate(x => x.Rotate(rot));
        }
    }

    private byte[] ResizeThumbnail(byte[] thumbnailData, int targetSize, int rawOrientation = 1)
    {
        try
        {
            using var ms = new MemoryStream(thumbnailData);
            using var image = Image.Load(ms);

            ApplyOrientation(image, thumbnailData, rawOrientation);

            var size = CalculateSize(image.Size, targetSize);

            // 既にターゲットサイズ以下の場合はリサイズ不要
            if (size == image.Size)
            {
                using var outputMs2 = new MemoryStream();
                image.SaveAsJpeg(outputMs2, new JpegEncoder { Quality = _jpegQuality });
                return outputMs2.ToArray();
            }

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = size,
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3
            }));

            using var outputMs = new MemoryStream();
            image.SaveAsJpeg(outputMs, new JpegEncoder { Quality = _jpegQuality });
            return outputMs.ToArray();
        }
        catch
        {
            // リサイズ失敗時は元のサムネイルを返す
            return thumbnailData;
        }
    }

    private static Size CalculateSize(Size originalSize, int targetSize)
    {
        if (originalSize.Width <= targetSize && originalSize.Height <= targetSize)
            return originalSize;

        var ratio = Math.Min(
            (double)targetSize / originalSize.Width,
            (double)targetSize / originalSize.Height);

        return new Size(
            (int)(originalSize.Width * ratio),
            (int)(originalSize.Height * ratio));
    }
}
