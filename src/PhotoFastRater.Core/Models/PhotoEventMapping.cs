namespace PhotoFastRater.Core.Models;

public class PhotoEventMapping
{
    public int PhotoId { get; set; }
    public Photo Photo { get; set; } = null!;

    public int EventId { get; set; }
    public Event Event { get; set; } = null!;

    public DateTime AddedDate { get; set; }
}
