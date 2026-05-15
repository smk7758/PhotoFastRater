using System.Windows.Media.Imaging;

namespace PhotoFastRater.UI.Services;

/// <summary>
/// JPEGファイルのレーティングをEXIF/XMPメタデータとして書き戻すサービス。
/// InPlaceBitmapMetadataWriterで非破壊書き込みを試み、失敗した場合のみ再エンコードする。
/// </summary>
public class ExifWriteService
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };

    public async Task WriteRatingAsync(string filePath, int rating)
    {
        if (!SupportedExtensions.Contains(Path.GetExtension(filePath))) return;
        await Task.Run(() => WriteRatingCore(filePath, rating));
    }

    private void WriteRatingCore(string filePath, int rating)
    {
        try
        {
            if (TryWriteInPlace(filePath, rating)) return;
            ReencodeWithRating(filePath, rating);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExifWriteService] {filePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// インプレースメタデータ書き込みを試みる（画像データ無変換）。
    /// </summary>
    private static bool TryWriteInPlace(string filePath, int rating)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.Default);
            var writer = decoder.Frames[0].CreateInPlaceBitmapMetadataWriter();
            if (writer == null) return false;

            // BitmapMetadata.Rating は0〜5の星数。Windowsエクスプローラーが読む。
            writer.Rating = rating;

            // XMP rating も書き込む（Lightroom / Capture One 互換）
            try { writer.SetQuery("/xmp/xmp:Rating", rating.ToString()); } catch { }

            // EXIF tag 0x4746 (Windows Photo Viewer 星数マッピング値)
            try { writer.SetQuery("/app1/ifd/{ushort=18246}", (ushort)MapToExifRating(rating)); } catch { }

            return writer.TrySave();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 再エンコードでメタデータを書き込む（JPEG品質95%で再圧縮）。
    /// </summary>
    private static void ReencodeWithRating(string filePath, int rating)
    {
        BitmapFrame frame;
        BitmapMetadata metadata;

        using (var stream = File.OpenRead(filePath))
        {
            var decoder = BitmapDecoder.Create(
                stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
            metadata = (frame.Metadata?.Clone() as BitmapMetadata) ?? new BitmapMetadata("jpg");
        }

        metadata.Rating = rating;
        try { metadata.SetQuery("/xmp/xmp:Rating", rating.ToString()); } catch { }
        try { metadata.SetQuery("/app1/ifd/{ushort=18246}", (ushort)MapToExifRating(rating)); } catch { }

        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(frame, null, metadata, null));

        var tempPath = filePath + ".rating-tmp";
        try
        {
            using (var outStream = File.OpenWrite(tempPath))
                encoder.Save(outStream);
            File.Replace(tempPath, filePath, null);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// 星数(0〜5) → Windowsエクスプローラー用EXIF値 (0/1/25/50/75/99)
    /// </summary>
    private static int MapToExifRating(int stars) => stars switch
    {
        1 => 1,
        2 => 25,
        3 => 50,
        4 => 75,
        5 => 99,
        _ => 0
    };
}
