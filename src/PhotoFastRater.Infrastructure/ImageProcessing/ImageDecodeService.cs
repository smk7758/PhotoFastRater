using PhotoFastRater.Core.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace PhotoFastRater.Infrastructure.ImageProcessing;

/// <summary>Decodes comparison images to a bounded display size instead of retaining source-resolution pixels.</summary>
public sealed class ImageDecodeService(RawThumbnailGenerator rawGenerator) : IImageDecodeService
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".nef", ".arw", ".rw2", ".orf", ".raf", ".dng",
        ".pef", ".srw", ".x3f", ".3fr", ".mef", ".mrw", ".nrw", ".rwl"
    };

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>> DecodeAsync(
        string filePath,
        int maximumDimension,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (maximumDimension is < 256 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("比較対象の写真が見つかりません。", filePath);

        if (RawExtensions.Contains(Path.GetExtension(filePath)))
            return await rawGenerator.GenerateAsync(filePath, maximumDimension);

        using var image = await Image.LoadAsync(filePath, cancellationToken);
        image.Mutate(context =>
        {
            context.AutoOrient();
            if (image.Width > maximumDimension || image.Height > maximumDimension)
            {
                context.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maximumDimension, maximumDimension),
                    Sampler = KnownResamplers.Lanczos3
                });
            }
        });
        await using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 90 }, cancellationToken);
        return output.ToArray();
    }
}
