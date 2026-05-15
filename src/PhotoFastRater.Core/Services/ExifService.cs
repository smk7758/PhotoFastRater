using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Xmp;
using PhotoFastRater.Core.Models;
using GeoLocation = MetadataExtractor.GeoLocation;

namespace PhotoFastRater.Core.Services;

public class ExifService
{
    // XMP namespace URIs
    private const string XmpNsAdobe = "http://ns.adobe.com/xap/1.0/";
    private const string XmpNsMicrosoft = "http://ns.microsoft.com/photo/1.0/";

    // Windows Photo Viewer の EXIF レーティングタグ (0x4746)
    private const int TagWindowsRating = 0x4746;

    public Photo ExtractExifData(string filePath)
    {
        var directoryPath = Path.GetDirectoryName(filePath);
        var photo = new Photo
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FolderPath = directoryPath ?? string.Empty,
            FolderName = !string.IsNullOrEmpty(directoryPath) ? Path.GetFileName(directoryPath) : string.Empty,
            FileSize = new FileInfo(filePath).Length,
            ImportDate = DateTime.Now
        };

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // EXIF情報取得
            var exifSubIfdDir = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var exifIfd0Dir = directories.OfType<ExifIfd0Directory>().FirstOrDefault();

            if (exifSubIfdDir != null)
            {
                if (exifSubIfdDir.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTaken))
                    photo.DateTaken = dateTaken;

                if (exifSubIfdDir.TryGetDouble(ExifDirectoryBase.TagFNumber, out var aperture))
                    photo.Aperture = aperture;

                if (exifSubIfdDir.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
                    photo.ISO = iso;

                if (exifSubIfdDir.TryGetDouble(ExifDirectoryBase.TagFocalLength, out var focalLength))
                    photo.FocalLength = focalLength;

                if (exifSubIfdDir.TryGetDouble(ExifDirectoryBase.TagExposureBias, out var exposureComp))
                    photo.ExposureCompensation = exposureComp;

                var shutterSpeed = exifSubIfdDir.GetDescription(ExifDirectoryBase.TagExposureTime);
                if (shutterSpeed != null)
                    photo.ShutterSpeed = shutterSpeed;

            }

            if (exifIfd0Dir != null)
            {
                photo.CameraMake = exifIfd0Dir.GetDescription(ExifDirectoryBase.TagMake);
                photo.CameraModel = exifIfd0Dir.GetDescription(ExifDirectoryBase.TagModel);
            }

            // IFD0 フォールバック: 一部 RAW フォーマットで SubIFD に撮影設定が入らない場合がある
            if (exifIfd0Dir != null)
            {
                if (photo.Aperture == null && exifIfd0Dir.TryGetDouble(ExifDirectoryBase.TagFNumber, out var ap)) photo.Aperture = ap;
                if (photo.ISO == null && exifIfd0Dir.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso)) photo.ISO = iso;
                if (photo.FocalLength == null && exifIfd0Dir.TryGetDouble(ExifDirectoryBase.TagFocalLength, out var fl)) photo.FocalLength = fl;
                if (photo.ExposureCompensation == null && exifIfd0Dir.TryGetDouble(ExifDirectoryBase.TagExposureBias, out var ec)) photo.ExposureCompensation = ec;
                if (photo.ShutterSpeed == null) photo.ShutterSpeed = exifIfd0Dir.GetDescription(ExifDirectoryBase.TagExposureTime);
                if (photo.LensModel == null) photo.LensModel = exifIfd0Dir.GetDescription(ExifDirectoryBase.TagLensModel);
            }

            // 解像度: 優先度1=JpegDirectory(SOFヘッダー), 2=IFD0, 3=SubIFD
            var jpegDir = directories.OfType<JpegDirectory>().FirstOrDefault();
            if (jpegDir != null)
            {
                if (jpegDir.TryGetInt32(JpegDirectory.TagImageWidth, out var jw) && jw > 0) photo.Width = jw;
                if (jpegDir.TryGetInt32(JpegDirectory.TagImageHeight, out var jh) && jh > 0) photo.Height = jh;
            }
            if (photo.Width == 0 && exifIfd0Dir != null)
            {
                if (exifIfd0Dir.TryGetInt32(ExifDirectoryBase.TagImageWidth, out var iw) && iw > 0) photo.Width = iw;
                if (exifIfd0Dir.TryGetInt32(ExifDirectoryBase.TagImageHeight, out var ih) && ih > 0) photo.Height = ih;
            }
            if (photo.Width == 0 && exifSubIfdDir != null)
            {
                if (exifSubIfdDir.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var sw) && sw > 0) photo.Width = sw;
                if (exifSubIfdDir.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var sh) && sh > 0) photo.Height = sh;
            }

            var lensDir = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var lensFromSubIfd = lensDir?.GetDescription(ExifDirectoryBase.TagLensModel);
            if (lensFromSubIfd != null) photo.LensModel = lensFromSubIfd;

            var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();
            if (gpsDir != null)
            {
                var location = GetGeoLocation(gpsDir);
                if (location.HasValue)
                {
                    photo.Latitude = location.Value.Latitude;
                    photo.Longitude = location.Value.Longitude;
                }
            }

            // レーティング情報の読み取り
            ExtractRating(directories, exifIfd0Dir, photo);
        }
        catch
        {
            photo.DateTaken = File.GetCreationTime(filePath);
        }

        return photo;
    }

    private static void ExtractRating(
        IReadOnlyList<MetadataExtractor.Directory> directories,
        ExifIfd0Directory? exifIfd0Dir,
        Photo photo)
    {
        // 優先度 1: XMP xmp:Rating (Lightroom / Capture One / Adobe 標準)
        // xmp:Rating = -1:リジェクト, 0:未評価, 1-5:星評価
        var xmpDir = directories.OfType<XmpDirectory>().FirstOrDefault();
        if (xmpDir?.XmpMeta != null)
        {
            // 優先度 1: XMP xmp:Rating (Lightroom / Capture One / Adobe 標準)
            try
            {
                var ratingStr = xmpDir.XmpMeta.GetPropertyString(XmpNsAdobe, "Rating");
                if (ratingStr != null && int.TryParse(ratingStr, out var xmpRating))
                {
                    if (xmpRating == -1)
                        photo.IsRejected = true;
                    else if (xmpRating >= 1 && xmpRating <= 5)
                        photo.Rating = xmpRating;
                    return;
                }
            }
            catch { /* XmpException は無視 */ }

            // 優先度 2: XMP MicrosoftPhoto:Rating (Windows フォトアプリ / OneDrive)
            try
            {
                var msStr = xmpDir.XmpMeta.GetPropertyString(XmpNsMicrosoft, "Rating");
                if (msStr != null && int.TryParse(msStr, out var msRating)
                    && msRating >= 1 && msRating <= 5)
                {
                    photo.Rating = msRating;
                    return;
                }
            }
            catch { /* XmpException は無視 */ }
        }

        // 優先度 3: EXIF タグ 0x4746 (Windows フォトビューアー)
        // 値: 1→1★, 25→2★, 50→3★, 75→4★, 99→5★
        if (exifIfd0Dir != null && exifIfd0Dir.TryGetInt32(TagWindowsRating, out var winRating))
        {
            photo.Rating = MapWindowsExifRating(winRating);
        }
    }

    private static int MapWindowsExifRating(int windowsValue) => windowsValue switch
    {
        >= 99 => 5,
        >= 75 => 4,
        >= 50 => 3,
        >= 25 => 2,
        >= 1  => 1,
        _     => 0
    };

    private GeoLocation? GetGeoLocation(GpsDirectory gpsDir)
    {
        return gpsDir.TryGetGeoLocation(out GeoLocation geoLocation) ? geoLocation : null;
    }
}
