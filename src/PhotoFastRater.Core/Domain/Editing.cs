namespace PhotoFastRater.Core.Domain;

/// <summary>Represents a normalized non-destructive crop rectangle.</summary>
/// <remarks>All coordinates are in the inclusive range 0 through 1 and the rectangle must remain inside the image.</remarks>
public readonly record struct CropRect
{
    /// <summary>Initializes a validated normalized crop rectangle.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the rectangle is empty or outside the image.</exception>
    public CropRect(double x, double y, double width, double height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > 1 || y + height > 1)
            throw new ArgumentOutOfRangeException(nameof(width), "Crop rectangle must be non-empty and remain inside 0..1.");

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the normalized left coordinate.</summary>
    public double X { get; }
    /// <summary>Gets the normalized top coordinate.</summary>
    public double Y { get; }
    /// <summary>Gets the normalized width.</summary>
    public double Width { get; }
    /// <summary>Gets the normalized height.</summary>
    public double Height { get; }
}

/// <summary>Describes a repeatable, non-destructive export operation.</summary>
public sealed record ExportRecipe(
    string OutputDirectory,
    string FileNamePattern,
    string Format,
    int Quality,
    CropRect? Crop,
    int RotationDegrees,
    int? MaximumWidth,
    int? MaximumHeight,
    bool PreserveMetadata,
    int FrameWidth = 0,
    string FrameColor = "#FFFFFF",
    bool IncludeExifOverlay = false);

/// <summary>Stores the durable user choices for a two-to-four-pane comparison.</summary>
public sealed record CompareWorkspaceState
{
    /// <summary>Creates a comparison state containing two to four distinct stable photo IDs.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when fewer than two or more than four photos are supplied.</exception>
    /// <exception cref="ArgumentException">Thrown when IDs are duplicated or non-positive.</exception>
    public CompareWorkspaceState(
        IReadOnlyList<int> photoIds,
        bool synchronizeViewport = true,
        bool autoAdvance = false)
    {
        ArgumentNullException.ThrowIfNull(photoIds);
        if (photoIds.Count is < 2 or > 4)
            throw new ArgumentOutOfRangeException(nameof(photoIds), "Comparison requires two to four photos.");
        if (photoIds.Any(id => id <= 0) || photoIds.Distinct().Count() != photoIds.Count)
            throw new ArgumentException("Comparison photo IDs must be positive and distinct.", nameof(photoIds));

        PhotoIds = photoIds.ToArray();
        SynchronizeViewport = synchronizeViewport;
        AutoAdvance = autoAdvance;
    }

    /// <summary>Gets the stable photo IDs in pane order.</summary>
    public IReadOnlyList<int> PhotoIds { get; }

    /// <summary>Gets whether zoom and pan are shared by all panes.</summary>
    public bool SynchronizeViewport { get; }

    /// <summary>Gets whether post-rating navigation is enabled; defaults to false.</summary>
    public bool AutoAdvance { get; }
}
