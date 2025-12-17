using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using PhotoFastRater.Core.Models;
using GeoLocation = MetadataExtractor.GeoLocation;

namespace PhotoFastRater.Core.Services;

public class ExifService
{
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
                // 撮影日時
                if (exifSubIfdDir.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTaken))
                {
                    photo.DateTaken = dateTaken;
                }

                // 露出情報
                if (exifSubIfdDir.TryGetDouble(ExifDirectoryBase.TagFNumber, out var aperture))
                {
                    photo.Aperture = aperture;
                }

                if (exifSubIfdDir.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
                {
                    photo.ISO = iso;
                }

                if (exifSubIfdDir.TryGetDouble(ExifDirectoryBase.TagFocalLength, out var focalLength))
                {
                    photo.FocalLength = focalLength;
                }

                if (exifSubIfdDir.TryGetDouble(ExifDirectoryBase.TagExposureBias, out var exposureComp))
                {
                    photo.ExposureCompensation = exposureComp;
                }

                // シャッタースピード
                var shutterSpeed = exifSubIfdDir.GetDescription(ExifDirectoryBase.TagExposureTime);
                if (shutterSpeed != null)
                {
                    photo.ShutterSpeed = shutterSpeed;
                }

                // 画像サイズ
                if (exifSubIfdDir.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var width))
                {
                    photo.Width = width;
                }

                if (exifSubIfdDir.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var height))
                {
                    photo.Height = height;
                }
            }

            if (exifIfd0Dir != null)
            {
                // カメラ情報
                photo.CameraMake = exifIfd0Dir.GetDescription(ExifDirectoryBase.TagMake);
                photo.CameraModel = exifIfd0Dir.GetDescription(ExifDirectoryBase.TagModel);
            }

            // レンズ情報
            var lensDir = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (lensDir != null)
            {
                photo.LensModel = lensDir.GetDescription(ExifDirectoryBase.TagLensModel);
            }

            // GPS情報
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
        }
        catch
        {
            // EXIF読み取り失敗時はデフォルト値を使用
            photo.DateTaken = File.GetCreationTime(filePath);
        }

        return photo;
    }

    /// <summary>
    /// Parses various tags in an attempt to obtain a single object representing the latitude and longitude
    /// at which this image was captured.
    /// </summary>
    /// <returns>The geographical location of this image, or null if location could not be determined.</returns>
    private GeoLocation? GetGeoLocation(GpsDirectory gpsDir)
    {
        return gpsDir.TryGetGeoLocation(out GeoLocation geoLocation) ? geoLocation : null;
    }
}
