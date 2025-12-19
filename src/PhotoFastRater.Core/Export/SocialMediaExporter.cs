using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Export;

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
        // 元画像読み込み（フルサイズ）
        using var sourceImage = await Image.LoadAsync<Rgba32>(photo.FilePath);

        // 元画像のメタデータを保持
        var exifProfile = sourceImage.Metadata.ExifProfile?.DeepClone();
        var iptcProfile = sourceImage.Metadata.IptcProfile?.DeepClone();
        var xmpProfile = sourceImage.Metadata.XmpProfile?.DeepClone();
        var iccProfile = sourceImage.Metadata.IccProfile?.DeepClone();

        // リサイズ
        var resized = ResizeForPlatform(sourceImage, template);

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
        await withFrame.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = 95 });

        return outputPath;
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
