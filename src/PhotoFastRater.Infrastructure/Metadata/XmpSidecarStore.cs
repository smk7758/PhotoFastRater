using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Domain;

namespace PhotoFastRater.Infrastructure.Metadata;

/// <summary>Preserves unknown XMP content while atomically updating PhotoFastRater-owned fields.</summary>
public sealed class XmpSidecarStore : IXmpSidecarStore
{
    private static readonly XNamespace XmpMeta = "adobe:ns:meta/";
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Xmp = "http://ns.adobe.com/xap/1.0/";
    private static readonly XNamespace PhotoFastRater = "https://photofastrater.app/ns/1.0/";

    /// <inheritdoc />
    public async Task<XmpRatingDocument?> ReadAsync(string photoPath, CancellationToken cancellationToken = default)
    {
        var sidecarPath = GetSidecarPath(photoPath);
        if (!File.Exists(sidecarPath))
            return null;

        var document = await LoadSecurelyAsync(sidecarPath, cancellationToken);
        var description = document.Descendants(Rdf + "Description").FirstOrDefault();
        if (description is null)
            return null;

        var rating = ParseInt(description.Attribute(Xmp + "Rating")?.Value, 0);
        var favorite = ParseBool(description.Attribute(PhotoFastRater + "Favorite")?.Value);
        var rejected = ParseBool(description.Attribute(PhotoFastRater + "Rejected")?.Value) || rating == -1;
        var updatedUtc = ParseDateTime(description.Attribute(PhotoFastRater + "UpdatedUtc")?.Value);
        var revision = ParseLong(description.Attribute(PhotoFastRater + "Revision")?.Value);
        var source = description.Attribute(PhotoFastRater + "UpdateSource")?.Value ?? "external";
        return new XmpRatingDocument(new RatingState(rating, favorite, rejected), updatedUtc, revision, source);
    }

    /// <inheritdoc />
    public async Task WriteAsync(string photoPath, XmpRatingDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sidecarPath = GetSidecarPath(photoPath);
        var xmpDocument = File.Exists(sidecarPath)
            ? await LoadSecurelyAsync(sidecarPath, cancellationToken)
            : CreateDocument();

        var description = GetOrCreateDescription(xmpDocument);
        description.SetAttributeValue(Xmp + "Rating", document.Rating.Stars.ToString(CultureInfo.InvariantCulture));
        description.SetAttributeValue(PhotoFastRater + "Favorite", document.Rating.IsFavorite ? "true" : "false");
        description.SetAttributeValue(PhotoFastRater + "Rejected", document.Rating.IsRejected ? "true" : "false");
        description.SetAttributeValue(PhotoFastRater + "UpdatedUtc", document.UpdatedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        description.SetAttributeValue(PhotoFastRater + "Revision", document.Revision.ToString(CultureInfo.InvariantCulture));
        description.SetAttributeValue(PhotoFastRater + "UpdateSource", document.UpdateSource);

        var directory = Path.GetDirectoryName(sidecarPath)!;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(sidecarPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await xmpDocument.SaveAsync(stream, SaveOptions.DisableFormatting, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(sidecarPath))
                File.Replace(temporaryPath, sidecarPath, null);
            else
                File.Move(temporaryPath, sidecarPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <summary>Returns the collision-free sidecar path, including the source extension.</summary>
    public static string GetSidecarPath(string photoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(photoPath);
        return Path.GetFullPath(photoPath) + ".xmp";
    }

    private static async Task<XDocument> LoadSecurelyAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        return await XDocument.LoadAsync(reader, LoadOptions.PreserveWhitespace, cancellationToken);
    }

    private static XDocument CreateDocument() => new(
        new XElement(XmpMeta + "xmpmeta",
            new XAttribute(XNamespace.Xmlns + "x", XmpMeta),
            new XElement(Rdf + "RDF",
                new XAttribute(XNamespace.Xmlns + "rdf", Rdf),
                new XElement(Rdf + "Description",
                    new XAttribute(XNamespace.Xmlns + "xmp", Xmp),
                    new XAttribute(XNamespace.Xmlns + "pfr", PhotoFastRater)))));

    private static XElement GetOrCreateDescription(XDocument document)
    {
        var existing = document.Descendants(Rdf + "Description").FirstOrDefault();
        if (existing is not null)
            return existing;

        var rdf = document.Descendants(Rdf + "RDF").FirstOrDefault();
        if (rdf is null)
        {
            rdf = new XElement(Rdf + "RDF", new XAttribute(XNamespace.Xmlns + "rdf", Rdf));
            if (document.Root is null)
                document.Add(new XElement(XmpMeta + "xmpmeta", new XAttribute(XNamespace.Xmlns + "x", XmpMeta), rdf));
            else
                document.Root.Add(rdf);
        }

        var description = new XElement(Rdf + "Description");
        rdf.Add(description);
        return description;
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static bool ParseBool(string? value) => bool.TryParse(value, out var parsed) && parsed;

    private static DateTime ParseDateTime(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.UnixEpoch;
}
