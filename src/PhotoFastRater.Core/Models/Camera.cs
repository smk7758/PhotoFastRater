namespace PhotoFastRater.Core.Models;

public class Camera
{
    public int Id { get; set; }
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int PhotoCount { get; set; }
}
