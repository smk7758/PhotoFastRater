namespace PhotoFastRater.Core.ImageProcessing;

public interface IThumbnailGenerator
{
    Task<byte[]> GenerateAsync(string filePath, int targetSize);
}
