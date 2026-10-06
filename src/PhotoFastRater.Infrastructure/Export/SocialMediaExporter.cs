using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Core.Export;

namespace PhotoFastRater.Infrastructure.Export;

public class SocialMediaExporter : IImageExporter
{
    private readonly FrameRenderer _frameRenderer;
    private readonly ExifOverlayRenderer _exifRenderer;

    public SocialMediaExporter()
    {
        _frameRenderer = new FrameRenderer();
        _exifRenderer = new ExifOverlayRenderer();
    }

    public async Task<string> ExportAsync(Photo photo, ExportTemplate template, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(template);
        outputPath = Path.GetFullPath(outputPath);
        if (string.Equals(Path.GetFullPath(photo.FilePath), outputPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("元画像と異なる出力先を選択してください。", nameof(outputPath));
        // 元画像読み込み（フルサイズ）
        using var sourceImage = await Image.LoadAsync<Rgba32>(photo.FilePath);
        sourceImage.Mutate(context => context.AutoOrient());

        // 元画像のメタデータを保持
        var exifProfile = sourceImage.Metadata.ExifProfile?.DeepClone();
        var iptcProfile = sourceImage.Metadata.IptcProfile?.DeepClone();
        var xmpProfile = sourceImage.Metadata.XmpProfile?.DeepClone();
        var iccProfile = sourceImage.Metadata.IccProfile?.DeepClone();

        // リサイズ
        using var resized = ResizeForPlatform(sourceImage, template);

        // 枠追加
        using var withFrame = _frameRenderer.AddFrame(resized, template);

        // EXIF オーバーレイ
        _exifRenderer.RenderExifOverlay(withFrame, photo, template);

        // メタデータを復元
        if (exifProfile != null)
            withFrame.Metadata.ExifProfile = exifProfile;
        if (iptcProfile != null)
            withFrame.Metadata.IptcProfile = iptcProfile;
        if (xmpProfile != null)
            withFrame.Metadata.XmpProfile = xmpProfile;
        if (iccProfile != null)
            withFrame.Metadata.IccProfile = iccProfile;

        // 保存
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporary = outputPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                await withFrame.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 95 });
            for (var suffix = 0; ; suffix++)
            {
                var destination = suffix == 0 ? outputPath : Path.Combine(Path.GetDirectoryName(outputPath)!,
                    $"{Path.GetFileNameWithoutExtension(outputPath)}-{suffix}{Path.GetExtension(outputPath)}");
                try { File.Move(temporary, destination); return destination; }
                catch (IOException) when (File.Exists(destination)) { }
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                var garbage = Path.Combine(Path.GetDirectoryName(outputPath)!, "_GARBAGE"); Directory.CreateDirectory(garbage);
                File.Move(temporary, Path.Combine(garbage, Path.GetFileName(temporary)));
            }
        }
    }

    private static Image<Rgba32> ResizeForPlatform(Image<Rgba32> source, ExportTemplate template)
    {
        Size targetSize = template.TargetPlatform switch
        {
            SocialMediaPlatform.Instagram => new Size(1080, 1080),  // スクエア
            SocialMediaPlatform.Twitter => new Size(1200, 675),     // 16:9
            SocialMediaPlatform.Facebook => new Size(1200, 630),    // OGP
            _ => new Size(template.OutputWidth, template.OutputHeight)
        };

        var result = source.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = targetSize,
            Mode = template.MaintainAspectRatio ? ResizeMode.Max : ResizeMode.Stretch,
            Sampler = KnownResamplers.Lanczos3
        }));

        return result;
    }
}
