namespace PhotoFastRater.Core.Domain;

/// <summary>
/// Represents the user-owned classification of one photo.
/// </summary>
/// <remarks>The star value is always in the XMP-compatible range -1 through 5.</remarks>
public readonly record struct RatingState
{
    /// <summary>Initializes a validated rating state.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="stars"/> is outside -1 through 5.</exception>
    public RatingState(int stars, bool isFavorite, bool isRejected)
    {
        if (stars is < -1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(stars), "Rating must be between -1 and 5.");

        Stars = stars;
        IsFavorite = isFavorite;
        IsRejected = isRejected || stars == -1;
    }

    /// <summary>Gets the XMP star value. -1 means rejected and 0 means unrated.</summary>
    public int Stars { get; }

    /// <summary>Gets whether the photo is marked as a favorite independently of its stars.</summary>
    public bool IsFavorite { get; }

    /// <summary>Gets whether the photo is rejected.</summary>
    public bool IsRejected { get; }
}

/// <summary>Determines how a RAW and JPEG pair shares user changes.</summary>
public enum PairLinkMode
{
    /// <summary>Changes are mirrored to both members of the pair.</summary>
    Linked,

    /// <summary>Each member keeps an independent state.</summary>
    Independent
}

/// <summary>Describes the persistence state between the catalog and a sidecar.</summary>
public enum MetadataSyncStatus
{
    /// <summary>The catalog and sidecar agree.</summary>
    Synchronized,

    /// <summary>The catalog change has not yet been written to a sidecar.</summary>
    Pending,

    /// <summary>The sidecar could not be written because access was denied.</summary>
    AccessDenied,

    /// <summary>Both sides changed and require an explicit user decision.</summary>
    Conflict,

    /// <summary>The last synchronization failed for another recoverable reason.</summary>
    Failed
}
