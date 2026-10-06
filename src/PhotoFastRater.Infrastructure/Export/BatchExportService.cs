using System.Globalization;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Database.Repositories;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PhotoFastRater.Infrastructure.Export;

/// <summary>Produces JPEG, PNG, or TIFF derivatives while leaving every source file untouched.</summary>
public sealed class BatchExportService(PhotoRepository repository) : IExportService
{
    private static readonly char[] InvalidFileNameCharacters = Path.GetInvalidFileNameChars();
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2"
    };

    /// <inheritdoc />
    /// <remarks>Processing is O(pixels) per photo and retains at most one full decoded image.</remarks>
    public async Task<IReadOnlyList<ExportResult>> ExportAsync(
        IReadOnlyCollection<int> photoIds,
        ExportRecipe recipe,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRecipe(recipe);
        Directory.CreateDirectory(recipe.OutputDirectory);
        var distinctIds = photoIds.Distinct().ToArray();
        var photos = await repository.GetByIdsAsync(distinctIds, cancellationToken);
        var photosById = photos.ToDictionary(photo => photo.Id);
        var results = new List<ExportResult>(photoIds.Count);
        var failed = 0;

        foreach (var photoId in distinctIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExportResult result;
            if (!photosById.TryGetValue(photoId, out var photo))
            {
                result = new ExportResult(photoId, null, "写真がDBに見つかりません。");
            }
            else
            {
                result = await ExportOneAsync(photo, recipe, cancellationToken);
            }

            results.Add(result);
            if (result.Error is not null)
                failed++;
            progress?.Report(new ExportProgress(results.Count, distinctIds.Length, failed));
        }

        return results;
    }

    private static async Task<ExportResult> ExportOneAsync(
        Photo photo,
        ExportRecipe recipe,
        CancellationToken cancellationToken)
    {
        if (photo.IsMissing || !File.Exists(photo.FilePath))
            return new ExportResult(photo.Id, null, "元画像が見つかりません。");
        if (RawExtensions.Contains(Path.GetExtension(photo.FilePath)))
            return new ExportResult(photo.Id, null, "RAWフル現像は未対応です。JPEG等へ現像してから書き出してください。");

        var temporaryPath = Path.Combine(recipe.OutputDirectory, $".pfr-{Guid.NewGuid():N}.tmp");
        try
        {
            using var image = await Image.LoadAsync<Rgba32>(photo.FilePath, cancellationToken);
            var profiles = recipe.PreserveMetadata ? ImageProfiles.Capture(image) : null;
            image.Mutate(context => context.AutoOrient());
            ApplyRotationAndCrop(image, recipe);
            ApplyResize(image, recipe);

            using var rendered = AddFrame(image, recipe);
            if (recipe.IncludeExifOverlay)
                new ExifOverlayRenderer().RenderExifOverlay(rendered, photo, CreateOverlayTemplate(recipe));
            profiles?.Restore(rendered);
            // AutoOrient physically rotates pixels, so retaining the old orientation tag would rotate them twice downstream.
            rendered.Metadata.ExifProfile?.RemoveValue(ExifTag.Orientation);

            await SaveAsync(rendered, temporaryPath, recipe, cancellationToken);
            var outputPath = MoveToAvailablePath(temporaryPath, photo, recipe);
            return new ExportResult(photo.Id, outputPath, null);
        }
        catch (OperationCanceledException)
        {
            TryDeleteTemporary(temporaryPath);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidImageContentException or ArgumentException)
        {
            TryDeleteTemporary(temporaryPath);
            return new ExportResult(photo.Id, null, exception.Message);
        }
    }

    private static void ApplyRotationAndCrop(Image<Rgba32> image, ExportRecipe recipe)
    {
        if (recipe.RotationDegrees != 0)
            image.Mutate(context => context.Rotate(recipe.RotationDegrees));
        if (recipe.Crop is not { } crop)
            return;

        var rectangle = new Rectangle(
            (int)Math.Floor(crop.X * image.Width),
            (int)Math.Floor(crop.Y * image.Height),
            Math.Max(1, (int)Math.Ceiling(crop.Width * image.Width)),
            Math.Max(1, (int)Math.Ceiling(crop.Height * image.Height)));
        rectangle = Rectangle.Intersect(rectangle, new Rectangle(0, 0, image.Width, image.Height));
        image.Mutate(context => context.Crop(rectangle));
    }

    private static void ApplyResize(Image<Rgba32> image, ExportRecipe recipe)
    {
        if (recipe.MaximumWidth is null && recipe.MaximumHeight is null)
            return;
        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(recipe.MaximumWidth ?? int.MaxValue, recipe.MaximumHeight ?? int.MaxValue),
            Sampler = KnownResamplers.Lanczos3
        }));
    }

    private static Image<Rgba32> AddFrame(Image<Rgba32> image, ExportRecipe recipe)
    {
        if (recipe.FrameWidth <= 0)
            return image.Clone();
        using var clone = image.Clone();
        var template = new ExportTemplate
        {
            EnableFrame = true,
            FrameWidth = recipe.FrameWidth,
            FrameColor = recipe.FrameColor
        };
        return new FrameRenderer().AddFrame(clone, template);
    }

    private static ExportTemplate CreateOverlayTemplate(ExportRecipe recipe) => new()
    {
        EnableExifOverlay = true,
        Position = ExifOverlayPosition.BottomLeft,
        FontFamily = "Segoe UI",
        FontSize = 24,
        TextColor = "#FFFFFF",
        BackgroundColor = "#000000",
        BackgroundOpacity = 70,
        FrameColor = recipe.FrameColor
    };

    private static async Task SaveAsync(
        Image<Rgba32> image,
        string path,
        ExportRecipe recipe,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
        switch (NormalizeFormat(recipe.Format))
        {
            case "jpg":
                await image.SaveAsync(stream, new JpegEncoder { Quality = recipe.Quality }, cancellationToken);
                break;
            case "png":
                await image.SaveAsync(stream, new PngEncoder(), cancellationToken);
                break;
            case "tiff":
                await image.SaveAsync(stream, new TiffEncoder(), cancellationToken);
                break;
        }
    }

    private static string MoveToAvailablePath(string temporaryPath, Photo photo, ExportRecipe recipe)
    {
        var extension = NormalizeFormat(recipe.Format) switch { "jpg" => ".jpg", "png" => ".png", _ => ".tiff" };
        var stem = recipe.FileNamePattern
            .Replace("{name}", Path.GetFileNameWithoutExtension(photo.FileName), StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", photo.Id.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", photo.DateTaken.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        stem = string.Concat(stem.Select(character => InvalidFileNameCharacters.Contains(character) ? '_' : character)).Trim();
        if (string.IsNullOrWhiteSpace(stem))
            stem = $"photo-{photo.Id}";

        for (var suffix = 1; suffix <= 10_000; suffix++)
        {
            var name = suffix == 1 ? stem : $"{stem}-{suffix}";
            var outputPath = Path.Combine(recipe.OutputDirectory, name + extension);
            try
            {
                File.Move(temporaryPath, outputPath, false);
                return outputPath;
            }
            catch (IOException) when (File.Exists(outputPath))
            {
            }
        }
        throw new IOException("同名ファイルが多すぎるため、安全な出力名を決定できません。");
    }

    private static void ValidateRecipe(ExportRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (string.IsNullOrWhiteSpace(recipe.OutputDirectory) || !Path.IsPathFullyQualified(recipe.OutputDirectory))
            throw new ArgumentException("出力先には絶対パスが必要です。", nameof(recipe));
        if (string.IsNullOrWhiteSpace(recipe.FileNamePattern))
            throw new ArgumentException("命名規則が必要です。", nameof(recipe));
        if (recipe.Quality is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(recipe), "JPEG品質は1～100です。");
        if (recipe.RotationDegrees is not (0 or 90 or 180 or 270))
            throw new ArgumentOutOfRangeException(nameof(recipe), "回転は0、90、180、270度です。");
        if (recipe.MaximumWidth is <= 0 || recipe.MaximumHeight is <= 0 || recipe.FrameWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(recipe), "寸法は正の値が必要です。");
        _ = NormalizeFormat(recipe.Format);
    }

    private static string NormalizeFormat(string format) => format.Trim().TrimStart('.').ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "jpg",
        "png" => "png",
        "tif" or "tiff" => "tiff",
        _ => throw new ArgumentException("形式はJPEG、PNG、TIFFのいずれかです。", nameof(format))
    };

    private static void TryDeleteTemporary(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    private sealed record ImageProfiles(
        SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile? Exif,
        SixLabors.ImageSharp.Metadata.Profiles.Iptc.IptcProfile? Iptc,
        SixLabors.ImageSharp.Metadata.Profiles.Xmp.XmpProfile? Xmp,
        SixLabors.ImageSharp.Metadata.Profiles.Icc.IccProfile? Icc)
    {
        public static ImageProfiles Capture(Image<Rgba32> image) => new(
            image.Metadata.ExifProfile?.DeepClone(),
            image.Metadata.IptcProfile?.DeepClone(),
            image.Metadata.XmpProfile?.DeepClone(),
            image.Metadata.IccProfile?.DeepClone());

        public void Restore(Image<Rgba32> image)
        {
            image.Metadata.ExifProfile = Exif;
            image.Metadata.IptcProfile = Iptc;
            image.Metadata.XmpProfile = Xmp;
            image.Metadata.IccProfile = Icc;
        }
    }
}
