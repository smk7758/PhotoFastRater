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
                            return ResizeThumbnail(buffer, targetSize);
                        }
                    }
                }

                // 埋め込みサムネイルがない場合は空の配列を返す
                // （フォールバック処理は呼び出し側で実装）
                return Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                // エラーログ（実装されている場合）
                System.Diagnostics.Debug.WriteLine($"RAW thumbnail extraction failed for {filePath}: {ex.Message}");
                return Array.Empty<byte>();
            }
        });
    }

    private byte[] ResizeThumbnail(byte[] thumbnailData, int targetSize)
    {
        try
        {
            using var ms = new MemoryStream(thumbnailData);
            using var image = Image.Load(ms);

            var size = CalculateSize(image.Size, targetSize);

            // 既にターゲットサイズ以下の場合はリサイズ不要
            if (size == image.Size)
            {
                return thumbnailData;
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
