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
    bool PreserveMetadata);

/// <summary>Stores the durable user choices for a two-to-four-pane comparison.</summary>
public sealed record CompareWorkspaceState(
    IReadOnlyList<int> PhotoIds,
    bool SynchronizeViewport = true,
    bool AutoAdvance = false);
