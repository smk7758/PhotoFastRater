using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using PhotoFastRater.Core.ImageProcessing;

namespace PhotoFastRater.Infrastructure.ImageProcessing;

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

    /// <summary>Produces a bounded preview from the largest decodable embedded JPEG, never from RAW sensor data.</summary>
    public Task<byte[]> GenerateAsync(string filePath, int targetSize) => Task.Run(() =>
    {
        if (targetSize <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));
        var bytes = FindDecodableEmbeddedJpeg(filePath);
        return bytes.Length == 0 ? bytes : ResizeThumbnail(bytes, targetSize, ReadRawOrientation(filePath));
    });

    /// <summary>Returns a display JPEG, accepting small valid previews when a larger JPEG is absent or unsupported.</summary>
    public Task<byte[]> ExtractEmbeddedJpegBytesAsync(string filePath) => Task.Run(() =>
    {
        var bytes = FindDecodableEmbeddedJpeg(filePath);
        if (bytes.Length == 0) return bytes;
        using var image = Image.Load(bytes);
        ApplyOrientation(image, bytes, ReadRawOrientation(filePath));
        using var output = new MemoryStream();
        image.SaveAsJpeg(output, new JpegEncoder { Quality = _jpegQuality });
        return output.ToArray();
    });

    private static int ReadRawOrientation(string filePath)
    {
        try
        {
            var directory = ImageMetadataReader.ReadMetadata(filePath).OfType<ExifIfd0Directory>().FirstOrDefault();
            return directory?.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation) == true ? orientation : 1;
        }
        catch (Exception exception) when (exception is IOException or MetadataExtractor.ImageProcessingException) { return 1; }
    }

    private static byte[] FindDecodableEmbeddedJpeg(string filePath)
    {
        const int maximumScanBytes = 20 * 1024 * 1024;
        const long maximumPreviewPixels = 100_000_000;
        try
        {
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[(int)Math.Min(stream.Length, maximumScanBytes)];
            stream.ReadExactly(buffer);
            byte[] best = [];
            long bestPixels = 0;
            for (var index = 0; index < buffer.Length - 3; index++)
            {
                if (buffer[index] != 0xFF || buffer[index + 1] != 0xD8 || buffer[index + 2] != 0xFF) continue;
                var end = FindJpegEnd(buffer, index, buffer.Length);
                if (end <= index + 4) continue;
                var candidate = buffer[index..end];
                try
                {
                    var info = Image.Identify(candidate);
                    long pixels = (long)info.Width * info.Height;
                    if (pixels <= bestPixels || pixels > maximumPreviewPixels) continue;
                    // RAW sensor payloads may also have JPEG markers (e.g. lossless JPEG).
                    // A successful decode, rather than byte length, determines display support.
                    using var decoded = Image.Load(candidate);
                    best = candidate;
                    bestPixels = pixels;
                }
                catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or NotSupportedException) { }
            }
            return best;
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
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
