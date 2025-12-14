using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Export;

public class FrameRenderer
{
    public Image<Rgba32> AddFrame(Image<Rgba32> source, ExportTemplate template)
    {
        if (!template.EnableFrame || template.FrameWidth <= 0)
            return source;

        var frameWidth = template.FrameWidth;
        var newWidth = source.Width + frameWidth * 2;
        var newHeight = source.Height + frameWidth * 2;

        var result = new Image<Rgba32>(newWidth, newHeight);
        var frameColor = ParseColor(template.FrameColor);

        result.Mutate(ctx =>
        {
            // 枠を塗りつぶし
            ctx.Fill(frameColor);

            // 元画像を中央に配置
            ctx.DrawImage(source, new Point(frameWidth, frameWidth), 1f);
        });

        return result;
    }

    private static Color ParseColor(string hexColor)
    {
        // #FFFFFF 形式のカラーコードをパース
        if (hexColor.StartsWith("#"))
        {
            hexColor = hexColor.Substring(1);
        }

        if (hexColor.Length == 6)
        {
            var r = Convert.ToByte(hexColor.Substring(0, 2), 16);
            var g = Convert.ToByte(hexColor.Substring(2, 2), 16);
            var b = Convert.ToByte(hexColor.Substring(4, 2), 16);
            return Color.FromRgb(r, g, b);
        }

        return Color.White;
    }
}
