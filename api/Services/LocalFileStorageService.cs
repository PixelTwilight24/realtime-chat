namespace api.Services;

public class LocalFileStorageService : IFileStorageService
{
    private const string PublicPath = "/uploads";
    private readonly string _uploadsDirectory;

    public LocalFileStorageService(IWebHostEnvironment env)
    {
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        _uploadsDirectory = Path.Combine(webRoot, "uploads");
        Directory.CreateDirectory(_uploadsDirectory);
    }

    public async Task<string> SaveAsync(Stream content, string fileExtension, CancellationToken ct = default)
    {
        var fileName = $"{Guid.NewGuid()}{fileExtension}";
        var fullPath = Path.Combine(_uploadsDirectory, fileName);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, ct);

        return $"{PublicPath}/{fileName}";
    }
}
