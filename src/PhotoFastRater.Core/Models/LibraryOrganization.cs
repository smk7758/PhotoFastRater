namespace PhotoFastRater.Core.Models;

/// <summary>Represents one reusable photo label with a case-insensitive unique name.</summary>
public sealed class PhotoTag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public List<PhotoTagMapping> Photos { get; set; } = [];
}

/// <summary>Joins a photo and tag without duplicating either entity.</summary>
public sealed class PhotoTagMapping
{
    public int PhotoId { get; set; }
    public Photo Photo { get; set; } = null!;
    public int TagId { get; set; }
    public PhotoTag Tag { get; set; } = null!;
    public DateTime AddedUtc { get; set; }
}

/// <summary>Represents a user-defined collection that may have one parent.</summary>
public sealed class PhotoCollection
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public PhotoCollection? Parent { get; set; }
    public List<PhotoCollection> Children { get; set; } = [];
    public List<PhotoCollectionMapping> Photos { get; set; } = [];
}

/// <summary>Joins one photo to one hierarchical collection.</summary>
public sealed class PhotoCollectionMapping
{
    public int PhotoId { get; set; }
    public Photo Photo { get; set; } = null!;
    public int CollectionId { get; set; }
    public PhotoCollection Collection { get; set; } = null!;
    public DateTime AddedUtc { get; set; }
}
