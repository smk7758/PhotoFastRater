namespace PhotoFastRater.Core.Models;

public class PhotoGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public GroupingType Type { get; set; }
    public string? FilterCriteria { get; set; }  // JSON serialized
    public int PhotoCount { get; set; }
}

public enum GroupingType
{
    ByDate,
    ByCamera,
    ByLens,
    ByRating,
    Custom
}
