using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using PhotoFastRater.Core.Models;
using System.Text.Json;

namespace PhotoFastRater.Core.Export;

public class ExifOverlayRenderer
{
    public void RenderExifOverlay(Image<Rgba32> image, Photo photo, ExportTemplate template)
    {
        if (!template.EnableExifOverlay)
            return;

        var exifFields = DeserializeExifFields(template.DisplayFields);
        var exifText = BuildExifText(photo, exifFields);

        if (string.IsNullOrWhiteSpace(exifText))
            return;

        try
        {
            var font = SystemFonts.CreateFont(template.FontFamily, template.FontSize);
            var textColor = ParseColor(template.TextColor);
            var bgColor = ParseColor(template.BackgroundColor);

            image.Mutate(ctx =>
            {
                var position = CalculatePosition(image.Size, template, font, exifText);

                // テキストサイズを測定
                var textOptions = new TextOptions(font);
                var textSize = TextMeasurer.MeasureSize(exifText, textOptions);

                // 背景を描画
                var padding = 10f;
                var bgRect = new RectangleF(
                    position.X - padding,
                    position.Y - padding,
                    textSize.Width + padding * 2,
                    textSize.Height + padding * 2);

                var rgba32 = bgColor.ToPixel<Rgba32>();
                rgba32.A = (byte)(255 * template.BackgroundOpacity / 100);
                var bgColorWithAlpha = Color.FromPixel(rgba32);

                ctx.Fill(bgColorWithAlpha, bgRect);

                // テキストを描画
                var drawingOptions = new DrawingOptions();
                var richTextOptions = new RichTextOptions(font)
                {
                    Origin = position,
                    WrappingLength = -1,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                };

                ctx.DrawText(richTextOptions, exifText, textColor);
            });
        }
        catch
        {
            // フォント読み込み失敗などは無視
        }
    }

    private static List<ExifField> DeserializeExifFields(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ExifField>
            {
                ExifField.CameraModel,
                ExifField.LensModel,
                ExifField.FocalLength,
                ExifField.Aperture,
                ExifField.ShutterSpeed,
                ExifField.ISO
            };
        }

        try
        {
            return JsonSerializer.Deserialize<List<ExifField>>(json) ?? new List<ExifField>();
        }
        catch
        {
            return new List<ExifField>();
        }
    }

    private static string BuildExifText(Photo photo, List<ExifField> fields)
    {
        var lines = new List<string>();

        foreach (var field in fields)
        {
            switch (field)
            {
                case ExifField.CameraModel:
                    if (!string.IsNullOrEmpty(photo.CameraModel))
                        lines.Add($"{photo.CameraModel}");
                    break;
                case ExifField.LensModel:
                    if (!string.IsNullOrEmpty(photo.LensModel))
                        lines.Add($"{photo.LensModel}");
                    break;
                case ExifField.FocalLength:
                    if (photo.FocalLength.HasValue)
                        lines.Add($"{photo.FocalLength:F0}mm");
                    break;
                case ExifField.Aperture:
                    if (photo.Aperture.HasValue)
                        lines.Add($"f/{photo.Aperture:F1}");
                    break;
                case ExifField.ShutterSpeed:
                    if (!string.IsNullOrEmpty(photo.ShutterSpeed))
                        lines.Add($"{photo.ShutterSpeed}s");
                    break;
                case ExifField.ISO:
                    if (photo.ISO.HasValue)
                        lines.Add($"ISO {photo.ISO}");
                    break;
                case ExifField.DateTaken:
                    lines.Add($"{photo.DateTaken:yyyy/MM/dd}");
                    break;
                case ExifField.Location:
                    if (!string.IsNullOrEmpty(photo.LocationName))
                        lines.Add($"{photo.LocationName}");
                    break;
            }
        }

        return string.Join("\n", lines);
    }

    private static PointF CalculatePosition(Size imageSize, ExportTemplate template, Font font, string text)
    {
        var textOptions = new TextOptions(font);
        var textSize = TextMeasurer.MeasureSize(text, textOptions);
        var padding = 20f;

        return template.Position switch
        {
            ExifOverlayPosition.TopLeft => new PointF(padding, padding),
            ExifOverlayPosition.TopRight => new PointF(imageSize.Width - textSize.Width - padding, padding),
            ExifOverlayPosition.BottomLeft => new PointF(padding, imageSize.Height - textSize.Height - padding),
            ExifOverlayPosition.BottomRight => new PointF(
                imageSize.Width - textSize.Width - padding,
                imageSize.Height - textSize.Height - padding),
            ExifOverlayPosition.Custom => new PointF(
                imageSize.Width * template.CustomX / 100f,
                imageSize.Height * template.CustomY / 100f),
            _ => new PointF(padding, imageSize.Height - textSize.Height - padding)
        };
    }

    private static Color ParseColor(string hexColor)
    {
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
