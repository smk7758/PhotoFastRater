namespace PhotoFastRater.Core.Models;

public class Event
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public EventType Type { get; set; }  // Location, Event, Custom

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public string? Location { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string? CoverPhotoPath { get; set; }
    public int PhotoCount { get; set; }

    public List<PhotoEventMapping> Photos { get; set; } = new();
}

public enum EventType
{
    Location,
    Event,
    Custom
}
