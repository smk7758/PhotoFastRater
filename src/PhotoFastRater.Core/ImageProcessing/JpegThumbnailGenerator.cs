using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace PhotoFastRater.Core.ImageProcessing;

public class JpegThumbnailGenerator : IThumbnailGenerator
{
    private readonly int _jpegQuality;

    public JpegThumbnailGenerator(int jpegQuality = 85)
    {
        _jpegQuality = jpegQuality;
    }

    public async Task<byte[]> GenerateAsync(string filePath, int targetSize)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var image = Image.Load(filePath);

                var size = CalculateSize(image.Size, targetSize);

                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = size,
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3
                }));

                using var ms = new MemoryStream();
                image.SaveAsJpeg(ms, new JpegEncoder { Quality = _jpegQuality });
                return ms.ToArray();
            }
            catch (SixLabors.ImageSharp.UnknownImageFormatException ex)
            {
                throw new InvalidOperationException($"Unknown image format for file: {filePath}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to generate thumbnail for file: {filePath}", ex);
            }
        });
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
