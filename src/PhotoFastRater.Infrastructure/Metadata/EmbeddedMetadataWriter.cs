using System.Globalization;
using System.Windows.Media.Imaging;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Domain;

namespace PhotoFastRater.Infrastructure.Metadata;

/// <summary>Writes JPEG metadata only when WIC can update the existing metadata block in place.</summary>
public sealed class EmbeddedMetadataWriter : IEmbeddedMetadataWriter
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg"
    };

    /// <inheritdoc />
    public Task<MetadataWriteResult> WriteRatingAsync(
        string filePath,
        RatingState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => WriteRatingCore(filePath, state), cancellationToken);
    }

    private static MetadataWriteResult WriteRatingCore(string filePath, RatingState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        if (!SupportedExtensions.Contains(Path.GetExtension(fullPath)))
            return new MetadataWriteResult(false, "unsupported-format", "Embedded rating writes support JPEG files only.");
        if (!File.Exists(fullPath))
            return new MetadataWriteResult(false, "file-not-found", "The source image no longer exists.");

        try
        {
            using var stream = File.Open(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
            var writer = decoder.Frames[0].CreateInPlaceBitmapMetadataWriter();
            if (writer is null)
                return new MetadataWriteResult(false, "no-padding", "The JPEG has no writable metadata padding; pixels were not re-encoded.");

            writer.Rating = Math.Max(0, state.Stars);
            TrySetQuery(writer, "/xmp/xmp:Rating", state.Stars.ToString(CultureInfo.InvariantCulture));
            TrySetQuery(writer, "/app1/ifd/{ushort=18246}", (ushort)MapToExifRating(state.Stars));

            return writer.TrySave()
                ? new MetadataWriteResult(true)
                : new MetadataWriteResult(false, "in-place-save-failed", "The metadata block could not be updated in place; pixels were not re-encoded.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new MetadataWriteResult(false, "access-denied", ex.Message);
        }
        catch (IOException ex)
        {
            return new MetadataWriteResult(false, "io-error", ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return new MetadataWriteResult(false, "unsupported-metadata", ex.Message);
        }
        catch (FileFormatException ex)
        {
            return new MetadataWriteResult(false, "invalid-image", ex.Message);
        }
    }

    private static void TrySetQuery(InPlaceBitmapMetadataWriter writer, string query, object value)
    {
        try
        {
            writer.SetQuery(query, value);
        }
        catch (ArgumentException)
        {
            // Optional metadata dialects may be absent; the primary WIC rating can still succeed.
        }
        catch (NotSupportedException)
        {
            // Do not rewrite or re-encode merely to add an optional metadata dialect.
        }
    }

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
