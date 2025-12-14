using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Export;

public interface IImageExporter
{
    Task<string> ExportAsync(Photo photo, ExportTemplate template, string outputPath);
}
